using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// Uploads the buffer a <see cref="Gst.Video.VideoGLTextureUploadMeta"/> is
/// attached to into the textures whose identifiers the span holds.
/// </summary>
/// <param name="meta">The metadata item the upload was attached to.</param>
/// <param name="textureIds">
/// The identifiers of the textures to upload into, exactly
/// <see cref="Gst.Video.VideoGLTextureUploadMeta.NTextures"/> of them.
/// </param>
/// <returns><see langword="true"/> when the upload succeeded.</returns>
/// <remarks>
/// <para>
/// This is <c>GstVideoGLTextureUpload</c>, written by hand rather than
/// generated. The gir types the <c>texture_id</c> of the callback as a bare
/// <c>guint</c> with the star in the <c>c:type</c> alone, the same lie
/// <c>gst_video_gl_texture_upload_meta_upload</c> carries, while the C hands
/// over the address of an array of up to four identifiers
/// (<c>gstvideometa.c:1284-1290</c>); a delegate that took the first identifier
/// by value could not be called correctly.
/// </para>
/// <para>
/// The function is invoked on whatever thread uploads the buffer, which has to
/// be a thread with an OpenGL context, and it is invoked for every copy of the
/// buffer the item was carried across as well.
/// </para>
/// </remarks>
public delegate bool VideoGLTextureUpload(
    Gst.Video.VideoGLTextureUploadMeta meta,
    ReadOnlySpan<uint> textureIds);

public static unsafe partial class VideoGlobal
{
    /// <summary>
    /// Attaches an OpenGL texture upload to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to attach the metadata to.</param>
    /// <param name="textureOrientation">Which way up the textures are.</param>
    /// <param name="textureType">
    /// What each texture holds, one to four entries; the count is the
    /// <c>n_textures</c> of the item.
    /// </param>
    /// <param name="upload">
    /// The function that performs the upload, which the item keeps for as long
    /// as it lives and shares with every copy of it.
    /// </param>
    /// <returns>
    /// The metadata item, which the buffer owns, or <see langword="null"/> when
    /// the buffer refused it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_buffer_add_video_gl_texture_upload_meta</c>. The C takes
    /// the upload function, a user data copy and a user data free over one
    /// <c>user_data</c>, and the gir gives a closure index to the upload alone,
    /// which is the shape the generator refuses; the copy and the free are
    /// supplied here instead, as the pair the library needs. The item is
    /// uploaded through
    /// <see cref="Gst.Video.VideoGLTextureUploadMeta.Upload(System.ReadOnlySpan{uint})"/>.
    /// </para>
    /// <para>
    /// The managed <paramref name="upload"/> is held by a
    /// <see cref="Gst.Interop.CallbackHandle"/> the library releases through the
    /// user data free it is given, so it stays alive for as long as the buffer
    /// carries the item. A copy of the buffer copies the item, and the copy
    /// takes a handle of its own to the same delegate
    /// (<c>gstvideometa.c:1197-1198</c>), which is why the copy and the free
    /// are always passed together: a copy without a free would let two items
    /// free the one handle they share, and a free without a copy would leak
    /// every handle.
    /// </para>
    /// <para>
    /// A buffer somebody else holds is refused, which is a
    /// <see langword="null"/> return rather than an exception, because the C
    /// itself checks what <c>gst_buffer_add_meta</c> answered
    /// (<c>gstvideometa.c:1259-1260</c>) - the same shape as
    /// <c>BufferAddVideoMeta</c>. It returns before it stores the user data, so
    /// the free it was handed never runs on that path and the handle is
    /// released here instead.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="buffer"/> or <paramref name="upload"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="textureType"/> is empty or holds more than four entries.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The buffer wrapper was disposed.</exception>
    public static Gst.Video.VideoGLTextureUploadMeta? BufferAddVideoGLTextureUploadMeta(
        Gst.Buffer buffer,
        Gst.Video.VideoGLTextureOrientation textureOrientation,
        ReadOnlySpan<Gst.Video.VideoGLTextureType> textureType,
        Gst.Video.VideoGLTextureUpload upload)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(upload);

        if (textureType.IsEmpty || textureType.Length > Gst.Video.VideoGLTextureUploadMeta.MaxTextures)
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"An upload holds one to {Gst.Video.VideoGLTextureUploadMeta.MaxTextures} textures; {textureType.Length} type(s) were given."),
                nameof(textureType));
        }

        nint bufferHandle = buffer.Handle;

        // The C memcpys all four slots whatever n_textures says
        // (gstvideometa.c:1264), so the block that is handed over is always
        // four wide and zero filled; the zeroing is written out so that a
        // future [SkipLocalsInit] cannot hand the item the stack's leavings.
        Gst.Video.VideoGLTextureType* block =
            stackalloc Gst.Video.VideoGLTextureType[Gst.Video.VideoGLTextureUploadMeta.MaxTextures];
        Span<Gst.Video.VideoGLTextureType> types =
            new(block, Gst.Video.VideoGLTextureUploadMeta.MaxTextures);
        types.Clear();
        textureType.CopyTo(types);

        Gst.Interop.CallbackHandle state = Gst.Interop.CallbackHandle.Alloc(upload);
        nint nativeResult = VideoGLTextureUploadMetaNative.BufferAddVideoGLTextureUploadMeta(
            bufferHandle,
            textureOrientation,
            (uint)textureType.Length,
            block,
            VideoGLTextureUploadTrampoline.Pointer,
            state.UserData,
            VideoGLTextureUploadTrampoline.CopyUserData,
            (nint)Gst.Interop.CallbackHandle.DestroyNotify);
        GC.KeepAlive(buffer);

        if (nativeResult == 0)
        {
            // Nothing was stored, so the free that was handed over never runs.
            state.Free();
            return null;
        }

        return Gst.Video.VideoGLTextureUploadMeta.FromNative(nativeResult);
    }
}

/// <summary>The native entry points of <see cref="Gst.Video.VideoGLTextureUpload"/>.</summary>
/// <remarks>
/// The upload half is a <c>GstVideoGLTextureUpload</c> and the copy half is the
/// <c>GBoxedCopyFunc</c> the attach call takes beside it. Neither is generated:
/// the gir shape of the callback is refused by the planner, and the copy is
/// what pairs with <see cref="Gst.Interop.CallbackHandle.DestroyNotify"/> so
/// that a copied item holds a handle of its own rather than the one its source
/// holds.
/// </remarks>
internal static unsafe class VideoGLTextureUploadTrampoline
{
    /// <summary>Gets the upload address that is handed to native code.</summary>
    internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&Invoke;

    /// <summary>Gets the user data copy address that is handed to native code.</summary>
    internal static nint CopyUserData => (nint)(delegate* unmanaged[Cdecl]<nint, nint>)&Copy;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Invoke(nint meta, nint textureId)
    {
        try
        {
            if (meta == 0)
            {
                return default;
            }

            // The upload function is handed the item and the block only; the
            // state is the user_data field of the item itself
            // (gstvideometa.c:1289), which is the handle the attach allocated.
            VideoGLTextureUploadMetaRaw* raw = (VideoGLTextureUploadMetaRaw*)meta;
            if (Gst.Interop.CallbackHandle.GetState<Gst.Video.VideoGLTextureUpload>(raw->UserData)
                is not { } callback)
            {
                return default;
            }

            Gst.Video.VideoGLTextureUploadMeta metaValue = Gst.Video.VideoGLTextureUploadMeta.FromNative(meta)
                ?? throw new InvalidOperationException("GstVideoGLTextureUpload passed no meta.");

            int count = (int)Math.Min(raw->NTextures, (uint)Gst.Video.VideoGLTextureUploadMeta.MaxTextures);
            return callback(metaValue, new ReadOnlySpan<uint>((void*)textureId, count)) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return default;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nint Copy(nint userData)
    {
        try
        {
            // A copied item is freed by the same destroy notification as its
            // source, so it needs a handle of its own to the one delegate.
            return Gst.Interop.CallbackHandle.GetState<Gst.Video.VideoGLTextureUpload>(userData) is { } callback
                ? Gst.Interop.CallbackHandle.Alloc(callback).UserData
                : 0;
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return 0;
        }
    }
}
