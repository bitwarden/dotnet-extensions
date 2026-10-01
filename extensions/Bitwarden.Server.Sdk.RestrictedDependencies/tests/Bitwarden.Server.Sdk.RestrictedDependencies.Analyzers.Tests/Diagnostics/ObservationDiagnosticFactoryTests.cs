namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Diagnostics;

/// <summary>
/// The BW0017 rows a repository's baseline tool reads instead of hosting a second scanner: one
/// usage row per use, the declared member set of each sealed type, every exception, and each
/// type's owner. Driven through <see cref="ToolHost"/>, the way that tool drives the analyzer.
/// </summary>
public class ObservationDiagnosticFactoryTests(ObserveModeRunFixture run) : IClassFixture<ObserveModeRunFixture>
{
    [Fact]
    public void ObserveMode_EmitsOneRowPerUse_AndEnforcesNothing()
    {
        var diagnostics = run.Diagnostics;

        Assert.Empty(diagnostics.Where(d => d.Id != DiagnosticDescriptors.Observation.Id));

        var usages = ToolHost.Rows(diagnostics, ObservationConstants.UsageRow);
        Assert.Collection(usages,
            row =>
            {
                Assert.Equal(SampleProjectFixture.Type, row[ObservationConstants.Type]);
                Assert.Equal("injection", row[ObservationConstants.UsageKind]);
                Assert.Null(row[ObservationConstants.Member]);
                Assert.Equal(SampleProjectFixture.ConstructorSite, row[ObservationConstants.Site]);
                Assert.Equal("1", row[ObservationConstants.Count]);
                Assert.Equal("src/Api/Consumer.cs", row[ObservationConstants.File]);
                Assert.Equal("false", row[ObservationConstants.Excepted]);
            },
            row =>
            {
                Assert.Equal("member", row[ObservationConstants.UsageKind]);
                Assert.Equal(SampleProjectFixture.CanAccessPremium, row[ObservationConstants.Member]);
                Assert.Equal(SampleProjectFixture.RunSite, row[ObservationConstants.Site]);
            });
    }

    [Fact]
    public void ObserveMode_EmitsTheSealedMemberSetAndTheOwner()
    {
        var diagnostics = run.Diagnostics;

        Assert.Equal(
            SampleProjectFixture.DeclaredMembers.OrderBy(m => m, StringComparer.Ordinal),
            ToolHost.Rows(diagnostics, ObservationConstants.DeclaredMemberRow).Select(r => r[ObservationConstants.Member]));

        var row = Assert.Single(ToolHost.Rows(diagnostics, ObservationConstants.RestrictedTypeRow));
        Assert.Equal(SampleProjectFixture.Type, row[ObservationConstants.Type]);
        Assert.Equal("PM-1", row[ObservationConstants.Tracking]);
        Assert.Equal("unowned", row[ObservationConstants.Owner]);
    }

    [Fact]
    public async Task ObserveMode_ReportsAnExceptedUseSeparately_AndKeepsItOutOfTheBudget()
    {
        var excepted = SampleProjectFixture.Consumer.Replace(
            "public class Consumer",
            """
            [Bitwarden.Server.Sdk.RestrictedDependencies.RestrictedDependencyException(typeof(IUserService),
                Owner = "team-billing", Reason = "PM-12345", Expires = "2099-12-31")]
            public class Consumer
            """);

        var diagnostics = await ToolHost.RunAsync(observe: true, enableObservations: true, consumer: excepted);

        // A class-level exception covers injection, locator, concrete and escape uses inside the
        // class. A member use needs an exception on the member itself, so it stays in the budget.
        var usages = ToolHost.Rows(diagnostics, ObservationConstants.UsageRow);
        Assert.Equal("true", ToolHost.OfKind(usages, "injection")[ObservationConstants.Excepted]);
        Assert.Equal("false", ToolHost.OfKind(usages, "member")[ObservationConstants.Excepted]);

        var exception = Assert.Single(ToolHost.Rows(diagnostics, ObservationConstants.ExceptionRow));
        Assert.Equal(SampleProjectFixture.Type, exception[ObservationConstants.Type]);
        Assert.Equal("team-billing", exception[ObservationConstants.Owner]);
        Assert.Equal("PM-12345", exception[ObservationConstants.Reason]);
        Assert.Equal("2099-12-31", exception[ObservationConstants.Expires]);
        Assert.Equal("true", exception[ObservationConstants.Valid]);
        Assert.Equal("false", exception[ObservationConstants.Expired]);
    }

    /// <summary>
    /// A tracked-only member's row is marked, which is the only way the shrink-only comparison can
    /// tell a total that is meant to rise from one that is not.
    /// </summary>
    [Fact]
    public async Task ObserveMode_MarksATrackedOnlyMemberUse()
    {
        var consumer = SampleProjectFixture.ConsumerPreamble + """
                public Guid? Run(User user) => _userService.GetProperUserId(new Principal());
            }
            """;

        var diagnostics = await ToolHost.RunAsync(observe: true, enableObservations: true, consumer: consumer);

        var usages = ToolHost.Rows(diagnostics, ObservationConstants.UsageRow);
        Assert.Equal("true", ToolHost.OfKind(usages, "member")[ObservationConstants.Tracked]);
        Assert.Equal("false", ToolHost.OfKind(usages, "injection")[ObservationConstants.Tracked]);
    }
}
