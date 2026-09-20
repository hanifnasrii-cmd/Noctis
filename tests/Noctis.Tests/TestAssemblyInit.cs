using System.Runtime.CompilerServices;
using Noctis.Services;

namespace Noctis.Tests;

/// <summary>
/// Mirrors what Program.Main does before the first log line: the core's log header
/// is app-agnostic (it names the entry assembly), and the desktop app installs its
/// own describer (version, release channel, install source). Tests that read the
/// header expect the app's version of it, so install it before any test writes.
/// </summary>
internal static class TestAssemblyInit
{
    [ModuleInitializer]
    internal static void Init() => DebugLog.DescribeBuild = UpdateService.DescribeBuild;
}
