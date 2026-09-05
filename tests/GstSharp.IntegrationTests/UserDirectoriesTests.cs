using Gst.GLib;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The per user directories GLib resolves, which are what an application joins
/// its own cache path to.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class UserDirectoriesTests
{
    /// <summary>
    /// GLib always resolves a cache directory, and it is an absolute path of
    /// the platform.
    /// </summary>
    [Fact]
    public void TheCacheDirectoryIsAnAbsolutePath()
    {
        string cache = UserDirectories.CacheDir;

        Assert.False(string.IsNullOrWhiteSpace(cache));
        Assert.True(Path.IsPathRooted(cache), cache);
    }
}
