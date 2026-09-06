using Avalonia.Headless.XUnit;
using Noctis.Helpers;
using Xunit;

namespace Noctis.Tests;

/// <summary>Confirmation texts clear themselves; a newer message under the same key survives the older timer.</summary>
public class TransientStatusTests
{
    [AvaloniaFact]
    public async Task Show_SetsText_ThenClearsAfterDuration()
    {
        var text = "";
        TransientStatus.Show("t1", v => text = v, "Registered.", TimeSpan.FromMilliseconds(80));
        Assert.Equal("Registered.", text);
        await Task.Delay(400);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("", text);
    }

    [AvaloniaFact]
    public async Task NewerMessage_IsNotWipedByTheOlderTimer()
    {
        var text = "";
        TransientStatus.Show("t2", v => text = v, "first", TimeSpan.FromMilliseconds(80));
        await Task.Delay(20);
        TransientStatus.Show("t2", v => text = v, "second", TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("second", text);
    }
}
