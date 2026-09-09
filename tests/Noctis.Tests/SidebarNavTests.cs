using System;
using System.IO;
using System.Linq;
using Noctis.Converters;
using Noctis.Services;
using Noctis.ViewModels;
using Xunit;

namespace Noctis.Tests;

public class SidebarNavTests
{
    [Fact]
    public void Sidebar_HasLyricsStudioSection_BetweenVisualizerAndSettings_WithGeometryIcons()
    {
        var root = Path.Combine(Path.GetTempPath(), "NoctisTests", Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new SidebarViewModel(new PersistenceService(root), new FakeLibraryService());
            var keys = vm.NavItems.Select(i => i.Key).ToList();

            var visualizer = keys.IndexOf("visualizer");
            var studio = keys.IndexOf("lyricsstudio");
            var settings = keys.IndexOf("settings");
            Assert.True(visualizer >= 0 && studio == visualizer + 1 && settings == studio + 1,
                $"expected visualizer, lyricsstudio, settings in order; got {string.Join(",", keys)}");

            // Both draw as StreamGeometries (PathIcon), not PNG masks — the sidebar template
            // switches on this, so a bitmap key here would show the fallback playlist icon.
            foreach (var key in new[] { "visualizer", "lyricsstudio" })
                Assert.False(IconKeyToGeometryConverter.HasKey(vm.NavItems.First(i => i.Key == key).IconGlyph),
                    $"{key} must use a geometry icon");

            Assert.Equal("Nav.LyricsStudio", SidebarViewModel.LabelKey("lyricsstudio"));
            // The Settings rail's Lyrics page borrows the island's lyrics bubble (a PNG mask).
            Assert.True(IconKeyToGeometryConverter.HasKey("LyricsBubbleIcon"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
