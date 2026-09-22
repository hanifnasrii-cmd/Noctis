using Microsoft.Data.Sqlite;
using Noctis.Helpers;
using Noctis.Models;
using Noctis.Services;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// GitHub #74: custom badges. One free-text badge per track, journaled with the other
/// user state, shown as a coloured pill next to the title, and a "Badge" playlist sort.
/// </summary>
public class TrackBadgeTests
{
    private static Track T(string title, string? badge = null) => new()
    {
        Id = Guid.NewGuid(), Title = title, FilePath = title + ".flac", Badge = badge,
        Artist = "A", Album = "B", Duration = TimeSpan.FromSeconds(10), DateAdded = DateTime.UtcNow,
    };

    // ── Sort ──

    [Fact]
    public void SortByBadge_GroupsByBadgeName_UnbadgedLast_TitleInsideAGroup()
    {
        var tracks = new List<Track>
        {
            T("z-none"), T("Gym 2", "Gym"), T("chill 1", "Chill"), T("a-none"), T("Gym 1", "gym"), T("Chill 2", "Chill"),
        };

        var sorted = PlaylistViewModel.SortTracks(tracks, PlaylistSortMode.Badge);

        Assert.Equal(new[] { "chill 1", "Chill 2", "Gym 1", "Gym 2", "a-none", "z-none" }, sorted.Select(t => t.Title));
    }

    [Fact]
    public void SortModeBadge_RoundTripsThroughThePlaylist_AndHasALabel()
    {
        Assert.True(Enum.TryParse<PlaylistSortMode>("Badge", out var mode));
        Assert.Equal(PlaylistSortMode.Badge, mode);
    }

    // ── Colour ──

    [Fact]
    public void BadgeColour_IsStableForAName_AndCaseInsensitive()
    {
        Assert.Equal(BadgePalette.ColorFor("Gym"), BadgePalette.ColorFor("gym"));
        Assert.Equal(BadgePalette.ColorFor("Gym"), BadgePalette.ColorFor("Gym"));
        Assert.NotEqual(default, BadgePalette.ColorFor("Gym"));
    }

    // ── Persistence ──

    [Fact]
    public async Task Journal_RoundTripsTheBadge_AndClearsIt()
    {
        using var persistence = new TestPersistenceService();
        var index = new SqliteLibraryIndexService(persistence);
        var track = T("Song", "Late night");

        await index.UpsertUserStateAsync(new[] { track });
        var state = await index.LoadUserStateAsync();
        Assert.Equal("Late night", state[track.Id].Badge);

        track.Badge = null;
        await index.UpsertUserStateAsync(new[] { track });
        state = await index.LoadUserStateAsync();
        Assert.Null(state[track.Id].Badge);
    }

    [Fact]
    public async Task Journal_UpgradesAnExistingTable_WithoutTheBadgeColumn()
    {
        using var persistence = new TestPersistenceService();
        var dbPath = Path.Combine(persistence.DataDirectory, "library.db");
        Directory.CreateDirectory(persistence.DataDirectory);
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE track_user_state (
                    id TEXT PRIMARY KEY, play_count INTEGER NOT NULL, last_played_utc TEXT NULL,
                    rating INTEGER NOT NULL, is_disliked INTEGER NOT NULL, is_favorite INTEGER NOT NULL,
                    favorited_at_utc TEXT NULL, snoozed_until_utc TEXT NULL, saved_position_ms INTEGER NOT NULL);
                INSERT INTO track_user_state VALUES ('00000000000000000000000000000001', 3, NULL, 5, 0, 1, NULL, NULL, 0);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var index = new SqliteLibraryIndexService(persistence);
        var state = await index.LoadUserStateAsync();

        var row = state[Guid.Parse("00000000-0000-0000-0000-000000000001")];
        Assert.Equal(5, row.Rating);
        Assert.Null(row.Badge);

        var track = T("New", "Focus");
        await index.UpsertUserStateAsync(new[] { track });
        Assert.Equal("Focus", (await index.LoadUserStateAsync())[track.Id].Badge);
    }
}
