using System.Diagnostics.CodeAnalysis;

namespace Gst.Interop;

/// <summary>
/// Sink for exceptions that are caught inside a callback invoked by native
/// code.
/// </summary>
/// <remarks>
/// A managed exception must never unwind through a native stack frame, so every
/// callback that GStreamer or GLib can invoke wraps its body in a
/// <c>try</c>/<c>catch</c> and reports the failure here. The default behaviour
/// writes the exception to the standard error stream; set
/// <c>GSTSHARP_FAILFAST=1</c> in the environment to turn such a failure into an
/// immediate <see cref="Environment.FailFast(string, Exception)"/> instead,
/// which is usually what you want in a test run.
/// </remarks>
public static class ExceptionTrap
{
    private static readonly bool FailFastRequested =
        string.Equals(Environment.GetEnvironmentVariable("GSTSHARP_FAILFAST"), "1", StringComparison.Ordinal);

    /// <summary>
    /// Raised for every exception that was caught on a native callback
    /// boundary. Handlers must not throw.
    /// </summary>
    public static event Action<Exception>? UnhandledException;

    /// <summary>
    /// Reports an exception that was caught on a native callback boundary.
    /// </summary>
    /// <param name="exception">The exception that was caught.</param>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failing report handler must not replace the exception that is being reported.")]
    public static void Report(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (FailFastRequested)
        {
            Environment.FailFast("GstSharp.Net: unhandled exception on a native callback boundary.", exception);
        }

        Action<Exception>? handler = UnhandledException;
        if (handler is not null)
        {
            try
            {
                handler(exception);
                return;
            }
            catch (Exception nested)
            {
                Console.Error.WriteLine($"GstSharp.Net: the exception handler itself failed: {nested}");
            }
        }

        Console.Error.WriteLine($"GstSharp.Net: unhandled exception on a native callback boundary: {exception}");
    }
}
