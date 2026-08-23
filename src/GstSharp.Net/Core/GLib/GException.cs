using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// The memory layout of a <c>GError</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GErrorNative
{
    private readonly uint _domain;
    private readonly int _code;
    private readonly nint _message;

    /// <summary>Gets the quark of the error domain.</summary>
    public Quark Domain => new(_domain);

    /// <summary>Gets the domain specific error code.</summary>
    public int Code => _code;

    /// <summary>Gets the pointer to the null terminated error message.</summary>
    public nint MessagePointer => _message;
}

/// <summary>
/// A <c>GError</c> that was reported by GLib, GStreamer or one of their
/// plugins.
/// </summary>
public class GException : Exception
{
    /// <summary>
    /// Initialises a new instance with a default message.
    /// </summary>
    public GException()
        : base("The operation failed.")
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message.
    /// </summary>
    /// <param name="message">The message of the exception.</param>
    public GException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and cause.
    /// </summary>
    /// <param name="message">The message of the exception.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public GException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance from the fields of a <c>GError</c>.
    /// </summary>
    /// <param name="domain">The quark of the error domain.</param>
    /// <param name="code">The domain specific error code.</param>
    /// <param name="message">The message of the error.</param>
    public GException(Quark domain, int code, string message)
        : base(message)
    {
        Domain = domain;
        Code = code;
    }

    /// <summary>
    /// Gets the quark of the error domain, for example <c>gst-core-error-quark</c>.
    /// </summary>
    public Quark Domain { get; }

    /// <summary>
    /// Gets the domain specific error code.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// Reads a <c>GError</c> the binding only borrows into a managed value.
    /// </summary>
    /// <param name="error">
    /// The <c>GError*</c> to read, may be <see cref="nint.Zero"/>.
    /// </param>
    /// <returns>
    /// The exception value, or <see langword="null"/> when
    /// <paramref name="error"/> was <see cref="nint.Zero"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The three fields are copied eagerly, and the pointer is never released:
    /// it belongs to whatever produced it. That is what a signal argument and
    /// a <c>transfer-ownership="none"</c> return hand over, and both are only
    /// valid while the call or the emission that produced them runs - the
    /// error behind <c>ges_asset_get_error</c> is cleared by
    /// <c>ges_asset_needs_reload</c>, and the error of
    /// <c>GstDiscoverer::discovered</c> is freed once the emission returns.
    /// The value this answers outlives both, because it shares nothing with
    /// them.
    /// </para>
    /// <para>
    /// It stays internal, as <see cref="Gst.Interop.GObjectNative"/> does: the
    /// generated trampolines of every module in this repository reach it
    /// through <c>InternalsVisibleTo</c>.
    /// </para>
    /// </remarks>
    internal static unsafe GException? FromBorrowed(nint error)
    {
        if (error == nint.Zero)
        {
            return null;
        }

        GErrorNative* native = (GErrorNative*)error;
        return new GException(
            native->Domain,
            native->Code,
            GMarshal.PtrToStringUtf8(native->MessagePointer) ?? "The operation failed.");
    }

    /// <summary>
    /// Validates that an exception value can be built into a <c>GError</c>.
    /// </summary>
    /// <param name="error">The value to validate, may be <see langword="null"/>.</param>
    /// <param name="paramName">The name of the parameter that carries it.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="error"/> carries no error domain, no message, or a
    /// message with an embedded null.
    /// </exception>
    /// <remarks>
    /// <c>g_error_new_literal</c> answers <c>NULL</c> with a critical when the
    /// domain is zero or the message is <c>NULL</c> (gerror.c:340-347), and
    /// every constructor but <see cref="GException(Quark, int, string)"/>
    /// leaves <see cref="Domain"/> at zero. A domain the binding owned itself
    /// is deliberately not substituted here: a call that reports an error of
    /// no domain is one no consumer of the message can classify, and
    /// accepting one later is an additive change. A message that carries an
    /// embedded null is refused here as well, so that the parameter the
    /// exception names is the one the caller passed rather than the internal
    /// one of the string encoder that would otherwise reject it.
    /// </remarks>
    internal static void ValidateForNative(GException? error, string paramName)
    {
        if (error is null)
        {
            return;
        }

        if (error.Domain.Value == 0)
        {
            throw new ArgumentException(
                "The error has no domain. A GError handed to native code needs a registered GQuark; "
                + "construct it with the GException(Quark, int, string) constructor.",
                paramName);
        }

        if (string.IsNullOrEmpty(error.Message))
        {
            throw new ArgumentException("The error has no message.", paramName);
        }

        if (error.Message.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("The error message contains an embedded null.", paramName);
        }
    }

    /// <summary>
    /// Throws the error that a native call stored in its <c>GError**</c> out
    /// parameter, and releases it.
    /// </summary>
    /// <param name="error">
    /// The <c>GError*</c> that the call produced. It is set back to
    /// <see cref="nint.Zero"/> before the exception is thrown.
    /// </param>
    /// <exception cref="GException">The pointer was not <see cref="nint.Zero"/>.</exception>
    public static unsafe void ThrowIfSet(ref nint error)
    {
        if (error == nint.Zero)
        {
            return;
        }

        GErrorNative* native = (GErrorNative*)error;
        Quark domain = native->Domain;
        int code = native->Code;
        string message = GMarshal.PtrToStringUtf8(native->MessagePointer) ?? "The operation failed.";

        GLibNative.ErrorFree(error);
        error = nint.Zero;

        throw new GException(domain, code, message);
    }
}
