namespace ConfigManager.Tests.ConfigClasses
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class TestConfig
    {
        public TestConfig()
        {
            Foo = "foo";
            Bar = "bar";
            Baz = "baz";
        }
        public string Foo { get; set; }
        public string Bar { get; set; }
        public string Baz { get; set; }
    }
}
