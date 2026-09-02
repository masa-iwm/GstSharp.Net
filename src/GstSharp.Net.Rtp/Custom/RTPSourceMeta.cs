using System.Runtime.InteropServices;

namespace Gst.Rtp;

/// <content>
/// The two members that carry an optional SSRC, which C states as a pointer
/// that may be NULL and the gir as a plain <c>guint32</c>.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_buffer_add_rtp_source_meta</c> and
/// <c>gst_rtp_source_meta_set_ssrc</c> take their <c>ssrc</c> as a
/// <c>guint32 *</c> that is allowed to be NULL, which is how the absent SSRC of
/// a buffer is spelled, and dereference it when it is not
/// (<c>gstrtpmeta.c</c>:63-64, :151-152). The gir carries neither a direction
/// nor an array annotation on it, so the parameter would be generated as a
/// value and the library would read through whatever address that value spells;
/// both are bound here over a <see cref="Nullable{T}"/> instead, where the
/// absent SSRC is a NULL pointer and nothing else.
/// </para>
/// </remarks>
public sealed unsafe partial class RTPSourceMeta
{
    /// <summary>
    /// The number of contributing sources a source meta can hold, which is
    /// <c>GST_RTP_SOURCE_META_MAX_CSRC_COUNT</c>.
    /// </summary>
    private const int MaxCsrcCount = 15;

    /// <summary>
    /// Attaches RTP source information to a buffer.
    /// </summary>
    /// <param name="buffer">The buffer the meta is added to.</param>
    /// <param name="ssrc">
    /// The synchronization source, or <see langword="null"/> when the buffer
    /// states none.
    /// </param>
    /// <param name="csrc">The contributing sources, at most 15 of them.</param>
    /// <returns>
    /// The meta, which the buffer owns, or <see langword="null"/> when the meta
    /// could not be added - which is what a buffer that is not writable does.
    /// </returns>
    /// <remarks>
    /// The gir says the return is never NULL, and <c>gstrtpmeta.c</c>:58-61
    /// says otherwise: <c>gst_buffer_add_meta</c> answers NULL for a buffer it
    /// may not write to, and the function forwards that.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="csrc"/> holds more than the fifteen entries an RTP
    /// header can carry, which the library refuses.
    /// </exception>
    public static RTPSourceMeta? Add(Gst.Buffer buffer, uint? ssrc, ReadOnlySpan<uint> csrc)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(csrc.Length, MaxCsrcCount, nameof(csrc));

        uint ssrcValue = ssrc.GetValueOrDefault();
        nint result;
        fixed (uint* csrcPointer = csrc)
        {
            result = GstBufferAddRtpSourceMeta(
                buffer.Handle,
                ssrc.HasValue ? &ssrcValue : null,
                csrcPointer,
                (uint)csrc.Length);
        }

        System.GC.KeepAlive(buffer);
        return FromNative(result);
    }

    /// <summary>
    /// Sets or clears the synchronization source of this meta.
    /// </summary>
    /// <param name="ssrc">
    /// The synchronization source, or <see langword="null"/> to state that the
    /// meta carries none.
    /// </param>
    /// <returns><see langword="true"/> on success.</returns>
    /// <remarks>
    /// A <see langword="null"/> argument leaves the SSRC field as it is and
    /// clears <see cref="SsrcValid"/>, which is what the absent SSRC of a
    /// source meta is (<c>gstrtpmeta.c</c>:150-157).
    /// </remarks>
    public bool SetSsrc(uint? ssrc)
    {
        uint ssrcValue = ssrc.GetValueOrDefault();
        int result = GstRtpSourceMetaSetSsrc(Handle, ssrc.HasValue ? &ssrcValue : null);
        System.GC.KeepAlive(this);
        return result != 0;
    }

    /// <summary>The <c>gst_buffer_add_rtp_source_meta</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_buffer_add_rtp_source_meta")]
    private static partial nint GstBufferAddRtpSourceMeta(nint buffer, uint* ssrc, uint* csrc, uint csrcCount);

    /// <summary>The <c>gst_rtp_source_meta_set_ssrc</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_rtp_source_meta_set_ssrc")]
    private static partial int GstRtpSourceMetaSetSsrc(nint meta, uint* ssrc);
}
