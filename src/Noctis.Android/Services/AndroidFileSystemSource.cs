using Android.Content;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using Microsoft.Win32.SafeHandles;
using Noctis.Services;
using AUri = Android.Net.Uri;

namespace Noctis.Android.Services;

/// <summary>
/// IFileSystemSource over a Storage Access Framework tree. Roots are persisted tree URIs
/// (AppSettings.MusicFolders); entries are document URIs, which become Track.FilePath and
/// are what ExoPlayer plays. Directories are listed with one DocumentsContract query each
/// (DocumentFile.ListFiles would issue one query per child). Folder rules (excludedRoots)
/// are a desktop feature and are ignored here.
/// </summary>
public sealed class AndroidFileSystemSource : IFileSystemSource
{
    private static readonly string[] Projection =
    {
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified,
    };

    private readonly Context _context;
    private readonly ContentResolver _resolver;

    public AndroidFileSystemSource(Context context)
    {
        _context = context;
        _resolver = context.ContentResolver ?? throw new InvalidOperationException("No ContentResolver");
    }

    public bool RootExists(string root)
    {
        try
        {
            var tree = DocumentFile.FromTreeUri(_context, AUri.Parse(root)!);
            return tree != null && tree.Exists() && tree.CanRead();
        }
        catch (Exception ex)
        {
            // A revoked grant surfaces as SecurityException; "unavailable" is the right answer.
            DebugLog.Write("Library", $"SAF root check failed for {root}: {ex.Message}");
            return false;
        }
    }

    public IEnumerable<ScanEntry> EnumerateAudioFiles(
        string root,
        IReadOnlyCollection<string> excludedRoots,
        IReadOnlySet<string> ignoredFolderNames,
        Action<string> reportFailedDirectory)
    {
        var treeUri = AUri.Parse(root)!;
        var stack = new Stack<string>();
        stack.Push(DocumentsContract.GetTreeDocumentId(treeUri)!);

        while (stack.Count > 0)
        {
            var docId = stack.Pop();
            var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, docId)!;
            // Reported on failure as the directory's DOCUMENT uri: every track under it has a
            // document uri starting with this one, which is how LibraryService.PathIsUnder
            // keeps those known tracks instead of dropping them.
            var directoryUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, docId)!.ToString()!;

            // Materialize the listing first: a yield inside try/catch is not allowed, and a
            // cursor error mid-listing must be reported, not thrown into the scan.
            var rows = new List<(string Id, string Name, string Mime, long Size, long ModifiedMs)>();
            try
            {
                using var cursor = _resolver.Query(childrenUri, Projection, null, null, null);
                if (cursor == null)
                {
                    reportFailedDirectory(directoryUri);
                    continue;
                }
                while (cursor.MoveToNext())
                {
                    rows.Add((
                        cursor.GetString(0) ?? string.Empty,
                        cursor.GetString(1) ?? string.Empty,
                        cursor.GetString(2) ?? string.Empty,
                        cursor.IsNull(3) ? 0 : cursor.GetLong(3),
                        cursor.IsNull(4) ? 0 : cursor.GetLong(4)));
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write("Library", $"SAF listing failed for {childrenUri}: {ex.Message}");
                // Document uri, not childrenUri: every track under this directory has a
                // document uri starting with directoryUri, which is what
                // LibraryService.PathIsUnder matches to keep those known tracks.
                reportFailedDirectory(directoryUri);
                continue;
            }

            foreach (var row in rows)
            {
                if (row.Id.Length == 0) continue;
                if (row.Mime == DocumentsContract.Document.MimeTypeDir)
                {
                    if (ignoredFolderNames.Contains(row.Name.ToLowerInvariant())) continue;
                    stack.Push(row.Id);
                    continue;
                }
                if (!MetadataService.SupportedExtensions.Contains(Path.GetExtension(row.Name))) continue;

                var docUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, row.Id)!;
                var path = docUri.ToString()!;
                var modified = DateTimeOffset.FromUnixTimeMilliseconds(row.ModifiedMs).UtcDateTime;
                yield return new ScanEntry(path, row.Name, row.Size, modified, LocalPath: null, OpenRead: () => OpenSeekable(docUri));
            }
        }
    }

    /// <summary>
    /// A seekable read stream over the document's file descriptor. OpenInputStream is
    /// forward-only and TagLib seeks, so the descriptor is detached into a FileStream that
    /// owns and closes it.
    /// </summary>
    private Stream OpenSeekable(AUri uri)
    {
        var pfd = _resolver.OpenFileDescriptor(uri, "r") ?? throw new IOException($"Cannot open {uri}");
        var fd = pfd.DetachFd();
        pfd.Dispose();
        return new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.Read, bufferSize: 1 << 16);
    }
}
