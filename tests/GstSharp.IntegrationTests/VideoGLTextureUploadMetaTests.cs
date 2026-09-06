using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written texture upload surface of
/// <see cref="Gst.Video.VideoGLTextureUploadMeta"/>: attaching a managed upload
/// function to a buffer, reaching it again, and the lifetime of the state that
/// carries it.
/// </summary>
/// <remarks>
/// No element of the GStreamer 1.28 tree implements the upload function, and a
/// real upload needs an OpenGL context on the calling thread, so what is
/// exercised here is the managed end of it:
/// <see cref="Gst.Video.VideoGlobal.BufferAddVideoGLTextureUploadMeta"/>
/// attaches a delegate, and
/// <see cref="Gst.Video.VideoGLTextureUploadMeta.Upload(System.ReadOnlySpan{uint})"/>
/// forwards the block of identifiers straight back to it without any OpenGL
/// being involved.
/// </remarks>
[Collection(GstCollection.Name)]
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

    /// <summary>
    /// An attached upload is handed the item it was attached to and exactly
    /// <c>n_textures</c> identifiers.
    /// </summary>
    /// <remarks>
    /// The C forwards the address of the whole four wide block
    /// (<c>gstvideometa.c:1284-1290</c>); the length the delegate sees is the
    /// count the item declares, which is what the caller asked for.
    /// </remarks>
    [Fact]
    public void AnAttachedUploadIsReachedWithTheDeclaredNumberOfTextureIds()
    {
        using Buffer buffer = Buffer.New();

        List<uint[]> seen = [];
        nint uploaded = 0;
        VideoGLTextureUpload upload = (meta, ids) =>
        {
            uploaded = meta.Handle;
            seen.Add(ids.ToArray());
            return true;
        };

        VideoGLTextureUploadMeta? added = VideoGlobal.BufferAddVideoGLTextureUploadMeta(
            buffer,
            VideoGLTextureOrientation.NormalYFlip,
            [VideoGLTextureType.Luminance, VideoGLTextureType.LuminanceAlpha],
            upload);

        Assert.NotNull(added);
        Assert.Equal(2u, added.NTextures);
        Assert.Equal(VideoGLTextureOrientation.NormalYFlip, added.TextureOrientation);

        Assert.True(added.Upload([11u, 22u, 33u, 44u]));
        Assert.Equal(added.Handle, uploaded);
        Assert.Equal<uint>([11u, 22u], Assert.Single(seen));

        Assert.Single(buffer.IterateMeta());
    }

    /// <summary>
    /// A copy of the buffer carries an item of its own that reaches the same
    /// delegate.
    /// </summary>
    /// <remarks>
    /// The library shares the upload function between an item and its copy and
    /// duplicates the state through the user data copy this binding supplies,
    /// so the copy holds a second handle to the one delegate. That second
    /// handle is what is read out of the raw item here: both user data slots
    /// are non-zero and they differ, which is the observable statement that the
    /// copy took a handle rather than the one its source holds. The copy and
    /// the free the transform carried over are checked against the pair this
    /// binding passes, so the rule that the two always travel together is
    /// pinned across the native transform as well.
    /// </remarks>
    [Fact]
    public unsafe void ACopyOfTheBufferReachesTheSameUpload()
    {
        using Buffer buffer = Buffer.New();

        int calls = 0;
        VideoGLTextureUpload upload = (meta, ids) =>
        {
            calls++;
            return true;
        };

        VideoGLTextureUploadMeta? added = VideoGlobal.BufferAddVideoGLTextureUploadMeta(
            buffer,
            VideoGLTextureOrientation.NormalYNormal,
            [VideoGLTextureType.Rgba],
            upload);
        Assert.NotNull(added);

        Buffer? copied = buffer.Copy();
        Assert.NotNull(copied);
        using Buffer copy = copied;

        Gst.Meta item = Assert.Single(copy.IterateMeta());
        VideoGLTextureUploadMeta? carried = VideoGLTextureUploadMeta.FromNative(item.Handle);
        Assert.NotNull(carried);
        Assert.NotEqual(added.Handle, carried.Handle);

        VideoGLTextureUploadMetaRaw* source = (VideoGLTextureUploadMetaRaw*)added.Handle;
        VideoGLTextureUploadMetaRaw* copiedRaw = (VideoGLTextureUploadMetaRaw*)carried.Handle;
        Assert.NotEqual((nint)0, source->UserData);
        Assert.NotEqual((nint)0, copiedRaw->UserData);
        Assert.NotEqual(source->UserData, copiedRaw->UserData);
        Assert.Equal(VideoGLTextureUploadTrampoline.CopyUserData, copiedRaw->UserDataCopy);
        Assert.Equal((nint)Gst.Interop.CallbackHandle.DestroyNotify, copiedRaw->UserDataFree);

        Assert.True(carried.Upload([7u]));
        Assert.True(added.Upload([7u]));
        Assert.Equal(2, calls);
    }

    /// <summary>
    /// The handle of an item and the handle of its copy are both released when
    /// the buffers that carry them are.
    /// </summary>
    /// <remarks>
    /// One weak reference is enough for both: the delegate stays alive while
    /// any <see cref="Gst.Interop.CallbackHandle"/> still names it, so a
    /// collected delegate is the statement that neither the item nor the copy
    /// leaked one.
    /// </remarks>
    [Fact]
    public void DisposingBothBuffersReleasesBothHandles()
    {
        WeakReference state = AttachToABufferAndItsCopy();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(state.IsAlive);
    }

    /// <summary>
    /// A buffer somebody else holds is refused, and the handle the refused
    /// attach allocated is released.
    /// </summary>
    /// <remarks>
    /// <c>gst_buffer_add_video_gl_texture_upload_meta</c> answers NULL before
    /// it stores the user data (<c>gstvideometa.c:1259-1260</c>), so the user
    /// data free it was handed never runs and nothing native would ever release
    /// the handle. This test is noisy on purpose: the library prints a critical
    /// for the writability check that failed inside
    /// <c>gst_buffer_add_meta</c>.
    /// </remarks>
    [Fact]
    public void ASharedBufferIsRefusedAndReleasesTheHandle()
    {
        using Buffer buffer = Buffer.New();

        nint shared = buffer.Handle;
        TestNatives.MiniObjectRef(shared);

        WeakReference state;
        try
        {
            Assert.False(buffer.IsWritable);
            state = AttachToARefusedBuffer(buffer);
            Assert.Empty(buffer.IterateMeta());
        }
        finally
        {
            TestNatives.MiniObjectUnref(shared);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(state.IsAlive);
    }

    /// <summary>
    /// Neither no texture type nor more than four of them is an item.
    /// </summary>
    [Fact]
    public void TheNumberOfTextureTypesIsCheckedAgainstTheOneToFourTheLibraryTakes()
    {
        using Buffer buffer = Buffer.New();
        VideoGLTextureUpload upload = (meta, ids) => true;

        Assert.Throws<ArgumentException>(
            "textureType",
            () => VideoGlobal.BufferAddVideoGLTextureUploadMeta(
                buffer,
                VideoGLTextureOrientation.NormalYNormal,
                [],
                upload));

        Assert.Throws<ArgumentOutOfRangeException>(
            "textureType",
            () => VideoGlobal.BufferAddVideoGLTextureUploadMeta(
                buffer,
                VideoGLTextureOrientation.NormalYNormal,
                [
                    VideoGLTextureType.Luminance,
                    VideoGLTextureType.Luminance,
                    VideoGLTextureType.Luminance,
                    VideoGLTextureType.Luminance,
                    VideoGLTextureType.Luminance,
                ],
                upload));

        Assert.Throws<ArgumentNullException>(
            "upload",
            () => VideoGlobal.BufferAddVideoGLTextureUploadMeta(
                buffer,
                VideoGLTextureOrientation.NormalYNormal,
                [VideoGLTextureType.Rgba],
                null!));

        Assert.Throws<ArgumentNullException>(
            "buffer",
            () => VideoGlobal.BufferAddVideoGLTextureUploadMeta(
                null!,
                VideoGLTextureOrientation.NormalYNormal,
                [VideoGLTextureType.Rgba],
                upload));

        Assert.Empty(buffer.IterateMeta());
    }

    /// <summary>
    /// Attaches an upload to a buffer, copies the buffer, releases both and
    /// reports what is left over.
    /// </summary>
    /// <returns>A weak reference to the upload function.</returns>
    /// <remarks>
    /// Everything is created in a frame of its own, so that neither the test
    /// frame nor the delegate cache of the compiler keeps the delegate alive
    /// when the collection above decides whether the two handles did.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AttachToABufferAndItsCopy()
    {
        int calls = 0;
        VideoGLTextureUpload upload = (meta, ids) =>
        {
            calls++;
            return true;
        };

        WeakReference weak = new(upload);

        using (Buffer buffer = Buffer.New())
        {
            Assert.NotNull(VideoGlobal.BufferAddVideoGLTextureUploadMeta(
                buffer,
                VideoGLTextureOrientation.NormalYNormal,
                [VideoGLTextureType.Rgba],
                upload));

            Buffer? copied = buffer.Copy();
            Assert.NotNull(copied);
            copied.Dispose();
        }

        Assert.Equal(0, calls);
        return weak;
    }

    /// <summary>
    /// Attaches an upload to a buffer that is not writable and reports what is
    /// left over.
    /// </summary>
    /// <param name="buffer">The buffer somebody else holds.</param>
    /// <returns>A weak reference to the upload function.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AttachToARefusedBuffer(Buffer buffer)
    {
        int calls = 0;
        VideoGLTextureUpload upload = (meta, ids) =>
        {
            calls++;
            return true;
        };

        WeakReference weak = new(upload);

        Assert.Null(VideoGlobal.BufferAddVideoGLTextureUploadMeta(
            buffer,
            VideoGLTextureOrientation.NormalYNormal,
            [VideoGLTextureType.Rgba],
            upload));

        Assert.Equal(0, calls);
        return weak;
    }
}
