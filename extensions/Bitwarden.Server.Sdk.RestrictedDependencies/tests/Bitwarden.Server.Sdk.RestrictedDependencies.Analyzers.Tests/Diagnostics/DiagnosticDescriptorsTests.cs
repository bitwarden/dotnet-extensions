using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Diagnostics;

/// <summary>
/// <see cref="DiagnosticDescriptors.All"/> is the only list the family derives from: <c>Ids</c>
/// gates the suppression scanner and <c>MustRemainErrors</c> gates the severity validator. A
/// descriptor left out of it is still reported, but becomes silently suppressible by #pragma and
/// silently lowerable by .editorconfig.
/// </summary>
public class DiagnosticDescriptorsTests
{
    [Fact]
    public void All_ContainsEveryDeclaredDescriptor()
    {
        var declared = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => ((DiagnosticDescriptor)field.GetValue(null)!).Id);

        Assert.Equal(
            declared.OrderBy(id => id, StringComparer.Ordinal),
            DiagnosticDescriptors.All.Select(descriptor => descriptor.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void MustRemainErrors_ExemptsOnlyTheExpiryWarningAndTheObservationChannel()
    {
        Assert.Equal(
            new[] { DiagnosticDescriptors.ExceptionExpired.Id, DiagnosticDescriptors.Observation.Id }.OrderBy(id => id, StringComparer.Ordinal),
            DiagnosticDescriptors.All
                .Select(descriptor => descriptor.Id)
                .Except(DiagnosticDescriptors.MustRemainErrors.Select(descriptor => descriptor.Id))
                .OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void MustRemainErrors_AreAllErrorsByDefault()
    {
        Assert.All(
            DiagnosticDescriptors.MustRemainErrors,
            descriptor => Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity));
    }

    [Fact]
    public void MustRemainErrors_AreNotConfigurable()
    {
        Assert.All(
            DiagnosticDescriptors.MustRemainErrors,
            descriptor => Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, descriptor.CustomTags));
    }

    /// <summary>
    /// A seed list with the channel left off is not a tool, so the run stays enforcing and says so
    /// through BW0015 (see <c>CompilationCoordinatorTests</c>). What this pins is the channel
    /// itself: nothing reaches BW0017 until the compilation options turn it on.
    /// </summary>
    [Fact]
    public async Task Observation_IsOffByDefault_UntilAToolEnablesIt()
    {
        var diagnostics = await ToolHost.RunAsync(observe: true, enableObservations: false);

        Assert.Empty(diagnostics.Where(d => d.Id == DiagnosticDescriptors.Observation.Id));
    }

    [Fact]
    public void PublicIds_MatchTheDescriptors()
    {
        Assert.Equal(
            DiagnosticDescriptors.All.Select(descriptor => descriptor.Id).OrderBy(id => id, StringComparer.Ordinal),
            DiagnosticConstants.All.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void PublicMustRemainErrors_MatchTheDescriptors()
    {
        Assert.Equal(
            DiagnosticDescriptors.MustRemainErrors.Select(descriptor => descriptor.Id).OrderBy(id => id, StringComparer.Ordinal),
            DiagnosticConstants.MustRemainErrors.OrderBy(id => id, StringComparer.Ordinal));
    }
}
