namespace Bitwarden.Server.Sdk.WriteSpans;

/// <summary>
/// Admits that an operation's writes span services without handling a failure part way yet: a known defect, named, so
/// a host that refuses undeclared spans can let it through while it waits to be fixed.
/// </summary>
/// <remarks>
/// <para>
/// Never a shape. The host's guard (<see cref="IServiceWritesGuard"/>) is told with this declaration on every span,
/// and is expected to report it as loudly as an undeclared one, so it stays visible until the code handles the
/// failure and declares <see cref="SpansServicesAttribute"/> instead.
/// </para>
/// <para>
/// The reason says what a failure part way leaves behind and where the defect is recorded, for example an issue
/// tracker's ticket.
/// </para>
/// </remarks>
/// <param name="reason">What a failure part way leaves behind, and where the defect is recorded.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SpansServicesUnhandledAttribute(string reason)
    : SpanDeclarationAttribute(reason, "Say what a failure part way leaves behind, and where the defect is recorded.");
