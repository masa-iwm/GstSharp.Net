using System.Runtime.CompilerServices;
using Gst.Interop;

namespace Gst;

public sealed unsafe partial class DebugCategory
{
    /// <summary>
    /// Returns the debug category of <paramref name="name"/>, registering it
    /// when it does not exist yet.
    /// </summary>
    /// <param name="name">
    /// The name of the category, which is what <c>GST_DEBUG</c> patterns and the
    /// log output match on.
    /// </param>
    /// <param name="color">
    /// The color the category is printed in, built from the
    /// <c>GST_DEBUG_FG_</c>, <c>GST_DEBUG_BG_</c> and <c>GST_DEBUG_BOLD</c>
    /// values of the C headers. Zero leaves it uncolored.
    /// </param>
    /// <param name="description">
    /// What the category covers, shown by <c>gst-launch-1.0 --gst-debug-help</c>,
    /// or <see langword="null"/> when there is nothing to say.
    /// </param>
    /// <returns>The category to log through.</returns>
    /// <remarks>
    /// <para>
    /// This is the function behind <c>GST_DEBUG_CATEGORY_INIT</c>, and it is
    /// <b>get or create</b>: calling it twice with the same name does not
    /// register two categories, it hands the same one back both times. The
    /// first registration wins completely — a second call with a different
    /// color or description is accepted and then silently ignored, so the
    /// category keeps the color and the description it was first created with.
    /// Registering a category is therefore idempotent and needs no guard of its
    /// own, but a category whose description shows up wrong is a name that
    /// something else in the process claimed first.
    /// </para>
    /// <para>
    /// The wrapper is a bare pointer holder that owns nothing: the debug system
    /// keeps every category for the lifetime of the process, and there is no
    /// way to release one (<see cref="Free"/> is obsolete because it corrupts
    /// memory). Nothing has to be disposed, and two calls with one name may
    /// well return two wrappers that point at the same native category.
    /// </para>
    /// <para>
    /// Like every other entry point of the binding, this needs
    /// <c>GstSharp.Initialize()</c> to have run.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty.
    /// </exception>
    public static DebugCategory New(string name, uint color = 0, string? description = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Span<byte> nameBuffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope nameScope = GMarshal.StackUtf8(name, nameBuffer);
        Span<byte> descriptionBuffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope descriptionScope = GMarshal.StackUtf8(description, descriptionBuffer);

        return FromNative(DebugCategoryNative.New(nameScope.Pointer, color, descriptionScope.Pointer))
            ?? throw new InvalidOperationException("_gst_debug_category_new returned no value.");
    }

    /// <summary>
    /// Logs one message in this category, tagged with the place it was written.
    /// </summary>
    /// <param name="level">
    /// How important the message is. It is only logged when the threshold of
    /// the category is at least this verbose.
    /// </param>
    /// <param name="message">
    /// The text to log, taken as it is: it is never used as a format string, so
    /// a percent sign in it is a percent sign.
    /// </param>
    /// <param name="source">
    /// The object the message is about, which the log output names, or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="file">The file of the call site; supplied by the compiler.</param>
    /// <param name="function">The member of the call site; supplied by the compiler.</param>
    /// <param name="line">The line of the call site; supplied by the compiler.</param>
    /// <remarks>
    /// <para>
    /// The last three parameters are what the <c>GST_CAT_DEBUG</c> family of
    /// macros fills in from <c>__FILE__</c>, <c>__FUNCTION__</c> and
    /// <c>__LINE__</c>. The compiler fills them in here, so they are the file,
    /// the member and the line of the code that called this — passing them
    /// explicitly is possible but rarely what is wanted.
    /// </para>
    /// <para>
    /// A message that the threshold of the category rejects is dropped here
    /// rather than in native code. That is the same first test
    /// <c>gst_debug_log_literal</c> applies, so nothing is suppressed that the
    /// library would have logged; doing it early saves marshalling three
    /// strings for a message nobody wants, which is what makes a debug level
    /// call cheap enough to leave in a hot path.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="message"/>, <paramref name="file"/> or
    /// <paramref name="function"/> is <see langword="null"/>.
    /// </exception>
    public void Log(
        DebugLevel level,
        string message,
        Gst.GObject.Object? source = null,
        [CallerFilePath] string file = "",
        [CallerMemberName] string function = "",
        [CallerLineNumber] int line = 0)
    {
        // Validated before the threshold test, so that a null message is a bug
        // that reports itself at every level rather than only at the ones that
        // happen to be enabled.
        ArgumentNullException.ThrowIfNull(message);

        if (level > GetThreshold())
        {
            return;
        }

        Global.DebugLogLiteral(this, level, file, function, line, source, message);
    }
}
