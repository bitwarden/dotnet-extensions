using System.Reflection;
using System.Runtime.InteropServices;

namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Locates the native Rust SDK library for the generated bindings.
/// </summary>
/// <remarks>
/// NuGet consumers resolve <c>runtimes/&lt;rid&gt;/native/</c> through deps.json. ProjectReference
/// consumers, such as this repo's tests, get the same layout copied to their output but no deps.json
/// entry, so the runtime never looks there. This resolver probes that path as a fallback:
/// <code>
/// bin/runtimes/osx-arm64/native/libbitwarden_uniffi_server.dylib
/// </code>
/// </remarks>
internal static class NativeLibraryResolver
{
    private const string LibraryName = "bitwarden_uniffi_server";

    /// <summary>
    /// Registers the resolver. Call once, before the first generated P/Invoke binds.
    /// </summary>
    internal static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }

        // Default probing first: covers NuGet consumers and published apps.
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var handle))
        {
            return handle;
        }

        var path = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            RuntimeInformation.RuntimeIdentifier,
            "native",
            FileName());

        return NativeLibrary.TryLoad(path, out handle) ? handle : IntPtr.Zero;
    }

    // Platform file name, e.g. libbitwarden_uniffi_server.so on Linux.
    private static string FileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{LibraryName}.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"lib{LibraryName}.dylib";
        }

        return $"lib{LibraryName}.so";
    }
}
