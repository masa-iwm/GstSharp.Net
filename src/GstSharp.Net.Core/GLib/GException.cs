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
