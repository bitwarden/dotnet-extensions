using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// A bulk operation's checks, which the standard endpoint filter makes before the handler runs.
/// </summary>
internal static class BulkChecks
{
    /// <summary>
    /// Checks one argument, if it is a bulk document.
    /// </summary>
    /// <param name="argument">The argument.</param>
    /// <param name="supports">The atomicity modes the operation offers.</param>
    /// <param name="targeting">How the operation lets resources be named.</param>
    /// <param name="query">The request's query, for <c>atomic</c>.</param>
    public static void Check(object? argument, Atomicity supports, Targeting targeting, Microsoft.AspNetCore.Http.IQueryCollection query)
    {
        if (argument is null || !IsBulk(argument.GetType()))
        {
            return;
        }

        Targeted(argument, targeting);

        if (Expression(argument) is { } filtered)
        {
            ResourceFields.Require(filtered.Query, filtered.Resource);
        }

        Atomically(argument, Wanted(query, supports), supports);
        Corresponding(argument);
    }

    /// <remarks>
    /// Exactly one of <c>ids</c> and <c>filter</c>, and only the ones the operation offers.
    /// Neither is the dangerous one: on a delete it would mean every resource in scope, and it
    /// is the request somebody makes by leaving a field out rather than by meaning it.
    /// </remarks>
    private static void Targeted(object argument, Targeting accepted)
    {
        var type = argument.GetType();

        // Creates and replaces carry their resources in data and have nothing to target.
        if (type.GetGenericTypeDefinition() is var definition
            && (definition == typeof(BulkCreate<>) || definition == typeof(BulkReplace<>)))
        {
            return;
        }

        // A search names its resources one way only, and the filter is required there.
        if (definition == typeof(Search<>))
        {
            return;
        }

        var ids = type.GetProperty("Ids")?.GetValue(argument) as System.Collections.IEnumerable;
        var filter = type.GetProperty("Filter")?.GetValue(argument);

        var named = ids is not null;
        var described = filter is not null;

        if (named && described)
        {
            throw new InvalidRequestException(
                "Name the resources with 'ids' or describe them with 'filter', not both. They "
                + "differ in what happens to a resource that is not there, so a request "
                + "carrying both has not said which answer it wants.",
                "/ids");
        }

        if (!named && !described)
        {
            throw new InvalidRequestException(
                accepted switch
                {
                    Targeting.Ids => "Name the resources to act on with 'ids'.",
                    Targeting.Filter => "Describe the resources to act on with 'filter'.",
                    _ => "Name the resources with 'ids' or describe them with 'filter'.",
                },
                "/ids");
        }

        if (named && !accepted.HasFlag(Targeting.Ids))
        {
            throw new InvalidRequestException(
                "This operation does not accept 'ids'. Describe the resources with 'filter'.",
                "/ids");
        }

        if (described && !accepted.HasFlag(Targeting.Filter))
        {
            throw new InvalidRequestException(
                "This operation does not accept 'filter'. Name the resources with 'ids'.",
                "/filter");
        }
    }

    /// <remarks>
    /// Absent means all-or-nothing where the operation offers it, which is the standards'
    /// default and the safer of the two, and means the operation's only mode where it offers
    /// one. The standards say what to default to when both are supported and are silent on the
    /// single-mode case; refusing a request that named nothing would be a poor reading of that
    /// silence.
    /// <para>
    /// Anything that is not <c>true</c> or <c>false</c> is a malformed request rather than a
    /// mode nobody offers, so it is refused either way, but saying which it is helps.
    /// </para>
    /// </remarks>
    private static Atomicity Wanted(Microsoft.AspNetCore.Http.IQueryCollection query, Atomicity supported)
    {
        if (!query.TryGetValue("atomic", out var values))
        {
            return supported.HasFlag(Atomicity.AllOrNothing)
                ? Atomicity.AllOrNothing
                : Atomicity.BestEffort;
        }

        var value = values.ToString();

        if (!bool.TryParse(value, out var atomic))
        {
            throw new InvalidRequestException(
                $"'{value}' is not a value 'atomic' accepts. It is 'true' for all-or-nothing or "
                + "'false' for best-effort.",
                "/atomic");
        }

        return atomic ? Atomicity.AllOrNothing : Atomicity.BestEffort;
    }

    /// <remarks>
    /// Read from whichever bulk attribute the operation carries, so a new bulk form declares
    /// its modes the same way the existing ones do.
    /// </remarks>
    /// <summary>
    /// What the operation declares: the atomicity it offers and how resources may be named.
    /// </summary>
    public static (Atomicity Supports, Targeting Targeting) Declared(IEnumerable<object> metadata)
    {

        foreach (var each in metadata)
        {
            switch (each)
            {
                case StandardBulkActionAttribute action:
                    return (action.Supports, action.Targeting);
                case StandardBulkUpdateAttribute update:
                    return (update.Supports, update.Targeting);
                case StandardBulkDeleteAttribute delete:
                    return (delete.Supports, delete.Targeting);
                case StandardBulkCreateAttribute create:
                    return (create.Supports, Targeting.Either);
                case StandardBulkReplaceAttribute replace:
                    return (replace.Supports, Targeting.Either);
            }
        }

        return (Atomicity.AllOrNothing, Targeting.Either);
    }

    /// <remarks>
    /// A body per resource has to line up with the resources it is for. The standards say the
    /// array goes with <c>ids</c> and matches its length, and both halves matter: a filter names
    /// a set the server computes, so there is no order for an array to correspond to, and two
    /// arrays of different lengths have no correspondence to read.
    /// </remarks>
    private static void Corresponding(object argument)
    {
        var type = argument.GetType();

        if (type.GetProperty("Data")?.GetValue(argument) is not { } data)
        {
            return;
        }

        if (data.GetType().GetProperty(nameof(BulkInput<object>.Each))?.GetValue(data)
            is not System.Collections.ICollection each)
        {
            return;
        }

        if (type.GetProperty("Ids")?.GetValue(argument) is not System.Collections.ICollection ids)
        {
            throw new InvalidRequestException(
                "A body for each resource goes with 'ids'. A filter names a set this service "
                + "works out, so there is no order for the bodies to follow.",
                "/data");
        }

        if (each.Count != ids.Count)
        {
            throw new InvalidRequestException(
                $"{ids.Count} resources were named and {each.Count} bodies were sent. Each body "
                + "is for the resource in the same position, so there must be one of each.",
                "/data");
        }
    }

    private static void Atomically(object? argument, Atomicity wanted, Atomicity supported)
    {
        if (!supported.HasFlag(wanted))
        {
            throw new InvalidRequestException(
                wanted == Atomicity.BestEffort
                    ? "This operation is all-or-nothing and does not support 'atomic=false'. "
                      + "Every resource it touches succeeds, or none of them does."
                    : "This operation is best-effort and does not support 'atomic=true'. Each "
                      + "resource succeeds or fails on its own.",
                "/atomic");
        }

        argument?.GetType()
            .GetProperty(nameof(BulkDelete<Resource>.Atomicity))
            ?.SetValue(argument, wanted);
    }

    /// <remarks>
    /// Matched structurally rather than by interface, because <c>BulkUpdate</c> and
    /// <c>BulkDelete</c> are records a caller sends and giving them a marker interface would
    /// put platform vocabulary into the published schema for no benefit to the reader.
    /// </remarks>
    private static (Query Query, Type Resource)? Expression(object? argument)
    {
        var type = argument?.GetType();

        if (type is null || !IsBulk(type))
        {
            return null;
        }

        return type.GetProperty("Filter")?.GetValue(argument) is Query query
            ? (query, type.GetGenericArguments()[0])
            : null;
    }

    /// <remarks>
    /// Matched structurally rather than by interface, because these are records a caller sends
    /// and a marker interface would put platform vocabulary into the published schema for no
    /// benefit to the reader.
    /// </remarks>
    private static bool IsBulk(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();

        return definition == typeof(BulkUpdate<>)
            || definition == typeof(BulkDelete<>)
            || definition == typeof(BulkCreate<>)
            || definition == typeof(BulkReplace<>)
            || definition == typeof(BulkAction<>)
            || definition == typeof(BulkAction<,>)
            || definition == typeof(Search<>);
    }
}
