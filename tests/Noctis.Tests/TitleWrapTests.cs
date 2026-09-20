using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Noctis.Helpers;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// Tile heart placement (09-13): the title TextBlock reports whether it wrapped by
/// setting <c>wrapped</c> on its tile-text panel, and clears it when it fits again.
/// </summary>
public class TitleWrapTests
{
    [AvaloniaFact]
    public void LongTitle_MarksThePanelWrapped_ShortTitleClearsIt()
    {
        var title = new TextBlock
        {
            Text = "Diles (feat. Arcángel, Ñengo Flow, DJ Luian & Mambo Kingz) - Single",
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            FontSize = 13,
        };
        TitleWrap.SetWatch(title, true);
        var panel = new StackPanel { Width = 180 };
        panel.Classes.Add("tile-text");
        panel.Children.Add(title);
        var win = new Window { Width = 400, Height = 200, Content = panel };
        win.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(title.TextLayout.TextLines.Count > 1);
        Assert.Contains("wrapped", panel.Classes);

        title.Text = "Volví";
        title.InvalidateMeasure();
        Dispatcher.UIThread.RunJobs();
        win.UpdateLayout();

        Assert.DoesNotContain("wrapped", panel.Classes);
    }
}
