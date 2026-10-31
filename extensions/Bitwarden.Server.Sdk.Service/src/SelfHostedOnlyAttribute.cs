namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// An operation a self-hosted install has and a cloud one refuses. A cloud install answers it as an invalid
/// request (422).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SelfHostedOnlyAttribute : Attribute;
