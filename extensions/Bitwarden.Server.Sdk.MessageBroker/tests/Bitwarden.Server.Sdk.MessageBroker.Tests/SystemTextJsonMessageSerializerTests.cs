using System.Buffers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Pins the wire-level forward-compatibility contract: a subscriber reading a message whose
/// JSON contains a <c>$type</c> discriminator it has not been rebuilt against must skip that
/// element silently and still surface the ones it recognizes. Breaks here mean a rolling deploy
/// with a newer publisher dead-letters valid messages.
/// </summary>
public class SystemTextJsonMessageSerializerTests
{
    [Fact]
    public void DeserializeVariants_SkipsUnknownDiscriminatorsBetweenKnownOnes()
    {
        var serializer = BuildSerializer();

        // Interleave an unknown "v3" between the two variants this subscriber was built against.
        // A publisher newer than the subscriber would send something shaped like this.
        var wire = """
            [
                {"$type":"v2"},
                {"$type":"v3","FieldFromFuture":42},
                {"$type":"v1"}
            ]
            """;

        var result = serializer.DeserializeVariants<VersionedPayload>(Encoding.UTF8.GetBytes(wire));

        Assert.Equal(2, result.Count);
        Assert.Contains(result, v => v is VersionedV2);
        Assert.Contains(result, v => v is VersionedV1);
    }

    [Fact]
    public void DeserializeVariants_ReturnsEmptyWhenEveryDiscriminatorIsUnknown()
    {
        var serializer = BuildSerializer();

        // Every variant on the wire is from a version this subscriber does not know.
        // The envelope will then throw later (no decodable variant), but the serializer
        // itself must not throw.
        var wire = """
            [
                {"$type":"v3"},
                {"$type":"v4"}
            ]
            """;

        var result = serializer.DeserializeVariants<VersionedPayload>(Encoding.UTF8.GetBytes(wire));

        Assert.Empty(result);
    }

    [Fact]
    public void RoundTrip_PreservesKnownVariants()
    {
        var serializer = BuildSerializer();

        var buffer = new ArrayBufferWriter<byte>();
        serializer.SerializeVariants<VersionedPayload>(
            [new VersionedV2(), new VersionedV1()],
            buffer);

        var result = serializer.DeserializeVariants<VersionedPayload>(buffer.WrittenSpan);

        Assert.Equal(2, result.Count);
    }

    private static IMessageSerializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddOptions<MessageBrokerSerializerOptions>("test");
        services.AddPublisher<VersionedPayload, VersionedV2>("test");
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredKeyedService<IMessageSerializer>(
            MessageBrokerKeys.Publisher<VersionedPayload>("test"));
    }
}
