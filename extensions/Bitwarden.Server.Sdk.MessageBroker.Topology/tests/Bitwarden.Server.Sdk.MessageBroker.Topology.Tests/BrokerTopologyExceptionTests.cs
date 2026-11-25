namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

public class BrokerTopologyExceptionTests
{
    [Fact]
    public void ItsMessageListsEveryProblem()
    {
        var exception = new BrokerTopologyException(["The first problem.", "The second problem."]);

        Assert.Equal("The services' messaging topology can't be provisioned:\n- The first problem.\n- The second problem.", exception.Message);
        Assert.Equal(["The first problem.", "The second problem."], exception.Problems);
    }
}
