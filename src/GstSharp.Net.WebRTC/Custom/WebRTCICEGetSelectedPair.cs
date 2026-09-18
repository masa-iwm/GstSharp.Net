using System.Runtime.InteropServices;

namespace Gst.WebRTC;

/// <content>
/// The selected candidate pair of an ICE agent, a call whose implementation
/// upstream removed in 1.28 without removing the assertion above it.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_webrtc_ice_get_selected_pair</c> is a vfunc dispatch whose
/// dispatcher asserts the slot it is about to call:
/// <c>g_assert (GST_WEBRTC_ICE_GET_CLASS (ice)-&gt;get_selected_pair)</c>
/// (1.28.6 <c>ice.c:326</c>). Upstream commit <c>e4eb90d489</c> (2025-02-15)
/// took <c>gst_webrtc_nice_get_selected_pair</c> and its class assignment out of
/// <c>gst-libs/gst/webrtc/nice/nice.c</c>, and the base class leaves the slot
/// at <c>NULL</c> (1.28.6 <c>ice.c:687</c>). Its first release tags are
/// 1.27.50 and 1.28.0. The slot is filled at 1.24.13 (<c>nice.c:1250</c>,
/// wired at <c>:1729</c>) and at 1.26.11 (<c>nice.c:1252</c>, wired at
/// <c>:1731</c>), so the call is answerable below 1.28 and nowhere above it.
/// </para>
/// <para>
/// On a 1.27.50 or newer library the C therefore aborts the process — a
/// <c>g_assert</c> failure, or a call through a <c>NULL</c> function pointer
/// on a library built with <c>-Dglib_assert=false</c> — and neither shape is
/// catchable from managed code. The member below answers the same condition
/// with a <see cref="NotSupportedException"/> instead, which leaves the
/// process alive and names the replacement.
/// </para>
/// <para>
/// The gate reads the version rather than the class slot, although the slot is
/// what the C asserts on. Reading it would need a mirror of
/// <c>GstWebRTCICEClass</c>, which the binding does not emit for this type,
/// and it would buy nothing: <c>GstWebRTCICE</c> is abstract, libnice's is the
/// only implementation of it a caller of this binding ever holds — webrtcbin
/// builds its agent out of <c>gst_webrtc_nice_new</c> — and the version is
/// exactly what says whether that implementation still fills the slot.
/// </para>
/// </remarks>
public abstract unsafe partial class WebRTCICE
{
    /// <summary>The <c>gst_webrtc_ice_get_selected_pair</c> function.</summary>
    /// <param name="stream">The #GstWebRTCICEStream</param>
    /// <param name="localStats">A pointer to #GstWebRTCICECandidateStats for local candidate</param>
    /// <param name="remoteStats">pointer to #GstWebRTCICECandidateStats for remote candidate</param>
    /// <returns>FALSE on failure, otherwise @local_stats @remote_stats will be set</returns>
    /// <exception cref="NotSupportedException">
    /// The loaded GStreamer is 1.27.50 or newer, where the call aborts the
    /// process; see the remarks on the class.
    /// </exception>
    /// <remarks>
    /// This is the hand written binding of a call that is only answerable
    /// below 1.28. What it marshals below that is what the generated member
    /// marshalled: both out parameters are <c>(transfer full)</c>, so each is
    /// adopted as a wrapper the caller disposes, and a call that answers
    /// <see langword="false"/> leaves both of them
    /// <see langword="null"/> because the C leaves both pointers untouched.
    /// </remarks>
    [Obsolete("Use gst_webrtc_ice_transport_get_selected_candidate_pair(). (deprecated since 1.28)")]
    public bool GetSelectedPair(Gst.WebRTC.WebRTCICEStream stream, out Gst.WebRTC.WebRTCICECandidateStats? localStats, out Gst.WebRTC.WebRTCICECandidateStats? remoteStats)
    {
        if (global::GstSharp.NativeVersion.IsAtLeast(1, 27, 50))
        {
            // The gate sits at 1.27.50 rather than at 1.28, because a 1.27.50
            // host carries e4eb90d489 and hits the same assertion.
            throw new NotSupportedException(
                "gst_webrtc_ice_get_selected_pair has no implementation from GStreamer 1.27.50 on: "
                + "upstream removed the libnice one (e4eb90d489) and left the g_assert of the "
                + "dispatcher, so the call would abort the process. Use "
                + "WebRTCICETransport.GetSelectedCandidatePair(), which needs 1.28.");
        }

        ArgumentNullException.ThrowIfNull(stream);
        nint localStatsNative = default;
        nint remoteStatsNative = default;
        int nativeResult = GstWebrtcIceGetSelectedPair(Handle, stream.Handle, &localStatsNative, &remoteStatsNative);
        localStats = Gst.WebRTC.WebRTCICECandidateStats.FromNative(localStatsNative, Gst.Interop.Transfer.Full);
        remoteStats = Gst.WebRTC.WebRTCICECandidateStats.FromNative(remoteStatsNative, Gst.Interop.Transfer.Full);
        bool result = nativeResult != 0;
        System.GC.KeepAlive(this);
        System.GC.KeepAlive(stream);
        return result;
    }

    /// <summary>The <c>gst_webrtc_ice_get_selected_pair</c> entry point.</summary>
    [LibraryImport("GstWebRTC", EntryPoint = "gst_webrtc_ice_get_selected_pair")]
    private static partial int GstWebrtcIceGetSelectedPair(nint ice, nint stream, nint* localStats, nint* remoteStats);
}
