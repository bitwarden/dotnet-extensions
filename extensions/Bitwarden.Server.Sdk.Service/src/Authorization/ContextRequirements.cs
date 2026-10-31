using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The requirement carrying one operation's <see cref="AuthorizationAttribute"/>.
/// </summary>
/// <param name="Options">What the operation demands.</param>
internal sealed record ContextRequirements(AuthorizationAttribute Options) : IAuthorizationRequirement;
