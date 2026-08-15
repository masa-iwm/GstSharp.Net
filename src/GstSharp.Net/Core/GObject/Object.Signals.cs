using System.Globalization;
using Gst.GLib;
using Gst.Interop;

namespace Gst.GObject;

/// <content>
/// The signals that no generated binding covers: emitting one by name, and
/// connecting to one by name.
/// </content>
/// <remarks>
/// <para>
/// A plugin may add signals that its <c>.gir</c> file never mentions, and
/// GStreamer offers a good deal of its functionality as action signals, which
/// are calls dressed up as signals. Both are reached here, through the
/// introspection GObject itself keeps: the signal is looked up by name, its
/// signature is queried, and the arguments are converted against it.
/// </para>
/// <para>
/// Nothing on this path uses reflection. The arguments travel as
/// <see cref="object"/> and are converted by a switch over the fundamental
/// types of GObject, and the handler of a dynamic connection runs through one
/// closure marshaller that works for every signature.
/// </para>
/// </remarks>
public partial class Object
{
    /// <summary>
    /// The number of values an emission builds on the stack before it falls
    /// back to the heap. No signal in GStreamer comes close.
    /// </summary>
    private const int StackValues = 16;

    /// <summary>
    /// Emits a signal by name and returns what it returned.
    /// </summary>
    /// <param name="detailedSignal">
    /// The name of the signal, optionally with a detail, for example
    /// <c>notify::name</c>.
    /// </param>
    /// <param name="args">
    /// The arguments of the signal, in the order it declares them and without
    /// the instance.
    /// </param>
    /// <returns>
    /// The value the signal returned, converted the way
    /// <see cref="Value.GetContent"/> converts it, or <see langword="null"/>
    /// for a signal that returns nothing, and for a null pointer of any kind.
    /// A boxed return value, which is what a mini object such as a
    /// <c>GstSample</c> is, comes back as a raw handle that the caller owns: it
    /// carries a reference of its own, so it has to be adopted by a wrapper
    /// with <c>Transfer.Full</c> or released by hand.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the way to call an action signal, for example
    /// <c>element.EmitSignal("resize", 1920, 1080)</c> on a sink that offers
    /// one, or <c>element.EmitSignal("try-pull-sample", 0UL)</c> on an
    /// <c>appsink</c>.
    /// </para>
    /// <para>
    /// Every argument is checked against the type the signal declares before
    /// anything is emitted, and a mismatch is an
    /// <see cref="ArgumentException"/> that names the expectation. An
    /// enumeration is passed as the generated enumeration member, whose numeric
    /// value is what reaches the signal; see <see cref="Value.CreateFor"/> for
    /// the conversions in full.
    /// </para>
    /// <para>
    /// A single <see langword="null"/> argument binds to the whole array, as
    /// C# does for every <c>params</c> parameter, so it has to be written as
    /// <c>(object?)null</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The object has no such signal, or the arguments do not match the
    /// signature of the signal.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public unsafe object? EmitSignal(string detailedSignal, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrEmpty(detailedSignal);

        // "EmitSignal(name, null)" binds null to the array itself.
        args ??= [];

        nint handle = Handle;
        GType instanceType = NativeType;

        if (!SignalQuery.TryParse(instanceType, detailedSignal, out SignalQuery query, out Quark detail))
        {
            throw new ArgumentException(
                $"\"{detailedSignal}\" is not a signal of {instanceType.Name}. " +
                SignalQuery.DescribeAvailable(instanceType),
                nameof(detailedSignal));
        }

        if (args.Length != query.ParameterCount)
        {
            throw new ArgumentException(
                $"The \"{query.Name}\" signal of {instanceType.Name} takes " +
                $"{query.ParameterCount.ToString(CultureInfo.InvariantCulture)} argument(s), and " +
                $"{args.Length.ToString(CultureInfo.InvariantCulture)} were given: {query}.",
                nameof(args));
        }

        int count = args.Length + 1;
        Span<GValueNative> values = count <= StackValues
            ? stackalloc GValueNative[StackValues]
            : new GValueNative[count];

        values = values[..count];

        // stackalloc is not guaranteed to be zeroed, and an uninitialised type
        // field would be unset as if it held something.
        values.Clear();

        GType returnType = query.ReturnType;
        Value returnValue = returnType.IsValid && returnType != GType.None ? Value.New(returnType) : default;

        try
        {
            // g_signal_emitv takes the instance as the first value, typed as the
            // instance itself rather than as G_TYPE_OBJECT.
            GObjectNative.ValueInit(ref values[0], instanceType.Value);
            GObjectNative.ValueSetInstance(ref values[0], handle);

            for (int i = 0; i < args.Length; i++)
            {
                GType expected = query.GetParameterType(i);

                try
                {
                    // The converted value moves into the array, which is what
                    // unsets it again.
                    Value converted = Value.CreateFor(args[i], expected);
                    values[i + 1] = converted.NativeValue;
                }
                catch (ArgumentException exception)
                {
                    throw new ArgumentException(
                        $"Argument {i.ToString(CultureInfo.InvariantCulture)} of the \"{query.Name}\" signal of " +
                        $"{instanceType.Name} is a {expected.Name}: {exception.Message} The signature is {query}.",
                        nameof(args),
                        exception);
                }
            }

            fixed (GValueNative* first = values)
            {
                GValueNative* result = returnValue.IsEmpty ? null : &returnValue.NativeValue;
                GObjectNative.SignalEmitV(first, query.Id, detail.Value, result);
            }

            // The value is unset in the finally, so anything it owns is handed
            // over rather than borrowed.
            return returnValue.IsEmpty ? null : returnValue.TakeDynamicContent();
        }
        finally
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].TypeValue != GType.InvalidValue)
                {
                    GObjectNative.ValueUnset(ref values[i]);
                }
            }

            returnValue.Dispose();
        }
    }

    /// <summary>
    /// Emits a signal by name and returns what it returned, typed.
    /// </summary>
    /// <typeparam name="T">
    /// The managed type of the return value:
    /// <see cref="bool"/>, <see cref="int"/>, <see cref="uint"/>,
    /// <see cref="long"/>, <see cref="ulong"/>, <see cref="float"/>,
    /// <see cref="double"/>, <see cref="string"/>, an <see cref="Object"/>, or
    /// <see cref="nint"/> for anything boxed. An enumeration comes back as its
    /// numeric value, so a signal that returns one is read as an
    /// <see cref="int"/> and converted by the caller, who knows the generated
    /// enumeration.
    /// </typeparam>
    /// <param name="detailedSignal">The name of the signal, optionally with a detail.</param>
    /// <param name="args">The arguments of the signal, without the instance.</param>
    /// <returns>
    /// The value the signal returned, or the default of <typeparamref name="T"/>
    /// when it returned nothing at all.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The object has no such signal, or the arguments do not match the
    /// signature of the signal.
    /// </exception>
    /// <exception cref="InvalidCastException">
    /// The signal returned something that is not a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public T? EmitSignal<T>(string detailedSignal, params object?[] args)
    {
        object? result = EmitSignal(detailedSignal, args);

        return result switch
        {
            null => default,
            T typed => typed,
            _ => throw new InvalidCastException(
                $"The \"{detailedSignal}\" signal of {NativeType.Name} did not return a {typeof(T)}."),
        };
    }

    /// <summary>
    /// Connects a handler to a signal by name, whatever its signature is.
    /// </summary>
    /// <param name="detailedSignal">
    /// The name of the signal, optionally with a detail, for example
    /// <c>notify::name</c> or the <c>about-to-finish</c> of <c>playbin</c>.
    /// </param>
    /// <param name="handler">The handler to run on every emission.</param>
    /// <param name="after">
    /// <see langword="true"/> to run the handler after the default one.
    /// </param>
    /// <returns>The identifier of the handler, which is never zero.</returns>
    /// <remarks>
    /// <para>
    /// The handler is disconnected by <see cref="RemoveHandler"/>, and
    /// disposing the object disconnects whatever is left, exactly as for a
    /// generated event. Disconnecting destroys the closure, which releases the
    /// state of the handler.
    /// </para>
    /// <para>
    /// The signal has to exist: this fails immediately rather than connecting
    /// to nothing, and the message lists what the object does have.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The object has no such signal.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public ulong ConnectSignal(string detailedSignal, DynamicSignalHandler handler, bool after = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(detailedSignal);
        ArgumentNullException.ThrowIfNull(handler);

        nint handle = Handle;
        GType instanceType = NativeType;

        if (!SignalQuery.TryParse(instanceType, detailedSignal, out SignalQuery query, out Quark detail))
        {
            throw new ArgumentException(
                $"\"{detailedSignal}\" is not a signal of {instanceType.Name}. " +
                SignalQuery.DescribeAvailable(instanceType),
                nameof(detailedSignal));
        }

        nint closure = DynamicSignalClosure.Create(handler);
        ulong id = GObjectNative
            .SignalConnectClosureById(handle, query.Id, detail.Value, closure, after ? 1 : 0)
            .Value;

        if (id == 0)
        {
            // Nothing took the closure over, so sinking it destroys it and
            // releases the state of the handler.
            DynamicSignalClosure.Sink(closure);
            throw new ArgumentException(
                $"Connecting a handler to \"{detailedSignal}\" of {instanceType.Name} failed.",
                nameof(detailedSignal));
        }

        Track(id);
        return id;
    }
}
