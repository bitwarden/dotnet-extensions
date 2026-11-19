namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Which failures set a message aside rather than leaving it to be tried again.
/// </summary>
internal static class OutboxFailures
{
    /// <summary>
    /// A failure sending again can never fix: one the broker reports as permanent (a message too
    /// large for it), a stored message this build can't read, or a handler that dead-lettered it.
    /// </summary>
    public static bool IsPermanent(Exception exception) =>
        exception is OutboxMessageSetAsideException || Microsoft.Extensions.DependencyInjection.SendFailures.IsPermanent(exception);
}
