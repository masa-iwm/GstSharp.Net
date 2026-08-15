using System.Text;

namespace Gst.Interop;

/// <summary>
/// Thrown when a native GStreamer module cannot be loaded.
/// </summary>
/// <remarks>
/// The message lists every file that was tried, together with the reason why
/// the loader considered it, so that a failed load can be diagnosed without a
/// debugger.
/// </remarks>
public sealed class GstNativeLoadException : DllNotFoundException
{
    /// <summary>
    /// Initialises a new instance with a default message.
    /// </summary>
    public GstNativeLoadException()
        : base("A native GStreamer module could not be loaded.")
    {
        AttemptedPaths = [];
    }

    /// <summary>
    /// Initialises a new instance with the given message.
    /// </summary>
    /// <param name="message">The message of the exception.</param>
    public GstNativeLoadException(string message)
        : base(message)
    {
        AttemptedPaths = [];
    }

    /// <summary>
    /// Initialises a new instance with the given message and cause.
    /// </summary>
    /// <param name="message">The message of the exception.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public GstNativeLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
        AttemptedPaths = [];
    }

    internal GstNativeLoadException(string logicalName, IReadOnlyList<string> attemptedPaths)
        : base(BuildMessage(logicalName, attemptedPaths))
    {
        LogicalName = logicalName;
        AttemptedPaths = attemptedPaths;
    }

    /// <summary>
    /// Gets the logical name of the module that could not be loaded, for
    /// example <c>Gst</c>.
    /// </summary>
    public string? LogicalName { get; }

    /// <summary>
    /// Gets the files that were tried, in the order in which they were tried.
    /// </summary>
    public IReadOnlyList<string> AttemptedPaths { get; }

    private static string BuildMessage(string logicalName, IReadOnlyList<string> attemptedPaths)
    {
        StringBuilder message = new();
        message.Append("Could not load the native GStreamer module \"").Append(logicalName).Append("\".");

        if (attemptedPaths.Count == 0)
        {
            message.Append(" No installation candidate was found.");
        }
        else
        {
            message.AppendLine().Append("Tried:");
            foreach (string attempt in attemptedPaths)
            {
                message.AppendLine().Append("  ").Append(attempt);
            }
        }

        message.AppendLine().AppendLine().Append(Hint());
        return message.ToString();
    }

    private static string Hint()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Install the official GStreamer runtime (MSVC or MinGW flavor) from " +
                "https://gstreamer.freedesktop.org/download/, or point the bindings at an existing " +
                "installation with Gst.Interop.NativeLoader.Configure(directory, flavor). The " +
                "directory has to be the one that holds the DLLs, normally <root>\\bin.";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "Install the official GStreamer framework from https://gstreamer.freedesktop.org/download/ " +
                "or run \"brew install gstreamer\", or point the bindings at an existing installation with " +
                "Gst.Interop.NativeLoader.Configure(directory, null).";
        }

        return "Install the GStreamer runtime with the package manager of the distribution, for example " +
            "\"apt install libgstreamer1.0-0 gstreamer1.0-plugins-base\", or point the bindings at an " +
            "existing installation with Gst.Interop.NativeLoader.Configure(directory, null).";
    }
}
