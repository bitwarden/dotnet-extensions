using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// Query values taken together.
/// </summary>
public sealed class ParityQuery
{
    /// <summary>
    /// A code.
    /// </summary>
    [FromQuery(Name = "code")]
    [StringLength(5)]
    public string? Code { get; set; }

    /// <summary>
    /// A count.
    /// </summary>
    [FromQuery(Name = "count")]
    [Range(1, 3)]
    public int? Count { get; set; }
}
