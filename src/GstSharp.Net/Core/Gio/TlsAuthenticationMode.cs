namespace Gst.Gio;

/// <summary>
/// How much a TLS server asks of the client that connects to it.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors <c>GTlsAuthenticationMode</c>. It names what a server does
/// about a client certificate: ignore the question, ask for one and carry on
/// without it, or refuse the connection when none arrives. Requesting a
/// certificate is not the same as validating it — a server that asks for one
/// still has to look at what it was given.
/// </para>
/// <para>
/// The generator names this enumeration in generated signatures through the
/// runtime-enumeration map of the planner, and the <c>GstRtspServer</c>
/// authentication surface is what uses it. The underlying type is part of that
/// contract: the values the gir declares are 0 to 2, so both sides are
/// <see langword="int"/>. They are the same numbers on every platform.
/// </para>
/// </remarks>
public enum TlsAuthenticationMode
{
    /// <summary>Client authentication is not required.</summary>
    None = 0,

    /// <summary>A certificate is requested from the client, but not required.</summary>
    Requested = 1,

    /// <summary>A certificate is required from the client.</summary>
    Required = 2,
}
