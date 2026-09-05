using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// What every <c>ParamSpecX.New</c> does before and after the constructor of
/// GObject: encode the three strings, refuse what the C constructors only
/// assert, and turn a null result into an exception.
/// </summary>
/// <remarks>
/// <para>
/// The checks are not decoration. <c>g_param_spec_internal</c> answers the null
/// pointer for an invalid name, and every constructor dereferences that answer
/// without testing it, so a name a caller made up out of a string it was handed
/// would terminate the process rather than fail. The remaining assertions of
/// the C constructors — the type of an enumeration, the range of a default —
/// merely log and answer null, which is what the checks here turn into an
/// exception that names the argument at fault.
/// </para>
/// <para>
/// The three <c>G_PARAM_STATIC_*</c> flags are stripped rather than passed on:
/// they tell GObject to keep the caller's pointers, and the pointers here
/// belong to a buffer that is released as soon as the call has returned.
/// GObject copies all three strings without them, which is what the binding
/// needs and what its documentation promises.
/// </para>
/// </remarks>
internal static unsafe class ParamSpecFactory
{
    /// <summary>
    /// The name, the nickname and the description of a property, encoded as
    /// UTF-8 for the duration of one call.
    /// </summary>
    internal readonly struct Strings : IDisposable
    {
        private readonly nint _name;
        private readonly nint _nick;
        private readonly nint _blurb;

        internal Strings(nint name, nint nick, nint blurb)
        {
            _name = name;
            _nick = nick;
            _blurb = blurb;
        }

        /// <summary>Gets the encoded name, which is never null.</summary>
        internal byte* Name => (byte*)_name;

        /// <summary>Gets the encoded nickname, which may be null.</summary>
        internal byte* Nick => (byte*)_nick;

        /// <summary>Gets the encoded description, which may be null.</summary>
        internal byte* Blurb => (byte*)_blurb;

        /// <summary>Releases the three buffers.</summary>
        public void Dispose()
        {
            GMarshal.Free(_name);
            GMarshal.Free(_nick);
            GMarshal.Free(_blurb);
        }
    }

    /// <summary>
    /// Encodes the three strings of a property description and checks that the
    /// name is one GObject accepts.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="nick">The nickname of the property, may be null.</param>
    /// <param name="blurb">The description of the property, may be null.</param>
    /// <returns>The encoded strings, which have to be disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not begin with an ASCII letter or carries a
    /// character other than an ASCII letter, a digit, <c>-</c> or <c>_</c>, or
    /// one of the strings contains a null character.
    /// </exception>
    internal static Strings Prepare(string name, string? nick, string? blurb)
    {
        ArgumentNullException.ThrowIfNull(name);

        nint namePointer = GMarshal.StringToUtf8Ptr(name);
        nint nickPointer = nint.Zero;
        nint blurbPointer = nint.Zero;

        try
        {
            if (GObjectNative.ParamSpecIsValidName((byte*)namePointer) == 0)
            {
                throw new ArgumentException(
                    $"'{name}' does not name a property: a name begins with an ASCII letter and carries " +
                    "ASCII letters, digits, '-' and '_' only.",
                    nameof(name));
            }

            nickPointer = GMarshal.StringToUtf8Ptr(nick);
            blurbPointer = GMarshal.StringToUtf8Ptr(blurb);
        }
        catch
        {
            GMarshal.Free(namePointer);
            GMarshal.Free(nickPointer);
            GMarshal.Free(blurbPointer);
            throw;
        }

        return new Strings(namePointer, nickPointer, blurbPointer);
    }

    /// <summary>
    /// Drops the three flags that would make GObject keep the caller's strings.
    /// </summary>
    /// <param name="flags">What the caller asked for.</param>
    /// <returns>The same flags without <c>G_PARAM_STATIC_STRINGS</c>.</returns>
    internal static uint Sanitize(ParamFlags flags) => (uint)(flags & ~ParamFlags.StaticStrings);

    /// <summary>
    /// Refuses a range a default does not lie in, which the C constructors only
    /// assert.
    /// </summary>
    /// <typeparam name="T">The type of the three bounds.</typeparam>
    /// <param name="minimum">The smallest accepted value.</param>
    /// <param name="maximum">The largest accepted value.</param>
    /// <param name="defaultValue">The value of a property nothing wrote to.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    internal static void CheckRange<T>(T minimum, T maximum, T defaultValue)
        where T : IComparable<T>
    {
        if (minimum.CompareTo(maximum) > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                minimum,
                $"The smallest accepted value is larger than the largest one, which is {maximum}.");
        }

        if (defaultValue.CompareTo(minimum) < 0 || defaultValue.CompareTo(maximum) > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultValue),
                defaultValue,
                $"The default lies outside the range {minimum} to {maximum}.");
        }
    }

    /// <summary>
    /// Narrows a value onto the C <c>long</c> of the platform, which is 32 bits
    /// wide on Windows and 64 bits wide everywhere else.
    /// </summary>
    /// <param name="value">The value to narrow.</param>
    /// <param name="parameterName">The argument the value came from.</param>
    /// <returns>The value as a C <c>long</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value does not fit a C <c>long</c> of this platform.
    /// </exception>
    internal static CLong ToCLong(long value, string parameterName)
    {
        if (sizeof(CLong) == 4 && (value < int.MinValue || value > int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "A C long is 32 bits wide on this platform, and the value does not fit one.");
        }

        return new CLong((nint)value);
    }

    /// <summary>
    /// Narrows a value onto the unsigned C <c>long</c> of the platform.
    /// </summary>
    /// <param name="value">The value to narrow.</param>
    /// <param name="parameterName">The argument the value came from.</param>
    /// <returns>The value as an unsigned C <c>long</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value does not fit an unsigned C <c>long</c> of this platform.
    /// </exception>
    internal static CULong ToCULong(ulong value, string parameterName)
    {
        if (sizeof(CULong) == 4 && value > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "An unsigned C long is 32 bits wide on this platform, and the value does not fit one.");
        }

        return new CULong((nuint)value);
    }

    /// <summary>
    /// Refuses a type that is not what a kind of specification describes.
    /// </summary>
    /// <param name="type">The type the caller passed.</param>
    /// <param name="required">The type it has to derive from.</param>
    /// <param name="parameterName">The argument the type came from.</param>
    /// <exception cref="ArgumentException">The type does not derive from the required one.</exception>
    internal static void CheckIsA(GType type, GType required, string parameterName)
    {
        if (!type.IsA(required))
        {
            throw new ArgumentException(
                $"The type {Describe(type)} is not a {required.Name}.",
                parameterName);
        }
    }

    /// <summary>
    /// Refuses a boxed type a <c>GValue</c> cannot carry, which is the second
    /// assertion of <c>g_param_spec_boxed</c>.
    /// </summary>
    /// <param name="type">The type the caller passed.</param>
    /// <param name="parameterName">The argument the type came from.</param>
    /// <exception cref="ArgumentException">The type cannot be carried by a value.</exception>
    internal static void CheckIsValueType(GType type, string parameterName)
    {
        if (GObjectNative.TypeCheckIsValueType(type.Value) == 0)
        {
            throw new ArgumentException(
                $"The type {Describe(type)} cannot be carried by a GValue.",
                parameterName);
        }
    }

    /// <summary>
    /// Turns the null pointer a constructor of GObject or GStreamer answers on
    /// failure into an exception.
    /// </summary>
    /// <param name="handle">What the constructor answered.</param>
    /// <returns>The same handle, once it is known to be one.</returns>
    /// <exception cref="InvalidOperationException">The constructor answered null.</exception>
    internal static nint Require(nint handle) =>
        handle != nint.Zero
            ? handle
            : throw new InvalidOperationException(
                "The library refused to build the specification; it logs the reason as a critical warning.");

    private static string Describe(GType type) => type.IsValid ? type.Name : "<invalid>";
}
