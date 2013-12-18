namespace ConfigManager
{
    using ConfigClasses;
    using NLog;
    using ServiceStack.Text;
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Timers;
    using System.Threading.Tasks;

    /// <summary>
    /// A data type containing metadata about a configuration item.
    /// </summary>
    public class ConfigurationItem
    {
        /// <summary>
        /// The name of the configuration item.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The raw configuration data.
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// The time the configuration file was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// A bool indicating whether the configuration item came from
        /// ConfigManager or a user-specified source delegate.
        /// </summary>
        public bool FromConfigManager { get; set; }
    }

    /// <summary>
    /// A data type containing metadata about a configuration file.
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// The parsed object from the configuration file.
        /// </summary>
        public object Parsed { get; set; }
        /// <summary>
        /// The raw text read from the configuration file.
        /// </summary>
        public string Raw { get; set; }
        /// <summary>
        /// The filepath to the configuration file.
        /// </summary>
        public string FilePath { get; set; }
        /// <summary>
        /// The time the configuration file was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// A utility class to make managing configuration files painless.
    /// ConfigManager uses json configuration files which map to strongly
    /// typed C# objects using ServiceStack.Text.DeserializeJson
    /// </summary>
    public class ConfigManager
    {
        private static Logger ConfigLogger =
            LogManager.GetCurrentClassLogger();
        private static ConcurrentDictionary<string, Configuration> _configs;
        private static List<FileSystemWatcher> _fsWatchers;
        private static readonly object _fsWatcherLock = new object();

        private static Func<string, ConfigurationItem> _getConfiguration;
        private static Action<ConfigurationItem> _putConfiguration;

        /// <summary>
        /// Development mode. If true, ConfigManager will look for
        /// files ending with .dev.conf rather than .conf
        /// </summary>
        public static bool DevMode { get; set; }

        /// <summary>
        /// A Utility Enum to ensure that typos aren't make for
        /// global configuration files.
        /// </summary>
        public enum ConfigKeys
        {
            /// <summary>
            /// Maps to the string of the same name.
            /// </summary>
            ConfigPathsConfig
        }

        static ConfigManager()
        {
            _configs = new ConcurrentDictionary<string, Configuration>();

            // TJ: Add the ConfigManagerConfig file default
            // to the class, then attempt to load 
            AddConfig<ConfigPathsConfig>(
                ConfigKeys.ConfigPathsConfig.ToString());
            AddConfig<ConfigPathsConfig>(
                ConfigKeys.ConfigPathsConfig.ToString(), update: true);

            SetupWatchDirectories();
        }

        /// <summary>
        /// A delegate settable by the user which says how to try
        /// and get the configuration before going to the file system.
        /// </summary>
        public static Func<string, ConfigurationItem> GetConfiguration
        {
            private get { return _getConfiguration; }
            set { _getConfiguration = value; }
        }

        /// <summary>
        /// A delegate settable by the user which says what to do after
        /// getting the configuration from the file system.
        /// </summary>
        public static Action<ConfigurationItem> PutConfiguration
        {
            private get { return _putConfiguration; }
            set { _putConfiguration = value; }
        }

        /// <summary>
        /// Adds a configuration to the manager.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the configuration object.
        /// </typeparam>
        /// <param name="configName">
        /// The name of the configuration object to add.
        /// </param>
        /// <param name="configPath">
        /// The file path (relative or absolute) to add.
        /// </param>
        /// <param name="update">
        /// Optional boolean argument that specifies
        /// the config should be updated if it exists.
        /// </param>
        public static void AddConfig<T>(
            string configName,
            string configPath = null,
            bool update = false)
            where T : new()
        {
            if (!update && _configs.ContainsKey(configName))
            {
                return;
            }

            if (configPath == null)
            {
                configPath = configName + ".conf";
            }

            _configs.AddOrUpdate(configName,
                (x) =>
                {
                    return CreateConfig<T>(configName, configPath);
                },
                (x, y) =>
                {
                    if (update)
                    {
                        y = CreateConfig<T>(configName, configPath);
                    }
                    return y;
                });
        }

        /// <summary>
        /// Adds and then gets the configuration of a given name.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the configuration object.
        /// </typeparam>
        /// <param name="configName">
        /// The name of the configuration object to add.
        /// </param>
        /// <param name="configPath">
        /// The file path (relative or absolute) to add.
        /// </param>
        /// <param name="update">
        /// Optional boolean argument that specifies the
        /// config should be updated if it exists.
        /// </param>
        /// <returns>
        /// An object of type T with the values in the specified config file.
        /// </returns>
        public static T GetCreateConfig<T>(
            string configName,
            string configPath = null,
            bool update = false)
            where T : new()
        {
            AddConfig<T>(configName, configPath: configPath, update: update);
            return GetConfig<T>(configName);
        }

        /// <summary>
        /// Creates a Configuration of the given type
        /// from the specified file path.
        /// </summary>
        /// <typeparam name="T">
        /// The type of the configuration object.
        /// </typeparam>
        /// <param name="configName">
        /// The name of the configuration.
        /// </param>
        /// <param name="configPath">
        /// The file path (relative or absolute) of the configuration file.
        /// </param>
        /// <returns>
        /// An object of type T with the values in the specified config file.
        /// </returns>
        private static Configuration CreateConfig<T>(
            string configName, string configPath)
            where T : new()
        {
            Configuration config = new Configuration();

            var path = GetPath(configPath);
            var fromFile = GetConfigFromFile(configName, path);
            var fromDelegate = GetConfigFromDelegate(configName);
            HandlePutConfig(fromFile, fromDelegate);

            var newest = fromFile.LastUpdated > fromDelegate.LastUpdated
                ? fromFile
                : fromDelegate;

            config.FilePath = path;
            config.LastUpdated = newest.LastUpdated;
            config.Raw = newest.Data;
            config.Parsed = ParseConfig<T>(config.Raw);

            return config;
        }

        /// <summary>
        /// Gets a configuration item from a file.
        /// </summary>
        /// <param name="name">The name of the configuration.</param>
        /// <param name="path">The path of the file.</param>
        /// <returns>
        /// The configuration item pulled from the specified file.
        /// </returns>
        private static ConfigurationItem GetConfigFromFile(
            string name, string path)
        {
            ConfigurationItem configItem = new ConfigurationItem();

            configItem.Name = name;
            configItem.Data = ReadConfig(path);
            configItem.LastUpdated = GetConfigUpdateTime(path);
            configItem.FromConfigManager = true;

            return configItem;
        }

        /// <summary>
        /// Get a configuration item from a delegate.
        /// </summary>
        /// <param name="name">The name of the configuration.</param>
        /// <returns>The configuration item pulled from the delegate.</returns>
        private static ConfigurationItem GetConfigFromDelegate(string name)
        {
            ConfigurationItem configItem = null;

            if(null != _getConfiguration)
            {
                configItem = _getConfiguration(name);
            }

            if (null == configItem)
            {
                configItem = new ConfigurationItem();
            }

            configItem.Name = name;

            return configItem;
        }

        private static void HandlePutConfig(
            ConfigurationItem fromFile,
            ConfigurationItem fromDelegate)
        {
            if (null != _putConfiguration
                && fromFile.Name == fromDelegate.Name
                && fromFile.LastUpdated > fromDelegate.LastUpdated
                && !string.IsNullOrEmpty(fromFile.Data))
            {
                Task.Run(() => _putConfiguration(fromFile));
            }
        }

        private static string GetPath(string configPath)
        {
            string devConfigPath = "";
            if (DevMode)
            {
                devConfigPath = configPath.Replace(".conf", ".dev.conf");
            }

            if (!Path.IsPathRooted(configPath))
            {
                DirectoryInfo[] paths = GetConfig<ConfigPathsConfig>(
                    ConfigKeys.ConfigPathsConfig.ToString()).Paths;
                foreach (DirectoryInfo path in paths)
                {
                    // TJ: Skip over nonexistant paths
                    if (path == null || !path.Exists)
                    {
                        continue;
                    }

                    FileInfo fi = null;

                    if (DevMode)
                    {
                        fi = path.EnumerateFiles(
                                devConfigPath,
                                SearchOption.AllDirectories).FirstOrDefault();
                    }

                    if (null == fi)
                    {
                        fi = path.EnumerateFiles(
                            configPath,
                            SearchOption.AllDirectories).FirstOrDefault();
                    }
                    if (fi != null)
                    {
                        configPath = fi.FullName;
                        break;
                    }
                }
            }

            return configPath;
        }

        private static void SetupWatchDirectories()
        {
            lock (_fsWatcherLock)
            {
                _fsWatchers = new List<FileSystemWatcher>();

                DirectoryInfo[] paths = GetConfig<ConfigPathsConfig>(
                    ConfigKeys.ConfigPathsConfig.ToString()).Paths;

                var fsEventHandler =
                    new FileSystemEventHandler(HandleUpdate);
                var errorEventHandler =
                    new ErrorEventHandler(HandleError);
                var renamedEventHandler =
                    new RenamedEventHandler(HandleRename);

                _fsWatchers.AddRange(paths
                    .Where(path => null != path && path.Exists)
                    .Select(path =>
                    {
                        var fsWatcher = new FileSystemWatcher(
                            path.FullName, "*.conf");

                        fsWatcher.NotifyFilter =
                              NotifyFilters.LastWrite
                            | NotifyFilters.FileName
                            | NotifyFilters.DirectoryName;

                        fsWatcher.IncludeSubdirectories = true;

                        fsWatcher.Changed += fsEventHandler;
                        fsWatcher.Created += fsEventHandler;
                        fsWatcher.Deleted += fsEventHandler;
                        fsWatcher.Error += errorEventHandler;
                        fsWatcher.Renamed += renamedEventHandler;

                        fsWatcher.EnableRaisingEvents = true;

                        return fsWatcher;
                    }));
            }
        }

        private static void TearDownWatchDirectories()
        {
            lock (_fsWatcherLock)
            {
                if (null == _fsWatchers)
                {
                    return;
                }

                foreach (var fsWatcher in _fsWatchers)
                {
                    fsWatcher.Dispose();
                }

                _fsWatchers = null;
            }
        }

        /// <summary>
        /// Remove the specified config file from the ConfigManager
        /// </summary>
        /// <param name="key">The specified config file key</param>
        public static void RemoveConfig(string key)
        {
            Configuration config;

            key = key.Split('\\', '/').LastOrDefault();

            string conf = ".conf";
            if (key.EndsWith(conf))
            {
                key = key.Substring(0, key.Length - conf.Length);
            }

            if (DevMode)
            {
                string dev = ".dev";
                if (key.EndsWith(dev))
                {
                    key = key.Substring(0, key.Length - dev.Length);
                }
            }

            _configs.TryRemove(key, out config);

            if (ConfigKeys.ConfigPathsConfig.ToString() == key)
            {
                TearDownWatchDirectories();
                SetupWatchDirectories();
            }
        }

        private static void HandleUpdate(object sender, FileSystemEventArgs e)
        {
            RemoveConfig(e.Name);
        }

        private static void HandleError(object sender, ErrorEventArgs e)
        {
            ConfigLogger.LogException(
                LogLevel.Error,
                "Exception thrown by FileSystemWatcher",
                e.GetException());
        }

        private static void HandleRename(object sender, RenamedEventArgs e)
        {
            RemoveConfig(e.OldName);
            RemoveConfig(e.Name);
        }

        /// <summary>
        /// Returns the configuration object of the specified type and name
        /// from the in-memory dictionary of the ConfigManager.
        /// </summary>
        /// <typeparam name="T">The type of the configuration object.
        /// </typeparam>
        /// <param name="configName">The name of the configuration
        /// object to retrieve.</param>
        /// <returns>An object of type T with the values in the
        /// ConfigManager.</returns>
        public static T GetConfig<T>(string configName) where T : new()
        {
            Configuration configuration;
            _configs.TryGetValue(configName, out configuration);
            return configuration == null ? new T() : (T)configuration.Parsed;
        }

        /// <summary>
        /// Returns the DateTime that the specified file was last written to.
        /// </summary>
        /// <param name="configPath">The file path.</param>
        /// <returns>
        /// The DateTime that the specified file was last written to.
        /// </returns>
        public static DateTime GetConfigUpdateTime(string configPath)
        {
            DateTime t = DateTime.MinValue;
            try
            {
                t = File.GetLastWriteTime(configPath);
            }
            catch (Exception e)
            {
                ConfigLogger.LogException(
                    LogLevel.Error,
                    "ConfigManager Error: could not read file creation time: "
                    + configPath, e);
            }

            return t;
        }

        /// <summary>
        /// Reads a relative or absolute file path and returns the contents.
        /// Nonrooted file paths are checked relative to the configured
        /// search directories.
        /// </summary>
        /// <param name="configPath">The file path.</param>
        /// <returns>The contents of the file that was found, or empty string
        /// if no file present.</returns>
        public static string ReadConfig(string configPath)
        {
            string config = "";
            // TJ: Check the configPath
            try
            {
                config = File.ReadAllText(configPath);
            }
            catch (System.IO.FileNotFoundException)
            {
                // Ignore
            }
            catch (Exception e)
            {
                ConfigLogger.LogException(
                    LogLevel.Error,
                    "ConfigManager Error: could not read file: "
                    + configPath, e);
            }

            return config;
        }

        /// <summary>
        /// Parses json into the specified object type.
        /// </summary>
        /// <typeparam name="T">The object type to return.</typeparam>
        /// <param name="configRaw">The raw json.</param>
        /// <returns>
        /// The contents of the raw json as the specified object type.
        /// </returns>
        public static T ParseConfig<T>(string configRaw) where T : new()
        {
            T config = default(T);
            if (configRaw == null)
            {
                ConfigLogger.Log(
                    LogLevel.Error,
                    "ConfigManager Error: "
                    +"Cannot deserialize null string for Type {0}",
                    typeof(T).ToString());
            }
            else if (configRaw != string.Empty)
            {
                config = JsonSerializer.DeserializeFromString<T>(configRaw);

                if (config == null)
                {
                    // Deserializing configraw failed, log configraw
                    ConfigLogger.Error(
                        "ConfigManager Error: "
                        +@"Unable to deserialize string ""{0}"" to Type {1}",
                        configRaw,
                        typeof(T).ToString());
                }
            }

            if (config == null)
            {
                config = new T();
            }
            return config;
        }
    }
}
