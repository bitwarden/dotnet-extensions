namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// One call a <see cref="ProductClient"/> is making: the token it is made with, and the answer once it came.
/// </summary>
/// <remarks>
/// Held for the call's own asynchronous flow only, as <see cref="OutgoingCall"/> holds a statement, and used up by the
/// first request sent inside it (<see cref="Take"/>): a token is never carried on to a second request nobody meant it
/// for. Two calls made at once each have their own.
/// </remarks>
internal sealed class ProductCall
{
    private static readonly AsyncLocal<ProductCall?> Ambient = new();

    private bool _taken;

    private ProductCall(string bearer)
    {
        Bearer = bearer;
    }

    /// <summary>
    /// The token the call is made with. A credential: never logged.
    /// </summary>
    public string Bearer { get; }

    /// <summary>
    /// The status, once the service answered.
    /// </summary>
    public int? Status { get; private set; }

    /// <summary>
    /// The body as text, once the service answered.
    /// </summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>
    /// Whether a second request was sent inside the call, and refused: a fault of the client, never an answer.
    /// </summary>
    public bool Overused { get; private set; }

    /// <summary>
    /// Whether the service answered.
    /// </summary>
    public bool Answered => Status is not null && !Overused;

    /// <summary>
    /// The status the service answered with.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing recorded an answer: the client was registered without <see cref="ProductCallHandler"/>.
    /// </exception>
    public int AnsweredStatus => Status ?? throw new InvalidOperationException(
        "A product client's call had no answer recorded: register the client with AddProductClient.");

    /// <summary>
    /// Starts a call made with <paramref name="bearer"/>, for the current asynchronous flow.
    /// </summary>
    /// <param name="bearer">The token.</param>
    /// <returns>The call.</returns>
    public static ProductCall Begin(string bearer)
    {
        var call = new ProductCall(bearer);
        Ambient.Value = call;
        return call;
    }

    /// <summary>
    /// The call the current request is part of, used up: null when it is part of none.
    /// </summary>
    /// <returns>The call, or null.</returns>
    /// <exception cref="InvalidOperationException">A request was already sent inside the call.</exception>
    public static ProductCall? Take()
    {
        if (Ambient.Value is not { } call)
        {
            return null;
        }

        if (call._taken)
        {
            call.Overused = true;
            throw new InvalidOperationException(
                "A product client's call sent a second request. Each call sends one, with the token it was given.");
        }

        call._taken = true;
        return call;
    }

    /// <summary>
    /// Records the service's answer.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="body">The body as text.</param>
    public void Record(int status, string body)
    {
        Status = status;
        Body = body;
    }

    /// <summary>
    /// Ends the call for the current asynchronous flow.
    /// </summary>
    public void End()
    {
        if (ReferenceEquals(Ambient.Value, this))
        {
            Ambient.Value = null;
        }
    }
}
