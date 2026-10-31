namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// An operation a cloud install has and a self-hosted one refuses. A self-hosted install answers it as an invalid
/// request (422).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NotSelfHostedOnlyAttribute : Attribute;
