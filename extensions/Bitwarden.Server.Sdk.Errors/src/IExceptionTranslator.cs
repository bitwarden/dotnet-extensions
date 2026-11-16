namespace Bitwarden.Server.Sdk.Errors;

/// <summary>
/// Says which standard failure a library's own exception means, so a host answers it as that one.
/// </summary>
/// <remarks>
/// <para>
/// For example, a concurrent write refused by Entity Framework is a <see cref="ConflictException"/>.
/// The package that knows the library translates its exceptions, so a host's exception handler
/// doesn't have to learn every library's types.
/// </para>
/// <para>
/// A host asks each registered translator in turn, and the first to answer wins. One that does not recognise the exception returns null.
/// </para>
/// </remarks>
public interface IExceptionTranslator
{
    /// <summary>
    /// The standard failure <paramref name="exception"/> means, or null.
    /// </summary>
    /// <param name="exception">The exception that reached the handler.</param>
    Exception? Translate(Exception exception);
}
