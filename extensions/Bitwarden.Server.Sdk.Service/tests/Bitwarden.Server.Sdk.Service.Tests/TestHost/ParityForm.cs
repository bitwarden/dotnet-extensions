using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// A form a protocol endpoint takes.
/// </summary>
public sealed class ParityForm
{
    /// <summary>
    /// Required, being non-nullable.
    /// </summary>
    [FromForm(Name = "grant_type")]
    public string GrantType { get; set; } = null!;

    /// <summary>
    /// Short.
    /// </summary>
    [FromForm(Name = "scope")]
    [StringLength(10)]
    public string? Scope { get; set; }
}
