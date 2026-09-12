using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.RtspServer;

/// <content>
/// The stream transports of a session media, which the C hands out as the
/// array it keeps for itself.
/// </content>
/// <remarks>
/// <c>gst_rtsp_session_media_get_transports</c> answers a <c>GPtrArray</c>,
/// which the generator only marshals for signals, and the gir puts a
/// <c>transfer full</c> on it that is true of the container alone: the C
/// returns its own array with one extra reference and leaves the elements to
/// the <c>g_object_unref</c> free function the array carries. The member below
/// is therefore the whole binding: it references what it hands out and drops
/// the container once.
/// </remarks>
public unsafe partial class RTSPSessionMedia
{
    /// <summary>
    /// Reads the stream transports of this session media, one slot per stream
    /// of the media.
    /// </summary>
    /// <returns>
    /// The transports, indexed by stream index, with
    /// <see langword="null"/> for every stream that has not been set up.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_rtsp_session_media_get_transports</c>. The array is as
    /// long as the media has streams and its index is the stream index, so a
    /// slot pairs with <c>RTSPMedia.GetStream</c> of the same index; a slot is
    /// <see langword="null"/> until a SETUP request configures that stream,
    /// which is what makes the answer readable before a client has asked for
    /// anything. <see cref="GetTransport(uint)"/> is the single slot twin of
    /// this member.
    /// </para>
    /// <para>
    /// The C hands its own array back with one extra reference on the
    /// container and none on the elements, which the array releases itself.
    /// Every transport is therefore wrapped as a reference of its own and the
    /// container is dropped before this member returns, so what comes back
    /// outlives the call and nothing here frees a transport the session still
    /// uses.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public RTSPStreamTransport?[] GetTransports()
    {
        nint array = GstRtspSessionMediaGetTransports(Handle);
        if (array == 0)
        {
            System.GC.KeepAlive(this);
            return [];
        }

        try
        {
            // GPtrArray states its own length and its elements in the two
            // public fields of its header, which is what Gst.GLib mirrors.
            Gst.GLib.PtrArrayNative native = *(Gst.GLib.PtrArrayNative*)array;
            if (native.Length == 0 || native.Data == 0)
            {
                return [];
            }

            nint* elements = (nint*)native.Data;
            RTSPStreamTransport?[] transports = new RTSPStreamTransport?[native.Length];
            for (uint index = 0; index < native.Length; index++)
            {
                // A transfer of none is a reference of the wrapper's own, over
                // the one the array holds for the session.
                transports[index] = Gst.GObject.Object.FromNative<RTSPStreamTransport>(
                    elements[index],
                    Transfer.None);
            }

            return transports;
        }
        finally
        {
            // The one reference the call added, and nothing of the elements:
            // releasing those is the free function of the array, and it runs
            // when the session media itself is done with them.
            PtrArrayUnref(array);
            System.GC.KeepAlive(this);
        }
    }

    /// <summary>The <c>gst_rtsp_session_media_get_transports</c> entry point.</summary>
    [LibraryImport("GstRtspServer", EntryPoint = "gst_rtsp_session_media_get_transports")]
    private static partial nint GstRtspSessionMediaGetTransports(nint media);

    /// <summary>The <c>g_ptr_array_unref</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_ptr_array_unref")]
    private static partial void PtrArrayUnref(nint array);
}
