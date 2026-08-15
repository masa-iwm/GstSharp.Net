using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// A <c>GParamSpec</c>: the description of one property of a class.
/// </summary>
public sealed class ParamSpec : IDisposable
{
    private nint _handle;

    /// <summary>
    /// Wraps a native <c>GParamSpec</c>.
    /// </summary>
    /// <param name="handle">The parameter specification to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// <see cref="Transfer.None"/> when the wrapper has to take its own.
    /// </param>
    public ParamSpec(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "A parameter specification must not be null.");
        }

        _handle = transfer == Transfer.Full ? handle : GObjectNative.ParamSpecRefSink(handle);
    }

    /// <summary>
    /// Gets the native <c>GParamSpec</c>.
    /// </summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
            return _handle;
        }
    }

    /// <summary>
    /// Gets the name of the property, for example <c>uri</c>.
    /// </summary>
    public string Name => GMarshal.PtrToStringUtf8(GObjectNative.ParamSpecGetName(Handle)) ?? string.Empty;

    /// <summary>
    /// Gets the type of the values of the property.
    /// </summary>
    public GType ValueType => ValueTypeOf(Handle);

    /// <summary>
    /// Reads the value type out of a native <c>GParamSpec</c>.
    /// </summary>
    /// <param name="pspec">The parameter specification to read.</param>
    /// <returns>The type of the values of the property.</returns>
    /// <remarks>
    /// GObject exposes the field through the <c>G_PARAM_SPEC_VALUE_TYPE</c>
    /// macro only, so the offset of the public structure is used:
    /// <c>GTypeInstance</c>, <c>name</c> and the padded <c>flags</c> take three
    /// pointer sized slots.
    /// </remarks>
    internal static unsafe GType ValueTypeOf(nint pspec) =>
        pspec == nint.Zero ? GType.Invalid : new GType(*(nuint*)((byte*)pspec + (3 * sizeof(nint))));

    /// <summary>
    /// Releases the reference this wrapper holds.
    /// </summary>
    public void Dispose()
    {
        nint handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero)
        {
            GObjectNative.ParamSpecUnref(handle);
        }
    }
}
