using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Planning;

/// <summary>
/// One argument of a signal: how it is marshalled, and the name it carries on
/// the arguments class the handler receives.
/// </summary>
/// <param name="Argument">The marshalling of the argument.</param>
/// <param name="PropertyName">The C# name of the property that exposes it.</param>
internal sealed record SignalArgument(ArgumentPlan Argument, string PropertyName);

/// <summary>
/// Everything the emitters need to write one <c>&lt;glib:signal&gt;</c>: the
/// arguments carrier, the event accessors and the trampoline that GObject
/// invokes.
/// </summary>
/// <remarks>
/// A signal is not a callable that generated code invokes but one that native
/// code invokes, so the plan describes the shape of the native handler:
/// <c>instance</c> first, then the gir parameters, then the <c>user_data</c> of
/// the closure. Everything the handler receives is borrowed, exactly like the
/// arguments of a callback.
/// </remarks>
internal sealed class SignalPlan
{
    /// <summary>Gets the gir declaration.</summary>
    internal required GirSignal Signal { get; init; }

    /// <summary>Gets the name the handler is connected under, for example <c>pad-added</c>.</summary>
    internal required string SignalName { get; init; }

    /// <summary>Gets the C# name of the event, for example <c>PadAdded</c>.</summary>
    internal required string Name { get; init; }

    /// <summary>
    /// Gets the C# name of the arguments class, or <see langword="null"/> when
    /// the signal carries no argument and the handler is given
    /// <c>System.EventArgs.Empty</c>.
    /// </summary>
    internal string? ArgsName { get; init; }

    /// <summary>
    /// Gets the C# type of the arguments the handler receives, which is
    /// <c>System.EventArgs</c> for a signal without arguments.
    /// </summary>
    internal required string ArgsType { get; init; }

    /// <summary>Gets the C# name of the trampoline method.</summary>
    internal required string TrampolineName { get; init; }

    /// <summary>
    /// Gets the C# name of the delegate type of a signal that returns a value,
    /// or <see langword="null"/> when the event uses <c>System.EventHandler</c>.
    /// </summary>
    internal string? HandlerName { get; init; }

    /// <summary>Gets the C# type of the event.</summary>
    internal required string EventType { get; init; }

    /// <summary>Gets the arguments, without the instance and the user data.</summary>
    internal required IReadOnlyList<SignalArgument> Arguments { get; init; }

    /// <summary>Gets the value the handler returns.</summary>
    internal required ReturnPlan Return { get; init; }

    /// <summary>
    /// Gets a value indicating whether the signal carries a detail, as
    /// <c>message::eos</c> does. This milestone connects to the plain name, so
    /// the handler of a detailed signal sees every detail.
    /// </summary>
    internal bool IsDetailed { get; init; }
}
