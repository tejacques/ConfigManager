namespace ConfigManager.Tests
{
    using ConfigClasses;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    [TestFixture]
    public class ConfigManagerTests
    {
        private static int loops;
        private static string json = @"
[
""Foo"",
""Bar"",
""Baz"",
""1"",""2"",""3"",""4"",""5"",""6"",""7"",""8"",""9"",""10"",
""1"",""2"",""3"",""4"",""5"",""6"",""7"",""8"",""9"",""10"",
""1"",""2"",""3"",""4"",""5"",""6"",""7"",""8"",""9"",""10"",
""1"",""2"",""3"",""4"",""5"",""6"",""7"",""8"",""9"",""10"",
""1"",""2"",""3"",""4"",""5"",""6"",""7"",""8"",""9"",""10""
]
";
        [TestFixtureSetUp]
        public void SetUp()
        {
            loops = 100000;
            ConfigManager.DevMode = true;
            var parsedJsonDotNet = JsonConvert.DeserializeObject<List<string>>(json);
        }

        [Test]
        public static void TestConfigManager()
        {
            TestConfig config = 
                ConfigManager.GetCreateConfig<TestConfig>("TestDefault");
            Assert.NotNull(config);
        }

        [Test]
        public static void TestConfigParse()
        {
            TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                ("Test1");
            Assert.NotNull(config);
            Assert.AreEqual("1", config.Foo);
            Assert.AreEqual("2", config.Bar);
            Assert.AreEqual("3", config.Baz);
        }

        [Test]
        public static void TestConfigParseJson()
        {
            TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                ("TestJson");
            Assert.NotNull(config);
            Assert.AreEqual("1", config.Foo);
            Assert.AreEqual("2", config.Bar);
            Assert.AreEqual("3", config.Baz);
        }

        [Test]
        public static void TestConfigParseYaml()
        {
            TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                ("TestYaml");
            Assert.NotNull(config);
            Assert.AreEqual("1", config.Foo);
            Assert.AreEqual("2", config.Bar);
            Assert.AreEqual("3", config.Baz);
        }

        [Test]
        public static void TestConfigParseSerial()
        {
            for (int i = 0; i < loops; i++)
            {
                TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                    ("Test2");
                Assert.NotNull(config);
                Assert.AreEqual("a", config.Foo);
                Assert.AreEqual("b", config.Bar);
                Assert.AreEqual("c", config.Baz);
            }
        }

        [Test]
        public static void TestConfigParseSerialYaml()
        {
            for (int i = 0; i < loops; i++)
            {
                TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                    ("TestYaml2");
                Assert.NotNull(config);
                Assert.AreEqual("a", config.Foo);
                Assert.AreEqual("b", config.Bar);
                Assert.AreEqual("c", config.Baz);
            }
        }

        [Test]
        public static void TestConfigParseParallel()
        {
            Parallel.For(0, loops, (x) =>
            {
                TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                    ("Test3");
                Assert.NotNull(config);
                Assert.AreEqual("!", config.Foo);
                Assert.AreEqual("@", config.Bar);
                Assert.AreEqual("#", config.Baz);
            });
        }

        [Test]
        public static void TestConfigParseParallelYaml()
        {
            Parallel.For(0, loops, (x) =>
            {
                TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                    ("TestYaml3");
                Assert.NotNull(config);
                Assert.AreEqual("!", config.Foo);
                Assert.AreEqual("@", config.Bar);
                Assert.AreEqual("#", config.Baz);
            });
        }

        [Test]
        public static void TestConfigParseMultipleParallel()
        {
            Parallel.For(0, loops, (x) =>
            {
                TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                    ("Test_" + (x % 100), "");
                Assert.NotNull(config);
                Assert.AreEqual("foo", config.Foo);
                Assert.AreEqual("bar", config.Bar);
                Assert.AreEqual("baz", config.Baz);
            });
        }

        [Test]
        public static void TestConfigEvents()
        {
            // Test getting the config when the config file doesn't exist
            TestConfig config = 
                ConfigManager.GetCreateConfig<TestConfig>("TestUpdate");
            Assert.NotNull(config);
            Assert.AreEqual("foo", config.Foo);
            Assert.AreEqual("bar", config.Bar);
            Assert.AreEqual("baz", config.Baz);

            // Create a new config file based on Test1
            string path = "TestUpdate.conf";
            using (StreamWriter sw = new StreamWriter(path, append:true))
            {
                sw.Write(JsonConvert.SerializeObject(
                    ConfigManager
                    .GetCreateConfig<TestConfig>("Test1")));
                sw.Flush();
            }

            // Sleep to let the ConfigManager deal with the events
            Thread.Sleep(100);

            // Re-grab the config from the ConfigManager
            config = ConfigManager.GetCreateConfig<TestConfig>("TestUpdate");
            Assert.NotNull(config);
            Assert.AreEqual("1", config.Foo);
            Assert.AreEqual("2", config.Bar);
            Assert.AreEqual("3", config.Baz);

            using (StreamWriter sw = new StreamWriter(path, append: true))
            {
                sw.Write(" ");
                sw.Flush();
            }

            // Sleep to let the ConfigManager deal with the events
            Thread.Sleep(100);

            // Re-grab the config from the ConfigManager
            config = ConfigManager.GetCreateConfig<TestConfig>("TestUpdate");
            Assert.NotNull(config);
            Assert.AreEqual("1", config.Foo);
            Assert.AreEqual("2", config.Bar);
            Assert.AreEqual("3", config.Baz);

            // Delete the config file
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            // Sleep to let the ConfigManager deal with the events
            Thread.Sleep(100);

            // Re-grab the config from the ConfigManager
            config = ConfigManager.GetCreateConfig<TestConfig>("TestUpdate");
            Assert.NotNull(config);
            Assert.AreEqual("foo", config.Foo);
            Assert.AreEqual("bar", config.Bar);
            Assert.AreEqual("baz", config.Baz);
        }

        [TestFixtureTearDown]
        public static void TearDown()
        {
            // Delete the config file in case there was an error
            string path = "TestUpdate.conf";
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
