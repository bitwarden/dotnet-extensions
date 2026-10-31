using System.Security.Claims;
using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// What proves whom a request is for, for a service's own calls to exchange their context token from: the session a
/// product service validated, when it names a party, or the context token a domain service's call carried.
/// </summary>
public sealed class CallerProofTests
{
    private const string Session = "a-session-token";

    private static DefaultHttpContext Request(string? authorization = $"Bearer {Session}", bool authenticated = true)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], authenticated ? "Bearer" : null)),
        };

        if (authorization is not null)
        {
            http.Request.Headers.Authorization = authorization;
        }

        return http;
    }

    private static CallerProof Proof(HttpContext? http, bool productService, CallerCredential? credential = CallerCredential.User) =>
        new(new Accessor(http), new StatedContext(credential), productService);

    // --- A product service: the session it validated ----------------------------------------------------------------

    [Theory]
    [InlineData(CallerCredential.User)]
    [InlineData(CallerCredential.Organization)]
    [InlineData(CallerCredential.Machine)]
    public void ProductService_ASessionNamingAParty_IsTheBearerToken(CallerCredential credential)
    {
        Assert.Equal(Session, Proof(Request(), productService: true, credential).SubjectToken);
    }

    // The scheme is matched without regard to case, as HTTP's are, and the token read without surrounding space.
    [Fact]
    public void ProductService_ALowerCaseSchemeAndSpacing_IsTheBearerToken()
    {
        Assert.Equal(Session, Proof(Request($"bearer  {Session} "), productService: true).SubjectToken);
    }

    // A Send's credential names no party, so it proves nobody.
    [Fact]
    public void ProductService_ACredentialNamingNoParty_IsNone()
    {
        Assert.Null(Proof(Request(), productService: true, credential: null).SubjectToken);
    }

    [Fact]
    public void ProductService_Unauthenticated_IsNone()
    {
        Assert.Null(Proof(Request(authenticated: false), productService: true).SubjectToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    public void ProductService_NoBearerToken_IsNone(string? authorization)
    {
        Assert.Null(Proof(Request(authorization), productService: true).SubjectToken);
    }

    // A product service never forwards a context token a call carried: the session is its only proof.
    [Fact]
    public void ProductService_AKeptContextToken_IsNotItsProof()
    {
        var http = Request(authorization: null);
        CallerContextHeaders.Keep(http, Verified("a-context-token"));

        Assert.Null(Proof(http, productService: true).SubjectToken);
    }

    // --- A domain service: the context token its call carried, once verified -----------------------------------------

    [Fact]
    public void DomainService_AVerifiedContextToken_IsThatToken()
    {
        var http = Request();
        CallerContextHeaders.Keep(http, Verified("a-context-token"));

        Assert.Equal("a-context-token", Proof(http, productService: false).SubjectToken);
    }

    // Its caller's own bearer token is a service's credential, not proof of whom the call is for.
    [Fact]
    public void DomainService_NoVerifiedContextToken_IsNone()
    {
        Assert.Null(Proof(Request(), productService: false).SubjectToken);
    }

    // Outside a request (a message consumer, a job), nothing proves whom a call is for.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutsideARequest_IsNone(bool productService)
    {
        var proof = new CallerProof(new HttpContextAccessor(), new Context(new HttpContextAccessor(), productService), productService);

        Assert.Null(proof.SubjectToken);
    }

    private static VerifiedContext Verified(string token) =>
        new(null, Guid.NewGuid(), null, new CallerContextValue(), token, CallerContextToken.Delegated);

    /// <summary>
    /// The request being handled, held plainly rather than flowed with the async context.
    /// </summary>
    private sealed class Accessor(HttpContext? http) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = http;
    }

    /// <summary>
    /// A request's context, with the kind of credential the session was.
    /// </summary>
    private sealed class StatedContext(CallerCredential? credential) : IContext
    {
        public Guid? OrganizationId => null;

        public Guid? UserId => null;

        public Guid? ProviderId => null;

        public CallerCredential? Credential => credential;
    }
}
