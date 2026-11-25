using System.Reflection;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Idempotency;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The operations a service's contract marks as honouring an <c>Idempotency-Key</c>
/// (<see cref="IdempotencyKeys.Extension"/>): the only ones a client sends a key to.
/// </summary>
internal sealed class KeyedOperations
{
    private readonly IReadOnlyList<(string Method, string[] Segments)> operations;

    private KeyedOperations(IReadOnlyList<(string Method, string[] Segments)> operations) => this.operations = operations;

    /// <summary>
    /// None: a client whose assembly embeds no contract sends no keys.
    /// </summary>
    public static KeyedOperations None { get; } = new([]);

    /// <summary>
    /// How many operations the contract marks.
    /// </summary>
    public int Count => operations.Count;

    /// <summary>
    /// The marked operations in a contract.
    /// </summary>
    /// <param name="contract">The service's OpenAPI document.</param>
    public static KeyedOperations FromContract(Stream contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var found = new List<(string, string[])>();
        foreach (var (path, item) in JsonNode.Parse(contract)?["paths"]?.AsObject() ?? [])
        {
            foreach (var (method, operation) in item?.AsObject() ?? [])
            {
                if (operation?[IdempotencyKeys.Extension] is JsonValue value && value.TryGetValue<bool>(out var marked) && marked)
                {
                    found.Add((method.ToUpperInvariant(), Segments(path)));
                }
            }
        }

        return new KeyedOperations(found);
    }

    /// <summary>
    /// The marked operations in the contract a client assembly embeds as <c>openapi.json</c>.
    /// </summary>
    /// <param name="client">The client package's assembly.</param>
    /// <returns>The operations; none when the assembly embeds no contract.</returns>
    public static KeyedOperations FromAssembly(Assembly client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var contract = client.GetManifestResourceStream("openapi.json");

        return contract is null ? None : FromContract(contract);
    }

    /// <summary>
    /// Whether a request goes to a marked operation: the same method, and an address whose path ends with the
    /// operation's path, a <c>{parameter}</c> standing for any one segment. The end, because a callee may be reached
    /// under a path prefix its contract doesn't name.
    /// </summary>
    /// <param name="method">The request's method.</param>
    /// <param name="address">The request's address.</param>
    public bool Accepts(HttpMethod method, Uri? address)
    {
        if (operations.Count == 0 || address is null)
        {
            return false;
        }

        var path = Segments(address.IsAbsoluteUri ? address.AbsolutePath : address.OriginalString.Split('?')[0]);

        return operations.Any(operation =>
            string.Equals(operation.Method, method.Method, StringComparison.OrdinalIgnoreCase) && EndsWith(path, operation.Segments));
    }

    private static bool EndsWith(string[] path, string[] template)
    {
        if (template.Length > path.Length)
        {
            return false;
        }

        var offset = path.Length - template.Length;
        for (var i = 0; i < template.Length; i++)
        {
            var wanted = template[i];
            var parameter = wanted.Length > 2 && wanted[0] == '{' && wanted[^1] == '}';
            if (!parameter && !string.Equals(wanted, path[offset + i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Segments(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);
}
