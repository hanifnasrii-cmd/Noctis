namespace Noctis.Services;

/// <summary>
/// One audio file found by a scan. <see cref="Path"/> is the identity stored in
/// <see cref="Models.Track.FilePath"/> (a local path, or a content:// document URI on
/// Android); <see cref="Name"/> is the display file name (extension, untitled fallback);
/// <see cref="LocalPath"/> is non-null only when the file is reachable by path, which
/// unlocks the desktop-only extras (ffprobe metrics, folder art, tag writes).
/// <see cref="OpenRead"/> must return a seekable stream: TagLib seeks.
/// </summary>
public sealed record ScanEntry(
    string Path,
    string Name,
    long Length,
    DateTime LastWriteTimeUtc,
    string? LocalPath,
    Func<Stream> OpenRead);

/// <summary>
/// Where a library scan gets its files from. Desktop: <see cref="LocalFileSystemSource"/>
/// (directory walk). Android: a Storage Access Framework tree, where nothing has a path.
/// Keeps <see cref="LibraryService"/> path-agnostic.
/// </summary>
public interface IFileSystemSource
{
    /// <summary>False when a configured root is not reachable right now (unplugged drive,
    /// revoked SAF grant). The scan then aborts rather than treating the root as empty.</summary>
    bool RootExists(string root);

    /// <summary>
    /// Streams every supported audio file under <paramref name="root"/>, depth-first,
    /// skipping folders whose name is in <paramref name="ignoredFolderNames"/> (lower-case)
    /// and anything under <paramref name="excludedRoots"/> (normalized, from folder rules).
    /// A folder that cannot be listed is reported through
    /// <paramref name="reportFailedDirectory"/> and skipped, never thrown.
    /// </summary>
    IEnumerable<ScanEntry> EnumerateAudioFiles(
        string root,
        IReadOnlyCollection<string> excludedRoots,
        IReadOnlySet<string> ignoredFolderNames,
        Action<string> reportFailedDirectory);
}
