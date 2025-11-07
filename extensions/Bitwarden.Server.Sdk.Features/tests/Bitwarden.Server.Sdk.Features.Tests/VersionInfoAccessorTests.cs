using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Bitwarden.Server.Sdk.Features.Tests;

public class VersionInfoAccessorTests
{
    private readonly IHostEnvironment _hostEnvironment;

    private readonly VersionInfoAccessor _sut;

    public VersionInfoAccessorTests()
    {
        _hostEnvironment = Substitute.For<IHostEnvironment>();

        _sut = new VersionInfoAccessor(_hostEnvironment, NullLogger<VersionInfoAccessor>.Instance);
    }

    [Fact]
    public void InvalidApplicationName_ReturnsNull()
    {
        _hostEnvironment.ApplicationName.Returns("Test");

        var versionInfo = _sut.Get();
        Assert.Null(versionInfo);
    }

    [Fact]
    public void ApplicationNameIsAssemblyName_ReturnsVersion()
    {
        _hostEnvironment.ApplicationName.Returns(typeof(VersionInfoAccessorTests).Assembly.FullName);

        var versionInfo = _sut.Get();
        Assert.NotNull(versionInfo);
    }

    [Fact]
    public void Get_CalledMultipleTimes_ReturnsSameInstance()
    {
        _hostEnvironment.ApplicationName.Returns(typeof(VersionInfoAccessorTests).Assembly.FullName);

        var first = _sut.Get();
        var second = _sut.Get();

        Assert.Same(first, second);
    }
}
