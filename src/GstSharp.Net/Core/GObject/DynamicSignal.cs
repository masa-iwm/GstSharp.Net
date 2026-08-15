using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// A handler of a signal that is connected by name, without a generated
/// binding for it.
/// </summary>
/// <param name="sender">The object the signal was emitted on.</param>
/// <param name="args">
/// The arguments of the signal, in the order it declares them and without the
/// instance. Every argument is converted the way
/// <see cref="Value.GetContent"/> converts it: a primitive for the numeric
/// types, a <see cref="string"/>, an <see cref="Object"/> wrapper, a
/// <see cref="ParamSpec"/> for the parameter specification of a
/// <c>notify</c>, and the raw handle of anything boxed, which includes the
/// mini objects of GStreamer.
/// </param>
/// <returns>
/// The value the signal returns, or <see langword="null"/> to leave it at its
/// default. It is converted with <see cref="Value.CreateFor"/> against the
/// return type of the signal, so the same rules apply as for the arguments of
/// <see cref="Object.EmitSignal(string, object?[])"/>.
/// </returns>
/// <remarks>
/// <para>
/// The arguments are borrowed for the duration of the call: the wrappers that
/// this delegate receives belong to the emission, and a
/// <see cref="ParamSpec"/> among them is disposed as soon as the handler
/// returns. A handler that wants to keep an argument has to take a reference
/// of its own, for example by copying the value it needs out of it.
/// </para>
/// <para>
/// An <see cref="Object"/> argument is the shared wrapper of that object, so
/// it stays valid on its own terms; it must not be disposed by the handler.
/// </para>
/// </remarks>
public delegate object? DynamicSignalHandler(Object sender, object?[] args);

/// <summary>
/// The <c>GClosure</c> behind <see cref="DynamicSignalHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// A C callback cannot be generic over signatures, so a signal that no
/// generated trampoline covers is connected as a closure instead: GObject
/// collects the arguments into an array of <c>GValue</c> and hands them to one
/// marshaller, which is the same for every signature.
/// </para>
/// <para>
/// The marshaller is installed as the <em>meta</em> marshaller of the closure,
/// which is what makes it work without knowing the layout of a
/// <c>GClosure</c>: a meta marshaller receives its own <c>marshal_data</c>
/// pointer, so the state of the handler travels in the call rather than in a
/// bitfield packed structure that would have to be read field by field.
/// </para>
/// </remarks>
internal static unsafe class DynamicSignalClosure
{
    /// <summary>
    /// The layout of a <c>GClosure</c>, which is only needed for its size:
    /// <c>g_closure_new_simple</c> is told how much to allocate.
    /// </summary>
    /// <remarks>
    /// The ten bit fields of the C structure share the first <c>guint</c>,
    /// followed by the <c>marshal</c> function pointer, the <c>data</c> pointer
    /// and the <c>notifiers</c> pointer.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GClosureLayout
    {
        internal uint Bits;
        internal nint Marshal;
        internal nint Data;
        internal nint Notifiers;
    }

    /// <summary>
    /// Creates a floating closure that runs <paramref name="handler"/>.
    /// </summary>
    /// <param name="handler">The handler to run.</param>
    /// <returns>
    /// The new closure, which is floating: connecting it takes it over, and a
    /// closure that is never connected has to be sunk with
    /// <see cref="Sink"/>.
    /// </returns>
    internal static nint Create(DynamicSignalHandler handler)
    {
        CallbackHandle state = CallbackHandle.Alloc(handler);
        nint closure = GObjectNative.ClosureNewSimple((uint)Unsafe.SizeOf<GClosureLayout>(), state.UserData);

        if (closure == nint.Zero)
        {
            state.Free();
            throw new InvalidOperationException("GObject refused to allocate a closure.");
        }

        // The state is released when the closure dies, whether that is because
        // the handler was disconnected or because the object was finalised.
        GObjectNative.ClosureAddFinalizeNotifier(closure, state.UserData, CallbackHandle.ClosureNotify);
        GObjectNative.ClosureSetMetaMarshal(closure, state.UserData, &Invoke);
        return closure;
    }

    /// <summary>
    /// Destroys a closure that was never connected.
    /// </summary>
    /// <param name="closure">The floating closure to destroy.</param>
    /// <remarks>
    /// Sinking a closure that nothing else referenced releases the only
    /// reference it has, which finalises it and runs the notifier that frees
    /// the state of the handler.
    /// </remarks>
    internal static void Sink(nint closure)
    {
        if (closure != nint.Zero)
        {
            GObjectNative.ClosureSink(closure);
        }
    }

    /// <summary>
    /// The <c>GClosureMarshal</c> that runs a
    /// <see cref="DynamicSignalHandler"/>.
    /// </summary>
    /// <param name="closure">The closure that is being invoked.</param>
    /// <param name="returnValue">
    /// The value to write the result into, initialised to the return type of
    /// the signal, or <see langword="null"/> for a signal that returns nothing.
    /// </param>
    /// <param name="parameterCount">
    /// The number of values in <paramref name="parameterValues"/>, including
    /// the instance.
    /// </param>
    /// <param name="parameterValues">
    /// The instance, followed by the arguments of the signal.
    /// </param>
    /// <param name="invocationHint">The emission hint, which is not used.</param>
    /// <param name="marshalData">The state of the handler.</param>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Invoke(
        nint closure,
        GValueNative* returnValue,
        uint parameterCount,
        GValueNative* parameterValues,
        nint invocationHint,
        nint marshalData)
    {
        _ = closure;
        _ = invocationHint;

        List<ParamSpec>? borrowed = null;

        try
        {
            if (CallbackHandle.GetState<DynamicSignalHandler>(marshalData) is not DynamicSignalHandler handler)
            {
                return;
            }

            if (parameterValues is null || parameterCount == 0)
            {
                return;
            }

            // The first value is the instance the signal was emitted on.
            Object? sender = Object.FromNative(
                GObjectNative.ValueGetObject(ref parameterValues[0]),
                Transfer.None);

            if (sender is null)
            {
                return;
            }

            int count = (int)parameterCount - 1;
            object?[] args = count == 0 ? [] : new object?[count];

            for (int i = 0; i < count; i++)
            {
                args[i] = Read(ref parameterValues[i + 1], ref borrowed);
            }

            object? result = handler(sender, args);

            if (returnValue is not null && result is not null)
            {
                Value converted = Value.CreateFor(result, returnValue->Type);
                try
                {
                    GObjectNative.ValueCopy(ref converted.NativeValue, ref *returnValue);
                }
                finally
                {
                    converted.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
        finally
        {
            if (borrowed is not null)
            {
                foreach (ParamSpec specification in borrowed)
                {
                    specification.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Converts one argument of the emission into a managed object.
    /// </summary>
    /// <param name="native">The value to read, which stays owned by the emission.</param>
    /// <param name="borrowed">
    /// The wrappers that are only valid while the handler runs, and that are
    /// disposed once it returns.
    /// </param>
    /// <returns>The managed value of the argument.</returns>
    private static object? Read(ref GValueNative native, ref List<ParamSpec>? borrowed)
    {
        // A bitwise copy of the header: nothing is owned by it, and it is never
        // unset, so the emission keeps its arguments.
        Value value = default;
        value.NativeValue = native;

        if (GObjectNative.TypeFundamental(native.TypeValue) == GType.ParamValue)
        {
            nint specification = value.GetParam();
            if (specification == nint.Zero)
            {
                return null;
            }

            ParamSpec wrapper = new(specification, Transfer.None);
            (borrowed ??= []).Add(wrapper);
            return wrapper;
        }

        return value.GetDynamicContent();
    }
}
