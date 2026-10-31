using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// A call's caller context, as the context token Identity signed for it says.
/// </summary>
/// <param name="Organization">The organization the call concerns.</param>
/// <param name="User">The user it is made on behalf of.</param>
/// <param name="Provider">The provider on whose authority it is made.</param>
/// <param name="Context">The <see cref="BitwardenHeaders.Context"/> object.</param>
/// <param name="Token">The context token itself, the subject of this service's own onward calls.</param>
/// <param name="Mode"><see cref="CallerContextToken.Delegated"/> or <see cref="CallerContextToken.Asserted"/>.</param>
/// <param name="Hops">
/// How many services the call passed through, its <c>act</c> chain's length: 1 for a call straight from the service that
/// asserted or first delegated the context.
/// </param>
internal sealed record VerifiedContext(
    Guid? Organization,
    Guid? User,
    Guid? Provider,
    CallerContextValue Context,
    string Token,
    string Mode,
    int Hops = 1);
