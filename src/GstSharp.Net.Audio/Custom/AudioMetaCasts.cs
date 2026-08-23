using System;

namespace Gst.Audio;

public sealed unsafe partial class AudioLevelMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstAudioLevelMeta</c>.</summary>
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
    public static Gst.Audio.AudioLevelMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Audio.AudioGlobal.AudioLevelMetaApiGetType()
            ? Gst.Audio.AudioLevelMeta.FromNative(handle)
            : null;
    }
}

public sealed unsafe partial class AudioClippingMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstAudioClippingMeta</c>.</summary>
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
    public static Gst.Audio.AudioClippingMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Audio.AudioGlobal.AudioClippingMetaApiGetType()
            ? Gst.Audio.AudioClippingMeta.FromNative(handle)
            : null;
    }
}
