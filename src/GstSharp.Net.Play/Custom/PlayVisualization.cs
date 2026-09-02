namespace Gst.Play;

/// <content>
/// The two fields of a visualization descriptor. A boxed record made of two
/// <c>gchar*</c> has no C accessor for either of them, so the generator drops
/// both and the wrapper reads the storage itself.
/// </content>
public sealed unsafe partial class PlayVisualization
{
    /// <summary>
    /// Gets the name of the visualization element, which is what
    /// <see cref="Play.SetVisualization(string?)"/> takes.
    /// </summary>
    /// <remarks>
    /// The field is a <c>gchar*</c> the descriptor owns; what is handed back is
    /// a managed copy of it, so it outlives the wrapper.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public string Name
    {
        get
        {
            PlayVisualizationRaw* raw = (PlayVisualizationRaw*)Handle;
            string? name = Gst.Interop.GMarshal.PtrToStringUtf8(raw->Name);
            GC.KeepAlive(this);

            // gst_play_update_visualization_list fills both fields from the
            // registry with g_strdup, and the name of an element factory is
            // never null, so an empty string is only what a descriptor built
            // some other way could carry.
            return name ?? string.Empty;
        }
    }

    /// <summary>
    /// Gets the human readable description of the visualization element, which
    /// is the description metadata of its factory.
    /// </summary>
    /// <remarks>
    /// The field is a <c>gchar*</c> the descriptor owns; what is handed back is
    /// a managed copy of it, so it outlives the wrapper.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public string Description
    {
        get
        {
            PlayVisualizationRaw* raw = (PlayVisualizationRaw*)Handle;
            string? description = Gst.Interop.GMarshal.PtrToStringUtf8(raw->Description);
            GC.KeepAlive(this);
            return description ?? string.Empty;
        }
    }
}
