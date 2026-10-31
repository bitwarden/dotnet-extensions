using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Shapes the service's OpenAPI document: its title, its servers, and the credentials a caller
/// supplies.
/// </summary>
internal static class ApiDocumentation
{
    /// <summary>
    /// The specification version the document is written and served as.
    /// </summary>
    /// <remarks>
    /// .NET defaults to OpenAPI 3.1, where nullability is expressed as a type union
    /// (<c>"type": ["null", "string"]</c>) because 3.1 aligns with JSON Schema 2020-12, which
    /// dropped the <c>nullable</c> keyword. The standards say a nullable field MUST be marked
    /// <c>nullable</c>, which is 3.0 vocabulary, and 3.0 is the version every generator
    /// handles.
    /// </remarks>
    internal const OpenApiSpecVersion SpecVersion = OpenApiSpecVersion.OpenApi3_0;

    /// <summary>
    /// The service's own credential, obtained from Identity via client credentials.
    /// </summary>
    private const string ServiceScheme = "service";

    /// <summary>
    /// The organization a request concerns, sent as a header.
    /// </summary>
    private const string OrganizationScheme = "organization";

    /// <summary>
    /// The user a request is made on behalf of, sent as a header.
    /// </summary>
    private const string UserScheme = "user";

    /// <summary>
    /// The provider on whose authority a request is made, sent as a header.
    /// </summary>
    private const string ProviderScheme = "provider";

    /// <summary>
    /// Where the originating request came from and how it was made, sent as one JSON header.
    /// </summary>
    private const string ContextScheme = "context";

    /// <summary>
    /// A person's session token, which a product service requires.
    /// </summary>
    private const string SessionScheme = "session";

    /// <param name="options">The document's options.</param>
    /// <param name="serviceName">Used as the document title.</param>
    /// <param name="tokenUrl">
    /// Identity's token endpoint, for the client-credentials flow. When null the OAuth2 scheme is
    /// omitted rather than published pointing nowhere, so the Authorize dialog offers only the
    /// credentials that actually work.
    /// </param>
    /// <param name="isProductService">
    /// Whether the service is a product service, which people call with their own session. Such a
    /// service refuses the caller-context headers (<see cref="BitwardenHeaders.All"/>), so the
    /// document offers the session alone.
    /// </param>
    public static void Configure(OpenApiOptions options, string serviceName, Uri? tokenUrl, bool isProductService = false)
    {
        options.OpenApiVersion = SpecVersion;

        // Names the document schemas after the domain rather than after the platform's generics.
        options.CreateSchemaReferenceId = SchemaNames.For;

        // The document must describe the enveloped wire shape, not the flat record.
        // Nullability decides what is required, before anything restructures the schema.
        options.AddSchemaTransformer<NumberSchemaTransformer>();
        options.AddSchemaTransformer<DecimalSchemaTransformer>();
        options.AddSchemaTransformer<EnumSchemaTransformer>();
        options.AddSchemaTransformer<DateSchemaTransformer>();
        options.AddSchemaTransformer<RequiredByNullabilitySchemaTransformer>();
        options.AddSchemaTransformer<ReadOnlySchemaTransformer>();
        options.AddSchemaTransformer<DefaultStringLengthSchemaTransformer>();
        options.AddSchemaTransformer<Features.FeatureFlagsSchemaTransformer>();
        options.AddSchemaTransformer<ResourceSchemaTransformer>();
        options.AddSchemaTransformer<ResourceDocumentSchemaTransformer>();
        options.AddDocumentTransformer<ResourceEnvelopeDocumentTransformer>();
        options.AddOperationTransformer<StandardResponsesOperationTransformer>();
        options.AddOperationTransformer<BulkAtomicityOperationTransformer>();
        options.AddDocumentTransformer<StandardErrorResponseDocumentTransformer>();
        options.AddDocumentTransformer<PagingDocumentTransformer>();
        options.AddDocumentTransformer<FieldsDocumentTransformer>();
        options.AddDocumentTransformer<FilteringDocumentTransformer>();
        options.AddDocumentTransformer<SortingDocumentTransformer>();
        options.AddDocumentTransformer<PageMetaDocumentTransformer>();
        options.AddDocumentTransformer<RequestBodyDescriptionDocumentTransformer>();

        // Before the ordering, which sorts by the published name rather than the type name.
        options.AddDocumentTransformer<TagDocumentTransformer>();

        // Last, so it orders everything the transformers above added.
        options.AddDocumentTransformer<AlphabeticalDocumentTransformer>();

        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = serviceName;

            // An empty servers list makes a client resolve requests against the origin it fetched
            // the document from. With entries present, Swagger UI offers a "Servers" picker and the
            // generated clients bake in whichever host generated the document, neither of which we
            // want while there is exactly one place a service is reachable.
            document.Servers?.Clear();

            document.Components ??= new OpenApiComponents();
            var schemes = new Dictionary<string, IOpenApiSecurityScheme>();

            if (tokenUrl is not null)
            {
                schemes[ServiceScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Description =
                        "The calling service's own credential. Client id is `internal.{service}`; " +
                        "the secret is the shared internal identity key.",
                    Flows = new OpenApiOAuthFlows
                    {
                        ClientCredentials = new OpenApiOAuthFlow { TokenUrl = tokenUrl }
                    }
                };
            }

            // Not offered where they are refused. A product service takes whom a request concerns
            // from the session token, and a document advertising the headers would
            // invite the one request that service answers 400.
            if (isProductService)
            {
                // Always published, whatever is known about Identity's address: every request to
                // such a service carries a person's session, and a client generated from this
                // document decides from it whether to attach one.
                schemes[SessionScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description =
                        "A person's session token, from Identity's token endpoint. The person, the " +
                        "organization and the provider a request concerns are the ones it names."
                };
            }
            else
            {
                schemes[OrganizationScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = BitwardenHeaders.Organization,
                    Description =
                        "The organization this request concerns. Leave empty for data that belongs to " +
                        "no organization. Believed from a service's own credential only."
                };

                schemes[UserScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = BitwardenHeaders.User,
                    Description =
                        "The user this request is made on behalf of. Leave empty for background work " +
                        "with no user. Believed from a service's own credential only."
                };

                schemes[ProviderScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = BitwardenHeaders.Provider,
                    Description =
                        "The provider on whose authority this request is made. Leave empty unless the " +
                        "operation takes a provider; refused where it doesn't. Believed from a service's " +
                        "own credential only."
                };

                schemes[ContextScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = BitwardenHeaders.Context,
                    Description =
                        "Where the originating request came from and how it was made: a closed JSON " +
                        "object, version 1, " +
                        "`{\"v\":1,\"clientIp\":\"…\",\"deviceType\":\"…\",\"deviceIdentifier\":\"…\",\"credential\":\"USER|ORGANIZATION|MACHINE|SYSTEM\",\"recordedUser\":\"…\",\"organizations\":[\"…\"]}`, " +
                        "every member but `v` optional and `recordedUser` only with `SYSTEM`. An unknown " +
                        "or repeated member, or more than 16384 bytes, is refused 400. Audit-only, but " +
                        "`organizations`, read for feature flags alone: nothing is authorized on it. " +
                        "Believed from a service's own credential only."
                };
            }

            document.Components.SecuritySchemes = schemes;

            // Declared document-wide so every operation inherits them, and so Swagger UI sends the
            // values entered once via Authorize on every request.
            var requirement = new OpenApiSecurityRequirement();
            foreach (var name in schemes.Keys)
            {
                requirement[new OpenApiSecuritySchemeReference(name, document)] = [];
            }

            document.Security = [requirement];

            return Task.CompletedTask;
        });
    }
}
