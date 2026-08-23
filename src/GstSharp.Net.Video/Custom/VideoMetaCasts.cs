using System;

namespace Gst.Video;

public sealed unsafe partial class VideoMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstVideoMeta</c>.</summary>
    /// <param name="meta">The item to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the item implements
    /// another API.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMeta</c> is the first field of
    /// every typed metadata structure, so both wrappers address the same storage
    /// inside the same buffer, and both die when the item is removed.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="meta"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    public static Gst.Video.VideoMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Video.VideoGlobal.VideoMetaApiGetType()
            ? Gst.Video.VideoMeta.FromNative(handle)
            : null;
    }
}

public sealed unsafe partial class VideoCropMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstVideoCropMeta</c>.</summary>
    /// <param name="meta">The item to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the item implements
    /// another API.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMeta</c> is the first field of
    /// every typed metadata structure, so both wrappers address the same storage
    /// inside the same buffer, and both die when the item is removed.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="meta"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    public static Gst.Video.VideoCropMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Video.VideoGlobal.VideoCropMetaApiGetType()
            ? Gst.Video.VideoCropMeta.FromNative(handle)
            : null;
    }
}

public sealed unsafe partial class VideoRegionOfInterestMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstVideoRegionOfInterestMeta</c>.</summary>
    /// <param name="meta">The item to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the item implements
    /// another API.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMeta</c> is the first field of
    /// every typed metadata structure, so both wrappers address the same storage
    /// inside the same buffer, and both die when the item is removed.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="meta"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    public static Gst.Video.VideoRegionOfInterestMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Video.VideoGlobal.VideoRegionOfInterestMetaApiGetType()
            ? Gst.Video.VideoRegionOfInterestMeta.FromNative(handle)
            : null;
    }
}

public sealed unsafe partial class VideoCaptionMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstVideoCaptionMeta</c>.</summary>
    /// <param name="meta">The item to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the item implements
    /// another API.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMeta</c> is the first field of
    /// every typed metadata structure, so both wrappers address the same storage
    /// inside the same buffer, and both die when the item is removed.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="meta"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    public static Gst.Video.VideoCaptionMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Video.VideoGlobal.VideoCaptionMetaApiGetType()
            ? Gst.Video.VideoCaptionMeta.FromNative(handle)
            : null;
    }
}
