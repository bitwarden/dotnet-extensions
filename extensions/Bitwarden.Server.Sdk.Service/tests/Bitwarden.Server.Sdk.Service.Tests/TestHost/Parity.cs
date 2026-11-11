using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// A resource with something to refuse in every member.
/// </summary>
public sealed record ParityItem : Resource
{
    /// <summary>
    /// Required, being non-nullable, and short.
    /// </summary>
    [StringLength(10)]
    [Sortable(Default = true)]
    public string Name { get; init; } = null!;

    /// <summary>
    /// Held to the platform's default length.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Within a range.
    /// </summary>
    [Range(1, 100)]
    public int Quantity { get; init; }

    /// <summary>
    /// A nested object with rules of its own.
    /// </summary>
    public ParityAddress? Address { get; init; }

    /// <summary>
    /// A list of them.
    /// </summary>
    public List<ParityAddress>? Others { get; init; }

    /// <summary>
    /// Ciphertext, with a bound of its own.
    /// </summary>
    [Encrypted(MaxLength = 50)]
    public string? Secret { get; init; }
}

/// <summary>
/// A nested object.
/// </summary>
public sealed record ParityAddress
{
    /// <summary>
    /// Required, being non-nullable.
    /// </summary>
    public string City { get; init; } = null!;

    /// <summary>
    /// Short.
    /// </summary>
    [StringLength(5)]
    public string? Zip { get; init; }
}

/// <summary>
/// A surface with something to refuse in every request, whose answers <c>MinimalApiParityTests</c>
/// holds to the recorded ones.
/// </summary>
public static class Parity
{
    /// <summary>
    /// The collection's path.
    /// </summary>
    public const string Collection = "api/v1/parity-items";

    /// <summary>
    /// Maps the surface.
    /// </summary>
    /// <param name="endpoints">The service's endpoints.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var items = endpoints.MapGroup(Collection).WithStandards();
        items.MapPost("", CreateParityItem);
        items.MapGet("", ListParityItems);
        items.MapGet("required", ListRequiredParityItems);
        items.MapGet("by-guid/{id}", GetParityItemByGuid);
        items.MapGet("{id}", GetParityItem);
        items.MapPut("{id}", UpdateParityItem);
        items.MapPost("{id}/actions/submit", Submit);
        items.MapPost("{id}/actions/submit-optionally", SubmitOptionally);
        items.MapDelete("{id}", DeleteParityItem);
        items.MapPut("{id}/file", UploadFile);
        items.MapPost("form", ExchangeForm);
        items.MapGet("by-query", ListParityItemsByQuery);

        endpoints.MapPost($"{Collection}:bulk-delete", BulkDeleteParityItems).WithStandards();
    }

    /// <summary>
    /// Create an item
    /// </summary>
    /// <param name="request">The item.</param>
    /// <returns>The created item</returns>
    [StandardCreate]
    public static ResourceDocument<ParityItem> CreateParityItem(ResourceDocument<ParityItem> request) =>
        StandardResponses.CreateResponse(request.Data with { Id = "created" });

    /// <summary>
    /// List items
    /// </summary>
    /// <param name="id">Only this one.</param>
    /// <param name="createdAt">Only those created within this range.</param>
    /// <param name="tag">Only those with these tags.</param>
    /// <param name="sort">The order.</param>
    /// <param name="permanent">A flag.</param>
    /// <param name="grant">A grant.</param>
    /// <param name="since">A moment.</param>
    /// <param name="email">An address, in a header.</param>
    /// <param name="pageNumber">Which page.</param>
    /// <param name="pageSize">How many to a page.</param>
    /// <returns>The items</returns>
    [StandardReadMany]
    public static PagedResourceListDocument<ParityItem> ListParityItems(
        Filter<Guid>? id,
        Filter<Range<DateTimeOffset>>? createdAt,
        Filter<List<string>>? tag,
        Sort? sort,
        [FromQuery] bool permanent = false,
        [FromQuery] Guid? grant = null,
        [FromQuery] DateTimeOffset? since = null,
        [FromHeader(Name = "X-Request-Email")] string? email = null,
        PageNumber pageNumber = default,
        PageSize pageSize = default)
    {
        var page = new Page { Number = pageNumber, Size = pageSize };

        return StandardResponses.ReadManyResponse(
            ParityAnswers.Three(ParityAnswers.Invariant(() =>
                $"{id?.Value} {createdAt?.Value} {string.Join('|', tag?.Value ?? [])} {sort} {permanent} {grant} {since:O} {email}")),
            page);
    }

    /// <summary>
    /// List items in an organization, which must be named
    /// </summary>
    /// <param name="organizationId">The organization.</param>
    /// <returns>The items</returns>
    [StandardReadMany("required")]
    public static ResourceListDocument<ParityItem> ListRequiredParityItems(Filter<Guid> organizationId) =>
        StandardResponses.ReadManyResponse<ParityItem>([ParityAnswers.Item("one", organizationId.Value.ToString())]);

    /// <summary>
    /// Get an item by a GUID
    /// </summary>
    /// <param name="id">The item.</param>
    /// <returns>The item</returns>
    [StandardReadOne("by-guid/{id}")]
    public static ResourceDocument<ParityItem> GetParityItemByGuid(Guid id) =>
        StandardResponses.ReadOneResponse(ParityAnswers.Item(id.ToString(), "by guid"));

    /// <summary>
    /// Get an item
    /// </summary>
    /// <param name="id">The item.</param>
    /// <param name="fields">The attributes to return.</param>
    /// <returns>The item</returns>
    [StandardReadOne]
    public static ResourceDocument<ParityItem> GetParityItem(string id, Fields<ParityItem> fields = default) =>
        id == "missing" ? throw new NotFoundException() : StandardResponses.ReadOneResponse(ParityAnswers.Item(id, "read"), fields);

    /// <summary>
    /// Replace an item
    /// </summary>
    /// <param name="id">The item.</param>
    /// <param name="request">What it becomes.</param>
    /// <returns>The item</returns>
    [StandardUpdate]
    public static ResourceDocument<ParityItem> UpdateParityItem(string id, ResourceDocument<ParityItem> request) =>
        StandardResponses.UpdateResponse(request.Data with { Id = id });

    /// <summary>
    /// Submit an item
    /// </summary>
    /// <param name="id">The item.</param>
    /// <param name="request">What to submit.</param>
    /// <returns>The item</returns>
    [StandardAction]
    public static ResourceDocument<ParityItem> Submit(string id, ParityInput request) =>
        StandardResponses.ActionResponse(ParityAnswers.Item(id, $"{request.Subject} {request.Count}"));

    /// <summary>
    /// Submit an item, perhaps with nothing
    /// </summary>
    /// <param name="id">The item.</param>
    /// <param name="request">What to submit, if anything.</param>
    [StandardAction]
    public static void SubmitOptionally(string id, ParityInput? request)
    {
    }

    /// <summary>
    /// Upload an item's file
    /// </summary>
    /// <param name="id">The item.</param>
    /// <param name="request">The request, whose body is the file.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many bytes arrived</returns>
    [Consumes("application/octet-stream")]
    public static async Task<ParityAddress> UploadFile(string id, HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return new ParityAddress { City = $"{id} {buffer.Length}" };
    }

    /// <summary>
    /// Exchange a form
    /// </summary>
    /// <param name="form">The form.</param>
    /// <returns>What was sent</returns>
    public static ParityAddress ExchangeForm([FromForm] ParityForm form) => new() { City = $"{form.GrantType} {form.Scope}" };

    /// <summary>
    /// List items by query values
    /// </summary>
    /// <param name="query">The values.</param>
    /// <returns>The items</returns>
    [StandardReadMany("by-query")]
    public static ResourceListDocument<ParityItem> ListParityItemsByQuery([AsParameters] ParityQuery query) =>
        StandardResponses.ReadManyResponse<ParityItem>([ParityAnswers.Item("one", $"{query.Code} {query.Count}")]);

    /// <summary>
    /// Delete items
    /// </summary>
    /// <param name="request">Which.</param>
    /// <returns>How many</returns>
    [StandardBulkDelete(Collection, Targeting = Targeting.Ids)]
    public static BulkResult BulkDeleteParityItems(BulkDelete<ParityItem> request) =>
        BulkResult.Affected(request.Ids?.Count ?? 0);

    /// <summary>
    /// Delete an item
    /// </summary>
    /// <param name="id">The item.</param>
    [StandardDelete]
    public static void DeleteParityItem(string id)
    {
    }
}
