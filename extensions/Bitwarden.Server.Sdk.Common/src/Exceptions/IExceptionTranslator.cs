namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Says which platform exception a library's own exception means, so it is answered as that one.
/// </summary>
/// <remarks>
/// <para>
/// For example, a concurrent write refused by Entity Framework is a <see cref="ConflictException"/>.
/// The package that knows the library translates its exceptions, so the exception handler doesn't
/// have to learn every library's types.
/// </para>
/// <para>
/// The first translator to answer wins. One that does not recognise the exception returns null.
/// </para>
/// </remarks>
public interface IExceptionTranslator
{
    /// <summary>
    /// The platform exception <paramref name="exception"/> means, or null.
    /// </summary>
    /// <param name="exception">The exception that reached the handler.</param>
    Exception? Translate(Exception exception);
}
