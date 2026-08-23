using System;

namespace Gst;

public sealed unsafe partial class CustomMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstCustomMeta</c>.</summary>
    /// <param name="meta">The item to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the item implements
    /// another API.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMeta</c> is the first field of
    /// every typed metadata structure, so both wrappers address the same storage
    /// inside the same buffer, and both die when the item is removed. The guard
    /// is the one exception of this family: a custom metadata item gets an API
    /// <c>GType</c> of its own per registered name, so what is asked is whether
    /// the implementation was registered as a custom one at all, not whether it
    /// matches one type.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="meta"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    public static Gst.CustomMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.Info.IsCustom()
            ? Gst.CustomMeta.FromNative(handle)
            : null;
    }
}

public sealed unsafe partial class ParentBufferMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstParentBufferMeta</c>.</summary>
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
    public static Gst.ParentBufferMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Global.ParentBufferMetaApiGetType()
            ? Gst.ParentBufferMeta.FromNative(handle)
            : null;
    }
}

public sealed unsafe partial class ProtectionMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstProtectionMeta</c>.</summary>
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
    public static Gst.ProtectionMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Global.ProtectionMetaApiGetType()
            ? Gst.ProtectionMeta.FromNative(handle)
            : null;
    }
}

public sealed unsafe partial class ReferenceTimestampMeta
{
    /// <summary>Reinterprets a metadata item as a <c>GstReferenceTimestampMeta</c>.</summary>
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
    public static Gst.ReferenceTimestampMeta? FromMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint handle = meta.RequireHandle();
        return meta.ApiType == Gst.Global.ReferenceTimestampMetaApiGetType()
            ? Gst.ReferenceTimestampMeta.FromNative(handle)
            : null;
    }
}
