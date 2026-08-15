namespace Gst.Interop;

/// <summary>
/// Ownership transfer of a native value that crosses the managed boundary.
/// </summary>
/// <remarks>
/// The values mirror the <c>transfer-ownership</c> annotation of the GObject
/// introspection data.
/// </remarks>
public enum Transfer
{
    /// <summary>
    /// The caller does not receive ownership. A wrapper that wants to keep the
    /// value alive has to take its own reference or copy.
    /// </summary>
    None,

    /// <summary>
    /// The caller owns the container (for example the array or list) but not
    /// the elements inside it.
    /// </summary>
    Container,

    /// <summary>
    /// The caller owns the value and everything it contains, and has to release
    /// it when it is done with it.
    /// </summary>
    Full,
}
