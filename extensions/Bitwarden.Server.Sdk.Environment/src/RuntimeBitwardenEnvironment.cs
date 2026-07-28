using Bitwarden.Server.Sdk.Environment.Setup;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Environment;

internal sealed class RuntimeBitwardenEnvironment : IBitwardenEnvironment
{
    public RuntimeBitwardenEnvironment(IVersionInfoAccessor versionInfoAccessor, IOptions<SelfHostDetails> selfHostDetails)
    {
        ArgumentNullException.ThrowIfNull(versionInfoAccessor);
        ArgumentNullException.ThrowIfNull(selfHostDetails);

        var version = versionInfoAccessor.Get();
        var details = selfHostDetails.Value;

        Version = version?.Version.ToString() ?? string.Empty;
        GitHash = version?.GitHash;
        SelfHosted = details.SelfHosted;
        SelfHostFlavor = details.SelfHostFlavor;
    }

    public string Version { get; }
    public string? GitHash { get; }
    public bool SelfHosted { get; }
    public string? SelfHostFlavor { get; }
}
