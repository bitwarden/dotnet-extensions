using Bitwarden.Extensions.Hosting.Licensing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bitwarden.Extensions.Hosting.Tests.Licensing;

public class PostConfigureLicensingOptionsTests
{
    private readonly InternalLicensingOptions _internalLicensingOptions;
    private readonly IHostEnvironment _hostEnvironment;

    private readonly PostConfigureLicensingOptions _sut;

    public PostConfigureLicensingOptionsTests()
    {
        _internalLicensingOptions = new InternalLicensingOptions();
        _hostEnvironment = Substitute.For<IHostEnvironment>();

        _sut = new PostConfigureLicensingOptions(
            Options.Create(_internalLicensingOptions),
            NullLogger<PostConfigureLicensingOptions>.Instance,
            _hostEnvironment
        );
    }

    [Fact]
    public void PostConfigure_Works()
    {
        _hostEnvironment
            .ApplicationName
            .Returns("Test");

        var options = new LicensingOptions();

        _sut.PostConfigure(Options.DefaultName, options);

        Assert.NotNull(options.SigningCertificate);
    }
}
