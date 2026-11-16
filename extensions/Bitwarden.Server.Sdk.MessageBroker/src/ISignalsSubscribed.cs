namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// A subscriber that says when its subscription is in place, so the readiness check can count a
/// hosted consumer as receiving on a positive signal rather than on time passing. Implemented by
/// the built-in backends; a subscriber that doesn't implement it counts as receiving once its
/// consumer starts.
/// </summary>
internal interface ISignalsSubscribed<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    /// <summary>
    /// As <see cref="ISubscriber{TPayload, TCeiling}.SubscribeAsync"/>, calling
    /// <paramref name="onSubscribed"/> once the subscription is in place: before the first message,
    /// and even if none ever comes.
    /// </summary>
    IAsyncEnumerable<Envelope<TPayload, TCeiling>> SubscribeAsync(Action onSubscribed, CancellationToken cancellationToken);
}
