using System;

namespace Gst.Video;

public sealed unsafe partial class VideoGLTextureUploadMeta
{
    /// <summary>The number of texture identifiers the C function reads.</summary>
    /// <remarks>
    /// <c>GST_VIDEO_MAX_PLANES</c>, which is what
    /// <c>gst_video_gl_texture_upload_meta_upload</c> forwards to the upload
    /// function of the metadata item.
    /// </remarks>
    private const int MaxTextures = 4;

    /// <summary>
    /// Uploads the buffer that owns this metadata item into the textures whose
    /// identifiers the span holds.
    /// </summary>
    /// <param name="textureIds">
    /// The identifiers of the textures to upload into, at least
    /// <see cref="NTextures"/> of them.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the upload succeeded.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_video_gl_texture_upload_meta_upload</c>. The C function
    /// takes a <c>guint texture_id[4]</c> and hands the block to the upload
    /// function the metadata item carries (<c>gstvideometa.c:1284-1289</c>),
    /// while the gir types the parameter as a bare <c>guint</c> with the star
    /// in the <c>c:type</c> alone; that is why the call is written by hand.
    /// </para>
    /// <para>
    /// The caller must have OpenGL set up and must call this from a thread on
    /// which it is valid to upload to an OpenGL texture
    /// (<c>gstvideometa.h:341-343</c>).
    /// </para>
    /// <para>
    /// No element of the GStreamer 1.28 tree implements the upload function,
    /// and the call that attaches the metadata item is a deprecated closure
    /// path this binding does not bind, so an item can only reach managed code
    /// attached to a buffer by a native element.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="textureIds"/> holds fewer than <see cref="NTextures"/>
    /// identifiers.
    /// </exception>
    public bool Upload(ReadOnlySpan<uint> textureIds)
    {
        uint declared = NTextures;
        if (textureIds.Length < declared)
        {
            throw new ArgumentException(
                $"The metadata item declares {declared} texture(s), so at least that many identifiers have to "
                + $"be given; {textureIds.Length} were.",
                nameof(textureIds));
        }

        // The C function reads four elements whatever n_textures says, so the
        // block that is handed over is always four wide and zero filled. Only
        // the declared count is copied in: gst_buffer_add_video_gl_texture_
        // upload_meta bounds it to 1..4 (gstvideometa.c:1253), and a longer
        // span is the caller's own storage rather than something to read past
        // the end of this one from.
        uint* block = stackalloc uint[MaxTextures];
        int copied = (int)Math.Min(declared, (uint)MaxTextures);
        textureIds[..copied].CopyTo(new Span<uint>(block, copied));

        int nativeResult = VideoGLTextureUploadMetaNative.Upload(Handle, block);
        GC.KeepAlive(this);
        return nativeResult != 0;
    }

    /// <summary>
    /// The shape this call shipped with in 1.28.5, which never worked.
    /// </summary>
    /// <param name="textureId">The identifier of the one texture to upload into.</param>
    /// <returns>
    /// <see langword="true"/> when the upload succeeded.
    /// </returns>
    /// <remarks>
    /// The C function dereferences its <c>texture_id</c> as an array of four,
    /// so the shipped overload handed the identifier itself over as the address
    /// of that array. This overload is kept only so that code compiled against
    /// 1.28.5 still builds; it forwards a one element span, which is a working
    /// call for a metadata item that declares one texture and an
    /// <see cref="ArgumentException"/> for any other. Use
    /// <see cref="Upload(ReadOnlySpan{uint})"/> instead.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The metadata item declares more than one texture.
    /// </exception>
    [Obsolete("This overload passed the texture id where the library reads an array of four; use " +
        "Upload(ReadOnlySpan<uint>). It will be removed in 1.30.", error: false)]
    public bool Upload(uint textureId) => Upload([textureId]);
}
