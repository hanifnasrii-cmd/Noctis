using System.Net;
using System.Text;
using Noctis.Models;
using Noctis.Services;
using Xunit;

namespace Noctis.Tests;

public class ArtistImageServiceTests
{
    [Fact]
    public async Task FetchAndCacheAsync_PrefersExactDeezerArtistMatch()
    {
        using var persistence = new TestPersistenceService();
        var wrongImageUrl = "https://images.example/wrong.jpg";
        var futureImageUrl = "https://images.example/future.jpg";
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.deezer.com/search/artist", StringComparison.Ordinal))
            {
                return JsonResponse($$"""
                {
                  "data": [
                    { "name": "Future Islands", "picture_big": "{{wrongImageUrl}}" },
                    { "name": "Future", "picture_xl": "{{futureImageUrl}}" }
                  ]
                }
                """);
            }

            return ImageResponse();
        });
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Future" };

        await service.FetchAndCacheAsync(new[] { artist });

        Assert.Equal(futureImageUrl, handler.RequestedImageUrls.Single());
        Assert.False(string.IsNullOrWhiteSpace(artist.ImagePath));
        Assert.True(File.Exists(artist.ImagePath));
    }

    /// <summary>09-15: the artist page shows Deezer's fan count. The portrait search
    /// records the chosen account's nb_fan beside the portrait (no extra request), the
    /// real account wins over a same-name impostor, and a later ask reads the sidecar.</summary>
    [Fact]
    public async Task FetchAndCacheAsync_RecordsTheChosenAccountsFanCount_AndGetFanCountReadsIt()
    {
        using var persistence = new TestPersistenceService();
        var searches = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.deezer.com/search/artist", StringComparison.Ordinal))
            {
                searches++;
                return JsonResponse("""
                {
                  "data": [
                    { "name": "Bad Bunny", "nb_fan": 6, "nb_album": 1, "picture_xl": "https://images.example/fake.jpg" },
                    { "name": "Bad Bunny", "nb_fan": 7994069, "nb_album": 88, "picture_xl": "https://images.example/real.jpg" }
                  ]
                }
                """);
            }
            return ImageResponse();
        });
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Bad Bunny" };

        Assert.Null(service.TryGetCachedFanCount(artist.Id));
        await service.FetchAndCacheAsync(new[] { artist });

        Assert.Equal("https://images.example/real.jpg", handler.RequestedImageUrls.Single());
        Assert.Equal(7994069, service.TryGetCachedFanCount(artist.Id));
        Assert.Equal(1, searches);

        // Fresh sidecar: served from disk, no second search.
        Assert.Equal(7994069, await service.GetFanCountAsync(artist.Id, artist.Name));
        Assert.Equal(1, searches);
    }

    [Fact]
    public async Task GetFanCountAsync_LooksTheArtistUpWhenNothingIsRecorded()
    {
        using var persistence = new TestPersistenceService();
        var handler = new StubHttpMessageHandler(request => JsonResponse("""
            { "data": [ { "name": "Aventura", "nb_fan": 1234567, "picture_xl": "https://images.example/a.jpg" } ] }
            """));
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var id = Guid.NewGuid();

        Assert.Equal(1234567, await service.GetFanCountAsync(id, "Aventura"));
        Assert.Equal(1234567, service.TryGetCachedFanCount(id));
        Assert.Empty(handler.RequestedImageUrls); // the count alone never downloads a photo
    }

    [Fact]
    public async Task FetchAndCacheAsync_QueuesConcurrentRequests()
    {
        using var persistence = new TestPersistenceService();
        var firstImageRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstImage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.deezer.com/search/artist", StringComparison.Ordinal))
            {
                var artistName = Uri.UnescapeDataString(request.RequestUri.Query)
                    .Contains("Second", StringComparison.OrdinalIgnoreCase)
                    ? "Second"
                    : "First";
                return JsonResponse($$"""
                { "data": [ { "name": "{{artistName}}", "picture_big": "https://images.example/{{artistName}}.jpg" } ] }
                """);
            }

            if (url.Contains("/First.jpg", StringComparison.Ordinal))
            {
                firstImageRequested.SetResult();
                await releaseFirstImage.Task;
            }

            return ImageResponse();
        });
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var first = new Artist { Id = Guid.NewGuid(), Name = "First" };
        var second = new Artist { Id = Guid.NewGuid(), Name = "Second" };

        var firstFetch = service.FetchAndCacheAsync(new[] { first });
        await firstImageRequested.Task;
        var secondFetch = service.FetchAndCacheAsync(new[] { second });
        releaseFirstImage.SetResult();
        await Task.WhenAll(firstFetch, secondFetch);

        Assert.True(File.Exists(first.ImagePath));
        Assert.True(File.Exists(second.ImagePath));
    }

    // ── Refresh sweep: cached portraits follow the service ──

    private static StubHttpMessageHandler DeezerHandler(string name, string imageUrl, byte fill = 0x11)
        => new(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.deezer.com/search/artist", StringComparison.Ordinal))
                return JsonResponse($$"""{ "data": [ { "name": "{{name}}", "picture_xl": "{{imageUrl}}" } ] }""");
            return ImageResponse(fill);
        });

    private static string Sidecar(string url) => "v2\n" + url + "\n";

    private static string SidecarUrl(string artworkDir, Guid id)
        => File.ReadAllLines(Path.Combine(artworkDir, $"{id}.src"))[1].Trim();

    private static void AgeSidecar(string artworkDir, Guid id, string url)
    {
        var src = Path.Combine(artworkDir, $"{id}.src");
        File.WriteAllText(src, Sidecar(url));
        File.SetLastWriteTimeUtc(src, DateTime.UtcNow - ArtistImageService.RefreshInterval - TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task FetchAndCacheAsync_PrefersTheExactMatchWithMostFans()
    {
        // Real Deezer response shape for "Bad Bunny": an impostor with the same name is
        // listed FIRST with 6 fans; the real artist follows with millions.
        using var persistence = new TestPersistenceService();
        var handler = new StubHttpMessageHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://api.deezer.com/search/artist", StringComparison.Ordinal))
                return JsonResponse("""
                { "data": [
                    { "name": "Bad Bunny", "nb_fan": 6, "nb_album": 1, "picture_xl": "https://images.example/fake.jpg" },
                    { "name": "Bad Bunny", "nb_fan": 7988527, "nb_album": 88, "picture_xl": "https://images.example/real.jpg" },
                    { "name": "Bad Bunny Chapin", "nb_fan": 24, "nb_album": 1, "picture_xl": "https://images.example/chapin.jpg" }
                ] }
                """);
            return ImageResponse();
        });
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Bad Bunny" };

        await service.FetchAndCacheAsync(new[] { artist });

        Assert.Equal("https://images.example/real.jpg", handler.RequestedImageUrls.Single());
        Assert.Equal("https://images.example/real.jpg", SidecarUrl(ArtworkDir(persistence), artist.Id));
    }

    [Fact]
    public async Task FetchAndCacheAsync_OldFormatSidecarIsRecheckedImmediately()
    {
        using var persistence = new TestPersistenceService();
        var handler = DeezerHandler("Future", "https://images.example/real.jpg", fill: 0x22);
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Future" };
        var cachedPath = service.GetCachedImagePath(artist.Id);
        File.WriteAllBytes(cachedPath, ImageBytes(0x11));
        // Pre-v2 sidecar: bare URL, written a minute ago — would be "fresh" by time alone.
        File.WriteAllText(Path.Combine(ArtworkDir(persistence), $"{artist.Id}.src"), "https://images.example/fake.jpg");

        await service.FetchAndCacheAsync(new[] { artist });

        Assert.Equal("https://images.example/real.jpg", handler.RequestedImageUrls.Single());
        Assert.Equal(0x22, File.ReadAllBytes(cachedPath)[8]);
        Assert.Equal("https://images.example/real.jpg", SidecarUrl(ArtworkDir(persistence), artist.Id));
    }

    private static string ArtworkDir(TestPersistenceService p) => Path.Combine(p.DataDirectory, "artwork", "artists");

    [Fact]
    public async Task FetchAndCacheAsync_ReplacesCachedPortraitWhenServiceUrlChanged()
    {
        using var persistence = new TestPersistenceService();
        var handler = DeezerHandler("Future", "https://images.example/new.jpg", fill: 0x22);
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Future" };
        var cachedPath = service.GetCachedImagePath(artist.Id);
        File.WriteAllBytes(cachedPath, ImageBytes(0x11));
        AgeSidecar(ArtworkDir(persistence), artist.Id, "https://images.example/old.jpg");
        var ready = new List<string>();

        await service.FetchAndCacheAsync(new[] { artist }, (_, p) => ready.Add(p));

        Assert.Equal("https://images.example/new.jpg", handler.RequestedImageUrls.Single());
        Assert.Equal(0x22, File.ReadAllBytes(cachedPath)[8]);
        Assert.Equal("https://images.example/new.jpg", SidecarUrl(ArtworkDir(persistence), artist.Id));
        Assert.Contains(cachedPath, ready);
    }

    [Fact]
    public async Task FetchAndCacheAsync_UnchangedUrlOnlyStampsCheckTime()
    {
        using var persistence = new TestPersistenceService();
        var handler = DeezerHandler("Future", "https://images.example/same.jpg", fill: 0x22);
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Future", ImagePath = null };
        var cachedPath = service.GetCachedImagePath(artist.Id);
        File.WriteAllBytes(cachedPath, ImageBytes(0x11));
        AgeSidecar(ArtworkDir(persistence), artist.Id, "https://images.example/same.jpg");
        var src = Path.Combine(ArtworkDir(persistence), $"{artist.Id}.src");
        var before = File.GetLastWriteTimeUtc(src);

        await service.FetchAndCacheAsync(new[] { artist });

        Assert.Empty(handler.RequestedImageUrls);
        Assert.Equal(0x11, File.ReadAllBytes(cachedPath)[8]);
        Assert.True(File.GetLastWriteTimeUtc(src) > before);
    }

    [Fact]
    public async Task FetchAndCacheAsync_FreshSidecarMakesNoRequests()
    {
        using var persistence = new TestPersistenceService();
        var searches = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            searches++;
            return JsonResponse("""{ "data": [] }""");
        });
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Future" };
        File.WriteAllBytes(service.GetCachedImagePath(artist.Id), ImageBytes(0x11));
        File.WriteAllText(Path.Combine(ArtworkDir(persistence), $"{artist.Id}.src"), Sidecar("https://images.example/same.jpg"));

        await service.FetchAndCacheAsync(new[] { artist });

        Assert.Equal(0, searches);
    }

    [Fact]
    public async Task FetchAndCacheAsync_NeverTouchesCustomPortrait()
    {
        using var persistence = new TestPersistenceService();
        var searches = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            searches++;
            return JsonResponse("""{ "data": [ { "name": "Future", "picture_xl": "https://images.example/new.jpg" } ] }""");
        });
        var service = new ArtistImageService(new HttpClient(handler), persistence);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Future" };
        await service.SetCustomImageAsync(artist, ImageBytes(0x11));
        // Even a stale sidecar (should not exist for custom, but be defensive) must not trigger.
        AgeSidecar(ArtworkDir(persistence), artist.Id, "https://images.example/old.jpg");

        await service.FetchAndCacheAsync(new[] { artist });

        Assert.Equal(0, searches);
        Assert.Equal(0x11, File.ReadAllBytes(service.GetCachedImagePath(artist.Id))[8]);
    }

    private static byte[] ImageBytes(byte fill)
    {
        var bytes = new byte[6 * 1024];
        Array.Fill(bytes, fill);
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xE0;
        return bytes;
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage ImageResponse(byte fill = 0)
    {
        // Must exceed ArtistImageService's 5120-byte Last.fm-placeholder purge
        // threshold, otherwise the background purge task races the cache write
        // and deletes the file before the test asserts File.Exists. Starts with
        // JPEG magic bytes so it passes the HttpSafety.LooksLikeImage gate.
        var bytes = ImageBytes(fill);
        return new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new("image/jpeg") }
            }
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public List<string> RequestedImageUrls { get; } = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "images.example")
                RequestedImageUrls.Add(request.RequestUri.ToString());

            return await _handler(request);
        }
    }
}
