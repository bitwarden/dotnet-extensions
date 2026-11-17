namespace Bitwarden.Server.Sdk.Sagas.Tests;

public sealed class ReadingRoutesTests
{
    [Theory]
    [InlineData("/api/v1/provider-clients/presented/actions/check-plan")]
    [InlineData("/api/v1/organizations/presented/actions/find-by-identifier")]
    [InlineData("/api/v1/accounts/presented/actions/holds")]
    [InlineData("/api/v1/archived-accounts/presented/actions/look-up-copy")]
    [InlineData("/api/v1/plans/presented/actions/validate")]
    [InlineData("/api/v1/domains/presented/actions/resolve-owner")]
    [InlineData("/api/v1/things/presented/actions/get-many")]
    [InlineData("/api/v1/things/presented/actions/filter")]
    [InlineData("/api/v1/things/presented/actions/check")]
    [InlineData("/api/v1/invoice-previews")]
    // A trailing slash reads as it does in an address.
    [InlineData("/api/v1/things/presented/actions/check/")]
    // The verb names the read, so a write named with a reading verb counts as a read.
    [InlineData("/api/v1/payments/1/actions/resolve-and-refund")]
    public void AReadingRoute_Reads(string route) => Assert.True(ReadingRoutes.Reads(route));

    [Theory]
    [InlineData("/api/v1/memberships")]
    [InlineData("/api/v1/domains/1/actions/verify")]
    [InlineData("/api/v1/memberships/1/actions/confirm")]
    [InlineData("/api/v1/checks")]
    [InlineData("/api/v1/things/presented/actions/checkout")]
    [InlineData("/api/v1/things/presented/actions/check/extra")]
    // A line break after a reading route is not the end of the route.
    [InlineData("/api/v1/things/presented/actions/check\n")]
    [InlineData("/api/v1/invoice-previews\n")]
    [InlineData(null)]
    public void AnyOtherRoute_Writes(string? route) => Assert.False(ReadingRoutes.Reads(route));

    [Theory]
    [InlineData("http://billing/api/v1/invoice-previews?x=1", true)]
    [InlineData("api/v1/accounts/presented/actions/holds", true)]
    [InlineData("http://organization/api/v1/memberships?validateOnly=true", true)]
    [InlineData("http://organization/api/v1/memberships?a=1&validateOnly=TRUE", true)]
    [InlineData("http://organization/api/v1/memberships?validateOnly=false", false)]
    [InlineData("http://organization/api/v1/memberships", false)]
    [InlineData("http://things/api/v1/things/presented/actions/check/", true)]
    [InlineData("http://things/api/v1/things/presented/actions/check/?x=1", true)]
    // Every validateOnly must be true: a callee that binds the first value would write on the first of these.
    [InlineData("http://organization/api/v1/memberships?validateOnly=false&validateOnly=true", false)]
    [InlineData("http://organization/api/v1/memberships?validateOnly=true&validateOnly=false", false)]
    [InlineData("http://organization/api/v1/memberships?validateOnly=true&validateOnly=", false)]
    [InlineData("http://organization/api/v1/memberships?validateOnly&validateOnly=true", false)]
    [InlineData("http://organization/api/v1/memberships?validateOnly=true&validateOnly=true", true)]
    // A fragment is not part of the address sent, whether the address is absolute or relative.
    [InlineData("http://billing/api/v1/invoice-previews#top", true)]
    [InlineData("api/v1/invoice-previews#top", true)]
    [InlineData("api/v1/memberships#?validateOnly=true", false)]
    public void AnAddress_ReadsByItsPathOrValidateOnly(string address, bool reads) =>
        Assert.Equal(reads, ReadingRoutes.Reads(new Uri(address, UriKind.RelativeOrAbsolute)));
}
