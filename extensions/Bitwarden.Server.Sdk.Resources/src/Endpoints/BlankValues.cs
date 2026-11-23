using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Resources.Endpoints;

/// <summary>
/// What a handler receives for a <c>string</c> route, query or header value: what an MVC controller's binder gives
/// it, where the platform passes a blank value as sent and a query value sent more than once joined.
/// </summary>
internal static class BlankValues
{
    /// <summary>
    /// Gives each <c>string</c> parameter the value a controller's binder reads, the first a query sent, and none for a
    /// blank one (empty, or whitespace only), or the parameter's default when it declares one; and each blank value of
    /// a <c>string</c> array null, keeping the array's length.
    /// </summary>
    /// <param name="context">The handler's arguments.</param>
    /// <param name="parameters">The handler's parameters.</param>
    /// <param name="acceptsInvalid">
    /// Whether the handler answers invalid requests itself, and so receives a required value as sent: the refusal it
    /// gives may name what was sent.
    /// </param>
    public static void BindAsNone(EndpointFilterInvocationContext context, IReadOnlyList<RequestParameter> parameters, bool acceptsInvalid)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.Kind != ParameterKind.Simple || parameter.Index >= context.Arguments.Count)
            {
                continue;
            }

            if (parameter.Info.ParameterType == typeof(string))
            {
                var (values, _) = RequestRefusal.Read(context.HttpContext, parameter);

                // The request delegate generator passes none for a string that was not sent, where a controller and the
                // runtime factory pass its default.
                if (values.Count == 0)
                {
                    if (context.Arguments[parameter.Index] is null && parameter.Info.HasDefaultValue)
                    {
                        context.Arguments[parameter.Index] = parameter.Info.DefaultValue;
                    }

                    continue;
                }

                object? value = values[0];
                if (string.IsNullOrWhiteSpace(values[0]) && !(acceptsInvalid && parameter.Required))
                {
                    value = parameter.Info.HasDefaultValue ? parameter.Info.DefaultValue : null;
                }

                context.Arguments[parameter.Index] = value;
            }
            else if (context.Arguments[parameter.Index] is string?[] elements && elements.Any(string.IsNullOrWhiteSpace))
            {
                context.Arguments[parameter.Index] = elements.Select(element => string.IsNullOrWhiteSpace(element) ? null : element).ToArray();
            }
        }
    }
}
