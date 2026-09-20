namespace Noctis.Helpers;

/// <summary>
/// Finds the music video that belongs to an audio file (Discord, aaron 09-15): a clip
/// with the same base name next to the song, or in a <c>videos</c> folder beside it.
/// </summary>
public static class MusicVideoLocator
{
    public static readonly string[] Extensions = { ".mp4", ".m4v", ".mov", ".mkv", ".webm" };
    private static readonly string[] SubFolders = { "videos", "Videos", "video", "Video" };

    public static string? Find(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath)) return null;
        string? folder; string stem;
        try
        {
            folder = Path.GetDirectoryName(audioPath);
            stem = Path.GetFileNameWithoutExtension(audioPath);
        }
        catch { return null; }
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(stem)) return null;

        foreach (var ext in Extensions)
        {
            var sibling = Path.Combine(folder, stem + ext);
            if (File.Exists(sibling)) return sibling;
        }
        foreach (var sub in SubFolders)
        {
            var dir = Path.Combine(folder, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var ext in Extensions)
            {
                var candidate = Path.Combine(dir, stem + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
