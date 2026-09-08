using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Noctis.Services;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;
using Xunit.Abstractions;

namespace Noctis.Tests;

/// <summary>
/// The Settings card clips its content to a 21px rounded rect (MainWindow SettingsCard).
/// The page header keeps the scrollbar clear of the top corner, but the bar used to run
/// to the very bottom of the card where the curve cut off the end of the track and thumb
/// (user screenshot, 2026-09-07). The bar is inset by the corner radius; the content
/// itself still scrolls to the card edge.
/// </summary>
public class SettingsScrollbarInsetTests
{
    private readonly ITestOutputHelper _output;
    public SettingsScrollbarInsetTests(ITestOutputHelper output) => _output = output;

    private sealed class NoOpPlayHistory : IPlayHistoryService
    {
        public IReadOnlyList<Noctis.Models.PlayHistoryEvent> Events => Array.Empty<Noctis.Models.PlayHistoryEvent>();
        public Task PreloadAsync() => Task.CompletedTask;
        public void RecordPlay(Noctis.Models.Track track) { }
        public void RecordSkip(Noctis.Models.Track track) { }
        public Task FlushAsync() => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task VerticalBar_StopsShortOfTheCardCorner_ContentDoesNot()
    {
        var root = Path.Combine(Path.GetTempPath(), "NoctisTests", Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new SettingsViewModel(new PersistenceService(root), new FakeLibraryService(), new NoOpPlayHistory());
            await vm.LoadAsync();
            vm.SelectedSettingsTab = SettingsViewModel.TabGeneral;
            var view = new SettingsView { DataContext = vm };
            var window = new Window { Width = 1000, Height = 720, Content = view };
            window.Show();
            window.UpdateLayout();

            var sv = view.FindControl<ScrollViewer>("SettingsScrollViewer")!;
            Assert.True(sv.Extent.Height > sv.Viewport.Height, "General page must overflow for the bar to show");
            var bar = sv.GetVisualDescendants().OfType<ScrollBar>()
                .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical && b.TemplatedParent == sv);
            Assert.True(bar.IsVisible);

            var barBottom = bar.TranslatePoint(new Point(0, bar.Bounds.Height), view)!.Value.Y;
            var contentBottom = sv.TranslatePoint(new Point(0, sv.Bounds.Height), view)!.Value.Y;
            var viewBottom = view.Bounds.Height;
            _output.WriteLine($"view {viewBottom} scrollviewer bottom {contentBottom} bar bottom {barBottom} bar margin {bar.Margin}");

            Assert.True(viewBottom - barBottom >= 21, $"bar ends {viewBottom - barBottom}px above the card bottom; needs the 21px corner");
            Assert.True(Math.Abs(viewBottom - contentBottom) < 0.51, "content viewport must still reach the card edge");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
