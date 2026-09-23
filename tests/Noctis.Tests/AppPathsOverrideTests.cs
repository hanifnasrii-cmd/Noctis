using System;
using System.IO;
using Noctis.Helpers;
using Xunit;

namespace Noctis.Tests;

/// <summary>
/// The Android head redirects the data root to the app's private files directory before
/// building services. Once any service has resolved the root, a different override must
/// fail loudly instead of splitting the profile across two folders.
/// </summary>
public class AppPathsOverrideTests
{
    [Fact]
    public void OverrideDataRoot_SameRootIsIdempotent_DifferentRootAfterResolveThrows()
    {
        var resolved = AppPaths.DataRoot;                  // resolves (or was already resolved by another test)
        AppPaths.OverrideDataRoot(resolved);               // same value: fine
        Assert.Equal(resolved, AppPaths.DataRoot);

        var other = Path.Combine(Path.GetTempPath(), "NoctisTests", Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(() => AppPaths.OverrideDataRoot(other));
        Assert.Equal(resolved, AppPaths.DataRoot);
    }
}
