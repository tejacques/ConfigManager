ConfigManager
=============

What is it?
-----------

A json configuration file manager for C# / .NET / Mono

How can I get it?
-----------------

ConfigManager is available as a NuGet package: https://www.nuget.org/packages/ConfigManager/

```
PM> Install-Package ConfigManager
```

Why was it made?
----------------

I wanted a strongly typed configuration system that would automatically update it's values in long running processes, so if the configuration file changed, the changes would propagate automatically without restarting the binary or interupting service.

Example Usage
-------------

ConfigManager is a global statically available static class that is typically used as follows:

ProjectDirectory/Config/configuredNames.conf
```json
[
  "Billy",
  "John",
  "Harold"
]
```

```csharp
public void Example()
{
    List<string> configuredNames = ConfigManager.GetCreateConfig<List<string>>("configuredNames");
    foreach(string name in configuredNames)
    {
        Console.WriteLine(name);
    }
}
```

The output will be:
```
  Billy
  John
  Harold
```
