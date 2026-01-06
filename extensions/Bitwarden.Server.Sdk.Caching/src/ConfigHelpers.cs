using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Caching;

internal static class ConfigHelpers
{
    public static bool TryGetDefinedValue(IConfigurationSection primarySection, IConfigurationSection defaultSection, string key, out string? value)
    {
        if (primarySection.GetChildren().Any(s => s.Key == key))
        {
            // There is an explicit value defined for this key in the given section
            value = primarySection.GetValue<string?>(key);
            return true;
        }

        if (defaultSection.GetChildren().Any(s => s.Key == key))
        {
            value = defaultSection.GetValue<string?>(key);
            return true;
        }

        value = null;
        return false;
    }
}
