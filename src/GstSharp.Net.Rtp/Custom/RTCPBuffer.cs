using System.Runtime.InteropServices;

namespace Gst.Rtp;

/// <content>
/// The constructor that names the block it is given as one the library takes
/// over, which no managed caller can hand it.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_rtcp_buffer_new_take_data</c> is
/// <c>return gst_buffer_new_wrapped (data, len)</c> (gstrtcpbuffer.c:60-71),
/// and that wraps the block with <c>g_free</c> as its release function
/// (gstbuffer.c:1038-1042): the memory has to come from the GLib allocator, and
/// the buffer releases it when its last reference goes. The gir says
/// <c>transfer-ownership="none"</c> for the block all the same - the upstream
/// doc comment lacks the <c>(transfer full)</c> that the identical RTP twin
/// carries (gstrtcpbuffer.c:51 against gstrtpbuffer.c:153) - so the generated
/// member pinned the caller's own array and handed that address over.
/// </para>
/// <para>
/// The member is written here instead, with the signature the generated one
/// had: the bytes are copied into a block of the GLib allocator's and that copy
/// is what the buffer takes over, which is what
/// <see cref="RTCPBuffer.NewCopyData"/> does through the C function upstream
/// wrote for it.
/// </para>
/// </remarks>
public unsafe partial struct RTCPBuffer
{
    /// <summary>Creates a buffer that carries a copy of <paramref name="data"/>.</summary>
    /// <param name="data">The bytes of the new buffer.</param>
    /// <returns>A newly allocated buffer of the size of <paramref name="data"/>.</returns>
    /// <remarks>
    /// The name is the one upstream gave the C function, which takes the block
    /// it is handed over rather than copying it. This binding copies, because
    /// the library releases that block with <c>g_free</c> and only the GLib
    /// allocator makes one it may: what the member does is therefore exactly
    /// what <see cref="RTCPBuffer.NewCopyData"/> does, under the C name.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> is empty. The C function refuses a zero length
    /// with a critical and answers nothing (gstrtcpbuffer.c:63), so the length
    /// is checked here rather than raised there.
    /// </exception>
    public static Gst.Buffer NewTakeData(Span<byte> data)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("An RTCP buffer cannot be created from an empty block.", nameof(data));
        }

        nint block;
        fixed (byte* source = data)
        {
            block = Gst.Interop.GLibNative.Memdup2(source, (nuint)data.Length);
        }

        nint nativeResult = GstRtcpBufferNewTakeData(block, (uint)data.Length);
        Gst.Buffer? buffer = Gst.Buffer.FromNative(nativeResult, Gst.Interop.Transfer.Full);
        if (buffer is null)
        {
            // The C answers NULL only when it refused its arguments, and it
            // never took the block over in that case. The guard above makes
            // that unreachable; the copy is released all the same rather than
            // left to no owner.
            Gst.Interop.GLibNative.Free(block);
            throw new InvalidOperationException("gst_rtcp_buffer_new_take_data returned no value.");
        }

        return buffer;
    }

    /// <summary>The <c>gst_rtcp_buffer_new_take_data</c> entry point.</summary>
    [LibraryImport("GstRtp", EntryPoint = "gst_rtcp_buffer_new_take_data")]
    private static partial nint GstRtcpBufferNewTakeData(nint data, uint len);
}
