using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.WebRTC;

/// <content>
/// The stream factory of an ICE agent, whose <c>(transfer full)</c> annotation
/// the C did not keep before 1.26.10 and 1.27.50.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_webrtc_ice_add_stream</c> is annotated <c>(transfer full)</c> in the
/// gir and in the base class (<c>ice.c</c>), but the libnice implementation
/// below it — <c>gst_webrtc_nice_add_stream</c> in
/// <c>gst-libs/gst/webrtc/nice/nice.c</c> — returned <c>item-&gt;stream</c>,
/// the reference the agent's own <c>nice_stream_map</c> holds, without taking
/// one. The reference is missing in every 1.24.x release and in 1.26.0 through
/// 1.26.9 (checked 1.24.2, 1.24.10, 1.24.12, 1.24.13 and 1.26.0 through
/// 1.26.9). Upstream commit <c>ea6200de</c> ("webrtc: Keep a ref of the
/// ICEStream in the TransportStream", MR 10312) added the
/// <c>gst_object_ref (item-&gt;stream)</c> the annotation always promised; its
/// first release tags are 1.27.50 and 1.28.0, and it was backported to the 1.26
/// branch as <c>dbc83c3ec1</c> ("webrtc: Keep a ref of the ICEStream in the
/// TransportStream"), whose first tag is 1.26.10. The reference is therefore
/// present from 1.26.10 and from 1.27.50 / 1.28.0 on.
/// </para>
/// <para>
/// The runtime floor of this binding is 1.24, so on a runtime that predates the
/// fix the generated member adopted the agent's only reference: disposing the
/// wrapper freed the stream while the agent still listed it, and the agent's own
/// finalizer — <c>_clear_ice_stream</c>, which reads the <c>ice</c> property
/// off <c>item-&gt;stream</c> and then unreferences it — was a use after free.
/// A <c>Transfer.None</c> wrapper would not help, because the stream is built
/// with <c>g_object_new</c> and never sunk in 1.24
/// (<c>_create_nice_stream_item</c> calls <c>gst_webrtc_nice_stream_new</c>),
/// so it is floating in the map and the constructor of
/// <see cref="Gst.GObject.Object"/> sinks a floating handle whatever the
/// transfer says. The member below mirrors upstream's line instead: it takes
/// the reference the C forgot.
/// </para>
/// </remarks>
public abstract unsafe partial class WebRTCICE
{
    /// <summary>
    /// Whether the native library already takes the reference its annotation
    /// promises, which is every release from 1.26.10 and from 1.27.50 on.
    /// </summary>
    /// <remarks>
    /// The gate names both first tags, because the fix reached 1.26 as the
    /// backport <c>dbc83c3ec1</c> rather than as <c>ea6200de</c> itself. It
    /// errs toward taking the reference: a reference too many leaks one small
    /// object, a reference too few frees a live one. That is why the 1.27
    /// clause starts at 1.27.50 — a git build of main between
    /// <c>ea6200de</c> and the 1.27.50 tag reports 1.27.2.1, so it is read as
    /// lacking the fix and gets the extra reference by that policy.
    /// </remarks>
    private static readonly bool AddStreamReturnsItsOwnReference = ComputeAddStreamReturnsItsOwnReference();

    /// <summary>The <c>gst_webrtc_ice_add_stream</c> function.</summary>
    /// <param name="sessionId">The session id</param>
    /// <returns>The #GstWebRTCICEStream, or %NULL</returns>
    /// <remarks>
    /// This is the hand written binding of a call whose ownership the C kept
    /// only from 1.26.10 and from 1.27.50 on; see the remarks on the class. On
    /// a runtime that predates the fix
    /// the reference the annotation promises is taken here, so that disposing
    /// the wrapper leaves the one the agent's stream map holds intact.
    /// </remarks>
    public Gst.WebRTC.WebRTCICEStream? AddStream(uint sessionId)
    {
        nint nativeResult = GstWebRTCIceAddStream(Handle, sessionId);
        if (nativeResult != 0 && !AddStreamReturnsItsOwnReference)
        {
            // Before ea6200de (and its 1.26 backport dbc83c3ec1) the libnice
            // implementation handed out the agent's own reference; take the
            // one the annotation promises so that
            // disposing the wrapper leaves the agent's intact.
            GObjectNative.ObjectRef(nativeResult);
        }

        WebRTCICEStream? result =
            Gst.GObject.Object.FromNative<WebRTCICEStream>(nativeResult, Transfer.Full);
        System.GC.KeepAlive(this);
        return result;
    }

    /// <summary>Reads the version gate off the loaded native library.</summary>
    /// <returns>
    /// <see langword="true"/> when the running GStreamer is 1.26.10 or newer on
    /// the 1.26 branch, or 1.27.50 or newer anywhere else.
    /// </returns>
    /// <remarks>
    /// <see cref="Gst.Version.IsAtLeast(uint, uint, uint)"/> answers the
    /// 1.27.50 half on its own, and the 1.26 half is that question asked of the
    /// 1.26 branch alone, which is why the minor version is named beside it: a
    /// bare <c>IsAtLeast(1, 26, 10)</c> would also answer
    /// <see langword="true"/> for 1.27.1, where the fix is not. The read needs
    /// an initialised binding, which is given: the only caller is
    /// <see cref="AddStream"/>, and a live <c>WebRTCICE</c> wrapper cannot
    /// exist before <c>GstSharp.Initialize</c> has run.
    /// </remarks>
    private static bool ComputeAddStreamReturnsItsOwnReference()
    {
        Gst.Version version = global::GstSharp.NativeVersion;

        return version.IsAtLeast(1, 27, 50)
            || (version.Major == 1 && version.Minor == 26 && version.IsAtLeast(1, 26, 10));
    }

    /// <summary>The <c>gst_webrtc_ice_add_stream</c> entry point.</summary>
    [LibraryImport("GstWebRTC", EntryPoint = "gst_webrtc_ice_add_stream")]
    private static partial nint GstWebRTCIceAddStream(nint ice, uint sessionId);
}
