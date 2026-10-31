using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Which service this process is, as given to <see cref="Service"/>.
/// </summary>
/// <param name="Name">The service's identifier.</param>
/// <param name="Title">The service's human-readable name.</param>
/// <param name="IsProductService">
/// Whether it is marked <see cref="ProductServiceAttribute"/>: people call it directly, holding
/// their own session.
/// </param>
internal sealed record ServiceIdentity(string Name, string Title, bool IsProductService = false) : IServiceIdentity;
