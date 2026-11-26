using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class RabbitDeliveryCountTests
{
    /// <summary>
    /// RabbitMQ sends <c>x-delivery-count</c> as a 64-bit integer that counts failed deliveries from 0, so the
    /// delivery count is one more, whatever the redelivered flag says.
    /// </summary>
    [Theory]
    [InlineData(0L, 1)]
    [InlineData(2L, 3)]
    public void ReadsTheHeaderAsRabbitMqSendsIt(object header, int expected)
    {
        var headers = new Dictionary<string, object?> { ["x-delivery-count"] = header };

        Assert.Equal(expected, RabbitDeliveryCount.Read(headers, redelivered: true));
    }

    /// <summary>
    /// A classic queue sends no count, only whether the message was delivered before.
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void WithoutTheHeader_FallsBackToTheRedeliveredFlag(bool redelivered, int expected)
    {
        Assert.Equal(expected, RabbitDeliveryCount.Read(new Dictionary<string, object?>(), redelivered));
        Assert.Equal(expected, RabbitDeliveryCount.Read(null, redelivered));
    }
}
