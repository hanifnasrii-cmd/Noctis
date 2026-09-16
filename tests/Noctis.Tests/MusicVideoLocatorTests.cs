using Noctis.Helpers;
using Xunit;

namespace Noctis.Tests;

/// <summary>Music video discovery: same-name clip beside the song, or in a videos folder.</summary>
public class MusicVideoLocatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NoctisTests", Guid.NewGuid().ToString("N"));
    public MusicVideoLocatorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Audio(string name = "09 - Runaway.flac")
    {
        var p = Path.Combine(_dir, name); File.WriteAllBytes(p, new byte[] { 1 }); return p;
    }

    [Fact]
    public void Sibling_SameStem_IsFound()
    {
        var audio = Audio();
        var video = Path.Combine(_dir, "09 - Runaway.mp4"); File.WriteAllBytes(video, new byte[] { 1 });
        Assert.Equal(video, MusicVideoLocator.Find(audio));
    }

    [Fact]
    public void VideosSubfolder_IsFound()
    {
        var audio = Audio();
        Directory.CreateDirectory(Path.Combine(_dir, "videos"));
        var video = Path.Combine(_dir, "videos", "09 - Runaway.mkv"); File.WriteAllBytes(video, new byte[] { 1 });
        Assert.Equal(video, MusicVideoLocator.Find(audio));
    }

    [Fact]
    public void DifferentStem_OrNothing_ReturnsNull()
    {
        var audio = Audio();
        File.WriteAllBytes(Path.Combine(_dir, "10 - Hell of a Life.mp4"), new byte[] { 1 });
        Assert.Null(MusicVideoLocator.Find(audio));
        Assert.Null(MusicVideoLocator.Find(null));
        Assert.Null(MusicVideoLocator.Find(""));
    }
}
