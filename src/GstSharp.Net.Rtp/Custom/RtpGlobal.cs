using System.Runtime.InteropServices;

namespace Gst.Rtp;

/// <content>
/// The two NTP header extension setters whose block of bytes the generator
/// cannot see as an array.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_rtp_hdrext_set_ntp_56</c> and <c>gst_rtp_hdrext_set_ntp_64</c> write
/// into a block the caller owns and are handed its length beside it, but the
/// gir spells their <c>data</c> as a bare <c>gpointer</c> with no array
/// element, while the matching getters carry <c>(array length=size)</c> and
/// therefore already take a <see cref="Span{T}"/>. An annotation override
/// never turns a bare pointer into an array, so the generated pair would be a
/// raw <c>nint</c> next to a <c>size</c> the caller has to keep honest. Both
/// are bound here over the same span shape as the getters instead.
/// </para>
/// <para>
/// The C answers FALSE for a NULL block and for one shorter than the extension
/// (<c>gstrtphdrext.c</c>:71-72 and :119-120), but it does so through
/// <c>g_return_val_if_fail</c>, which logs a critical. The length is checked
/// here before the call, so a short span answers the same false without the
/// log, and an empty span - which pins to a null pointer - never reaches the
/// library either.
/// </para>
/// </remarks>
public static unsafe partial class RtpGlobal
{
    /// <summary>
    /// The number of bytes the NTP-56 header extension occupies,
    /// <c>GST_RTP_HDREXT_NTP_56_SIZE</c> of <c>gstrtphdrext.h</c>:46.
    /// </summary>
    private const int Ntp56Size = 7;

    /// <summary>
    /// The number of bytes the NTP-64 header extension occupies,
    /// <c>GST_RTP_HDREXT_NTP_64_SIZE</c> of <c>gstrtphdrext.h</c>:36.
    /// </summary>
    private const int Ntp64Size = 8;

    /// <summary>
    /// Writes an NTP time into the bytes of an NTP-56 header extension.
    /// </summary>
    /// <param name="data">The block to write into.</param>
    /// <param name="ntptime">The NTP time to write.</param>
    /// <returns>
    /// <see langword="true"/> once the bytes are written, and
    /// <see langword="false"/> only when <paramref name="data"/> is shorter
    /// than the seven bytes of the extension.
    /// </returns>
    /// <remarks>
    /// The low 56 bits of <paramref name="ntptime"/> land big endian in the
    /// first seven bytes of <paramref name="data"/>
    /// (<c>gstrtphdrext.c</c>:122-125): the top byte of a full 64 bit value is
    /// dropped, so a value that does not fit in 56 bits does not round trip
    /// through <see cref="RtpHdrextGetNtp56"/>. Anything past the seventh byte
    /// is left alone.
    /// </remarks>
    public static bool RtpHdrextSetNtp56(System.Span<byte> data, ulong ntptime)
    {
        if (data.Length < Ntp56Size)
        {
            return false;
        }

        fixed (byte* dataPointer = data)
        {
            int nativeResult = GstRtpHdrextSetNtp56(dataPointer, (uint)data.Length, ntptime);
            return nativeResult != 0;
        }
    }

    /// <summary>
    /// Writes an NTP time into the bytes of an NTP-64 header extension.
    /// </summary>
    /// <param name="data">The block to write into.</param>
    /// <param name="ntptime">The NTP time to write.</param>
    /// <returns>
    /// <see langword="true"/> once the bytes are written, and
    /// <see langword="false"/> only when <paramref name="data"/> is shorter
    /// than the eight bytes of the extension.
    /// </returns>
    /// <remarks>
    /// The whole of <paramref name="ntptime"/> lands big endian in the first
    /// eight bytes of <paramref name="data"/> (<c>gstrtphdrext.c</c>:74).
    /// Anything past the eighth byte is left alone.
    /// </remarks>
    public static bool RtpHdrextSetNtp64(System.Span<byte> data, ulong ntptime)
    {
        if (data.Length < Ntp64Size)
        {
            return false;
        }

        fixed (byte* dataPointer = data)
        {
            int nativeResult = GstRtpHdrextSetNtp64(dataPointer, (uint)data.Length, ntptime);
            return nativeResult != 0;
        }
    }

    /// <summary>The <c>gst_rtp_hdrext_set_ntp_56</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_rtp_hdrext_set_ntp_56")]
    private static partial int GstRtpHdrextSetNtp56(byte* data, uint size, ulong ntptime);

    /// <summary>The <c>gst_rtp_hdrext_set_ntp_64</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_rtp_hdrext_set_ntp_64")]
    private static partial int GstRtpHdrextSetNtp64(byte* data, uint size, ulong ntptime);
}
