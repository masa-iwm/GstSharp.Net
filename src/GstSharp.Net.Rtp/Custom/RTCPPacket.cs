using System.Runtime.InteropServices;
using System.Text;

namespace Gst.Rtp;

/// <content>
/// The four APP and feedback members whose C signature the generator does not
/// bind: two that return a pointer into the mapped buffer which the gir types
/// as a single byte, and two that read and write a four byte name that is not a
/// C string.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_rtcp_packet_fb_get_fci</c> and <c>gst_rtcp_packet_app_get_data</c>
/// are declared <c>guint8 *</c> and the gir spells their return as a plain
/// <c>guint8</c>, so the generator refuses them rather than truncate a pointer
/// to one byte. The length lives in the matching <c>*_length</c> getter, which
/// counts 32 bit words (<c>gstrtcpbuffer.c</c>:2405, :2631), and both data
/// getters answer NULL for a packet whose word length is not greater than two
/// (:2462, :2691) - the state a packet is in before its length has been set.
/// </para>
/// <para>
/// <c>gst_rtcp_packet_app_get_name</c> returns a pointer to the four name bytes
/// of the packet, which are not zero terminated (<c>gstrtcpbuffer.c</c>
/// :2598-2606), and <c>gst_rtcp_packet_app_set_name</c> copies exactly four
/// bytes out of what it is handed (:2584). A marshalled C string would read
/// past the name in one direction and hand the library a terminator in the
/// other, so both are spelled here over the fixed four bytes the RTCP APP
/// header carries.
/// </para>
/// </remarks>
public unsafe partial struct RTCPPacket
{
    /// <summary>The number of bytes of the ASCII name of an APP packet.</summary>
    private const int AppNameLength = 4;

    /// <summary>
    /// Reads the Feedback Control Information of an RTPFB or PSFB packet.
    /// </summary>
    /// <returns>
    /// The FCI bytes, or an empty span when the packet carries none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The span points into the buffer the owning <see cref="RTCPBuffer"/> has
    /// mapped: it is valid until that buffer is unmapped, and adding, removing
    /// or resizing a packet of the same buffer invalidates it as well. Copy
    /// whatever has to outlive the mapping.
    /// </para>
    /// <para>
    /// The length is <see cref="FbGetFciLength"/> 32 bit words, so the span is
    /// four times as long in bytes. A packet whose FCI length is zero - one
    /// whose <see cref="FbSetFciLength"/> has not been called - answers an
    /// empty span rather than a span over whatever follows the header.
    /// </para>
    /// </remarks>
    public Span<byte> FbGetFci()
    {
        ushort words = FbGetFciLength();
        if (words == 0)
        {
            return Span<byte>.Empty;
        }

        fixed (RTCPPacket* self = &this)
        {
            byte* data = GstRtcpPacketFbGetFci(self);
            return data == null ? Span<byte>.Empty : new Span<byte>(data, words * 4);
        }
    }

    /// <summary>
    /// Reads the application dependent data of an APP packet.
    /// </summary>
    /// <returns>
    /// The data bytes, or an empty span when the packet carries none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The span points into the buffer the owning <see cref="RTCPBuffer"/> has
    /// mapped, under the same rules as <see cref="FbGetFci"/>: it dies with the
    /// mapping and with any change to the packet list of the buffer.
    /// </para>
    /// <para>
    /// The length is <see cref="AppGetDataLength"/> 32 bit words, so the span
    /// is four times as long in bytes; writing to it fills the data of a packet
    /// whose buffer was mapped for writing.
    /// </para>
    /// </remarks>
    public Span<byte> AppGetData()
    {
        ushort words = AppGetDataLength();
        if (words == 0)
        {
            return Span<byte>.Empty;
        }

        fixed (RTCPPacket* self = &this)
        {
            byte* data = GstRtcpPacketAppGetData(self);
            return data == null ? Span<byte>.Empty : new Span<byte>(data, words * 4);
        }
    }

    /// <summary>
    /// Reads the four byte name of an APP packet.
    /// </summary>
    /// <returns>
    /// The name, always four characters long, as the packet spells it.
    /// </returns>
    /// <remarks>
    /// The name field of an APP packet is four ASCII bytes with no terminator,
    /// and the library hands out a pointer to them inside the mapped buffer.
    /// All four are returned as they are, padding included, because those four
    /// bytes are the name; nothing is trimmed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The packet is not an APP packet, or its buffer is not mapped for
    /// reading, so the library answered no name.
    /// </exception>
    public string AppGetName()
    {
        fixed (RTCPPacket* self = &this)
        {
            byte* name = GstRtcpPacketAppGetName(self);
            return name == null
                ? throw new InvalidOperationException(
                    "gst_rtcp_packet_app_get_name returned no name; the packet is not an APP packet of a buffer mapped for reading.")
                : Encoding.ASCII.GetString(name, AppNameLength);
        }
    }

    /// <summary>
    /// Writes the four byte name of an APP packet.
    /// </summary>
    /// <param name="name">
    /// The name, which has to be exactly four ASCII characters.
    /// </param>
    /// <remarks>
    /// The library copies four bytes out of what it is handed and reads no
    /// terminator, so a shorter name would leave it reading past the string and
    /// a longer one would silently lose its tail. Both are refused here instead.
    /// On a packet that is not an APP packet, or whose buffer is not mapped for
    /// writing, GStreamer raises a critical and does nothing
    /// (<c>gstrtcpbuffer.c</c>:2578-2581); the method has no way of reporting
    /// that.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not four characters long, or one of them is
    /// not ASCII.
    /// </exception>
    public void AppSetName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length != AppNameLength)
        {
            throw new ArgumentException(
                $"The name of an APP packet is exactly {AppNameLength} characters long.",
                nameof(name));
        }

        Span<byte> bytes = stackalloc byte[AppNameLength];
        for (int i = 0; i < AppNameLength; i++)
        {
            if (name[i] > 0x7F)
            {
                throw new ArgumentException(
                    "The name of an APP packet is ASCII.",
                    nameof(name));
            }

            bytes[i] = (byte)name[i];
        }

        fixed (RTCPPacket* self = &this)
        fixed (byte* namePointer = bytes)
        {
            GstRtcpPacketAppSetName(self, namePointer);
        }
    }

    /// <summary>The <c>gst_rtcp_packet_fb_get_fci</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_rtcp_packet_fb_get_fci")]
    private static partial byte* GstRtcpPacketFbGetFci(RTCPPacket* packet);

    /// <summary>The <c>gst_rtcp_packet_app_get_data</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_rtcp_packet_app_get_data")]
    private static partial byte* GstRtcpPacketAppGetData(RTCPPacket* packet);

    /// <summary>The <c>gst_rtcp_packet_app_get_name</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_rtcp_packet_app_get_name")]
    private static partial byte* GstRtcpPacketAppGetName(RTCPPacket* packet);

    /// <summary>The <c>gst_rtcp_packet_app_set_name</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_rtcp_packet_app_set_name")]
    private static partial void GstRtcpPacketAppSetName(RTCPPacket* packet, byte* name);
}
