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
/// The wrapper exists ahead of the members that use it: nothing in the binding
/// takes or returns this type yet, because the planner has no way of naming a
/// hand written enumeration in generated code. It is the shape the
/// <c>GstRtsp</c> TLS surface needs once that support lands.
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
