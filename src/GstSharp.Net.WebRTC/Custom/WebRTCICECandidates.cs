using System.Collections.Generic;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.WebRTC;

/// <content>
/// The two candidate listings of an ICE agent, which the C hands out as a
/// block whose elements are freed one at a time and whose block is freed as a
/// whole.
/// </content>
/// <remarks>
/// Both entry points answer a NULL terminated
/// <c>GstWebRTCICECandidateStats**</c>, and what the planner has no shape for
/// is not the termination - the gir states no length and no zero termination,
/// which the reader defaults to zero terminated - but the element: a boxed
/// record is not a blittable value it marshals, and no annotation can say
/// that the block and its elements are freed by different frees.
/// The two frees do differ: every element was allocated on its own and is
/// released by <c>gst_webrtc_ice_candidate_stats_free</c>, which is the boxed
/// free of the type, while the block that holds them is a plain buffer the
/// caller releases with <c>g_free</c>. The members below are the whole binding
/// of both.
/// </remarks>
public abstract unsafe partial class WebRTCICE
{
    /// <summary>
    /// Lists the local ICE candidates of one stream.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>
    /// The candidates, which the caller owns, or an empty array.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_webrtc_ice_get_local_candidates</c>, available since
    /// 1.22. It is a snapshot: what comes back is the state of gathering at
    /// the moment of the call, so a stream that has not gathered yet answers
    /// an empty array and the same call answers more a moment later.
    /// </para>
    /// <para>
    /// Every element is adopted rather than copied. The block the agent
    /// allocated holds one <c>GstWebRTCICECandidateStats</c> per candidate,
    /// each allocated on its own, and the boxed free of that type is the very
    /// function that releases one — so the wrappers own what they were handed
    /// and releasing them is the disposal the C asks for. The block itself is
    /// released here. The caller disposes the elements, or leaves that to
    /// their finalizers.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This wrapper or <paramref name="stream"/> was disposed.</exception>
    public WebRTCICECandidateStats[] GetLocalCandidates(WebRTCICEStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        WebRTCICECandidateStats[] candidates =
            AdoptCandidates(GstWebRTCIceGetLocalCandidates(Handle, stream.Handle));
        System.GC.KeepAlive(this);
        System.GC.KeepAlive(stream);
        return candidates;
    }

    /// <summary>
    /// Lists the remote ICE candidates of one stream.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>
    /// The candidates, which the caller owns, or an empty array.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_webrtc_ice_get_remote_candidates</c>, available since
    /// 1.22 and the twin of <see cref="GetLocalCandidates"/> in every respect:
    /// the same snapshot, the same ownership, the same empty answer for a
    /// stream that has learned no candidate of the other side yet.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This wrapper or <paramref name="stream"/> was disposed.</exception>
    public WebRTCICECandidateStats[] GetRemoteCandidates(WebRTCICEStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        WebRTCICECandidateStats[] candidates =
            AdoptCandidates(GstWebRTCIceGetRemoteCandidates(Handle, stream.Handle));
        System.GC.KeepAlive(this);
        System.GC.KeepAlive(stream);
        return candidates;
    }

    /// <summary>
    /// Takes over a NULL terminated block of candidate statistics and releases
    /// the block.
    /// </summary>
    /// <param name="block">The block the C answered, or <c>0</c>.</param>
    /// <returns>The wrappers, which own their elements.</returns>
    /// <remarks>
    /// The nice backend never answers a null pointer — it appends the
    /// terminator whether or not it found a candidate — but the getters are a
    /// virtual method of an abstract class, so a backend that is not the nice
    /// one can, and that reads as the empty array.
    /// </remarks>
    private static WebRTCICECandidateStats[] AdoptCandidates(nint block)
    {
        if (block == 0)
        {
            return [];
        }

        nint* elements = (nint*)block;
        List<WebRTCICECandidateStats> candidates = [];
        try
        {
            for (int index = 0; elements[index] != 0; index++)
            {
                candidates.Add(
                    WebRTCICECandidateStats.FromNative(elements[index], Transfer.Full)
                    ?? throw new InvalidOperationException(
                        "An ICE agent answered a block with a null candidate in it."));
            }
        }
        catch
        {
            // The wrappers built so far own their elements and release them
            // when they are collected; the ones that were never adopted are
            // released here, so the block strands nothing either way.
            for (int index = candidates.Count; elements[index] != 0; index++)
            {
                GObjectNative.BoxedFree(WebRTCICECandidateStats.GetGType(), elements[index]);
            }

            throw;
        }
        finally
        {
            // The block is a plain buffer around the elements, so it is freed
            // on its own and never with a free that would touch them again.
            GLibNative.Free(block);
        }

        return [.. candidates];
    }

    /// <summary>The <c>gst_webrtc_ice_get_local_candidates</c> entry point.</summary>
    [LibraryImport("GstWebRTC", EntryPoint = "gst_webrtc_ice_get_local_candidates")]
    private static partial nint GstWebRTCIceGetLocalCandidates(nint ice, nint stream);

    /// <summary>The <c>gst_webrtc_ice_get_remote_candidates</c> entry point.</summary>
    [LibraryImport("GstWebRTC", EntryPoint = "gst_webrtc_ice_get_remote_candidates")]
    private static partial nint GstWebRTCIceGetRemoteCandidates(nint ice, nint stream);
}
