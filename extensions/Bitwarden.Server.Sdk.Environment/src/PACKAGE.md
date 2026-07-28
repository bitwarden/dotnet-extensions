# Bitwarden.Server.Sdk.Environment

Provides runtime environment information for Bitwarden server services, including version, Git hash,
and self-host details.

## Getting Started

Register the environment services in your DI container:

```csharp
services.AddBitwardenEnvironment();
```

If your service can run in a self-hosted configuration, configure `SelfHostDetails` accordingly:

```csharp
using Bitwarden.Server.Sdk.Environment.Setup;

// For a self-hosted instance:
services.Configure<SelfHostDetails>(details => details.MakeSelfHost("lite"));

// For a cloud instance (this is the default):
services.Configure<SelfHostDetails>(details => details.MakeCloud());
```

## Usage

Inject `IBitwardenEnvironment` wherever you need environment information:

```csharp
public class MyService(IBitwardenEnvironment environment)
{
    public void LogInfo()
    {
        Console.WriteLine($"Version:     {environment.Version}");
        Console.WriteLine($"Git hash:    {environment.GitHash}");
        Console.WriteLine($"Self-hosted: {environment.SelfHosted}");
        Console.WriteLine($"Flavor:      {environment.SelfHostFlavor}");
    }
}
```

## Version Resolution

The version and Git hash are read from the `AssemblyInformationalVersionAttribute` of the
application's entry assembly. The attribute value is expected to follow the format
`{version}+{gitHash}` (e.g., `1.2.3+af18b2952b`), which is produced automatically by the .NET SDK
when source control metadata is embedded in the build.

If the attribute is missing or cannot be parsed, `Version` returns an empty string and `GitHash`
returns `null`, and a warning is logged.
