using System.Collections.Concurrent;
using Bitwarden.Server.Sdk.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

public sealed record UserCreated(Guid UserId) : UserCreatedPayload.ISole;

public class UserCreatedPayload
    : Payload<UserCreatedPayload, UserCreated, UserCreated>, IPayloadVariants<UserCreatedPayload>, IPayloadWireName<UserCreatedPayload>
{
    public static string WireName => "user.created";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(UserCreated), "v1")];
}

public sealed record UserDeleted(Guid UserId) : UserDeletedPayload.ISole;

public class UserDeletedPayload
    : Payload<UserDeletedPayload, UserDeleted, UserDeleted>, IPayloadVariants<UserDeletedPayload>, IPayloadWireName<UserDeletedPayload>
{
    public static string WireName => "user.deleted";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(UserDeleted), "v1")];
}

public sealed record SendMail(Guid MailId) : SendMailPayload.ISole;

public class SendMailPayload
    : Payload<SendMailPayload, SendMail, SendMail>, IPayloadVariants<SendMailPayload>, IPayloadWireName<SendMailPayload>
{
    public static string WireName => "send.mail";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(SendMail), "v1")];
}

public sealed record SendPush(Guid PushId) : SendPushPayload.ISole;

public class SendPushPayload
    : Payload<SendPushPayload, SendPush, SendPush>, IPayloadVariants<SendPushPayload>, IPayloadWireName<SendPushPayload>
{
    public static string WireName => "send.push";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(SendPush), "v1")];
}

public sealed class MailRecorder
{
    public ConcurrentQueue<Guid> Handled { get; } = new();
}

public sealed class SendMailConsumer(MailRecorder recorder) : IMessageConsumer<SendMailPayload, SendMail>
{
    public Task HandleAsync(Envelope<SendMailPayload, SendMail> envelope, CancellationToken cancellationToken)
    {
        recorder.Handled.Enqueue(envelope.Payload.MailId);
        return Task.CompletedTask;
    }
}

public sealed class SendPushConsumer : IMessageConsumer<SendPushPayload, SendPush>
{
    public Task HandleAsync(Envelope<SendPushPayload, SendPush> envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class Sample
{
    public const string IdentityEvents = "identity.events";
    public const string NotificationCommands = "notification.commands";

    public static void Identity(IServiceCollection services)
    {
        services.AddPublisher<UserCreatedPayload, UserCreated>(IdentityEvents);
        services.AddPublisher<UserDeletedPayload, UserDeleted>(IdentityEvents);
    }

    public static void Vault(IServiceCollection services)
    {
        services.AddSubscriber<UserDeletedPayload, UserDeleted>(IdentityEvents, "vault");
        services.AddCommandSender<SendMailPayload, SendMail>(NotificationCommands);
    }

    public static void Notification(IServiceCollection services)
    {
        services.AddSingleton<MailRecorder>();
        services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>(NotificationCommands);
        services.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>(NotificationCommands);
    }

    public static IReadOnlyList<ServiceManifest> Manifests() =>
        [Manifest("identity", Identity), Manifest("vault", Vault), Manifest("notification", Notification)];

    public static ServiceManifest Manifest(string service, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        using var provider = services.BuildServiceProvider();
        return ServiceManifest.FromRequirements(service, provider.GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements);
    }
}
