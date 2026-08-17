using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstElement</c> subclass, registered from C#, that takes the
/// <c>change_state</c> slot over.
/// </summary>
/// <remarks>
/// <para>
/// This is the stage 0 proof of <c>docs/subclassing.md</c>: it exists to show
/// that the runtime primitives — <c>g_type_register_static</c> with a shared
/// <c>class_init</c>, the class struct mirrors, the interning lookup and the
/// captured parent class — add up to a native vtable that dispatches into
/// managed code and back out again. Stage 1 promotes the shape below into
/// <c>Gst.Element</c> itself, where <c>OnChangeState</c> and
/// <c>ChainUpChangeState</c> become part of the public surface.
/// </para>
/// <para>
/// Everything the type touches is internal to <c>GstSharp.Net</c> and reachable
/// here through the <c>InternalsVisibleTo</c> of the test assembly.
/// </para>
/// </remarks>
internal class ProbeElement : Element
{
    /// <summary>
    /// The <c>GType</c> name of the probe. It is unique in the process and it
    /// is spelled out rather than derived from the CLR name, per §3.5.
    /// </summary>
    internal const string GTypeName = "GstSharpTestProbeElement";

    /// <summary>
    /// The registration, run at most once and only when the type is first
    /// needed, which is after the fixture has loaded the native libraries.
    /// </summary>
    private static readonly Lazy<GType> Registration = new(Register, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly List<StateChange> _transitions = [];

    /// <summary>
    /// Creates an instance of the managed type.
    /// </summary>
    /// <remarks>
    /// The whole construction path of §5.2: the registration resolves the
    /// <c>GType</c>, <c>SubclassRegistry.NewInstance</c> calls
    /// <c>g_object_new</c>, and the handle it returns goes straight into the
    /// existing wrapper constructor, which sinks the floating reference and
    /// interns the wrapper. The handle is never spelled in this file: it
    /// travels inside a <see cref="SubclassCtorArgs"/> that only the registry
    /// hands out.
    /// </remarks>
    internal ProbeElement()
        : this(SubclassRegistry.NewInstance(RegisteredType))
    {
    }

    private ProbeElement(SubclassCtorArgs args)
        : base(args.Handle, args.Transfer)
    {
    }

    /// <summary>Gets the type the probe is registered as.</summary>
    internal static GType RegisteredType => Registration.Value;

    /// <summary>
    /// Gets or sets a value indicating whether the override throws instead of
    /// chaining up, which is how the exception policy of §4.1 is exercised.
    /// </summary>
    internal bool ThrowOnChangeState { get; set; }

    /// <summary>
    /// Gets or sets managed state that has nothing to do with the native
    /// object, so that a test can tell one wrapper from another after a
    /// collection.
    /// </summary>
    internal string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Gets the transitions the override has seen, oldest first.
    /// </summary>
    internal IReadOnlyList<StateChange> Transitions
    {
        get
        {
            lock (_transitions)
            {
                return _transitions.ToArray();
            }
        }
    }

    /// <summary>Forgets the transitions seen so far.</summary>
    internal void ClearTransitions()
    {
        lock (_transitions)
        {
            _transitions.Clear();
        }
    }

    /// <summary>
    /// The managed override of <c>GstElementClass.change_state</c>.
    /// </summary>
    /// <param name="transition">The transition GStreamer asks for.</param>
    /// <returns>The result of the transition.</returns>
    /// <remarks>
    /// Per-subclass behaviour is ordinary C# virtual dispatch on the interned
    /// wrapper; the native side only ever sees the single static trampoline.
    /// </remarks>
    protected virtual StateChangeReturn OnChangeState(StateChange transition)
    {
        lock (_transitions)
        {
            _transitions.Add(transition);
        }

        if (ThrowOnChangeState)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant($"The managed override refuses {transition}."));
        }

        return ChainUpChangeState(transition);
    }

    /// <summary>
    /// Runs the implementation of <c>change_state</c> below the managed
    /// override.
    /// </summary>
    /// <param name="transition">The transition to perform.</param>
    /// <returns>The result the parent class reports.</returns>
    protected StateChangeReturn ChainUpChangeState(StateChange transition)
    {
        StateChangeReturn result = ChainUpChangeState(Handle, transition);
        GC.KeepAlive(this);
        return result;
    }

    /// <summary>
    /// The static core of the chain-up, shared by the instance helper above and
    /// by the no-wrapper fallback of the trampoline.
    /// </summary>
    /// <param name="element">The native element.</param>
    /// <param name="transition">The transition to perform.</param>
    /// <returns>The result the parent class reports.</returns>
    /// <remarks>
    /// The slot is read out of the class the registration captured, not out of
    /// the element's own class, whose slot is the trampoline. Resolving the
    /// descriptor from the exact type of the instance is correct only because a
    /// managed subclass cannot be derived from, which
    /// <c>SubclassRegistry.Register</c> enforces (§4.4).
    /// <c>GstElement.change_state</c> is never null, so there is no documented
    /// default to fall back to here.
    /// </remarks>
    private static unsafe StateChangeReturn ChainUpChangeState(nint element, StateChange transition)
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
    /// The three rules of §4.1, in order: look the wrapper up and never
    /// fabricate one; chain up when there is none, which is what makes the
    /// construction window and the window after <c>Dispose</c> behave as if the
    /// slot had not been overridden; and turn an exception into the documented
    /// error value of this slot rather than letting it unwind into a native
    /// frame — without chaining up afterwards, because chaining up after the
    /// managed side effects already happened is less predictable than failing
    /// the operation.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ChangeStateTrampoline(nint element, int transition)
    {
        try
        {
            if (GObjectObject.TryGetInterned(element) is ProbeElement managed)
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

    /// <summary>
    /// Registers the managed type with GObject.
    /// </summary>
    /// <returns>The new type.</returns>
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
