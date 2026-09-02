using System;
using System.Reflection;
using Gst.Video;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written upload surface of
/// <see cref="Gst.Video.VideoGLTextureUploadMeta"/>.
/// </summary>
/// <remarks>
/// The call itself cannot be exercised. No element of the GStreamer 1.28 tree
/// implements the upload function of the metadata item, and the call that
/// attaches one is a deprecated closure path this binding skips, so an item can
/// only reach managed code attached to a buffer by a native element — and the
/// upload would need an OpenGL context on the calling thread in any case. What
/// is asserted here is the surface: the span overload that carries the four
/// texture identifiers the C function reads, and the obsolete overload that
/// keeps code compiled against 1.28.5 building.
/// </remarks>
public sealed class VideoGLTextureUploadMetaTests
{
    /// <summary>
    /// Both overloads are public, and the one that shipped is marked obsolete.
    /// </summary>
    [Fact]
    public void TheUploadSurfaceCarriesASpanOverloadAndAnObsoleteBridge()
    {
        MethodInfo? span = typeof(VideoGLTextureUploadMeta).GetMethod(
            nameof(VideoGLTextureUploadMeta.Upload),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [typeof(ReadOnlySpan<uint>)],
            modifiers: null);

        MethodInfo? shipped = typeof(VideoGLTextureUploadMeta).GetMethod(
            nameof(VideoGLTextureUploadMeta.Upload),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            [typeof(uint)],
            modifiers: null);

        Assert.NotNull(span);
        Assert.Equal(typeof(bool), span.ReturnType);
        Assert.Null(span.GetCustomAttribute<ObsoleteAttribute>());

        Assert.NotNull(shipped);
        Assert.Equal(typeof(bool), shipped.ReturnType);

        ObsoleteAttribute? obsolete = shipped.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(obsolete);
        Assert.False(obsolete.IsError);
        Assert.Contains("Upload(ReadOnlySpan<uint>)", obsolete.Message, StringComparison.Ordinal);
    }
}
