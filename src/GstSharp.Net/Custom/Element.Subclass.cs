using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

namespace Gst;

/// <content>
/// The subclassing surface of <c>GstElement</c>: how a managed element is
/// registered and constructed, and the one vfunc every element has.
/// </content>
public abstract unsafe partial class Element
{
    /// <summary>
    /// Constructs the managed subclass that
    /// <see cref="SubclassType.NewInstance"/> created an instance of.
    /// </summary>
    /// <param name="args">
    /// The instance, as the registration handed it out. The handle never
    /// appears in the subclass's own code.
    /// </param>
    /// <remarks>
    /// <para>
    /// <c>g_object_new</c> has already run by the time this is reached, so a
    /// vfunc that fired during construction found no interned wrapper and
    /// chained up; from here on every declared slot dispatches to this
    /// instance. See <c>docs/subclassing.md</c> §5.2.
    /// </para>
    /// <para>
    /// The ordinary C# caveat about virtual calls during construction applies
    /// and is not a defect of the binding: fields of a derived class are
    /// assigned before its constructor body runs but after this one, and an
    /// element that is handed to native code from inside a constructor can see
    /// its <c>OnX</c> methods called before that body finishes.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The instance is not a <c>GstElement</c>, which means the registration
    /// these arguments came from derives from an unrelated class.
    /// </exception>
    protected Element(SubclassCtorArgs args)
        : this(args.HandleFor(GetGType()), args.Transfer)
    {
    }

    /// <summary>
    /// Gets the declaration of <c>GstElementClass.change_state</c>, for a
    /// subclass that overrides <see cref="OnChangeState"/>.
    /// </summary>
    /// <remarks>
    /// <c>change_state</c> is the hook every element has, and the one place a
    /// managed element can allocate on the way up and release on the way down:
    /// overriding <c>GObjectClass.dispose</c> and <c>finalize</c> is a non-goal
    /// of the design, so the <c>READY</c> to <c>NULL</c> transition is where
    /// teardown belongs. See <c>docs/subclassing.md</c> §1.
    /// </remarks>
    public static VfuncOverride ChangeStateOverride { get; } = new(
        &GetGType,
        ElementClassRaw.ChangeStateOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, int, int>)&ChangeStateTrampoline);

    /// <summary>
    /// Registers a managed subclass of <c>GstElement</c> with GObject.
    /// </summary>
    /// <param name="typeName">
    /// The <c>GType</c> name, which has to be unique in the process, at least
    /// three characters long, start with a letter and be made of letters,
    /// digits, <c>_</c>, <c>-</c> and <c>+</c>. It is spelled out rather than
    /// derived from the CLR name, so that two assemblies cannot collide by
    /// accident; the convention is to prefix it with the application, as in
    /// <c>MyAppCounterSrc</c>.
    /// </param>
    /// <param name="configureClass">
    /// Describes the class while it is being initialised — metadata, pad
    /// templates — or <see langword="null"/> for a class that needs neither.
    /// </param>
    /// <param name="overrides">
    /// The slots the subclass takes over, as the base classes hand them out.
    /// Only these are patched, and each one has to have a matching
    /// <c>OnX</c> override.
    /// </param>
    /// <returns>
    /// The registration, to be kept in a <see langword="static"/>
    /// <see langword="readonly"/> field of the subclass and used to construct
    /// its instances.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The registration is made once, when the field that holds it is first
    /// touched, which is normally the first construction of the subclass. It
    /// needs the native libraries to be loaded, so <c>GstSharp.Initialize</c>
    /// has to have run.
    /// </para>
    /// <para>
    /// The parent is always the wrapped native class, so deriving a managed
    /// subclass from another managed subclass — which the single level chain-up
    /// cannot serve, see <c>docs/subclassing.md</c> §4.4 — cannot be expressed
    /// through this surface at all.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="typeName"/> or <paramref name="overrides"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The type name is not a legal <c>GType</c> name, or a declared slot
    /// belongs to a class that <c>GstElement</c> does not derive from.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is taken, or the class initialiser failed.
    /// </exception>
    public static SubclassType DefineSubclass(
        string typeName,
        Action<ClassConfig>? configureClass,
        params VfuncOverride[] overrides) =>
        SubclassType.Define(new GType(GetGType()), typeName, configureClass, overrides);

    /// <summary>
    /// Performs a state transition, and is called for every single step of one:
    /// a jump from <see cref="State.Null"/> to <see cref="State.Playing"/>
    /// arrives as three calls.
    /// </summary>
    /// <param name="transition">The transition GStreamer asks for.</param>
    /// <returns>
    /// The result of the transition.
    /// <see cref="StateChangeReturn.Success"/> when it is done,
    /// <see cref="StateChangeReturn.Async"/> when it will finish later,
    /// <see cref="StateChangeReturn.NoPreroll"/> for a live source that reached
    /// <see cref="State.Paused"/>, and
    /// <see cref="StateChangeReturn.Failure"/> when the element cannot go
    /// there.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The base implementation chains up, and an override normally does too:
    /// upward transitions are usually handled before the chain-up and downward
    /// ones after it, because the parent class starts and stops the machinery
    /// the subclass builds on.
    /// </para>
    /// <para>
    /// An exception is reported through <c>GstSharp.UnhandledCallbackException</c>
    /// and answered with <see cref="StateChangeReturn.Failure"/>; the chain-up
    /// does not run afterwards, so the transition does not half happen.
    /// </para>
    /// </remarks>
    protected virtual StateChangeReturn OnChangeState(StateChange transition) => ChainUpChangeState(transition);

    /// <summary>
    /// Runs the implementation of <c>change_state</c> below the managed
    /// override, which is the one the wrapped native class provides.
    /// </summary>
    /// <param name="transition">The transition to perform.</param>
    /// <returns>The result the parent class reports.</returns>
    /// <remarks>
    /// <c>GstElement.change_state</c> is what actually moves the pads and the
    /// children of the element, and it is never null in any class, so this
    /// always has something to call.
    /// </remarks>
    protected StateChangeReturn ChainUpChangeState(StateChange transition)
    {
        StateChangeReturn result = ChainUpChangeState(Handle, transition);
        GC.KeepAlive(this);
        return result;
    }

    /// <summary>
    /// The static core of the chain-up, shared by the instance helper and by
    /// the no-wrapper fallback of the trampoline.
    /// </summary>
    /// <param name="element">The native element.</param>
    /// <param name="transition">The transition to perform.</param>
    /// <returns>The result the parent class reports.</returns>
    /// <remarks>
    /// The slot is read out of the class the registration captured, never out
    /// of the element's own class, whose slot holds the trampoline. Resolving
    /// the descriptor from the exact type of the instance is correct only
    /// because a managed subclass cannot be derived from, which the
    /// registration enforces. See <c>docs/subclassing.md</c> §4.4.
    /// </remarks>
    private static StateChangeReturn ChainUpChangeState(nint element, StateChange transition)
    {
        ElementClassRaw* parent = (ElementClassRaw*)SubclassRegistry.DescriptorFor(element).ParentClass;
        delegate* unmanaged[Cdecl]<nint, int, int> slot =
            (delegate* unmanaged[Cdecl]<nint, int, int>)parent->ChangeState;

        return (StateChangeReturn)slot(element, (int)transition);
    }

    /// <summary>
    /// The one unmanaged entry point that every managed element subclass shares
    /// for this slot.
    /// </summary>
    /// <param name="element">The element the vtable was reached through.</param>
    /// <param name="transition">The transition, as a <c>GstStateChange</c>.</param>
    /// <returns>The transition result, as a <c>GstStateChangeReturn</c>.</returns>
    /// <remarks>
    /// The three rules of <c>docs/subclassing.md</c> §4.1, in order: look the
    /// wrapper up and never fabricate one; chain up when there is none, which
    /// is what makes the construction window and the window after
    /// <c>Dispose</c> behave as if the slot had not been overridden; and turn
    /// an exception into the documented error value of this slot rather than
    /// letting it unwind into a native frame — without chaining up afterwards,
    /// because chaining up once the managed side effects have happened is less
    /// predictable than failing the operation.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ChangeStateTrampoline(nint element, int transition)
    {
        try
        {
            if (GObjectObject.TryGetInterned(element) is Element managed)
            {
                return (int)managed.OnChangeState((StateChange)transition);
            }

            return (int)ChainUpChangeState(element, (StateChange)transition);
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
            return (int)StateChangeReturn.Failure;
        }
    }
}
