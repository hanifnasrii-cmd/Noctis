namespace Noctis.Models;

/// <summary>
/// Display row for the Home tab's ranked "Most Listened To" list:
/// a track plus its 1-based rank by play count.
/// </summary>
public sealed class TopSongRow
{
    public required Track Track { get; init; }

    /// <summary>1-based rank by play count (or position in the recent-play list).</summary>
    public int Rank { get; init; }

    /// <summary>
    /// True for Home's "Last Played" chart, which shares the row template with "Most
    /// Played": the numeral is a plain position (no podium) and a click queues the
    /// last-played list rather than the top-songs list.
    /// </summary>
    public bool IsLastPlayed { get; init; }

    /// <summary>Podium tints for the rank numeral (Home, Albums artist rows, Artist page):
    /// #1 gold, #2 silver, #3 bronze; everything below stays the dim default.</summary>
    public bool IsTop => !IsLastPlayed && Rank == 1;
    public bool IsSecond => !IsLastPlayed && Rank == 2;
    public bool IsThird => !IsLastPlayed && Rank == 3;
}
