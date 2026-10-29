namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Says which platform exception a library's own exception means, so it is answered as that one.
/// </summary>
/// <remarks>
/// <para>
/// For a failure raised by a library the platform does not reference, whose meaning a package
/// that does reference it knows. A concurrent write refused by Entity Framework is a
/// <see cref="ConflictException"/>; only the data package knows Entity Framework, so it says so
/// here rather than the service's exception handler learning every library's types.
/// </para>
/// <para>
/// Consulted by the service's exception handler before it maps an exception to a response. The
/// first translator to answer wins; one that does not recognise the exception returns null.
/// </para>
/// </remarks>
public interface IExceptionTranslator
{
    /// <summary>The platform exception <paramref name="exception"/> means, or null.</summary>
    /// <param name="exception">The exception that reached the handler.</param>
    Exception? Translate(Exception exception);
}
