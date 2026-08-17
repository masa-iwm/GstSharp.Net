// A GStreamer element whose change_state is implemented in C#. The smoke test
// registers it and drives it so that ILC has to compile the whole subclassing
// path: the UnmanagedCallersOnly trampoline, the shared class_init and
// instance_init of the registry, the class struct mirrors and the chain-up.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

/// <summary>
/// A managed <c>GstElement</c> subclass that counts the transitions it is
/// asked to perform and lets the parent class perform them.
/// </summary>
/// <remarks>
/// This mirrors the shape of <c>docs/subclassing.md</c> §4: one static
/// trampoline per slot, a lookup that never fabricates a wrapper, a chain-up
/// through the class the registration captured, and a per-slot error default
/// for exceptions. Stage 1 moves the shape into <c>Gst.Element</c> itself; the
/// sample reaches the stage 0 runtime through <c>InternalsVisibleTo</c>.
/// </remarks>
internal class ManagedElement : Element
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedElement";

    private static readonly Lazy<GType> Registration = new(Register, LazyThreadSafetyMode.ExecutionAndPublication);

    private int _transitions;

    /// <summary>Creates an instance of the managed type.</summary>
    internal ManagedElement()
        : this(SubclassRegistry.NewInstance(RegisteredType))
    {
    }

    private ManagedElement(SubclassCtorArgs args)
        : base(args.Handle, args.Transfer)
    {
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Registration.Value;

    /// <summary>Gets how often the managed override has run.</summary>
    internal int Transitions => Volatile.Read(ref _transitions);

    /// <summary>The managed override of <c>GstElementClass.change_state</c>.</summary>
    /// <param name="transition">The transition GStreamer asks for.</param>
    /// <returns>The result of the transition.</returns>
    protected virtual StateChangeReturn OnChangeState(StateChange transition)
    {
        Interlocked.Increment(ref _transitions);
        return ChainUpChangeState(transition);
    }

    /// <summary>Runs the implementation below the managed override.</summary>
    /// <param name="transition">The transition to perform.</param>
    /// <returns>The result the parent class reports.</returns>
    protected StateChangeReturn ChainUpChangeState(StateChange transition)
    {
        StateChangeReturn result = ChainUpChangeState(Handle, transition);
        GC.KeepAlive(this);
        return result;
    }

    private static unsafe StateChangeReturn ChainUpChangeState(nint element, StateChange transition)
    {
        ElementClassRaw* parent = (ElementClassRaw*)SubclassRegistry.DescriptorFor(element).ParentClass;
        delegate* unmanaged[Cdecl]<nint, int, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int, int>)parent->ChangeState;

        return (StateChangeReturn)slot(element, (int)transition);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ChangeStateTrampoline(nint element, int transition)
    {
        try
        {
            if (GObjectObject.TryGetInterned(element) is ManagedElement managed)
            {
                return (int)managed.OnChangeState((StateChange)transition);
            }

            // No live wrapper: behave as if the slot had not been overridden.
            return (int)ChainUpChangeState(element, (StateChange)transition);
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return (int)StateChangeReturn.Failure;
        }
    }

    private static unsafe GType Register()
    {
        SubclassDescriptor descriptor = new(
            new GType(GetGType()),
            GTypeName,
            [
                new VfuncSlot(
                    ElementClassRaw.ChangeStateOffset,
                    (nint)(delegate* unmanaged[Cdecl]<nint, int, int>)&ChangeStateTrampoline),
            ]);

        return SubclassRegistry.Register(descriptor);
    }
}
