using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Noctis.Localization;
using Noctis.Services;
using Noctis.ViewModels;
using Noctis.Views;
using Xunit;
using Xunit.Abstractions;

namespace Noctis.Tests;

/// <summary>Switching the Language picker must relabel the open Settings page at once.</summary>
[Collection("Localization")]
public class LanguageLiveSwitchTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    public LanguageLiveSwitchTests(ITestOutputHelper output) { _output = output; Loc.Instance.SetCulture("en"); }
    public void Dispose() => Loc.Instance.SetCulture("en");

    [AvaloniaFact]
    public async Task PickingFrench_RelabelsTheOpenGeneralPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "NoctisTests", Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new SettingsViewModel(new PersistenceService(root), new FakeLibraryService(), new NoOpPlayHistory());
            await vm.LoadAsync();
            vm.SelectedSettingsTab = SettingsViewModel.TabGeneral;
            var view = new SettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 720, Content = view };
            window.Show();
            window.UpdateLayout();

            var label = view.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == "Restore last played track");
            var description = view.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == Loc.T("Settings.Desc.General"));
            var englishDescription = description.Text;

            vm.LanguageChoice = vm.LanguageOptions.Single(o => o.Code == "fr");
            window.UpdateLayout();
            _output.WriteLine($"after switch: '{label.Text}' / '{description.Text}' culture={Loc.Instance.Culture.Name}");
            Assert.Equal("Restaurer le dernier titre lu", label.Text);
            Assert.Equal("fr", vm.LanguageChoice!.Code); // survives the picker rebuild
            Assert.Equal("fr", Loc.Instance.Culture.Name);
            // The page description is a cached Loc.T string; it re-reads too (English until Crowdin
            // carries the key, but the property must at least be re-raised without throwing).
            Assert.Equal(Loc.T("Settings.Desc.General"), description.Text);
            _ = englishDescription;
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("es", "Español (Spanish)")]
    [InlineData("ko", "한국어 (Korean)")]
    [InlineData("zh-Hans", "中文（简体） (Chinese, Simplified)")]
    public void LanguageEntries_ShowNativeThenEnglishName(string code, string expected)
        => Assert.Equal(expected, SettingsViewModel.DescribeCulture(code));

    private sealed class NoOpPlayHistory : IPlayHistoryService
    {
        public System.Collections.Generic.IReadOnlyList<Noctis.Models.PlayHistoryEvent> Events => Array.Empty<Noctis.Models.PlayHistoryEvent>();
        public Task PreloadAsync() => Task.CompletedTask;
        public void RecordPlay(Noctis.Models.Track track) { }
        public void RecordSkip(Noctis.Models.Track track) { }
        public Task FlushAsync() => Task.CompletedTask;
    }
}
