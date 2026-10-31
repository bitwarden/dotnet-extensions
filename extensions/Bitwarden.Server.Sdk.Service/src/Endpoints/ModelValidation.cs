using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Validates a bound model as an ASP.NET MVC controller validates one, so a handler is held to the
/// same rules, and refused in the same words, as a controller action.
/// </summary>
/// <remarks>
/// <para>
/// The rules are the model's declarations and the two nobody declares: a non-nullable reference
/// member is required, and a resource's string member is held to <see cref="DefaultStringMaxLength"/>
/// unless it is bounded.
/// </para>
/// <para>
/// The order is a controller's too: members in declaration order, depth first, an object's own
/// rules (its <see cref="IValidatableObject"/>, its class-level attributes) only once its members
/// pass. A message is the attribute's own, given the member's display name.
/// </para>
/// </remarks>
internal static class ModelValidation
{
    /// <summary>
    /// The length a resource's string member is limited to when it declares nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unbounded string is an unbounded request. Making every author remember
    /// <c>[StringLength]</c> means some will not, so the bound is the default and declaring one is
    /// how you change it: the opposite of the usual arrangement, and the safer one.
    /// </para>
    /// <para>
    /// Applies only to resources, because that is the surface a caller fills in. A member declaring
    /// <see cref="EncryptedAttribute"/> is bounded by it: ciphertext is longer than the default
    /// allows for even a short secret, and the declaration states its own maximum.
    /// </para>
    /// </remarks>
    internal const int DefaultStringMaxLength = 255;

    /// <summary>
    /// A controller stops recording errors at this many, and so does this.
    /// </summary>
    private const int MaxErrors = 200;

    /// <summary>
    /// And stops descending here.
    /// </summary>
    private const int MaxDepth = 32;

    private static readonly ConcurrentDictionary<Type, Member[]> Members = new();

    /// <summary>
    /// Validates a model, adding what fails to <paramref name="errors"/>.
    /// </summary>
    /// <param name="model">The bound model.</param>
    /// <param name="prefix">The key it is reported under: empty for a body, <c>Data</c> for a document's resource.</param>
    /// <param name="services">For the validation context, as a controller supplies the request's.</param>
    /// <param name="errors">Where failures are added.</param>
    public static void Validate(object model, string prefix, IServiceProvider services, List<ModelError> errors)
    {
        Visit(model, model.GetType(), prefix, services, errors, depth: 0, visited: new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// Validates a model bound member by member, from a form or the query, whose members are keyed
    /// as a controller keys them: by the name the caller sent each one under.
    /// </summary>
    public static void ValidateBound(object model, IServiceProvider services, List<ModelError> errors)
    {
        Visit(model, model.GetType(), string.Empty, services, errors, depth: 0,
            visited: new HashSet<object>(ReferenceEqualityComparer.Instance), boundNames: true);
    }

    /// <summary>
    /// The name a bound member is sent under: its binding attribute's, or its own.
    /// </summary>
    public static string BoundName(PropertyInfo property) =>
        property.GetCustomAttributes(inherit: true).OfType<Microsoft.AspNetCore.Http.Metadata.IFromFormMetadata>().FirstOrDefault()?.Name
        ?? property.GetCustomAttributes(inherit: true).OfType<Microsoft.AspNetCore.Http.Metadata.IFromQueryMetadata>().FirstOrDefault()?.Name
        ?? property.GetCustomAttributes(inherit: true).OfType<Microsoft.AspNetCore.Http.Metadata.IFromRouteMetadata>().FirstOrDefault()?.Name
        ?? property.GetCustomAttributes(inherit: true).OfType<Microsoft.AspNetCore.Http.Metadata.IFromHeaderMetadata>().FirstOrDefault()?.Name
        ?? property.Name;

    /// <summary>
    /// Visits a complex value: its members, then, if they pass, its own rules.
    /// </summary>
    private static bool Visit(
        object model,
        Type type,
        string key,
        IServiceProvider services,
        List<ModelError> errors,
        int depth,
        HashSet<object> visited,
        bool boundNames = false)
    {
        if (depth > MaxDepth || !visited.Add(model))
        {
            return true;
        }

        var before = errors.Count;

        if (IsDictionary(type))
        {
            // Each entry keyed by its own key, as it is sent: Items[home].Number.
            foreach (var (entryKey, value) in Entries(model))
            {
                if (value is not null && IsComplex(value.GetType()))
                {
                    Visit(value, value.GetType(), $"{key}[{entryKey}]", services, errors, depth + 1, visited);
                }
            }
        }
        else if (model is IEnumerable sequence && model is not string)
        {
            var index = 0;

            foreach (var item in sequence)
            {
                if (item is not null && IsComplex(item.GetType()))
                {
                    Visit(item, item.GetType(), $"{key}[{index}]", services, errors, depth + 1, visited);
                }

                index++;
            }
        }
        else
        {
            foreach (var member in MembersOf(type))
            {
                if (errors.Count >= MaxErrors)
                {
                    break;
                }

                var value = member.Property.GetValue(model);
                var memberName = boundNames ? BoundName(member.Property) : member.Property.Name;
                var memberKey = string.IsNullOrEmpty(key) ? memberName : $"{key}.{memberName}";
                var childrenPassed = true;

                if (value is not null && member.Complex)
                {
                    childrenPassed = Visit(value, value.GetType(), memberKey, services, errors, depth + 1, visited);
                }

                if (childrenPassed)
                {
                    Run(member, model, value, memberKey, services, errors);
                }
            }
        }

        if (errors.Count > before)
        {
            return false;
        }

        // The object's own rules, only once everything in it passes.
        if (model is IValidatableObject validatable)
        {
            var context = new ValidationContext(model, services, items: null) { DisplayName = type.Name };

            foreach (var result in validatable.Validate(context) ?? [])
            {
                if (result == ValidationResult.Success || result is null)
                {
                    continue;
                }

                var names = result.MemberNames?.ToList() ?? [];

                if (names.Count == 0)
                {
                    errors.Add(new ModelError(key, result.ErrorMessage ?? string.Empty));
                }

                foreach (var name in names)
                {
                    errors.Add(new ModelError(Join(key, name), result.ErrorMessage ?? string.Empty));
                }
            }
        }

        return errors.Count == before;
    }

    /// <summary>
    /// Runs a member's validators against its value.
    /// </summary>
    private static void Run(Member member, object container, object? value, string key, IServiceProvider services, List<ModelError> errors)
    {
        foreach (var validator in member.Validators)
        {
            var context = new ValidationContext(container, services, items: null)
            {
                DisplayName = member.DisplayName,
                MemberName = member.Property.Name,
            };

            var result = validator.GetValidationResult(value, context);

            if (result == ValidationResult.Success || result is null)
            {
                continue;
            }

            var message = result.ErrorMessage ?? string.Empty;
            var others = result.MemberNames?
                .Where(name => !string.Equals(name, member.Property.Name, StringComparison.Ordinal))
                .ToList() ?? [];

            if (others.Count == 0)
            {
                errors.Add(new ModelError(key, message));
            }

            foreach (var other in others)
            {
                errors.Add(new ModelError(Join(key, other), message));
            }
        }
    }

    private static string Join(string prefix, string name) =>
        string.IsNullOrEmpty(prefix) ? name : string.IsNullOrEmpty(name) ? prefix : $"{prefix}.{name}";

    private static Member[] MembersOf(Type type) => Members.GetOrAdd(type, Describe);

    /// <summary>
    /// What a controller's metadata would say about each of a type's members.
    /// </summary>
    private static Member[] Describe(Type type)
    {
        var resource = typeof(Resource).IsAssignableFrom(type);

        // A context of its own: one is not safe to share between types described at once.
        var nullability = new NullabilityInfoContext();

        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is { IsPublic: true })
            .Select(property => new Member(
                property,
                DisplayNameOf(property),
                ValidatorsOf(property, resource, nullability),
                IsComplex(property.PropertyType)))
            .ToArray();
    }

    /// <summary>
    /// A member's validators, in the order a controller runs them.
    /// </summary>
    private static ValidationAttribute[] ValidatorsOf(PropertyInfo property, bool resource, NullabilityInfoContext nullability)
    {
        var declared = property.GetCustomAttributes(inherit: true)
            .Concat(property.PropertyType.GetCustomAttributes(inherit: true))
            .OfType<ValidationAttribute>()
            .ToList();

        // Nullability is the contract: a non-nullable reference member is required. Present, an
        // empty string satisfies it, as MVC's implicit [Required] lets it; a declared one decides
        // for itself.
        if (!property.PropertyType.IsValueType
            && !declared.OfType<RequiredAttribute>().Any()
            && nullability.Create(property).ReadState == NullabilityState.NotNull)
        {
            declared.Add(new RequiredAttribute { AllowEmptyStrings = true });
        }

        if (resource && property.PropertyType == typeof(string))
        {
            foreach (var encrypted in declared.OfType<EncryptedAttribute>())
            {
                encrypted.EnsureBounded();
            }

            if (!declared.Any(attribute => attribute is StringLengthAttribute or MaxLengthAttribute or EncryptedAttribute))
            {
                declared.Add(new StringLengthAttribute(DefaultStringMaxLength));
            }
        }

        return [.. declared];
    }

    private static string DisplayNameOf(PropertyInfo property) =>
        property.GetCustomAttribute<DisplayAttribute>()?.GetName()
        ?? property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
        ?? property.Name;

    /// <summary>
    /// Whether a controller would look inside a value of the type: not for anything it converts
    /// from a string, and not for free-form JSON, which has no members to validate.
    /// </summary>
    private static bool IsComplex(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return !RequestParameters.IsSimple(underlying)
            && underlying != typeof(object)
            && underlying != typeof(Uri)
            && underlying != typeof(Type)
            && underlying != typeof(JsonElement)
            && underlying != typeof(JsonDocument)
            && !typeof(JsonNode).IsAssignableFrom(underlying)
            && !typeof(Stream).IsAssignableFrom(underlying)
            && !typeof(Delegate).IsAssignableFrom(underlying)
            && underlying != typeof(CancellationToken)
            && !IsDictionaryOfSimple(underlying);
    }

    private static bool IsDictionary(Type type) =>
        typeof(IDictionary).IsAssignableFrom(type)
        || type.GetInterfaces().Append(type).Any(candidate => candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));

    /// <summary>
    /// A map of strings to plain values, such as a document's <c>meta</c>: nothing to validate inside.
    /// </summary>
    private static bool IsDictionaryOfSimple(Type type) =>
        IsDictionary(type) && ValueTypeOf(type) is var value && (value == type || !IsComplex(value));

    /// <summary>
    /// The type of a dictionary's values: <see cref="object"/> for a dictionary that does not say.
    /// </summary>
    private static Type ValueTypeOf(Type dictionary) =>
        dictionary.GetInterfaces().Append(dictionary)
            .FirstOrDefault(candidate => candidate.IsGenericType
                && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
            ?.GetGenericArguments()[1]
        ?? typeof(object);

    /// <summary>
    /// A dictionary's entries, whichever of the dictionary interfaces it implements.
    /// </summary>
    private static IEnumerable<(object? Key, object? Value)> Entries(object dictionary)
    {
        if (dictionary is IDictionary untyped)
        {
            foreach (DictionaryEntry entry in untyped)
            {
                yield return (entry.Key, entry.Value);
            }

            yield break;
        }

        foreach (var entry in (IEnumerable)dictionary)
        {
            var pair = entry.GetType();

            yield return (pair.GetProperty("Key")?.GetValue(entry), pair.GetProperty("Value")?.GetValue(entry));
        }
    }

    /// <summary>
    /// Orders failures as a controller's model state lists them.
    /// </summary>
    /// <remarks>
    /// Model state is a tree of key segments split on <c>.</c>, each node's children kept shortest
    /// name first, then alphabetically. It reads a level's own keys before anything below them:
    /// <c>Data.Name</c>, <c>Data.Secret</c>, <c>Data.Quantity</c>, then <c>Data.Address.Zip</c>,
    /// whatever order they were found in. Failures on the same key keep the order they were found in.
    /// </remarks>
    public static IReadOnlyList<ModelError> InModelStateOrder(IReadOnlyList<ModelError> errors)
    {
        var root = new Node();

        foreach (var error in errors)
        {
            var node = root;

            foreach (var segment in Segments(error.Key))
            {
                node = node.Child(segment);
            }

            node.Errors.Add(error);
        }

        var ordered = new List<ModelError>(errors.Count);
        root.Collect(ordered);

        return ordered;
    }

    /// <summary>
    /// A key's segments, as model state splits it: on <c>.</c>, an index staying with its member.
    /// </summary>
    private static string[] Segments(string key) => key.Length == 0 ? [] : key.Split('.');

    private sealed class Node
    {
        private readonly List<(string Segment, Node Node)> _children = [];

        public List<ModelError> Errors { get; } = [];

        public Node Child(string segment)
        {
            foreach (var (existing, node) in _children)
            {
                if (string.Equals(existing, segment, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }
            }

            var created = new Node();
            // Model state's own order: shorter names first, then alphabetically, ignoring case.
            var at = _children.FindIndex(child => Compare(child.Segment, segment) > 0);
            _children.Insert(at < 0 ? _children.Count : at, (segment, created));

            return created;
        }

        private static int Compare(string left, string right) =>
            left.Length != right.Length
                ? left.Length - right.Length
                : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

        public void Collect(List<ModelError> into)
        {
            into.AddRange(Errors);
            CollectChildren(into);
        }

        /// <summary>
        /// A level's own keys first, then each child's below it, as model state's enumerator reads it.
        /// </summary>
        private void CollectChildren(List<ModelError> into)
        {
            foreach (var (_, node) in _children)
            {
                into.AddRange(node.Errors);
            }

            foreach (var (_, node) in _children)
            {
                node.CollectChildren(into);
            }
        }
    }

    private sealed record Member(PropertyInfo Property, string DisplayName, ValidationAttribute[] Validators, bool Complex);
}
