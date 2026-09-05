using Gst.GLib;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The per user directories GLib resolves, which are what an application joins
/// its own cache path to.
/// </summary>
public class UserDirectoriesTests
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
