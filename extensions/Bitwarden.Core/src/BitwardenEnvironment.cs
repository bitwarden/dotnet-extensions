using System.ComponentModel;

namespace Bitwarden.Core;

public sealed class BitwardenEnvironment
{
    private readonly Uri? _baseUri;
    private readonly Uri? _identityUri;
    private readonly Uri? _apiUri;

    public static BitwardenEnvironment CloudUS { get; } = BuiltIn(
        baseUri: new Uri("https://vault.bitwarden.com"),
        identityUri: new Uri("https://identity.bitwarden.com"),
        apiUri: new Uri("https://api.bitwarden.com"));

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static BitwardenEnvironment DevelopmentEnvironment { get; } = Custom( 
        new Uri("http://localhost:33656"),
        new Uri("http://localhost:4000"));

    private static BitwardenEnvironment BuiltIn(Uri baseUri, Uri identityUri, Uri apiUri)
    {
        return new BitwardenEnvironment(false, baseUri, identityUri, apiUri);
    }

    public static BitwardenEnvironment Custom(Uri identityUri, Uri apiUri)
        => new(isCustom: true, baseUri: null, identityUri, apiUri);

    internal BitwardenEnvironment(bool isCustom, Uri? baseUri, Uri? identityUri = null, Uri? apiUri = null)
    {
        IsCustom = isCustom;
        // TODO: Do some validation
        _baseUri = baseUri;
        _identityUri = identityUri;
        _apiUri = apiUri;
    }

    public Uri IdentityUri
    {
        get
        {
            Uri CreateUri()
            {
                // TODO: Should have validation
                return new Uri(_baseUri!, "/identity");
            }

            return _identityUri ?? CreateUri();
        }
    }

    public Uri ApiUri
    {
        get
        {
            Uri CreateUri()
            {
                // TODO: Should have validation
                return new Uri(_baseUri!, "/api");
            }

            return _apiUri ?? CreateUri();
        }
    }
    public bool IsCustom { get; }
}
