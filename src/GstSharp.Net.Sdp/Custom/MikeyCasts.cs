using System;

namespace Gst.Sdp;

public sealed unsafe partial class MIKEYPayloadKEMAC
{
    /// <summary>Reinterprets a MIKEY payload as a <c>GstMIKEYPayloadKEMAC</c>.</summary>
    /// <param name="payload">The payload to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the payload is of
    /// another type.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMIKEYPayload</c> is the first
    /// field of every derived payload structure, so both wrappers address the
    /// same storage. The view takes no part in the ownership of it - it holds
    /// no reference of its own - so it is only good for as long as
    /// <paramref name="payload"/> is.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The payload was disposed.</exception>
    public static Gst.Sdp.MIKEYPayloadKEMAC? FromPayload(Gst.Sdp.MIKEYPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Type == Gst.Sdp.MIKEYPayloadType.Kemac
            ? Gst.Sdp.MIKEYPayloadKEMAC.FromNative(payload.Handle)
            : null;
    }
}

public sealed unsafe partial class MIKEYPayloadKeyData
{
    /// <summary>Reinterprets a MIKEY payload as a <c>GstMIKEYPayloadKeyData</c>.</summary>
    /// <param name="payload">The payload to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the payload is of
    /// another type.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMIKEYPayload</c> is the first
    /// field of every derived payload structure, so both wrappers address the
    /// same storage. The view takes no part in the ownership of it - it holds
    /// no reference of its own - so it is only good for as long as
    /// <paramref name="payload"/> is.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The payload was disposed.</exception>
    public static Gst.Sdp.MIKEYPayloadKeyData? FromPayload(Gst.Sdp.MIKEYPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Type == Gst.Sdp.MIKEYPayloadType.KeyData
            ? Gst.Sdp.MIKEYPayloadKeyData.FromNative(payload.Handle)
            : null;
    }
}

public sealed unsafe partial class MIKEYPayloadPKE
{
    /// <summary>Reinterprets a MIKEY payload as a <c>GstMIKEYPayloadPKE</c>.</summary>
    /// <param name="payload">The payload to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the payload is of
    /// another type.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMIKEYPayload</c> is the first
    /// field of every derived payload structure, so both wrappers address the
    /// same storage. The view takes no part in the ownership of it - it holds
    /// no reference of its own - so it is only good for as long as
    /// <paramref name="payload"/> is.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The payload was disposed.</exception>
    public static Gst.Sdp.MIKEYPayloadPKE? FromPayload(Gst.Sdp.MIKEYPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Type == Gst.Sdp.MIKEYPayloadType.Pke
            ? Gst.Sdp.MIKEYPayloadPKE.FromNative(payload.Handle)
            : null;
    }
}

public sealed unsafe partial class MIKEYPayloadRAND
{
    /// <summary>Reinterprets a MIKEY payload as a <c>GstMIKEYPayloadRAND</c>.</summary>
    /// <param name="payload">The payload to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the payload is of
    /// another type.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMIKEYPayload</c> is the first
    /// field of every derived payload structure, so both wrappers address the
    /// same storage. The view takes no part in the ownership of it - it holds
    /// no reference of its own - so it is only good for as long as
    /// <paramref name="payload"/> is.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The payload was disposed.</exception>
    public static Gst.Sdp.MIKEYPayloadRAND? FromPayload(Gst.Sdp.MIKEYPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Type == Gst.Sdp.MIKEYPayloadType.Rand
            ? Gst.Sdp.MIKEYPayloadRAND.FromNative(payload.Handle)
            : null;
    }
}

public sealed unsafe partial class MIKEYPayloadSP
{
    /// <summary>Reinterprets a MIKEY payload as a <c>GstMIKEYPayloadSP</c>.</summary>
    /// <param name="payload">The payload to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the payload is of
    /// another type.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMIKEYPayload</c> is the first
    /// field of every derived payload structure, so both wrappers address the
    /// same storage. The view takes no part in the ownership of it - it holds
    /// no reference of its own - so it is only good for as long as
    /// <paramref name="payload"/> is.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The payload was disposed.</exception>
    public static Gst.Sdp.MIKEYPayloadSP? FromPayload(Gst.Sdp.MIKEYPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Type == Gst.Sdp.MIKEYPayloadType.Sp
            ? Gst.Sdp.MIKEYPayloadSP.FromNative(payload.Handle)
            : null;
    }
}

public sealed unsafe partial class MIKEYPayloadT
{
    /// <summary>Reinterprets a MIKEY payload as a <c>GstMIKEYPayloadT</c>.</summary>
    /// <param name="payload">The payload to reinterpret.</param>
    /// <returns>
    /// The typed view, or <see langword="null"/> when the payload is of
    /// another type.
    /// </returns>
    /// <remarks>
    /// No conversion and no allocation: <c>GstMIKEYPayload</c> is the first
    /// field of every derived payload structure, so both wrappers address the
    /// same storage. The view takes no part in the ownership of it - it holds
    /// no reference of its own - so it is only good for as long as
    /// <paramref name="payload"/> is.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The payload was disposed.</exception>
    public static Gst.Sdp.MIKEYPayloadT? FromPayload(Gst.Sdp.MIKEYPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Type == Gst.Sdp.MIKEYPayloadType.T
            ? Gst.Sdp.MIKEYPayloadT.FromNative(payload.Handle)
            : null;
    }
}
