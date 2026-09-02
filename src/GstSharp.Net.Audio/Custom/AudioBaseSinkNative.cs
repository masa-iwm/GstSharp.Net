using System.Runtime.InteropServices;

namespace Gst.Audio;

/// <summary>
/// Raw entry points of <c>libgstaudio-1.0</c> that the hand written custom
/// slaving glue needs.
/// </summary>
/// <remarks>
/// <c>gst_audio_base_sink_set_custom_slaving_callback</c> is imported by hand
/// because its callback type carries a <c>GstClockTimeDiff *requested_skew</c>
/// the gir spells as a bare <c>gint64</c>, which no generated projection
/// covers.
/// </remarks>
internal static unsafe partial class AudioBaseSinkNative
{
    /// <summary>
    /// Installs the custom slaving callback of a sink, together with the state
    /// it is invoked with and the notification that releases that state.
    /// </summary>
    /// <param name="sink">The sink to install on.</param>
    /// <param name="callback">The entry point, or <c>0</c> to clear.</param>
    /// <param name="userData">The state, or <c>0</c>.</param>
    /// <param name="notify">The notification, or <c>0</c>.</param>
    [LibraryImport("GstAudio", EntryPoint = "gst_audio_base_sink_set_custom_slaving_callback")]
    internal static partial void SetCustomSlavingCallback(nint sink, nint callback, nint userData, nint notify);
}
