using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Bitwarden.Extensions.Hosting.Licensing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit.Abstractions;

namespace Bitwarden.Extensions.Hosting.Tests.Licensing;

public class DefaultLicensingServiceTests
{
    private readonly ITestOutputHelper _outputHelper;
    private readonly FakeTimeProvider _fakeTimeProvider;

    public DefaultLicensingServiceTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _fakeTimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task RoundTrip_Works()
    {
        var cloudSut = CreateSut(options =>
        {
            options.SigningCertificate = new X509Certificate2(TestData.TestCertificateWithPrivateKey, TestData.PfxPassword);
        });

        var license = cloudSut.CreateLicense(
        [
            new Claim("myClaim", "hello world!"),
        ], TimeSpan.FromMinutes(5));

        _fakeTimeProvider.Advance(TimeSpan.FromSeconds(1));

        var selfHostSut = CreateSut(options =>
        {
            options.SigningCertificate = new X509Certificate2(TestData.TestCertificateCerFormat);
        });

        var claims = await selfHostSut.VerifyLicenseAsync(license);

        Assert.NotEmpty(claims);
        Assert.Contains(claims, c => c.Type == "myClaim" && c.Value == "hello world!");
    }

    [Fact]
    public void CreateLicense_WithSelfHost_Fails()
    {
        var selfHostSut = CreateSut(options =>
        {
            options.SigningCertificate = new X509Certificate2(TestData.TestCertificateCerFormat);
        });

        var invalidOperation = Assert.Throws<InvalidOperationException>(
            () => selfHostSut.CreateLicense(Enumerable.Empty<Claim>(), TimeSpan.FromMinutes(5))
        );

        Assert.Equal(
            "Self-hosted services can not create a license, please check 'IsCloud' before calling this method.",
            invalidOperation.Message
        );
    }

    [Fact]
    public async Task RoundTrip_Expired_Fails()
    {

        var cloudSut = CreateSut(options =>
        {
            options.SigningCertificate = new X509Certificate2(TestData.TestCertificateWithPrivateKey, TestData.PfxPassword);
        });

        var license = cloudSut.CreateLicense(Enumerable.Empty<Claim>(), TimeSpan.FromMilliseconds(10));

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var selfHostSut = CreateSut(options =>
        {
            options.SigningCertificate = new X509Certificate2(TestData.TestCertificateCerFormat);
        });

        var validationException = Assert.ThrowsAsync<Exception>(
            async () => await selfHostSut.VerifyLicenseAsync(license)
        );
    }

    // TODO: Test license signed with a different key

    // TODO: Test verifying license with a different key

    private DefaultLicensingService CreateSut(Action<LicensingOptions> configureOptions)
    {
        var options = new LicensingOptions();
        configureOptions(options);
        return new DefaultLicensingService(Options.Create(options), _fakeTimeProvider);
    }
}
