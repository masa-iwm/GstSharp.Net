using System.Runtime.InteropServices;

namespace Gst.Sdp;

/// <content>
/// The timestamp payload, whose byte count is a table lookup on the type
/// beside it rather than a parameter of the call.
/// </content>
/// <remarks>
/// <c>gst_mikey_payload_t_set</c> takes a bare <c>const guint8 *</c> with no
/// length at all, and reads exactly as many bytes as
/// <c>GstMIKEYTSType</c> says: eight for an NTP time in either flavour, four
/// for a counter. A count that is derived from the value of another argument
/// is not a shape the emitter has, so the call is skipped and written here.
/// </remarks>
public sealed unsafe partial class MIKEYPayload
{
    /// <summary>
    /// Sets the timestamp of a <see cref="MIKEYPayloadType.T"/> payload.
    /// </summary>
    /// <param name="type">The kind of timestamp <paramref name="tsValue"/> holds.</param>
    /// <param name="tsValue">
    /// The timestamp itself, big endian, of exactly the length
    /// <paramref name="type"/> calls for: eight bytes for
    /// <see cref="MIKEYTSType.NtpUtc"/> and <see cref="MIKEYTSType.Ntp"/>,
    /// four for <see cref="MIKEYTSType.Counter"/>. The bytes are copied.
    /// </param>
    /// <returns><see langword="true"/> on success.</returns>
    /// <remarks>
    /// This is <c>gst_mikey_payload_t_set</c>. The length is checked here
    /// because the C cannot: it reads the number of bytes the type calls for
    /// out of a pointer it is handed no count for, so a short array is read
    /// past its end.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="type"/> is not one of the three the library sizes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tsValue"/> is not exactly as long as
    /// <paramref name="type"/> calls for.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The payload is not a <see cref="MIKEYPayloadType.T"/> payload. The C
    /// answers that with an assertion failure on the console and changes
    /// nothing.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The payload was disposed.</exception>
    public bool TSet(MIKEYTSType type, ReadOnlySpan<byte> tsValue)
    {
        int length = TimestampLength(type);

        if (Type != MIKEYPayloadType.T)
        {
            throw new InvalidOperationException(
                "A timestamp can only be set on a payload of type T.");
        }

        CheckTimestampLength(type, length, tsValue);

        fixed (byte* tsValuePointer = tsValue)
        {
            int nativeResult = GstMikeyPayloadTSet(Handle, (int)type, tsValuePointer);
            bool result = nativeResult != 0;
            System.GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>
    /// The number of bytes a timestamp of the given kind occupies.
    /// </summary>
    /// <param name="type">The kind of timestamp.</param>
    /// <returns>Eight for either NTP flavour, four for a counter.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="type"/> is not one of the three. The C answers such a
    /// value with FALSE, which the wrapper does not pass on: a length it
    /// cannot compute is a mistake in the call rather than a refusal.
    /// </exception>
    internal static int TimestampLength(MIKEYTSType type) => type switch
    {
        MIKEYTSType.NtpUtc or MIKEYTSType.Ntp => 8,
        MIKEYTSType.Counter => 4,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type),
            type,
            "The library sizes a timestamp of NtpUtc, Ntp or Counter and of nothing else."),
    };

    /// <summary>
    /// Throws unless the timestamp is exactly as long as its kind calls for.
    /// </summary>
    /// <param name="type">The kind of timestamp.</param>
    /// <param name="length">The number of bytes that kind occupies.</param>
    /// <param name="tsValue">The timestamp to measure.</param>
    /// <exception cref="ArgumentException">The length does not match.</exception>
    internal static void CheckTimestampLength(
        MIKEYTSType type,
        int length,
        ReadOnlySpan<byte> tsValue)
    {
        if (tsValue.Length != length)
        {
            throw new ArgumentException(
                $"tsValue must hold exactly {length} bytes for a timestamp of {type}: "
                + "the call is handed no count and reads that many.",
                nameof(tsValue));
        }
    }

    /// <summary>The <c>gst_mikey_payload_t_set</c> entry point.</summary>
    [LibraryImport("GstSdp", EntryPoint = "gst_mikey_payload_t_set")]
    private static partial int GstMikeyPayloadTSet(nint payload, int type, byte* tsValue);
}

/// <content>
/// The call that adds a timestamp payload, whose byte count is the same table
/// lookup.
/// </content>
public sealed unsafe partial class MIKEYMessage
{
    /// <summary>
    /// Adds a timestamp payload to the message.
    /// </summary>
    /// <param name="type">The kind of timestamp <paramref name="tsValue"/> holds.</param>
    /// <param name="tsValue">
    /// The timestamp itself, big endian, of exactly the length
    /// <paramref name="type"/> calls for: eight bytes for
    /// <see cref="MIKEYTSType.NtpUtc"/> and <see cref="MIKEYTSType.Ntp"/>,
    /// four for <see cref="MIKEYTSType.Counter"/>. The bytes are copied.
    /// </param>
    /// <returns><see langword="true"/> on success.</returns>
    /// <remarks>
    /// This is <c>gst_mikey_message_add_t</c>, the general form of
    /// <see cref="AddTNowNtpUtc"/>: it builds a
    /// <see cref="MIKEYPayloadType.T"/> payload, sets the timestamp on it and
    /// appends it. The length is checked here for the reason
    /// <see cref="MIKEYPayload.TSet"/> gives.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="type"/> is not one of the three the library sizes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tsValue"/> is not exactly as long as
    /// <paramref name="type"/> calls for.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The message was disposed.</exception>
    public bool AddT(MIKEYTSType type, ReadOnlySpan<byte> tsValue)
    {
        int length = MIKEYPayload.TimestampLength(type);
        MIKEYPayload.CheckTimestampLength(type, length, tsValue);

        fixed (byte* tsValuePointer = tsValue)
        {
            int nativeResult = GstMikeyMessageAddT(Handle, (int)type, tsValuePointer);
            bool result = nativeResult != 0;
            System.GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>The <c>gst_mikey_message_add_t</c> entry point.</summary>
    [LibraryImport("GstSdp", EntryPoint = "gst_mikey_message_add_t")]
    private static partial int GstMikeyMessageAddT(nint msg, int type, byte* tsValue);
}
