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
                    return CreateConfig<T>(configPath);
                },
                (x, y) =>
                {
                    if (update)
                    {
                        y = CreateConfig<T>(configPath);
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
        /// <param name="configPath">
        /// The file path (relative or absolute) of the configuration file.
        /// </param>
        /// <returns>
        /// An object of type T with the values in the specified config file.
        /// </returns>
        private static Configuration CreateConfig<T>(string configPath)
            where T : new()
        {
            Configuration config = new Configuration();

            config.FilePath = GetPath(configPath);
            config.LastUpdated = GetConfigUpdateTime(config.FilePath);
            config.Raw = ReadConfig(config.FilePath);
            config.Parsed = ParseConfig<T>(config.Raw);

            return config;
        }

        private static string GetPath(string configPath)
        {
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

                    FileInfo fi = path.EnumerateFiles(
                        configPath,
                        SearchOption.AllDirectories).FirstOrDefault();
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
                            path.FullName, "*.config");

                        fsWatcher.Changed += fsEventHandler;
                        fsWatcher.Created += fsEventHandler;
                        fsWatcher.Deleted += fsEventHandler;
                        fsWatcher.Error += errorEventHandler;
                        fsWatcher.Renamed += renamedEventHandler;

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

        public static void RemoveConfig(string key)
        {
            Configuration config;
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
            T config = JsonSerializer.DeserializeFromString<T>(configRaw);
            if (config == null)
            {
                config = new T();
            }
            return config;
        }
    }
}
