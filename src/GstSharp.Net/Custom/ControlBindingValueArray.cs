using System.Runtime.InteropServices;
using Gst.GObject;

namespace Gst;

/// <summary>
/// What the two <c>get_g_value_array</c> members share: the checks the C
/// answers with an assertion failure on the console, and the reason a
/// <see cref="System.Span{T}"/> of <see cref="Value"/> is a <c>GValue</c>
/// array.
/// </summary>
internal static class ControlBindingValueArrays
{
    /// <summary>
    /// Throws unless the arguments are ones the call can be made with.
    /// </summary>
    /// <param name="timestamp">The time of the first value.</param>
    /// <param name="interval">The time between two values.</param>
    /// <param name="values">The slots the values are written into.</param>
    /// <exception cref="System.ArgumentException">
    /// One of the two times holds nothing, or one of the slots holds
    /// something.
    /// </exception>
    internal static void Validate(
        ClockTime timestamp,
        ClockTime interval,
        System.ReadOnlySpan<Value> values)
    {
        if (timestamp.IsNone)
        {
            throw new System.ArgumentException(
                "timestamp must be a valid clock time: the call refuses GST_CLOCK_TIME_NONE.",
                nameof(timestamp));
        }

        if (interval.IsNone)
        {
            throw new System.ArgumentException(
                "interval must be a valid clock time: the call refuses GST_CLOCK_TIME_NONE.",
                nameof(interval));
        }

        for (int index = 0; index < values.Length; index++)
        {
            if (!values[index].IsEmpty)
            {
                throw new System.ArgumentException(
                    "Every slot must hold nothing on entry: the call initialises each one itself, "
                    + "and initialising a value twice is an assertion failure in GObject.",
                    nameof(values));
            }
        }
    }
}

/// <content>
/// The sampling member whose array of <c>GValue</c> the caller allocates.
/// </content>
/// <remarks>
/// <c>gst_control_binding_get_g_value_array</c> is refused on the element type
/// of its array rather than on its length, which the gir spells: a
/// <c>GValue</c> is not a blittable element the planner marshals. It is
/// written here instead, where the generated
/// <see cref="ControlBinding.GetValue"/> is its neighbour.
/// </remarks>
public abstract unsafe partial class ControlBinding
{
    /// <summary>
    /// Samples the bound property at a sequence of evenly spaced times.
    /// </summary>
    /// <param name="timestamp">The time of the first value.</param>
    /// <param name="interval">The time between one value and the next.</param>
    /// <param name="values">
    /// The slots the values are written into, one per sample. Every slot must
    /// hold nothing when the call is made; the call gives each one the type of
    /// the bound property.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the control source produced a value for the
    /// run. What a source that has no value at any of the times answers, and
    /// what a run of no samples at all answers, is the control source's to
    /// decide: the interpolation and the trigger source answer
    /// <see langword="false"/> for both, the LFO source answers
    /// <see langword="true"/> because it has a value at every time.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_control_binding_get_g_value_array</c>, the sampling call
    /// an element makes once per buffer rather than
    /// <see cref="GetValue"/> once per value. The number of samples is the
    /// length of <paramref name="values"/>: the C takes it as a count beside
    /// the array, and the span carries both.
    /// </para>
    /// <para>
    /// <b>The slots must hold nothing on entry, and must be disposed either
    /// way.</b> The call initialises each slot itself, so a slot that already
    /// holds something is an assertion failure in GObject; that is what the
    /// <see cref="System.ArgumentException"/> below stands in for. After a
    /// <see langword="false"/> answer some slots may hold nothing, and a
    /// control source that has no value at a given time leaves that one slot
    /// empty even when the answer is <see langword="true"/>. Disposing an
    /// empty value is a no-op, so every slot can be disposed unconditionally.
    /// </para>
    /// </remarks>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="timestamp"/> or <paramref name="interval"/> holds
    /// nothing, or one of the slots of <paramref name="values"/> holds
    /// something.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">
    /// The binding was disposed.
    /// </exception>
    public bool GetGValueArray(ClockTime timestamp, ClockTime interval, System.Span<Value> values)
    {
        ControlBindingValueArrays.Validate(timestamp, interval, values);

        // A Value holds one GValueNative and nothing else, which is what makes
        // a span of them a GValue array. The cast is the pointer but not the
        // proof: MemoryMarshal.Cast rescales the length by the two sizes
        // rather than throwing when they differ, so the length it answers is
        // the proof and is checked.
        System.Span<GValueNative> natives = MemoryMarshal.Cast<Value, GValueNative>(values);

        if (natives.Length != values.Length)
        {
            throw new System.InvalidOperationException(
                "A Gst.GObject.Value no longer has the size of a GValue, so a span of them is not "
                + "the array the call writes into.");
        }

        // An empty span pins to a null pointer, which the C refuses with an
        // assertion failure even for a count of zero; a count of zero over a
        // real address is accepted.
        GValueNative unused = default;

        fixed (GValueNative* pinned = natives)
        {
            int nativeResult = GstControlBindingGetGValueArray(
                Handle,
                timestamp.Nanoseconds,
                interval.Nanoseconds,
                (uint)values.Length,
                natives.IsEmpty ? &unused : pinned);

            bool result = nativeResult != 0;
            System.GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>The <c>gst_control_binding_get_g_value_array</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_control_binding_get_g_value_array")]
    private static partial int GstControlBindingGetGValueArray(
        nint binding,
        ulong timestamp,
        ulong interval,
        uint nValues,
        GValueNative* values);
}

/// <content>
/// The sampling member of a property whose array of <c>GValue</c> the caller
/// allocates.
/// </content>
public abstract unsafe partial class Object
{
    /// <summary>
    /// Samples a controlled property at a sequence of evenly spaced times.
    /// </summary>
    /// <param name="propertyName">The name of the property to sample.</param>
    /// <param name="timestamp">The time of the first value.</param>
    /// <param name="interval">The time between one value and the next.</param>
    /// <param name="values">
    /// The slots the values are written into, one per sample. Every slot must
    /// hold nothing when the call is made; the call gives each one the type of
    /// the property.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the control source produced a value for the
    /// run. A property with no control binding attached answers
    /// <see langword="false"/> as well, and is not an error.
    /// </returns>
    /// <remarks>
    /// This is <c>gst_object_get_g_value_array</c>, which looks the control
    /// binding of <paramref name="propertyName"/> up under the object lock and
    /// forwards to
    /// <see cref="ControlBinding.GetGValueArray(ClockTime, ClockTime, System.Span{Value})"/>;
    /// everything that member documents about the slots holds here as well.
    /// </remarks>
    /// <exception cref="System.ArgumentNullException">
    /// <paramref name="propertyName"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="timestamp"/> or <paramref name="interval"/> holds
    /// nothing, or one of the slots of <paramref name="values"/> holds
    /// something.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">
    /// The object was disposed.
    /// </exception>
    public bool GetGValueArray(
        string propertyName,
        ClockTime timestamp,
        ClockTime interval,
        System.Span<Value> values)
    {
        System.ArgumentNullException.ThrowIfNull(propertyName);
        ControlBindingValueArrays.Validate(timestamp, interval, values);

        // The length is the layout check, as on the binding member above.
        System.Span<GValueNative> natives = MemoryMarshal.Cast<Value, GValueNative>(values);

        if (natives.Length != values.Length)
        {
            throw new System.InvalidOperationException(
                "A Gst.GObject.Value no longer has the size of a GValue, so a span of them is not "
                + "the array the call writes into.");
        }

        GValueNative unused = default;

        System.Span<byte> propertyNameBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope propertyNameScope =
            Gst.Interop.GMarshal.StackUtf8(propertyName, propertyNameBuffer);

        fixed (GValueNative* pinned = natives)
        {
            int nativeResult = GstObjectGetGValueArray(
                Handle,
                propertyNameScope.Pointer,
                timestamp.Nanoseconds,
                interval.Nanoseconds,
                (uint)values.Length,
                natives.IsEmpty ? &unused : pinned);

            bool result = nativeResult != 0;
            System.GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>The <c>gst_object_get_g_value_array</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_object_get_g_value_array")]
    private static partial int GstObjectGetGValueArray(
        nint @object,
        byte* propertyName,
        ulong timestamp,
        ulong interval,
        uint nValues,
        GValueNative* values);
}
