using System;
using System.IO;
using Noctis.Helpers;
using SkiaSharp;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The artwork cache used to decode covers through Bitmap.DecodeToWidth(Stream), which
/// rents a file-sized buffer from ArrayPool&lt;byte&gt;.Shared per decode and leaves it
/// parked in the pool (224 MB after one screen of covers, never collected). Covers now
/// go through Skia's own file stream; these pin that the replacement decodes to the
/// same size Avalonia did, for both scalable (JPEG) and non-scalable (PNG) codecs.
/// </summary>
public class SkiaArtworkDecoderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "noctis-decoder-" + Guid.NewGuid().ToString("N"));

    public SkiaArtworkDecoderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string name, int width, int height, SKEncodedImageFormat format)
    {
        using var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.DarkOrange);
            canvas.DrawRect(new SKRect(0, 0, width / 2f, height / 2f), new SKPaint { Color = SKColors.Teal });
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(format, 90);
        var path = Path.Combine(_dir, name);
        using var file = File.Create(path);
        data.SaveTo(file);
        return path;
    }

    [Fact]
    public void DecodeToWidth_Png_KeepsAspect_AndLandsOnTheRequestedWidth()
    {
        var path = Write("cover.jpg", 300, 200, SKEncodedImageFormat.Png); // the store names PNGs .jpg too
        using var bmp = SkiaArtworkDecoder.DecodeToWidth(path, 100);
        Assert.NotNull(bmp);
        Assert.Equal(100, bmp!.Width);
        Assert.Equal(67, bmp.Height);
        Assert.Equal(SKColorType.Bgra8888, bmp.ColorType);
    }

    [Fact]
    public void DecodeToWidth_Jpeg_UsesTheCodecDownscale_ThenExactResize()
    {
        var path = Write("cover.jpg", 800, 600, SKEncodedImageFormat.Jpeg);
        using var bmp = SkiaArtworkDecoder.DecodeToWidth(path, 200);
        Assert.NotNull(bmp);
        Assert.Equal(200, bmp!.Width);
        Assert.Equal(150, bmp.Height);
        // Colours survive the scaled decode: top-left quadrant teal, bottom-right orange.
        var tl = bmp.GetPixel(10, 10);
        var br = bmp.GetPixel(190, 140);
        Assert.True(tl.Green > tl.Red, $"top-left {tl} should be teal");
        Assert.True(br.Red > br.Blue, $"bottom-right {br} should be orange");
    }

    [Fact]
    public void DecodeToWidth_NeverUpscales_PastTheSourceWidth()
    {
        var path = Write("small.png", 64, 64, SKEncodedImageFormat.Png);
        using var bmp = SkiaArtworkDecoder.DecodeToWidth(path, 512);
        Assert.NotNull(bmp);
        Assert.Equal(64, bmp!.Width);
    }

    [Fact]
    public void DecodeToWidth_OpensNonAsciiPaths()
    {
        var path = Write("Álbum — Niño 日本.png", 120, 90, SKEncodedImageFormat.Png);
        using var bmp = SkiaArtworkDecoder.DecodeToWidth(path, 60);
        Assert.NotNull(bmp);
        Assert.Equal(60, bmp!.Width);
        Assert.Equal(45, bmp.Height);
    }

    [Fact]
    public void DecodeToWidth_MissingOrGarbage_ReturnsNull()
    {
        Assert.Null(SkiaArtworkDecoder.DecodeToWidth(Path.Combine(_dir, "nope.jpg"), 100));
        var junk = Path.Combine(_dir, "junk.jpg");
        File.WriteAllText(junk, "not an image");
        Assert.Null(SkiaArtworkDecoder.DecodeToWidth(junk, 100));
    }

    [Fact]
    public void DecodeSubsampled_StaysUnderTheDimension_ByPowersOfTwo()
    {
        var path = Write("big.jpg", 800, 600, SKEncodedImageFormat.Jpeg);
        using var bmp = SkiaArtworkDecoder.DecodeSubsampled(path, 150);
        Assert.NotNull(bmp);
        Assert.True(bmp!.Width <= 150, $"width {bmp.Width}");
        Assert.Equal(100, bmp.Width); // 800 / 8
    }
}
