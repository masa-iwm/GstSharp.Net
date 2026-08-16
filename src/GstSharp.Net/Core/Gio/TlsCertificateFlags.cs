namespace Gst.Gio;

/// <summary>
/// The ways in which a TLS certificate can fail verification.
/// </summary>
/// <remarks>
/// <para>
/// GLib guarantees that at least one flag is set when verification fails, but
/// not that every flag that applies is set. Masking a single flag out is
/// therefore not a safe way to allow one kind of bad certificate: the flag that
/// was masked may be the only one reported even though another problem exists
/// as well.
/// </para>
/// <para>
/// The generator names this enumeration in generated signatures through the
/// runtime-enumeration map of the planner, and the <c>GstRtsp</c> TLS surface is
/// what uses it: the validation flags of a connection are read and written with
/// it, and the accept-certificate callback receives the flags that failed. The
/// underlying type is part of that contract — the values the gir declares run to
/// 127, so both sides are <see langword="int"/>.
/// </para>
/// </remarks>
[Flags]
public enum TlsCertificateFlags
{
    /// <summary>No flags set. Since GLib 2.74.</summary>
    NoFlags = 0,

    /// <summary>The signing certificate authority is not known.</summary>
    UnknownCa = 1,

    /// <summary>The certificate does not match the expected identity of the site.</summary>
    BadIdentity = 2,

    /// <summary>The certificate's activation time is still in the future.</summary>
    NotActivated = 4,

    /// <summary>The certificate has expired.</summary>
    Expired = 8,

    /// <summary>The certificate has been revoked.</summary>
    Revoked = 16,

    /// <summary>The certificate's algorithm is considered insecure.</summary>
    Insecure = 32,

    /// <summary>Some other error occurred while validating the certificate.</summary>
    GenericError = 64,

    /// <summary>The combination of every flag above.</summary>
    ValidateAll = 127,
}
