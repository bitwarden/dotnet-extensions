namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// The services' messaging requirements can't be provisioned as they are.
/// </summary>
public sealed class BrokerTopologyException : Exception
{
    /// <summary>
    /// Creates the exception from every problem found.
    /// </summary>
    /// <param name="problems">Each problem, one sentence each.</param>
    public BrokerTopologyException(IReadOnlyList<string> problems)
        : base("The services' messaging topology can't be provisioned:" + string.Concat(problems.Select(p => "\n- " + p)))
    {
        Problems = problems;
    }

    /// <summary>
    /// Each problem found, one sentence each.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }
}
