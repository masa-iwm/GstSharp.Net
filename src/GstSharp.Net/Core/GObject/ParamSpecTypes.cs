using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// A <c>GParamSpecBoolean</c>: a property that carries a boolean.
/// </summary>
/// <remarks>
/// The field is read at the offset of the public structure, because GObject
/// exposes it through a macro rather than through an accessor. The base class
/// already reads the description in general — the name, the flags and the type
/// of the values — and this adds what only a boolean property has.
/// </remarks>
public sealed unsafe class ParamSpecBoolean : ParamSpec
{
    internal ParamSpecBoolean(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a boolean property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecBoolean New(
        string name,
        string? nick,
        string? blurb,
        bool defaultValue,
        ParamFlags flags)
    {
        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecBoolean(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            defaultValue ? 1 : 0,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecBoolean(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public bool Default
    {
        get
        {
            bool value = *(int*)((byte*)Handle + FieldsOffset) != 0;
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecChar</c>: a property that carries a signed byte out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecChar : ParamSpec
{
    internal ParamSpecChar(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a signed byte property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecChar New(
        string name,
        string? nick,
        string? blurb,
        sbyte minimum,
        sbyte maximum,
        sbyte defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecChar(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimum,
            maximum,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecChar(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public sbyte Minimum
    {
        get
        {
            sbyte value = *(sbyte*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public sbyte Maximum
    {
        get
        {
            sbyte value = *(sbyte*)((byte*)Handle + FieldsOffset + 1);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public sbyte Default
    {
        get
        {
            sbyte value = *(sbyte*)((byte*)Handle + FieldsOffset + 2);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecUChar</c>: a property that carries an unsigned byte out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecUChar : ParamSpec
{
    internal ParamSpecUChar(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of an unsigned byte property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecUChar New(
        string name,
        string? nick,
        string? blurb,
        byte minimum,
        byte maximum,
        byte defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecUChar(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimum,
            maximum,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecUChar(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public byte Minimum
    {
        get
        {
            byte value = *((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public byte Maximum
    {
        get
        {
            byte value = *((byte*)Handle + FieldsOffset + 1);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public byte Default
    {
        get
        {
            byte value = *((byte*)Handle + FieldsOffset + 2);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecInt</c>: a property that carries a 32 bit integer out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecInt : ParamSpec
{
    internal ParamSpecInt(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a 32 bit integer property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecInt New(
        string name,
        string? nick,
        string? blurb,
        int minimum,
        int maximum,
        int defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecInt(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimum,
            maximum,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecInt(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public int Minimum
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public int Maximum
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 4);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public int Default
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecUInt</c>: a property that carries an unsigned 32 bit integer
/// out of a range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecUInt : ParamSpec
{
    internal ParamSpecUInt(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of an unsigned 32 bit integer property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecUInt New(
        string name,
        string? nick,
        string? blurb,
        uint minimum,
        uint maximum,
        uint defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecUInt(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimum,
            maximum,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecUInt(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public uint Minimum
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public uint Maximum
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset + 4);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public uint Default
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecLong</c>: a property that carries a C <c>long</c> out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only. A C <c>long</c> is 32 bits wide on
/// Windows and 64 bits wide everywhere else, so the offsets are computed from
/// <see cref="CLong"/> rather than written out, and the value is widened to
/// <see cref="long"/> on both.
/// </remarks>
public sealed unsafe class ParamSpecLong : ParamSpec
{
    internal ParamSpecLong(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a C <c>long</c> property. A C <c>long</c> is
    /// 32 bits wide on Windows and 64 bits wide everywhere else, so a bound
    /// that does not fit the platform is refused rather than truncated.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>,
    /// <paramref name="defaultValue"/> lies outside the range, or one of the
    /// three does not fit a C <c>long</c> of this platform.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecLong New(
        string name,
        string? nick,
        string? blurb,
        long minimum,
        long maximum,
        long defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        CLong nativeMinimum = ParamSpecFactory.ToCLong(minimum, nameof(minimum));
        CLong nativeMaximum = ParamSpecFactory.ToCLong(maximum, nameof(maximum));
        CLong nativeDefault = ParamSpecFactory.ToCLong(defaultValue, nameof(defaultValue));

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecLong(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            nativeMinimum,
            nativeMaximum,
            nativeDefault,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecLong(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public long Minimum
    {
        get
        {
            long value = Read(Handle, FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public long Maximum
    {
        get
        {
            long value = Read(Handle, FieldsOffset + sizeof(CLong));
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public long Default
    {
        get
        {
            long value = Read(Handle, FieldsOffset + (2 * sizeof(CLong)));
            GC.KeepAlive(this);
            return value;
        }
    }

    private static long Read(nint pspec, int offset) =>
        sizeof(CLong) == 4 ? *(int*)((byte*)pspec + offset) : *(long*)((byte*)pspec + offset);
}

/// <summary>
/// A <c>GParamSpecULong</c>: a property that carries an unsigned C <c>long</c>
/// out of a range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only. An unsigned C <c>long</c> is 32
/// bits wide on Windows and 64 bits wide everywhere else, so the offsets are
/// computed from <see cref="CULong"/> rather than written out, and the value is
/// widened to <see cref="ulong"/> on both.
/// </remarks>
public sealed unsafe class ParamSpecULong : ParamSpec
{
    internal ParamSpecULong(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of an unsigned C <c>long</c> property. A C <c>long</c> is
    /// 32 bits wide on Windows and 64 bits wide everywhere else, so a bound
    /// that does not fit the platform is refused rather than truncated.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>,
    /// <paramref name="defaultValue"/> lies outside the range, or one of the
    /// three does not fit a C <c>long</c> of this platform.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecULong New(
        string name,
        string? nick,
        string? blurb,
        ulong minimum,
        ulong maximum,
        ulong defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        CULong nativeMinimum = ParamSpecFactory.ToCULong(minimum, nameof(minimum));
        CULong nativeMaximum = ParamSpecFactory.ToCULong(maximum, nameof(maximum));
        CULong nativeDefault = ParamSpecFactory.ToCULong(defaultValue, nameof(defaultValue));

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecULong(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            nativeMinimum,
            nativeMaximum,
            nativeDefault,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecULong(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public ulong Minimum
    {
        get
        {
            ulong value = Read(Handle, FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public ulong Maximum
    {
        get
        {
            ulong value = Read(Handle, FieldsOffset + sizeof(CULong));
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public ulong Default
    {
        get
        {
            ulong value = Read(Handle, FieldsOffset + (2 * sizeof(CULong)));
            GC.KeepAlive(this);
            return value;
        }
    }

    private static ulong Read(nint pspec, int offset) =>
        sizeof(CULong) == 4 ? *(uint*)((byte*)pspec + offset) : *(ulong*)((byte*)pspec + offset);
}

/// <summary>
/// A <c>GParamSpecInt64</c>: a property that carries a 64 bit integer out of a
/// range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecInt64 : ParamSpec
{
    internal ParamSpecInt64(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a 64 bit integer property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecInt64 New(
        string name,
        string? nick,
        string? blurb,
        long minimum,
        long maximum,
        long defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecInt64(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimum,
            maximum,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecInt64(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public long Minimum
    {
        get
        {
            long value = *(long*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public long Maximum
    {
        get
        {
            long value = *(long*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public long Default
    {
        get
        {
            long value = *(long*)((byte*)Handle + FieldsOffset + 16);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecUInt64</c>: a property that carries an unsigned 64 bit
/// integer out of a range.
/// </summary>
/// <remarks>
/// The three fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecUInt64 : ParamSpec
{
    internal ParamSpecUInt64(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of an unsigned 64 bit integer property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecUInt64 New(
        string name,
        string? nick,
        string? blurb,
        ulong minimum,
        ulong maximum,
        ulong defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecUInt64(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimum,
            maximum,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecUInt64(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public ulong Minimum
    {
        get
        {
            ulong value = *(ulong*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public ulong Maximum
    {
        get
        {
            ulong value = *(ulong*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public ulong Default
    {
        get
        {
            ulong value = *(ulong*)((byte*)Handle + FieldsOffset + 16);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecFloat</c>: a property that carries a single precision number
/// out of a range.
/// </summary>
/// <remarks>
/// The four fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecFloat : ParamSpec
{
    internal ParamSpecFloat(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a single precision property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecFloat New(
        string name,
        string? nick,
        string? blurb,
        float minimum,
        float maximum,
        float defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecFloat(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimum,
            maximum,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecFloat(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public float Minimum
    {
        get
        {
            float value = *(float*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public float Maximum
    {
        get
        {
            float value = *(float*)((byte*)Handle + FieldsOffset + 4);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public float Default
    {
        get
        {
            float value = *(float*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the distance below which two values of the property count as the
    /// same one, which is what decides whether a write is a change.
    /// </summary>
    public float Epsilon
    {
        get
        {
            float value = *(float*)((byte*)Handle + FieldsOffset + 12);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecDouble</c>: a property that carries a double precision number
/// out of a range.
/// </summary>
/// <remarks>
/// The four fields are read at the offsets of the public structure, because
/// GObject exposes them through macros only.
/// </remarks>
public sealed unsafe class ParamSpecDouble : ParamSpec
{
    internal ParamSpecDouble(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a double precision property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="minimum">The smallest value the property accepts.</param>
    /// <param name="maximum">The largest value the property accepts.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minimum"/> is larger than <paramref name="maximum"/>, or
    /// <paramref name="defaultValue"/> lies outside the range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecDouble New(
        string name,
        string? nick,
        string? blurb,
        double minimum,
        double maximum,
        double defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckRange(minimum, maximum, defaultValue);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecDouble(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            minimum,
            maximum,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecDouble(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the smallest value the property accepts.</summary>
    public double Minimum
    {
        get
        {
            double value = *(double*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the largest value the property accepts.</summary>
    public double Maximum
    {
        get
        {
            double value = *(double*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the value the property has when nothing was written to it.</summary>
    public double Default
    {
        get
        {
            double value = *(double*)((byte*)Handle + FieldsOffset + 16);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the distance below which two values of the property count as the
    /// same one, which is what decides whether a write is a change.
    /// </summary>
    public double Epsilon
    {
        get
        {
            double value = *(double*)((byte*)Handle + FieldsOffset + 24);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecUnichar</c>: a property that carries one Unicode code point.
/// </summary>
/// <remarks>
/// The field is read at the offset of the public structure, because GObject
/// exposes it through a macro only.
/// </remarks>
public sealed unsafe class ParamSpecUnichar : ParamSpec
{
    internal ParamSpecUnichar(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a property that carries one Unicode code
    /// point.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="defaultValue">The code point the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecUnichar New(
        string name,
        string? nick,
        string? blurb,
        uint defaultValue,
        ParamFlags flags)
    {
        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecUnichar(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecUnichar(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>
    /// Gets the code point the property has when nothing was written to it.
    /// </summary>
    public uint Default
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecEnum</c>: a property that carries one member of an
/// enumeration.
/// </summary>
/// <remarks>
/// The default is read at the offset of the public structure, because GObject
/// exposes it through a macro only. The members are asked of the type of the
/// values rather than read out of the class pointer beside it, which is the
/// same table by a route that does not depend on the layout of a
/// <c>GEnumClass</c>.
/// </remarks>
public sealed unsafe class ParamSpecEnum : ParamSpec
{
    internal ParamSpecEnum(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a property that carries one member of an
    /// enumeration.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="enumType">The enumeration the values of the property belong to.</param>
    /// <param name="defaultValue">The member the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="enumType"/> is not an enumeration.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification, which is what it does
    /// when <paramref name="defaultValue"/> is not a member of
    /// <paramref name="enumType"/>.
    /// </exception>
    public static ParamSpecEnum New(
        string name,
        string? nick,
        string? blurb,
        GType enumType,
        int defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckIsA(enumType, GType.Enum, nameof(enumType));

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecEnum(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            enumType.Value,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecEnum(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the member the property has when nothing was written to it.</summary>
    public int Default
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the members of the enumeration, in the order the type declares
    /// them.
    /// </summary>
    public EnumValue[] Values => ValueType.GetEnumValues();
}

/// <summary>
/// A <c>GParamSpecFlags</c>: a property that carries a set of flags.
/// </summary>
/// <remarks>
/// The default is read at the offset of the public structure, because GObject
/// exposes it through a macro only. The members are asked of the type of the
/// values rather than read out of the class pointer beside it, which is the
/// same table by a route that does not depend on the layout of a
/// <c>GFlagsClass</c>.
/// </remarks>
public sealed unsafe class ParamSpecFlags : ParamSpec
{
    internal ParamSpecFlags(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a property that carries a set of flags.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="flagsType">The set the values of the property belong to.</param>
    /// <param name="defaultValue">The value the property has when nothing was written to it.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="flagsType"/> is not a set of flags.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification, which is what it does
    /// when <paramref name="defaultValue"/> carries a bit
    /// <paramref name="flagsType"/> does not declare.
    /// </exception>
    public static ParamSpecFlags New(
        string name,
        string? nick,
        string? blurb,
        GType flagsType,
        uint defaultValue,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckIsA(flagsType, GType.Flags, nameof(flagsType));

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecFlags(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            flagsType.Value,
            defaultValue,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecFlags(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>Gets the set the property has when nothing was written to it.</summary>
    public uint Default
    {
        get
        {
            uint value = *(uint*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the members of the set, in the order the type declares them.
    /// </summary>
    public FlagsValue[] Values => ValueType.GetFlagsValues();
}

/// <summary>
/// A <c>GParamSpecString</c>: a property that carries a string.
/// </summary>
/// <remarks>
/// The default is read at the offset of the public structure, because GObject
/// exposes it through a macro only. The three fields behind it — the sets of
/// characters a value may begin with and continue with, and the character an
/// offending one is replaced by — are not bound; they can be added later
/// without changing anything declared here.
/// </remarks>
public sealed unsafe class ParamSpecString : ParamSpec
{
    internal ParamSpecString(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a string property.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="defaultValue">
    /// The string the property has when nothing was written to it, which may be
    /// <see langword="null"/>. GObject copies it.
    /// </param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecString New(
        string name,
        string? nick,
        string? blurb,
        string? defaultValue,
        ParamFlags flags)
    {
        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint defaultPointer = GMarshal.StringToUtf8Ptr(defaultValue);

        try
        {
            nint handle = GObjectNative.ParamSpecString(
                strings.Name,
                strings.Nick,
                strings.Blurb,
                (byte*)defaultPointer,
                ParamSpecFactory.Sanitize(flags));

            return new ParamSpecString(ParamSpecFactory.Require(handle), Transfer.None);
        }
        finally
        {
            GMarshal.Free(defaultPointer);
        }
    }

    /// <summary>
    /// Gets the string the property has when nothing was written to it, or
    /// <see langword="null"/> when that default is the null pointer — which is
    /// what the <c>name</c> of a <c>GstObject</c> has, for instance.
    /// </summary>
    public string? Default
    {
        get
        {
            string? value = GMarshal.PtrToStringUtf8(*(nint*)((byte*)Handle + FieldsOffset));
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecGType</c>: a property that carries a type.
/// </summary>
/// <remarks>
/// The field is read at the offset of the public structure, because GObject
/// exposes it through a macro only.
/// </remarks>
public sealed unsafe class ParamSpecGType : ParamSpec
{
    internal ParamSpecGType(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a property that carries a type.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="isAType">
    /// The type every value of the property has to be or derive from.
    /// <see cref="GType.Invalid"/> and <see cref="GType.None"/> both stand for
    /// "any type": GObject spells that <c>G_TYPE_NONE</c>, and the invalid type
    /// is mapped onto it rather than passed on, because passing it on would
    /// build a property no type at all satisfies.
    /// </param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases. Without them GObject copies all
    /// three, which is what the binding needs.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// Disposing the wrapper releases it unless something else — installing it
    /// on a class, for instance — took a reference of its own.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecGType New(
        string name,
        string? nick,
        string? blurb,
        GType isAType,
        ParamFlags flags)
    {
        GType bound = isAType.IsValid ? isAType : GType.None;

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecGType(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            bound.Value,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecGType(ParamSpecFactory.Require(handle), Transfer.None);
    }

    /// <summary>
    /// Gets the type every value of the property has to be or derive from.
    /// </summary>
    public GType IsAType
    {
        get
        {
            GType value = new(*(nuint*)((byte*)Handle + FieldsOffset));
            GC.KeepAlive(this);
            return value;
        }
    }
}

/// <summary>
/// A <c>GParamSpecParam</c>: a property that carries a parameter specification
/// of its own.
/// </summary>
/// <remarks>
/// The class declares no field beyond the base class; what a value of the
/// property has to be is <see cref="ParamSpec.ValueType"/>, which is the class
/// of specification the property was declared with.
/// </remarks>
public sealed unsafe class ParamSpecParam : ParamSpec
{
    internal ParamSpecParam(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Builds the specification of a property that carries a parameter
    /// specification.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="paramType">
    /// The class of specification a value of the property has to be or derive
    /// from, such as the type of a <c>GParamSpecInt</c>.
    /// </param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or
    /// <paramref name="paramType"/> is not a class of specification.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecParam New(
        string name,
        string? nick,
        string? blurb,
        GType paramType,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckIsA(paramType, GType.Param, nameof(paramType));

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecParam(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            paramType.Value,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecParam(ParamSpecFactory.Require(handle), Transfer.None);
    }
}

/// <summary>
/// A <c>GParamSpecBoxed</c>: a property that carries a boxed type, which is how
/// a <c>GstCaps</c> or a <c>GstStructure</c> travels through a property.
/// </summary>
/// <remarks>
/// The class declares no field beyond the base class: the boxed type the
/// property carries is <see cref="ParamSpec.ValueType"/>.
/// </remarks>
public sealed unsafe class ParamSpecBoxed : ParamSpec
{
    internal ParamSpecBoxed(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Builds the specification of a boxed property.</summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="boxedType">
    /// The boxed type the property carries. It has to be a type a
    /// <c>GValue</c> can carry, which is what a boxed type registered the
    /// ordinary way is.
    /// </param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or
    /// <paramref name="boxedType"/> is not a boxed type a value can carry.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecBoxed New(
        string name,
        string? nick,
        string? blurb,
        GType boxedType,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckIsA(boxedType, GType.Boxed, nameof(boxedType));
        ParamSpecFactory.CheckIsValueType(boxedType, nameof(boxedType));

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecBoxed(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            boxedType.Value,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecBoxed(ParamSpecFactory.Require(handle), Transfer.None);
    }
}

/// <summary>
/// A <c>GParamSpecPointer</c>: a property that carries an address nothing
/// describes further.
/// </summary>
/// <remarks>
/// The class declares no field beyond the base class. A pointer property is
/// opaque by construction: nothing copies, frees or compares what it carries.
/// </remarks>
public sealed unsafe class ParamSpecPointer : ParamSpec
{
    internal ParamSpecPointer(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Builds the specification of a pointer property.</summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecPointer New(string name, string? nick, string? blurb, ParamFlags flags)
    {
        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecPointer(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecPointer(ParamSpecFactory.Require(handle), Transfer.None);
    }
}

/// <summary>
/// A <c>GParamSpecObject</c>: a property that carries a <c>GObject</c>, which
/// is how an element hands out a pad or a clock.
/// </summary>
/// <remarks>
/// The class declares no field beyond the base class: the type an object has to
/// be or derive from is <see cref="ParamSpec.ValueType"/>.
/// </remarks>
public sealed unsafe class ParamSpecObject : ParamSpec
{
    internal ParamSpecObject(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Builds the specification of an object property.</summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="objectType">
    /// The type an object has to be or derive from to be written to the
    /// property.
    /// </param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: the constructor
    /// of GObject hands out a floating specification and the wrapper sinks it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or
    /// <paramref name="objectType"/> does not derive from <c>GObject</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecObject New(
        string name,
        string? nick,
        string? blurb,
        GType objectType,
        ParamFlags flags)
    {
        ParamSpecFactory.CheckIsA(objectType, GType.Object, nameof(objectType));

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GObjectNative.ParamSpecObject(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            objectType.Value,
            ParamSpecFactory.Sanitize(flags));

        return new ParamSpecObject(ParamSpecFactory.Require(handle), Transfer.None);
    }
}

/// <summary>
/// A <c>GParamSpecVariant</c>: a property that carries a <c>GVariant</c>.
/// </summary>
/// <remarks>
/// The two own fields of the class — the <c>GVariantType</c> the values have to
/// match and the default value — are not read here, because neither
/// <c>GVariant</c> nor <c>GVariantType</c> is bound. The class exists so that a
/// variant property of a plugin is handed out as itself rather than as the base
/// class, and so that the name, the flags and the type of the values are
/// readable; there is no <c>New</c> for the same reason, as one could not be
/// given a type to build the specification over.
/// </remarks>
public sealed class ParamSpecVariant : ParamSpec
{
    internal ParamSpecVariant(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }
}
