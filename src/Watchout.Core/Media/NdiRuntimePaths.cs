namespace Watchout.Core.Media;

public static class NdiRuntimePaths
{
    public const string DllName = "Processing.NDI.Lib.x64.dll";

    public static readonly string[] EnvKeys =
    [
        "NDI_RUNTIME_DIR_V6",
        "NDI_RUNTIME_DIR_V5",
        "NDI_RUNTIME_DIR_V4",
        "NDILIB_REDIST_FOLDER",
    ];

    public static IReadOnlyList<string> Candidates(
        string? programFiles,
        IReadOnlyDictionary<string, string?> env,
        IEnumerable<string>? extraDirectories = null)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFile(string? file)
        {
            if (string.IsNullOrWhiteSpace(file)) return;
            var full = Path.GetFullPath(file);
            if (seen.Add(full)) paths.Add(full);
        }

        void AddDir(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            AddFile(Path.Combine(dir, DllName));
        }

        foreach (var key in EnvKeys)
            if (env.TryGetValue(key, out var value)) AddDir(value);

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var ndi = Path.Combine(programFiles, "NDI");
            AddDir(Path.Combine(ndi, "NDI 6 Runtime", "v6"));
            AddDir(Path.Combine(ndi, "NDI 6 Tools", "Runtime"));
            AddDir(Path.Combine(ndi, "NDI 5 Runtime", "v5"));
            AddDir(Path.Combine(ndi, "NDI 5 Tools", "Runtime"));
            AddDir(Path.Combine(ndi, "NDI Runtime", "v5"));
            AddDir(Path.Combine(ndi, "NDI 6 Runtime"));
            AddDir(Path.Combine(ndi, "NDI 5 Runtime"));
        }

        if (extraDirectories is not null)
            foreach (var dir in extraDirectories)
                AddDir(dir);

        return paths;
    }
}
