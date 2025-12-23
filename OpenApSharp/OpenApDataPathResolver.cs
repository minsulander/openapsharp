using System;
using System.IO;

namespace OpenApSharp;

/// <summary>
/// Resolves paths to the original OpenAP Python data directory (openap/data/...).
/// It walks up from the current base directory until it finds an "openap/data" folder.
/// </summary>
internal static class OpenApDataPathResolver {
    private static readonly Lazy<string> _openApDataRoot = new(LocateOpenApDataRoot);

    /// <summary>
    /// Gets the full path under the OpenAP data root for the given relative segments.
    /// Example: GetPath(\"aircraft\", \"a320.yml\").
    /// </summary>
    public static string GetPath(params string[] segments) {
        var root = _openApDataRoot.Value;
        return Path.Combine(root, Path.Combine(segments));
    }

    private static string LocateOpenApDataRoot() {
        var dir = AppContext.BaseDirectory;

        for (var i = 0; i < 10 && dir is not null; i++) {
            var candidate = Path.Combine(dir, "openap", "data");
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir, "openap", "openap", "data");
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir, "openapsharp", "openap", "openap", "data");
            if (Directory.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Unable to locate 'openap/data' directory from current base directory.");
    }
}


