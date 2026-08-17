using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The managed wrapper of a <c>GInitiallyUnowned</c>, that is of an object
/// whose first reference is floating.
/// </summary>
/// <remarks>
/// The introspection data carries no annotation for floating references, so the
/// runtime decides at run time: for an instance of this type hierarchy it asks
/// <c>g_object_is_floating</c> and sinks the reference when it is floating.
/// </remarks>
public class InitiallyUnowned : Object
{
    private static nuint _type;

    /// <summary>
    /// Wraps a native <c>GInitiallyUnowned</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <remarks>
    /// This is the constructor a wrapper of a <c>GstObject</c> derived type
    /// chains to — every <c>GstObject</c> has floating references — and the
    /// obligations of <see cref="Object(nint, Transfer)"/> apply unchanged. The
    /// floating reference itself needs nothing from the caller: it is sunk when
    /// there is one, whichever <see cref="Transfer"/> the call documented.
    /// </remarks>
    protected InitiallyUnowned(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the type of <c>GInitiallyUnowned</c>.
    /// </summary>
    public static GType Type
    {
        get
        {
            nuint cached = Volatile.Read(ref _type);
            if (cached == GType.InvalidValue)
            {
                cached = GObjectNative.InitiallyUnownedGetType();
                Volatile.Write(ref _type, cached);
            }

            return new GType(cached);
        }
    }

    /// <summary>
    /// Tests whether a type has floating references.
    /// </summary>
    /// <param name="type">The type to test.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="type"/> derives from
    /// <c>GInitiallyUnowned</c>.
    /// </returns>
    public static bool IsInitiallyUnownedType(GType type) =>
        type.IsValid && GObjectNative.TypeIsA(type.Value, Type.Value) != 0;
}
