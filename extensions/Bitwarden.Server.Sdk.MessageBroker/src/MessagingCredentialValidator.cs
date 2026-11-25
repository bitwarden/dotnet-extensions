using Azure.Core;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Refuses to start a host that reaches Azure Service Bus by namespace without a credential for it.
/// The library chooses no identity of its own: the app states which one the broker uses.
/// </summary>
internal sealed class MessagingCredentialValidator(IServiceProvider services) : IValidateOptions<MessagingOptions>
{
    internal const string Missing =
        $"{nameof(MessagingOptions.AzureServiceBusFullyQualifiedNamespace)} is set, but no credential is registered for it. " +
        $"Register the broker's TokenCredential under MessageBrokerKeys.Credential, for example a ManagedIdentityCredential " +
        "for the service's managed identity.";

    public ValidateOptionsResult Validate(string? name, MessagingOptions options) =>
        !string.IsNullOrEmpty(options.AzureServiceBusFullyQualifiedNamespace)
        && string.IsNullOrEmpty(options.AzureServiceBusConnectionString)
        && services.GetKeyedService<TokenCredential>(MessageBrokerKeys.Credential) is null
            ? ValidateOptionsResult.Fail(Missing)
            : ValidateOptionsResult.Success;
}
