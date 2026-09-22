namespace Noctis.Services;

/// <summary>The desktop scan source: a recursive directory walk (moved out of LibraryService unchanged).</summary>
public sealed class LocalFileSystemSource : IFileSystemSource
{
    public bool RootExists(string root) => Directory.Exists(root);

    public IEnumerable<ScanEntry> EnumerateAudioFiles(
        string root,
        IReadOnlyCollection<string> excludedRoots,
        IReadOnlySet<string> ignoredFolderNames,
        Action<string> reportFailedDirectory)
    {
        var stack = new Stack<string>();
        // Cycle guard keyed on the RESOLVED path: a junction/symlink pointing at
        // an ancestor re-enters the tree under an ever-growing logical path, so
        // the walked path alone never repeats and the DFS loops forever.
        var visited = new HashSet<string>(
            OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (IsUnderAnyRoot(current, excludedRoots)) continue;
            if (!visited.Add(ResolveRealPath(current))) continue;

            List<string> directories;
            List<string> files;
            try
            {
                // Materialized inside the try: the enumerables are lazy, so an I/O error
                // surfacing mid-listing (not just at open) would otherwise escape this
                // catch and abort the entire scan pipeline.
                directories = Directory.EnumerateDirectories(current).ToList();
                files = Directory.EnumerateFiles(current).ToList();
            }
            catch
            {
                // "Couldn't list" is not "doesn't exist": LibraryService keeps the known
                // tracks under a reported directory instead of dropping them.
                reportFailedDirectory(current);
                continue;
            }

            foreach (var dir in directories)
            {
                var name = Path.GetFileName(dir);
                if (ignoredFolderNames.Contains(name.ToLowerInvariant())) continue;
                if (IsUnderAnyRoot(dir, excludedRoots)) continue;
                stack.Push(dir);
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (!MetadataService.SupportedExtensions.Contains(ext)) continue;
                FileInfo fi;
                try { fi = new FileInfo(file); } catch { continue; }
                var captured = file;
                yield return new ScanEntry(file, fi.Name, fi.Length, fi.LastWriteTimeUtc, file, () => File.OpenRead(captured));
            }
        }
    }

    // Symlinked/junctioned directories are followed (symlinked music libraries are
    // legitimate); resolving to the final target is what makes the visited-set
    // above detect a loop regardless of the logical path it was reached through.
    private static string ResolveRealPath(string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
            return info.FullName;
        }
        catch
        {
            return dir;
        }
    }

    private static bool IsUnderAnyRoot(string path, IReadOnlyCollection<string> roots)
    {
        var normalized = LibraryService.NormalizePath(path);
        foreach (var root in roots)
        {
            if (LibraryService.IsUnderRoot(normalized, root))
                return true;
        }
        return false;
    }
}
