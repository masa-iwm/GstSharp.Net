using System.Runtime.InteropServices;

namespace Gst.Interop;

/// <summary>
/// The one structured log field of GLib the runtime writes.
/// </summary>
/// <remarks>
/// <c>GLogField</c> is <c>{ const gchar *key; gconstpointer value; gssize
/// length; }</c>. A length of <c>-1</c> says that the value is a NUL terminated
/// string, which is the only shape the runtime uses.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GLogFieldNative
{
    /// <summary>The name of the field, such as <c>MESSAGE</c>.</summary>
    internal byte* Key;

    /// <summary>The value of the field.</summary>
    internal nint Value;

    /// <summary>The length of the value, or <c>-1</c> for a NUL terminated string.</summary>
    internal nint Length;
}

internal static unsafe partial class GLibNative
{
    /// <summary>Value of <c>G_LOG_LEVEL_WARNING</c>.</summary>
    internal const uint LogLevelWarning = 1u << 4;

    /// <summary>
    /// Writes one message to the log of GLib.
    /// </summary>
    /// <remarks>
    /// This is the non variadic face of <c>g_warning</c> and friends. The
    /// runtime uses it rather than <c>g_log</c> because a variadic entry point
    /// cannot be imported portably, and because nothing the runtime logs is a
    /// format string: the message is always built on the managed side.
    /// </remarks>
    [LibraryImport("GLib", EntryPoint = "g_log_structured_array")]
    internal static partial void LogStructuredArray(uint logLevel, GLogFieldNative* fields, nuint fieldCount);

    /// <summary>
    /// Logs a warning the way <c>g_warning</c> would, without a format string.
    /// </summary>
    /// <param name="domain">The log domain, such as <c>GLib-GObject</c>.</param>
    /// <param name="message">The message, which is written literally.</param>
    /// <remarks>
    /// The runtime warns instead of throwing where GLib itself would warn: a
    /// property identifier a managed subclass never installed, or an object
    /// whose wrapper has gone. Both are reported to whoever runs the process
    /// through the channel the rest of GLib uses, so <c>G_DEBUG=fatal-warnings</c>
    /// catches them as well.
    /// </remarks>
    internal static void Warn(string domain, string message)
    {
        Span<byte> domainBuffer = stackalloc byte[GMarshal.StackBufferSize];
        Span<byte> messageBuffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope domainScope = GMarshal.StackUtf8(domain, domainBuffer);
        using Utf8Scope messageScope = GMarshal.StackUtf8(message, messageBuffer);

        ReadOnlySpan<byte> messageKey = "MESSAGE\0"u8;
        ReadOnlySpan<byte> domainKey = "GLIB_DOMAIN\0"u8;

        fixed (byte* messageKeyPointer = messageKey)
        fixed (byte* domainKeyPointer = domainKey)
        {
            GLogFieldNative* fields = stackalloc GLogFieldNative[2];
            fields[0].Key = messageKeyPointer;
            fields[0].Value = (nint)messageScope.Pointer;
            fields[0].Length = -1;
            fields[1].Key = domainKeyPointer;
            fields[1].Value = (nint)domainScope.Pointer;
            fields[1].Length = -1;

            LogStructuredArray(LogLevelWarning, fields, 2);
        }
    }
}
