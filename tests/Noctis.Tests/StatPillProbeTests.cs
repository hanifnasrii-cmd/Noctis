using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Noctis.Services;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;
using Xunit.Abstractions;

namespace Noctis.Tests;

public class StatPillProbeTests
{
    private readonly ITestOutputHelper _output;
    public StatPillProbeTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public async Task StatPills_ShareOneTopEdge_AndTextIsCentred()
    {
        var root = Path.Combine(Path.GetTempPath(), "NoctisTests", Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new SettingsViewModel(new PersistenceService(root), new FakeLibraryService(), new NoOpPlayHistory());
            await vm.LoadAsync();
            vm.SelectedSettingsTab = SettingsViewModel.TabStatistics;
            var view = new SettingsView { DataContext = vm };
            var window = new Window { Width = 1100, Height = 800, Content = view };
            window.Show();
            window.UpdateLayout();

            var pills = view.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("stat-pill")).ToList();
            Assert.NotEmpty(pills);
            var wrap = pills[0].GetVisualParent<WrapPanel>()!;
            foreach (var p in pills)
            {
                var top = p.TranslatePoint(new Point(0, 0), wrap)!.Value.Y;
                var texts = p.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => { var y = t.TranslatePoint(new Point(0, 0), p)!.Value.Y; var runs = string.Concat(t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? Array.Empty<string>()); return $"'{t.Text}{runs}' y={y:0.##} h={t.Bounds.Height:0.##}"; });
                _output.WriteLine($"pill top={top:0.##} h={p.Bounds.Height:0.##}  {string.Join(" | ", texts)}");
            }
            var tops = pills.Select(p => p.TranslatePoint(new Point(0, 0), wrap)!.Value.Y).ToList();
            Assert.True(tops.Max() - tops.Min() < 0.51, $"pill tops differ by {tops.Max() - tops.Min()}");
            // Every glyph run starts on the same line regardless of descenders in the label.
            var textTops = pills.SelectMany(p => p.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.TranslatePoint(new Point(0, 0), p)!.Value.Y)).ToList();
            Assert.All(pills, p => Assert.Single(p.GetVisualDescendants().OfType<TextBlock>()));
            Assert.True(textTops.Max() - textTops.Min() < 0.51, $"text tops differ by {textTops.Max() - textTops.Min()}");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private sealed class NoOpPlayHistory : IPlayHistoryService
    {
        public System.Collections.Generic.IReadOnlyList<Noctis.Models.PlayHistoryEvent> Events => Array.Empty<Noctis.Models.PlayHistoryEvent>();
        public Task PreloadAsync() => Task.CompletedTask;
        public void RecordPlay(Noctis.Models.Track track) { }
        public void RecordSkip(Noctis.Models.Track track) { }
        public Task FlushAsync() => Task.CompletedTask;
    }
}
