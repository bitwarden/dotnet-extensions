using Bitwarden.Extensions.Hosting.Exceptions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Extensions.Hosting.Attributes;

/// <summary>
/// Attribute to indicate that an instance is self-hosted.
/// </summary>
public class SelfHostedAttribute : ActionFilterAttribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the attribute is only allowed when self-hosted.
    /// </summary>
    public bool SelfHostedOnly { get; init; }
    /// <summary>
    /// Gets or sets a value indicating whether the attribute is only allowed when not self-hosted.
    /// </summary>
    public bool NotSelfHostedOnly { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelfHostedAttribute"/> class.
    /// </summary>
    /// <param name="context">Action context.</param>
    /// <exception cref="BadRequestException"></exception>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var globalSettings = context.HttpContext.RequestServices.GetRequiredService<GlobalSettingsBase>();
        if (SelfHostedOnly && !globalSettings.IsSelfHosted)
        {
            throw new BadRequestException("Only allowed when self-hosted.");
        }
        else if (NotSelfHostedOnly && globalSettings.IsSelfHosted)
        {
            throw new BadRequestException("Only allowed when not self-hosted.");
        }
    }
}
