using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// Raw entry points of <c>libgstvideo-1.0</c> that the hand written texture
/// upload glue needs.
/// </summary>
/// <remarks>
/// <c>gst_video_gl_texture_upload_meta_upload</c> is imported by hand because
/// its <c>texture_id</c> is a <c>guint texture_id[4]</c> the gir spells as a
/// bare <c>guint</c> with the star in the <c>c:type</c> alone, which no
/// generated projection covers.
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
}
