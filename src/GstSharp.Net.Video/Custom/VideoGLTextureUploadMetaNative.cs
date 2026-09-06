using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// Raw entry points of <c>libgstvideo-1.0</c> that the hand written texture
/// upload glue needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_video_gl_texture_upload_meta_upload</c> is imported by hand because
/// its <c>texture_id</c> is a <c>guint texture_id[4]</c> the gir spells as a
/// bare <c>guint</c> with the star in the <c>c:type</c> alone, which no
/// generated projection covers.
/// </para>
/// <para>
/// <c>gst_buffer_add_video_gl_texture_upload_meta</c> is imported by hand
/// because it takes the upload function, a user data copy and a user data free
/// over one <c>user_data</c> and the gir gives a closure index to the upload
/// alone, which the planner refuses. The three function pointers are passed as
/// raw addresses, since the two boxed callbacks have no generated delegate
/// either.
/// </para>
/// </remarks>
internal static unsafe partial class VideoGLTextureUploadMetaNative
{
    /// <summary>
    /// Uploads the buffer that owns the metadata item into the textures whose
    /// identifiers the block holds.
    /// </summary>
    /// <param name="meta">The metadata item.</param>
    /// <param name="textureId">The address of four texture identifiers.</param>
    /// <returns>Non-zero when the upload succeeded.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_gl_texture_upload_meta_upload")]
    internal static partial int Upload(nint meta, uint* textureId);

    /// <summary>Attaches an OpenGL texture upload to a buffer.</summary>
    /// <param name="buffer">The buffer, which must be writable.</param>
    /// <param name="textureOrientation">Which way up the textures are.</param>
    /// <param name="nTextures">How many textures the item declares, one to four.</param>
    /// <param name="textureType">The address of four texture types.</param>
    /// <param name="upload">The upload function, which the item stores.</param>
    /// <param name="userData">Its state, which the item stores.</param>
    /// <param name="userDataCopy">
    /// The <c>GBoxedCopyFunc</c> that duplicates the state for a copy of the
    /// item.
    /// </param>
    /// <param name="userDataFree">
    /// The <c>GBoxedFreeFunc</c> that releases the state of one item.
    /// </param>
    /// <returns>
    /// The metadata item, which the buffer owns, or <c>0</c> when the buffer
    /// refused it.
    /// </returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_buffer_add_video_gl_texture_upload_meta")]
    internal static partial nint BufferAddVideoGLTextureUploadMeta(
        nint buffer,
        Gst.Video.VideoGLTextureOrientation textureOrientation,
        uint nTextures,
        Gst.Video.VideoGLTextureType* textureType,
        nint upload,
        nint userData,
        nint userDataCopy,
        nint userDataFree);
}
