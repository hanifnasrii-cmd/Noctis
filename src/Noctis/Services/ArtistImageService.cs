using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noctis.Models;

namespace Noctis.Services;

public class ArtistImageService
{
    private const string DeezerSearchUrl = "https://api.deezer.com/search/artist";
    private const int DeezerSearchLimit = 10;
    private readonly HttpClient _http;
    private readonly string _artistArtworkDir;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private readonly Dictionary<string, DateTime> _failedArtistCooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan FailedArtistCooldown = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Delay before each artist's network work. Deezer allows 50 requests per 5 seconds
    /// and every artist costs up to two (search + image), so 120ms keeps a full-library
    /// sweep comfortably inside the budget.
    /// </summary>
    private const int RequestPacingMs = 120;

    /// <summary>Library the artist's own track titles are read from when several Deezer
    /// accounts share the name; null (tests, tools) falls back to fan count alone.</summary>
    private readonly ILibraryService? _library;

    public ArtistImageService(HttpClient http, IPersistenceService persistence, ILibraryService? library = null)
    {
        _http = http;
        _library = library;
        _artistArtworkDir = Path.Combine(persistence.DataDirectory, "artwork", "artists");
        Directory.CreateDirectory(_artistArtworkDir);

        // Defer the placeholder purge off the DI resolution path so it doesn't
        // enumerate the artwork directory on the UI thread during startup.
        _ = Task.Run(PurgeLastFmPlaceholders);
    }

    /// <summary>
    /// One-time cleanup: remove old Last.fm placeholder images (all ≤5KB, same generic star icon)
    /// so they get re-fetched from Deezer with real artist photos.
    /// </summary>
    private void PurgeLastFmPlaceholders()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_artistArtworkDir, "*.jpg"))
            {
                var info = new FileInfo(file);
                if (info.Length > 0 && info.Length <= 5120)
                    info.Delete();
            }
        }
        catch { }
    }

    public string GetCachedImagePath(Guid artistId)
        => Path.Combine(_artistArtworkDir, $"{artistId}.jpg");

    public bool HasCachedImage(Guid artistId)
        => File.Exists(GetCachedImagePath(artistId));

    /// <summary>Sentinel marking an artist whose image the user explicitly removed,
    /// so the background fetcher leaves it blank instead of re-downloading.</summary>
    private string GetRemovedMarkerPath(Guid artistId)
        => Path.Combine(_artistArtworkDir, $"{artistId}.removed");

    public bool IsImageRemoved(Guid artistId)
        => File.Exists(GetRemovedMarkerPath(artistId));

    // ── Deezer fan count (artist page "7,994,069 fans", 09-15) ──
    // The portrait search already reads nb_fan to tell the real account from a same-name
    // impostor, so the number is recorded beside the portrait for free; an artist whose
    // portrait predates this (or is custom/removed) gets one paced search of its own.

    /// <summary>How long a recorded fan count is shown before Deezer is asked again.</summary>
    internal static readonly TimeSpan FanCountRefreshInterval = TimeSpan.FromDays(7);

    private string GetFanCountPath(Guid artistId)
        => Path.Combine(_artistArtworkDir, $"{artistId}.fans");

    /// <summary>The last recorded Deezer fan count, or null when none was ever recorded.</summary>
    public long? TryGetCachedFanCount(Guid artistId)
    {
        try
        {
            var path = GetFanCountPath(artistId);
            if (!File.Exists(path)) return null;
            return long.TryParse(File.ReadAllText(path).Trim(), out var fans) && fans >= 0 ? fans : null;
        }
        catch
        {
            return null;
        }
    }

    internal void WriteFanCount(Guid artistId, long fans)
    {
        if (fans < 0) return;
        try
        {
            var path = GetFanCountPath(artistId);
            File.WriteAllText(path, fans.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArtistImage] Failed to record fan count for {artistId}: {ex.Message}");
        }
    }

    /// <summary>
    /// The artist's Deezer fan count: the recorded one while it is fresh, else one search
    /// (same match ranking as the portrait). Never throws except for cancellation; a
    /// failed lookup returns whatever was recorded before, or null.
    /// </summary>
    public async Task<long?> GetFanCountAsync(Guid artistId, string artistName, CancellationToken ct = default)
    {
        var cached = TryGetCachedFanCount(artistId);
        if (cached != null)
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(GetFanCountPath(artistId)) < FanCountRefreshInterval)
                    return cached;
            }
            catch { return cached; }
        }

        if (string.IsNullOrWhiteSpace(artistName) || artistName == "Unknown Artist")
            return cached;

        try
        {
            await Task.Delay(RequestPacingMs, ct).ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(DownloadTimeout);
            var match = await FindDeezerArtistAsync(artistName.Trim(), timeoutCts.Token).ConfigureAwait(false);
            if (match == null) return cached;
            WriteFanCount(artistId, match.Fans);
            return match.Fans;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArtistImage] Fan count lookup failed for '{artistName}': {ex.Message}");
            return cached;
        }
    }

    /// <summary>Sentinel marking a user-picked portrait: the refresh sweep must never
    /// replace it with whatever the service currently serves.</summary>
    private string GetCustomMarkerPath(Guid artistId)
        => Path.Combine(_artistArtworkDir, $"{artistId}.custom");

    /// <summary>
    /// Sidecar holding the URL the cached portrait was downloaded from. Its write time
    /// doubles as "last checked against the service": the refresh sweep re-queries the
    /// service once the sidecar is older than <see cref="RefreshInterval"/>, and only
    /// downloads when the URL changed (Deezer image URLs carry the image hash, so a new
    /// photo is a new URL). A cached portrait with no sidecar predates this and gets
    /// one download-and-compare on its first sweep.
    /// </summary>
    private string GetSourceMarkerPath(Guid artistId)
        => Path.Combine(_artistArtworkDir, $"{artistId}.src");

    /// <summary>How long a cached portrait is trusted before the service is asked again.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Saves a user-picked image as the artist's portrait, overriding any auto-fetched
    /// art and clearing a prior "removed" marker. Returns the cached path, or null on failure.
    /// </summary>
    public async Task<string?> SetCustomImageAsync(Artist artist, byte[] imageData)
    {
        if (imageData == null || imageData.Length == 0)
            return null;

        var cachedPath = GetCachedImagePath(artist.Id);
        try
        {
            await WriteImageAtomicAsync(cachedPath, imageData);

            var marker = GetRemovedMarkerPath(artist.Id);
            if (File.Exists(marker))
                File.Delete(marker);
            File.WriteAllText(GetCustomMarkerPath(artist.Id), string.Empty);
            TryDelete(GetSourceMarkerPath(artist.Id));

            artist.ImagePath = cachedPath;
            return cachedPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArtistImage] Failed to set custom image for '{artist.Name}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Removes the artist's portrait (custom or auto-fetched) and marks it so the
    /// background fetcher won't re-download it. The grid falls back to the placeholder.
    /// </summary>
    public void RemoveImage(Artist artist)
    {
        try
        {
            var cachedPath = GetCachedImagePath(artist.Id);
            if (File.Exists(cachedPath))
                File.Delete(cachedPath);

            File.WriteAllText(GetRemovedMarkerPath(artist.Id), string.Empty);
            TryDelete(GetCustomMarkerPath(artist.Id));
            TryDelete(GetSourceMarkerPath(artist.Id));
            artist.ImagePath = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArtistImage] Failed to remove image for '{artist.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches and caches artist images in the background, and keeps cached ones current:
    /// a service-fetched portrait whose last check is older than <see cref="RefreshInterval"/>
    /// is looked up again and replaced when the service now serves a different photo.
    /// Calls onImageReady for each artist that gets a new (or replaced) image; the shared
    /// bitmap cache is invalidated for a replaced file so live tiles repaint it.
    /// </summary>
    public async Task FetchAndCacheAsync(IReadOnlyList<Artist> artists, Action<Artist, string>? onImageReady = null)
    {
        // Get off the caller's thread before doing anything.
        //
        // The Artists tab calls this fire-and-forget from Refresh(). With an uncontended
        // semaphore the WaitAsync below completes synchronously, and with no
        // ConfigureAwait(false) the continuation stayed on the UI thread — so for every
        // already-cached artist the loop never hit a real await and just ran two blocking
        // File.Exists calls inline. On a 4,000-artist library that was thousands of stats
        // on the UI thread before the tab could paint.
        await Task.Yield();

        await _fetchGate.WaitAsync().ConfigureAwait(false);

        try
        {
            var artistsByName = artists
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var artist in artists)
            {
                var cachedPath = GetCachedImagePath(artist.Id);

                // Honor an explicit user removal: keep the portrait blank instead of
                // re-downloading. Checked before the cache hit so a lingering file
                // (e.g. from a fetch that raced the removal) never resurfaces.
                if (File.Exists(GetRemovedMarkerPath(artist.Id)))
                {
                    if (artist.ImagePath != null)
                        artist.ImagePath = null;
                    continue;
                }

                // Already cached: surface it, then re-check it against the service if due.
                if (File.Exists(cachedPath))
                {
                    if (artist.ImagePath != cachedPath)
                    {
                        artist.ImagePath = cachedPath;
                        onImageReady?.Invoke(artist, cachedPath);
                    }

                    if (IsRefreshDue(artist))
                    {
                        await Task.Delay(RequestPacingMs).ConfigureAwait(false);
                        try
                        {
                            if (await TryRefreshCachedImageAsync(artist.Name.Trim(), artist.Id, cachedPath).ConfigureAwait(false))
                            {
                                ArtworkCache.Invalidate(cachedPath);
                                artist.ImagePath = cachedPath;
                                onImageReady?.Invoke(artist, cachedPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ArtistImage] Refresh check failed for '{artist.Name}': {ex.Message}");
                        }
                    }
                    continue;
                }

                // Skip unknown artists
                if (string.IsNullOrWhiteSpace(artist.Name) || artist.Name == "Unknown Artist")
                    continue;

                var artistName = artist.Name.Trim();
                if (_failedArtistCooldownUntil.TryGetValue(artistName, out var retryAt) &&
                    DateTime.UtcNow < retryAt)
                {
                    TryUsePrimaryArtistImageFallback(artist, artistsByName, onImageReady);
                    continue;
                }

                // Rate limit: Deezer allows 50 requests per 5 seconds. Paced BEFORE the
                // request, so it applies to every network call. It used to sit at the
                // very bottom of the loop, after the `continue`s for both success and
                // fallback — i.e. only on outright failure. On a first scan each
                // *successful* artist costs at least two requests (search + image) with
                // zero pacing, so a few hundred artists burst far past the limit, Deezer
                // started 429-ing, and that poisoned the whole sweep into the 15-minute
                // cooldown.
                await Task.Delay(RequestPacingMs).ConfigureAwait(false);

                try
                {
                    if (await TryDownloadAndSaveAsync(artistName, artist.Id, cachedPath).ConfigureAwait(false))
                    {
                        artist.ImagePath = cachedPath;
                        onImageReady?.Invoke(artist, cachedPath);
                        _failedArtistCooldownUntil.Remove(artistName);
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Download watchdog fired (or shutdown) — don't burn the cooldown.
                    Debug.WriteLine($"[ArtistImage] Timed out for '{artist.Name}'");
                    continue;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ArtistImage] Failed for '{artist.Name}': {ex.Message}");
                }

                if (TryUsePrimaryArtistImageFallback(artist, artistsByName, onImageReady))
                {
                    _failedArtistCooldownUntil.Remove(artistName);
                    continue;
                }

                _failedArtistCooldownUntil[artistName] = DateTime.UtcNow.Add(FailedArtistCooldown);
            }
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    /// <summary>
    /// Looks the artist up online and re-downloads their photo, clearing any prior
    /// "removed" marker so the restored image isn't suppressed on the next refresh.
    /// Use to bring a portrait back after Remove, or to refresh a custom one.
    /// Returns the cached path, or null if nothing was found.
    /// </summary>
    public async Task<string?> RefetchImageAsync(Artist artist)
    {
        if (artist == null || string.IsNullOrWhiteSpace(artist.Name) || artist.Name == "Unknown Artist")
            return null;

        var artistName = artist.Name.Trim();
        var cachedPath = GetCachedImagePath(artist.Id);

        // Explicit "find online" hands the portrait back to the service, so a custom
        // marker is cleared too and the refresh sweep resumes tracking it.
        TryDelete(GetRemovedMarkerPath(artist.Id));
        TryDelete(GetCustomMarkerPath(artist.Id));

        try
        {
            if (await TryDownloadAndSaveAsync(artistName, artist.Id, cachedPath))
            {
                ArtworkCache.Invalidate(cachedPath);
                artist.ImagePath = cachedPath;
                return cachedPath;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArtistImage] Refetch failed for '{artist.Name}': {ex.Message}");
        }

        return null;
    }

    /// <summary>Hard ceiling for a single artist-image download, headers plus body.</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Resolves the artist's Deezer photo and writes it to <paramref name="cachedPath"/>,
    /// recording the source URL beside it. Returns true if an image was downloaded and saved.
    /// </summary>
    private async Task<bool> TryDownloadAndSaveAsync(string artistName, Guid artistId, string cachedPath,
        CancellationToken ct = default)
    {
        // HttpClient.Timeout only covers up to the response headers when
        // ResponseHeadersRead is used, and no token was threaded through the body read —
        // so a stalled/half-open connection to the CDN blocked ReadBytesBoundedAsync with
        // nothing able to cancel it. Because FetchAndCacheAsync holds _fetchGate for the
        // whole sweep, one stalled socket blocked every later artist-image fetch for the
        // rest of the process lifetime, and shutdown couldn't cancel it either.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DownloadTimeout);
        var token = timeoutCts.Token;

        var match = await FindDeezerArtistAsync(artistName, token).ConfigureAwait(false);
        if (match == null)
            return false;
        WriteFanCount(artistId, match.Fans);

        var imageData = await DownloadImageBytesAsync(match.ImageUrl, token).ConfigureAwait(false);
        if (imageData == null)
            return false;

        await WriteImageAtomicAsync(cachedPath, imageData, token).ConfigureAwait(false);
        WriteSourceMarker(artistId, match.ImageUrl);
        return true;
    }

    /// <summary>
    /// A service-fetched portrait is due for a check once its source sidecar is older
    /// than <see cref="RefreshInterval"/> (or missing: a cache written before the sidecar
    /// existed). Custom portraits and unknown artists never are.
    /// </summary>
    private bool IsRefreshDue(Artist artist)
    {
        if (string.IsNullOrWhiteSpace(artist.Name) || artist.Name == "Unknown Artist")
            return false;
        if (File.Exists(GetCustomMarkerPath(artist.Id)))
            return false;

        var src = GetSourceMarkerPath(artist.Id);
        if (!File.Exists(src))
            return true;
        // A sidecar written by an older match-ranking (before the fan-count tie-break)
        // may record an impostor's photo: re-check it now, not in 24 hours.
        if (ReadSourceMarker(src) == null)
            return true;
        return DateTime.UtcNow - File.GetLastWriteTimeUtc(src) >= RefreshInterval;
    }

    /// <summary>
    /// Sidecar format version. Bump when the way a photo is CHOSEN changes, so every
    /// portrait picked by the old logic is re-evaluated on the next sweep.
    /// v2: exact-name ties broken by Deezer fan count (impostor accounts share the name).
    /// v3: names compared without diacritics ("Arcángel" is "Arcangel" on Deezer) and
    ///     same-name accounts verified against the library's own track titles.
    /// </summary>
    private const string SourceMarkerVersion = "v3";

    /// <summary>Returns the recorded URL, or null when the sidecar is missing/outdated.</summary>
    private static string? ReadSourceMarker(string src)
    {
        try
        {
            if (!File.Exists(src)) return null;
            var lines = File.ReadAllLines(src);
            if (lines.Length < 2 || lines[0].Trim() != SourceMarkerVersion) return null;
            var url = lines[1].Trim();
            return url.Length == 0 ? null : url;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Asks the service for the artist's current photo URL. Unchanged URL: only the
    /// check time is stamped, nothing is downloaded. Changed URL (or no recorded URL):
    /// the photo is downloaded and swapped in if its bytes differ from the cached file.
    /// Returns true when the cached file was replaced.
    /// </summary>
    private async Task<bool> TryRefreshCachedImageAsync(string artistName, Guid artistId, string cachedPath)
    {
        using var timeoutCts = new CancellationTokenSource(DownloadTimeout);
        var token = timeoutCts.Token;

        var match = await FindDeezerArtistAsync(artistName, token).ConfigureAwait(false);
        if (match == null)
            return false; // service outage / no match: keep what we have, retry next sweep
        WriteFanCount(artistId, match.Fans);
        var imageUrl = match.ImageUrl;

        var src = GetSourceMarkerPath(artistId);
        var knownUrl = ReadSourceMarker(src);
        if (string.Equals(knownUrl, imageUrl, StringComparison.Ordinal))
        {
            WriteSourceMarker(artistId, imageUrl); // bumps the check time
            return false;
        }

        var imageData = await DownloadImageBytesAsync(imageUrl, token).ConfigureAwait(false);
        if (imageData == null)
            return false;

        WriteSourceMarker(artistId, imageUrl);

        // No recorded URL means the file may already be this very photo.
        if (knownUrl == null && File.Exists(cachedPath))
        {
            var existing = await File.ReadAllBytesAsync(cachedPath, token).ConfigureAwait(false);
            if (existing.AsSpan().SequenceEqual(imageData))
                return false;
        }

        await WriteImageAtomicAsync(cachedPath, imageData, token).ConfigureAwait(false);
        return true;
    }

    /// <summary>Deezer serves the same photo at any square size up to 1800px; the search
    /// JSON only offers 1000px (<c>picture_xl</c>). The artist page paints the portrait
    /// across the whole window, so ask for 1800 (measured 09-13: 296 KB vs 88 KB, same
    /// hash). Non-Deezer or unfamiliar URLs pass through untouched.</summary>
    internal static string UpgradeDeezerSize(string imageUrl)
        => imageUrl.Contains("dzcdn.net/images/artist/", StringComparison.OrdinalIgnoreCase)
            ? imageUrl.Replace("/1000x1000-", "/1800x1800-", StringComparison.Ordinal)
            : imageUrl;

    private async Task<byte[]?> DownloadImageBytesAsync(string imageUrl, CancellationToken token)
    {
        var bytes = await DownloadImageBytesOnceAsync(imageUrl, token).ConfigureAwait(false);
        // Should Deezer ever refuse the large size, the 1000px original still exists.
        if (bytes == null && imageUrl.Contains("/1800x1800-", StringComparison.Ordinal))
            bytes = await DownloadImageBytesOnceAsync(imageUrl.Replace("/1800x1800-", "/1000x1000-", StringComparison.Ordinal), token).ConfigureAwait(false);
        return bytes;
    }

    private async Task<byte[]?> DownloadImageBytesOnceAsync(string imageUrl, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode ||
            response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            return null;

        var imageData = await HttpSafety
            .ReadBytesBoundedAsync(response.Content, HttpSafety.MaxImageBytes, token).ConfigureAwait(false);
        // Magic-byte check: an error/HTML page must never be cached as artwork.
        if (imageData.Length == 0 || !HttpSafety.LooksLikeImage(imageData))
            return null;
        return imageData;
    }

    /// <summary>
    /// Writes via a temp file + rename so a tile decoding the portrait mid-write never
    /// reads a truncated JPEG — a replaced photo lands while the grid is showing it.
    /// </summary>
    private static async Task WriteImageAtomicAsync(string cachedPath, byte[] imageData, CancellationToken ct = default)
    {
        // The cache subdirectory is created once in the constructor, so a "Clear Artwork
        // Cache" (which deletes the whole artwork tree) left every later write throwing
        // DirectoryNotFoundException into a swallowing catch — artist photos silently
        // stopped caching until the next app restart.
        var dir = Path.GetDirectoryName(cachedPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = cachedPath + ".tmp";
        await File.WriteAllBytesAsync(tmp, imageData, ct).ConfigureAwait(false);
        File.Move(tmp, cachedPath, overwrite: true);
    }

    private void WriteSourceMarker(Guid artistId, string imageUrl)
    {
        try
        {
            var src = GetSourceMarkerPath(artistId);
            File.WriteAllText(src, SourceMarkerVersion + Environment.NewLine + imageUrl + Environment.NewLine);
            File.SetLastWriteTimeUtc(src, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArtistImage] Failed to record source for {artistId}: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    /// <summary>The Deezer account chosen for an artist: its photo URL and fan count.</summary>
    internal sealed record DeezerArtistMatch(string ImageUrl, long Fans);

    /// <summary>
    /// Searches Deezer for the artist and returns the best account's photo URL and fan count.
    /// Tries the full artist name first, then the primary artist for collaborations.
    /// </summary>
    private async Task<DeezerArtistMatch?> FindDeezerArtistAsync(string artistName, CancellationToken ct = default)
    {
        foreach (var candidate in BuildArtistCandidates(artistName))
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{DeezerSearchUrl}?q={Uri.EscapeDataString(candidate)}&limit={DeezerSearchLimit}";
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                continue;

            var json = await HttpSafety.ReadStringBoundedAsync(response.Content, ct: ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() == 0)
                continue;

            // Deezer's public search has no "verified" flag and returns impostor entries
            // with the SAME name — for "Bad Bunny" a 6-fan, 1-album account came back
            // FIRST and the real artist (7.99M fans, 88 albums) second. Returning on the
            // first exact match therefore swapped in a fake's photo. Names are compared
            // without diacritics: the real "Arcángel" is spelled "Arcangel" on Deezer, and
            // an accent-exact tier handed the page to a 1,683-fan duplicate (09-17).
            // Within the best tier the accounts are verified against the library's own
            // track titles (Deezer top tracks); fans then album count break what is left.
            var tier = new List<DeezerCandidate>();
            var bestRank = int.MaxValue;

            foreach (var item in data.EnumerateArray())
            {
                var imageUrl = GetBestDeezerImageUrl(item);
                if (string.IsNullOrWhiteSpace(imageUrl))
                    continue;

                var resultName = item.TryGetProperty("name", out var nameNode)
                    ? nameNode.GetString()
                    : null;
                var rank = RankDeezerArtistMatch(resultName, candidate);
                if (rank > bestRank)
                    continue;
                if (rank < bestRank)
                {
                    bestRank = rank;
                    tier.Clear();
                }
                tier.Add(new DeezerCandidate(ReadCount(item, "id"), imageUrl, ReadCount(item, "nb_fan"), ReadCount(item, "nb_album")));
            }

            if (tier.Count == 0)
                continue;

            tier.Sort((a, b) => b.Fans != a.Fans ? b.Fans.CompareTo(a.Fans) : b.Albums.CompareTo(a.Albums));
            var chosen = tier.Count > 1
                ? await VerifyAgainstLibraryAsync(artistName, tier, ct).ConfigureAwait(false)
                : tier[0];
            return new DeezerArtistMatch(chosen.ImageUrl, chosen.Fans);
        }

        return null;
    }

    /// <summary>One search hit in the best name tier, in Deezer's own numbers.</summary>
    internal sealed record DeezerCandidate(long Id, string ImageUrl, long Fans, long Albums);

    private const string DeezerArtistUrl = "https://api.deezer.com/artist";

    /// <summary>How many same-name accounts (fan-ordered) get their top tracks checked.</summary>
    private const int VerifyCandidateLimit = 3;

    /// <summary>
    /// Picks between same-name accounts by how many of the library's own titles for the
    /// artist appear in each account's Deezer top tracks; the most overlaps wins, and with
    /// no library titles or no overlap at all the fan order stands. Costs one request per
    /// checked account, only in the ambiguous case.
    /// </summary>
    private async Task<DeezerCandidate> VerifyAgainstLibraryAsync(string artistName, List<DeezerCandidate> tier, CancellationToken ct)
    {
        var titles = LibraryTitleKeys(artistName);
        if (titles.Count == 0)
            return tier[0];

        DeezerCandidate best = tier[0];
        var bestOverlap = 0;
        foreach (var candidate in tier.Take(VerifyCandidateLimit))
        {
            if (candidate.Id <= 0)
                continue;
            var overlap = await CountTopTrackOverlapAsync(candidate.Id, titles, ct).ConfigureAwait(false);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Normalised titles of the library tracks credited to the artist.</summary>
    private HashSet<string> LibraryTitleKeys(string artistName)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var tracks = _library?.Tracks;
        if (tracks == null)
            return keys;
        foreach (var track in tracks)
        {
            if (!ViewModels.LibraryAlbumsViewModel.ContainsArtistToken(track.Artist, artistName)
                && !ViewModels.LibraryAlbumsViewModel.ContainsArtistToken(track.AlbumArtist, artistName))
                continue;
            var key = TitleKey(track.Title);
            if (key.Length >= 3)
                keys.Add(key);
        }
        return keys;
    }

    private async Task<int> CountTopTrackOverlapAsync(long deezerId, HashSet<string> libraryKeys, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"{DeezerArtistUrl}/{deezerId}/top?limit=50", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return 0;
            var json = await HttpSafety.ReadStringBoundedAsync(response.Content, ct: ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return 0;

            var overlap = 0;
            foreach (var item in data.EnumerateArray())
            {
                var title = item.TryGetProperty("title_short", out var shortNode) ? shortNode.GetString() : null;
                if (string.IsNullOrWhiteSpace(title) && item.TryGetProperty("title", out var titleNode))
                    title = titleNode.GetString();
                var key = TitleKey(title);
                if (key.Length >= 3 && libraryKeys.Contains(key))
                    overlap++;
            }
            return overlap;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArtistImage] Top-track check failed for Deezer artist {deezerId}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Title comparison key: diacritics folded, case and punctuation dropped, any
    /// "(feat. …)" / "[…]" / " - …" suffix removed so a tagged "Me Acostumbré (feat. Bad
    /// Bunny)" meets Deezer's "Me Acostumbré".</summary>
    internal static string TitleKey(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;
        var cut = title.IndexOfAny(new[] { '(', '[' });
        if (cut > 0) title = title[..cut];
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) title = title[..dash];
        return string.Concat(FoldDiacritics(title).Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private static long ReadCount(JsonElement item, string property)
        => item.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var v)
            ? v
            : 0;

    private static string? GetBestDeezerImageUrl(JsonElement artistNode)
    {
        foreach (var propertyName in new[] { "picture_xl", "picture_big", "picture_medium" })
        {
            if (!artistNode.TryGetProperty(propertyName, out var node))
                continue;

            var imageUrl = node.GetString();
            if (!string.IsNullOrWhiteSpace(imageUrl) && !IsDeezerPlaceholderUrl(imageUrl))
                return UpgradeDeezerSize(imageUrl);
        }

        return null;
    }

    private static int RankDeezerArtistMatch(string? resultName, string queryName)
    {
        if (string.IsNullOrWhiteSpace(resultName))
            return 100;

        var result = FoldDiacritics(resultName.Trim());
        var query = FoldDiacritics(queryName.Trim());
        if (result.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 0;

        var compactResult = RemoveWhitespace(result);
        var compactQuery = RemoveWhitespace(query);
        if (compactResult.Equals(compactQuery, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (result.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 2;

        if (result.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 3;

        return 10;
    }

    private static string RemoveWhitespace(string value)
        => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>"Arcángel" → "Arcangel": Deezer spells many Latin artists without accents.</summary>
    internal static string FoldDiacritics(string value)
    {
        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool IsDeezerPlaceholderUrl(string url)
        => url.Contains("/artist//", StringComparison.Ordinal)
           || url.Contains("/images/artist//", StringComparison.Ordinal);

    private bool TryUsePrimaryArtistImageFallback(
        Artist artist,
        IReadOnlyDictionary<string, Artist> artistsByName,
        Action<Artist, string>? onImageReady)
    {
        foreach (var candidate in BuildArtistCandidates(artist.Name).Skip(1))
        {
            string? fallbackPath = null;
            if (artistsByName.TryGetValue(candidate, out var primaryArtist))
            {
                fallbackPath = primaryArtist.ImagePath;
                if (string.IsNullOrWhiteSpace(fallbackPath) || !File.Exists(fallbackPath))
                    fallbackPath = GetCachedImagePath(primaryArtist.Id);
            }

            if (string.IsNullOrWhiteSpace(fallbackPath) || !File.Exists(fallbackPath))
                fallbackPath = GetCachedImagePath(ComputeArtistId(candidate));

            if (!File.Exists(fallbackPath))
                continue;

            if (!string.Equals(artist.ImagePath, fallbackPath, StringComparison.OrdinalIgnoreCase))
            {
                artist.ImagePath = fallbackPath;
                onImageReady?.Invoke(artist, fallbackPath);
            }

            return true;
        }

        return false;
    }

    private static Guid ComputeArtistId(string artistName)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(artistName.Trim().ToLowerInvariant()));
        return new Guid(hash);
    }

    private static IEnumerable<string> BuildArtistCandidates(string artistName)
    {
        var normalized = artistName.Trim();
        if (normalized.Length == 0)
            yield break;

        yield return normalized;

        // Same separators as the artist index, so the portrait lookup for a grouped
        // name tries exactly the name the grid shows.
        var primary = Track.GetPrimaryArtist(normalized);

        if (!string.IsNullOrWhiteSpace(primary) &&
            !string.Equals(primary, normalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return primary;
        }
    }
}
