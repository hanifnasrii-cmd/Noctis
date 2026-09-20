namespace Noctis.Services;

/// <summary>
/// Gives the managed heap back after a burst of large garbage.
///
/// The startup pipeline (library parse, tag reads during the scan, artwork
/// backfill) leaves hundreds of MB of dead Large Object Heap arrays behind —
/// measured at 236 MB of unrooted byte[] on a 7k-track library, most of them
/// embedded cover files TagLib had materialised. Nothing allocates afterwards, so
/// the GC has no reason to run a gen2 and the memory stays committed for the whole
/// session: that is the "Noctis sits at 500+ MB doing nothing" report. A single
/// compacting full collection once the burst is over returns it.
///
/// Requests are debounced: every producer (startup, a scan, a backfill) calls
/// <see cref="RequestAfterIdle"/> when it finishes, and the collection runs once
/// <see cref="Quiet"/> has passed since the last request, so overlapping producers
/// pay for one collection, after all of them are done.
/// </summary>
public static class MemoryTrim
{
    /// <summary>How long after the last request the trim runs.</summary>
    public static TimeSpan Quiet { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>Test seam: what runs when the quiet period elapses. Null = the real collection.</summary>
    internal static Action? Collector { get; set; }

    private static readonly object Gate = new();
    private static int _pending;
    private static string _reason = "";

    public static void RequestAfterIdle(string reason)
    {
        int ticket;
        lock (Gate)
        {
            ticket = ++_pending;
            _reason = reason;
        }
        _ = Task.Delay(Quiet).ContinueWith(_ =>
        {
            lock (Gate)
            {
                if (ticket != _pending) return; // a later request restarted the quiet period
            }
            Run();
        }, TaskScheduler.Default);
    }

    private static void Run()
    {
        if (Collector is { } custom)
        {
            custom();
            return;
        }
        try
        {
            var before = GC.GetTotalMemory(false);
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            var after = GC.GetTotalMemory(false);
            DebugLog.Write("Memory",
                $"trim after {_reason}: managed {before / 1048576} MB -> {after / 1048576} MB");
        }
        catch
        {
            // A trim is best effort; never let it surface.
        }
    }
}
