namespace ConfigManager.Tests
{
    using ConfigClasses;
    using NUnit.Framework;
    using ServiceStack.Text;
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
        [TestFixtureSetUp]
        public void SetUp()
        {
            loops = 10000;
        }

        [Test]
        public static void TestConfigManager()
        {
            TestConfig config = ConfigManager.GetCreateConfig<TestConfig>("TestDefault");
            Assert.NotNull(config);
        }

        [Test]
        public static void TestConfigParse()
        {
            TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                ("Test1");
            Assert.NotNull(config);
            Assert.AreEqual(config.Foo, "1");
            Assert.AreEqual(config.Bar, "2");
            Assert.AreEqual(config.Baz, "3");
        }

        [Test]
        public static void TestConfigParseSerial()
        {
            for (int i = 0; i < loops; i++)
            {
                TestConfig config = ConfigManager.GetCreateConfig<TestConfig>
                    ("Test2");
                Assert.NotNull(config);
                Assert.AreEqual(config.Foo, "a");
                Assert.AreEqual(config.Bar, "b");
                Assert.AreEqual(config.Baz, "c");
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
                Assert.AreEqual(config.Foo, "!");
                Assert.AreEqual(config.Bar, "@");
                Assert.AreEqual(config.Baz, "#");
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
                Assert.AreEqual(config.Foo, "foo");
                Assert.AreEqual(config.Bar, "bar");
                Assert.AreEqual(config.Baz, "baz");
            });
        }

        [Test]
        public static void TestConfigEvents()
        {
            // Test getting the config when the config file doesn't exist
            TestConfig config = ConfigManager.GetCreateConfig<TestConfig>("TestUpdate");
            Assert.NotNull(config);
            Assert.AreEqual(config.Foo, "foo");
            Assert.AreEqual(config.Bar, "bar");
            Assert.AreEqual(config.Baz, "baz");

            // Create a new config file based on Test1
            string path = "TestUpdate.conf";
            using (StreamWriter sw = new StreamWriter(path, append:true))
            {
                sw.Write(ConfigManager.GetCreateConfig<TestConfig>("Test1").ToJson());
                sw.Flush();
            }

            // Sleep to let the ConfigManager deal with the events
            Thread.Sleep(100);

            // Re-grab the config from the ConfigManager
            config = ConfigManager.GetCreateConfig<TestConfig>("TestUpdate");
            Assert.NotNull(config);
            Assert.AreEqual(config.Foo, "1");
            Assert.AreEqual(config.Bar, "2");
            Assert.AreEqual(config.Baz, "3");

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
            Assert.AreEqual(config.Foo, "1");
            Assert.AreEqual(config.Bar, "2");
            Assert.AreEqual(config.Baz, "3");

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
            Assert.AreEqual(config.Foo, "foo");
            Assert.AreEqual(config.Bar, "bar");
            Assert.AreEqual(config.Baz, "baz");
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
