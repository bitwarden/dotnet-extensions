using System.Reflection;
using Bitwarden.Server.Sdk.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Finds and applies the extensions declared by the packages a service references.
/// </summary>
/// <remarks>
/// <para>
/// Walks the service's assembly and everything it references, transitively, reading
/// <see cref="ServiceExtensionAttribute"/> from each. The framework's and the runtime's own
/// assemblies are never followed, so nothing is loaded that was not going to be anyway.
/// </para>
/// <para>
/// Applied in a fixed order, by assembly name and then by type name, so two runs of the same
/// service configure it the same way.
/// </para>
/// </remarks>
internal static class ServiceExtensions
{
    public static void Apply(Assembly service, IServiceCollection services, IConfiguration configuration)
    {
        foreach (var extension in Declared(service))
        {
            var instance = (IServiceExtension)Activator.CreateInstance(extension)!;
            instance.Configure(services, configuration);
        }
    }

    internal static IReadOnlyList<Type> Declared(Assembly service)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([service]);
        var found = new List<(string Assembly, Type Extension)>();

        while (pending.TryPop(out var assembly))
        {
            if (!seen.Add(assembly.GetName().Name!))
            {
                continue;
            }

            foreach (var declared in assembly.GetCustomAttributes<ServiceExtensionAttribute>())
            {
                if (!typeof(IServiceExtension).IsAssignableFrom(declared.Extension))
                {
                    throw new InvalidOperationException(
                        $"{assembly.GetName().Name} declares {declared.Extension.FullName} as a service "
                        + $"extension, but it does not implement {nameof(IServiceExtension)}.");
                }

                found.Add((assembly.GetName().Name!, declared.Extension));
            }

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (seen.Contains(reference.Name!) || !MayDeclare(reference))
                {
                    continue;
                }

                pending.Push(Assembly.Load(reference));
            }
        }

        return [.. found
            .OrderBy(each => each.Assembly, StringComparer.Ordinal)
            .ThenBy(each => each.Extension.FullName, StringComparer.Ordinal)
            .Select(each => each.Extension)
            .Distinct()];
    }

    /// <remarks>
    /// Whether an assembly declares an extension can only be read by loading it, so the question
    /// is not asked of the framework or the runtime, which never will.
    /// </remarks>
    private static bool MayDeclare(AssemblyName reference)
    {
        var name = reference.Name ?? string.Empty;

        return !name.StartsWith("System", StringComparison.Ordinal)
            && !name.StartsWith("Microsoft", StringComparison.Ordinal)
            && name != "netstandard"
            && name != "mscorlib";
    }
}
