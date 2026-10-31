using System.Reflection;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// A handler's parameter, described for the API explorer as an MVC controller action's is.
/// </summary>
internal sealed class HandlerParameterDescriptor : ParameterDescriptor, IParameterInfoParameterDescriptor
{
    /// <inheritdoc />
    public required ParameterInfo ParameterInfo { get; init; }
}
