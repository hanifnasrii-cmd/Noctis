using Noctis.Services.LyricsStudio;
using Xunit;

namespace Noctis.Tests;

/// <summary>Lyrics Studio offers two models (2026-09-07). Old saved preferences that
/// name a retired size must still resolve to something in the catalog.</summary>
public class WhisperModelCatalogTests
{
    [Fact]
    public void Catalog_OffersBaseAndMediumOnly()
    {
        Assert.Equal(new[] { WhisperModelSize.Base, WhisperModelSize.Medium },
            WhisperModelManager.Catalog.Select(m => m.Size).ToArray());
    }

    [Theory]
    [InlineData("Tiny", WhisperModelSize.Base)]
    [InlineData("small", WhisperModelSize.Medium)]
    [InlineData("Base", WhisperModelSize.Base)]
    [InlineData("Medium", WhisperModelSize.Medium)]
    [InlineData("garbage", WhisperModelSize.Base)]
    [InlineData(null, WhisperModelSize.Base)]
    public void Parse_FoldsRetiredSizesOntoTheOfferedPair(string? saved, WhisperModelSize expected)
        => Assert.Equal(expected, WhisperModelManager.Parse(saved));

    [Fact]
    public void Info_NeverThrowsForARetiredSize()
    {
        Assert.Equal(WhisperModelSize.Base, WhisperModelManager.Info(WhisperModelSize.Tiny).Size);
        Assert.Equal(WhisperModelSize.Medium, WhisperModelManager.Info(WhisperModelSize.Small).Size);
    }
}
