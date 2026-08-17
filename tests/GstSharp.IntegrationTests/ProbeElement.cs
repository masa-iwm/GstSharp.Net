using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstElement</c> subclass, registered from C#, that takes the
/// <c>change_state</c> slot over.
/// </summary>
/// <remarks>
/// <para>
/// This is the smallest thing the public surface of stage 1 of
/// <c>docs/subclassing.md</c> can build: a registration, a construction path
/// and one overridden vfunc. Everything it uses —
/// <see cref="Element.DefineSubclass"/>,
/// <see cref="Element.ChangeStateOverride"/>,
/// <see cref="SubclassType.NewInstance"/>, <see cref="Element.OnChangeState"/>
/// and <see cref="Element.ChainUpChangeState"/> — is public, so this file
/// compiles against nothing the binding keeps to itself.
/// </para>
/// <para>
/// It was the stage 0 proof, written against the internal runtime; rebasing it
/// onto the public surface is what shows that the surface says everything the
/// runtime could.
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
    /// The registration, made once when this field is first touched, which is
    /// after the fixture has loaded the native libraries.
    /// </summary>
    private static readonly SubclassType Definition = DefineSubclass(GTypeName, null, ChangeStateOverride);

    private readonly List<StateChange> _transitions = [];

    /// <summary>
    /// Creates an instance of the managed type.
    /// </summary>
    /// <remarks>
    /// The whole construction path of §5.2: the registration resolves the
    /// <c>GType</c>, <c>g_object_new</c> creates the instance, and the wrapper
    /// constructor sinks the floating reference and interns the wrapper. The
    /// handle is never spelled in this file.
    /// </remarks>
    internal ProbeElement()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the probe is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets the registration of the probe.</summary>
    internal static SubclassType Registration => Definition;

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

    /// <inheritdoc/>
    protected override StateChangeReturn OnChangeState(StateChange transition)
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
}
