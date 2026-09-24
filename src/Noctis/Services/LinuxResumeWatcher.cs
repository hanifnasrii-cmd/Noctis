using System;
using System.IO;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Noctis.Services;

/// <summary>
/// Linux only: listens for the system's suspend/resume signal (login1's PrepareForSleep on
/// the system bus) and hands each resume to the window layer.
///
/// Discord (Mistery, 2026-09-22/23): on a Wayland session with the NVIDIA driver the Noctis
/// window came back from sleep see-through and stayed that way until the app was restarted;
/// NOCTIS_SOFTWARE_RENDER=1 changed nothing, so this is not a lost GL context. Without
/// NVreg_PreserveVideoMemoryAllocations the driver discards video memory across a suspend,
/// which takes XWayland's buffers for our X11 window with it — the client is never told, so
/// nothing it draws afterwards reaches the screen, while a fresh window (a restart) gets
/// fresh buffers. Unmapping and re-mapping the window makes XWayland allocate a new surface
/// and pixmap the same way; that is what the subscriber does on resume. The driver-side
/// setting remains the real fix and is what the user was asked to try.
/// </summary>
public sealed class LinuxResumeWatcher : IDisposable
{
    private const string Login1Bus = "org.freedesktop.login1";
    private const string Login1Path = "/org/freedesktop/login1";
    private const string Login1Manager = "org.freedesktop.login1.Manager";
    private const string PrepareForSleep = "PrepareForSleep";

    private readonly Action _onResume;
    private DBusConnection? _connection;
    private IDisposable? _match;
    private volatile bool _disposed;

    private LinuxResumeWatcher(Action onResume)
    {
        _onResume = onResume;
        _ = Task.Run(InitializeAsync);
    }

    /// <summary>
    /// Starts watching on the setups that need it (see <see cref="ShouldRemapAfterResume"/>);
    /// null everywhere else. Never throws — a missing system bus only costs the mitigation.
    /// </summary>
    public static LinuxResumeWatcher? TryStart(Action onResume)
    {
        try
        {
            if (!OperatingSystem.IsLinux())
                return null;
            if (!ShouldRemapAfterResume(
                    Environment.GetEnvironmentVariable("NOCTIS_RESUME_REMAP"),
                    Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                    File.Exists("/proc/driver/nvidia/version")))
                return null;
            return new LinuxResumeWatcher(onResume);
        }
        catch (Exception ex)
        {
            DebugLogger.Warn(DebugLogger.Category.UI, "ResumeWatch.Start", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The remap is targeted at the reported setup — an X11 client under XWayland on the
    /// proprietary NVIDIA driver — because re-mapping briefly unmaps the window (a flash,
    /// and tiling compositors may re-place it), which nobody else should pay for.
    /// NOCTIS_RESUME_REMAP=1 forces it on for testing other setups; =0 turns it off.
    /// Pure; internal for tests.
    /// </summary>
    internal static bool ShouldRemapAfterResume(string? overrideEnv, string? sessionType, bool nvidiaDriverPresent)
    {
        if (overrideEnv == "1") return true;
        if (overrideEnv == "0") return false;
        return nvidiaDriverPresent && string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);
    }

    private async Task InitializeAsync()
    {
        try
        {
            var address = DBusAddress.System;
            if (string.IsNullOrEmpty(address))
            {
                DebugLogger.Warn(DebugLogger.Category.UI, "ResumeWatch.NoBus",
                    "no system bus address; the window will not be refreshed after sleep");
                return;
            }

            var connection = new DBusConnection(address);
            await connection.ConnectAsync();

            var rule = new MatchRule
            {
                Type = MessageType.Signal,
                Sender = Login1Bus,
                Path = Login1Path,
                Interface = Login1Manager,
                Member = PrepareForSleep,
            };
            // PrepareForSleep(b start): true right before suspend, false right after resume.
            var match = await connection.AddMatchAsync(
                rule,
                static (Message message, object? _) => message.GetBodyReader().ReadBool(),
                (Notification<bool> signal) =>
                {
                    if (signal.Exception == null && !signal.Value)
                        OnResume();
                },
                emitOnCapturedContext: false);

            if (_disposed)
            {
                match.Dispose();
                connection.Dispose();
                return;
            }

            _connection = connection;
            _match = match;
            DebugLogger.Info(DebugLogger.Category.UI, "ResumeWatch.Started", $"{Login1Manager}.{PrepareForSleep}");
        }
        catch (Exception ex)
        {
            DebugLogger.Warn(DebugLogger.Category.UI, "ResumeWatch.Init", ex.Message);
        }
    }

    private void OnResume()
    {
        if (_disposed) return;
        DebugLogger.Info(DebugLogger.Category.UI, "ResumeWatch.Resumed",
            "system woke up; re-mapping visible windows so XWayland allocates fresh buffers");
        try
        {
            _onResume();
        }
        catch (Exception ex)
        {
            DebugLogger.Warn(DebugLogger.Category.UI, "ResumeWatch.Handler", ex.Message);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _match?.Dispose(); } catch { /* best effort on shutdown */ }
        try { _connection?.Dispose(); } catch { /* best effort on shutdown */ }
        _match = null;
        _connection = null;
    }
}
