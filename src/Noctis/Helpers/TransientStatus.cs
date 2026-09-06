using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Noctis.Helpers;

/// <summary>
/// Confirmation texts that appear after an action ("Registered.", "Saved to Downloads.") and
/// should leave on their own. <see cref="Show"/> sets the text through <paramref name="set"/>
/// and clears it after <see cref="Duration"/> unless something newer was shown under the same
/// key in the meantime, so a quick second action never has its message wiped by the first.
/// </summary>
public static class TransientStatus
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    private static readonly Dictionary<string, int> Versions = new();

    /// <param name="key">One key per status property (e.g. the property name).</param>
    public static void Show(string key, Action<string> set, string text, TimeSpan? duration = null)
    {
        int version;
        lock (Versions) version = Versions[key] = Versions.TryGetValue(key, out var v) ? v + 1 : 1;
        set(text);
        _ = ClearLaterAsync(key, version, set, duration ?? Duration);
    }

    private static async Task ClearLaterAsync(string key, int version, Action<string> set, TimeSpan delay)
    {
        try { await Task.Delay(delay); } catch { return; }
        bool current;
        lock (Versions) current = Versions.TryGetValue(key, out var v) && v == version;
        if (!current) return;
        if (Dispatcher.UIThread.CheckAccess()) set(string.Empty);
        else Dispatcher.UIThread.Post(() => set(string.Empty));
    }
}
