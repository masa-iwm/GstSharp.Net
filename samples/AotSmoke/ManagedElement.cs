// A GStreamer element whose change_state is implemented in C#. The smoke test
// registers it and drives it so that ILC has to compile the plainest shape of
// the subclassing surface: one declared slot, one override and one chain-up.
using Gst;
using Gst.GObject;

/// <summary>
/// A managed <c>GstElement</c> subclass that counts the transitions it is
/// asked to perform and lets the parent class perform them.
/// </summary>
internal sealed class ManagedElement : Element
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedElement";

    private static readonly SubclassType Definition = DefineSubclass(GTypeName, null, ChangeStateOverride);

    private int _transitions;

    /// <summary>Creates an instance of the managed type.</summary>
    internal ManagedElement()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how often the managed override has run.</summary>
    internal int Transitions => Volatile.Read(ref _transitions);

    /// <inheritdoc/>
    protected override StateChangeReturn OnChangeState(StateChange transition)
    {
        Interlocked.Increment(ref _transitions);
        return ChainUpChangeState(transition);
    }
}
