namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// What was wrong with the service's endpoints, found while they were built and reported
/// together when the service starts.
/// </summary>
/// <remarks>
/// Collected rather than thrown where found, so one start names everything that needs
/// changing. A set, because the framework builds each endpoint more than once.
/// </remarks>
internal sealed class StandardEndpointProblems
{
    private readonly HashSet<string> _problems = new(StringComparer.Ordinal);
    private readonly List<string> _ordered = [];

    public void Add(string problem)
    {
        lock (_problems)
        {
            if (_problems.Add(problem))
            {
                _ordered.Add(problem);
            }
        }
    }

    public IReadOnlyList<string> All
    {
        get
        {
            lock (_problems)
            {
                return [.. _ordered];
            }
        }
    }
}
