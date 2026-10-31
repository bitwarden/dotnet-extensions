using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Envelopes what a bulk create made, echoing each item's <c>lid</c> beside its <c>id</c>.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class BulkCreatedJsonConverter<T>
    : CollectionJsonConverter<BulkCreatedDocument<T>, T> where T : Resource
{
    protected override void Decorate(JsonArray resources, BulkCreatedDocument<T> value)
    {
        if (value.Lids is not { } lids)
        {
            return;
        }

        for (var i = 0; i < resources.Count && i < lids.Count; i++)
        {
            if (lids[i] is not { } lid || resources[i] is not JsonObject resource)
            {
                continue;
            }

            // Beside id and type, ahead of the attributes, as JSON:API places it.
            var rebuilt = new JsonObject();

            foreach (var member in resource.ToList())
            {
                resource.Remove(member.Key);

                if (member.Key == ResourceEnvelope.Attributes)
                {
                    rebuilt[ResourceEnvelope.Lid] = lid;
                }

                rebuilt[member.Key] = member.Value;
            }

            if (!rebuilt.ContainsKey(ResourceEnvelope.Lid))
            {
                rebuilt[ResourceEnvelope.Lid] = lid;
            }

            resources[i] = rebuilt;
        }
    }
}
