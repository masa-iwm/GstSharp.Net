using Gst.Interop;

namespace Gst.GObject;

/// <content>
/// The properties that no generated binding covers: writing one by name,
/// reading one by name as a managed type, and asking by name what a property
/// is before doing either.
/// </content>
/// <remarks>
/// <para>
/// Where a <c>.gir</c> file describes a property, the generated binding gives it
/// a typed member of its own and that member stays the better call: it says what
/// it takes in its signature. Plugin elements are the other half of the surface.
/// <c>x264enc</c>, the <c>d3d11</c> elements and <c>webrtcbin</c> carry
/// properties that no <c>.gir</c> file mentions, so the only way to reach one is
/// by name, and doing that through a <c>GValue</c> by hand is four lines for
/// what is conceptually an assignment.
/// </para>
/// <para>
/// These two members are that assignment. Neither uses reflection: the
/// conversion on the way in is <see cref="Value.CreateFor"/>, a switch over the
/// fundamental types of GObject, and the one on the way out is the same switch
/// read backwards.
/// </para>
/// </remarks>
public partial class Object
{
    /// <summary>
    /// Writes a property by name, converting the content against the type the
    /// property declares.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="content">
    /// The new value. <see langword="null"/> is only accepted by the property
    /// types that have a null value.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the one line an application writes against a property that no
    /// generated binding covers:
    /// </para>
    /// <code>
    /// encoder.SetProperty("bitrate", 4000);
    /// encoder.SetProperty("tune", "zerolatency");
    /// sink.SetProperty("sync", false);
    /// </code>
    /// <para>
    /// What may be written is the conversion contract of
    /// <see cref="Value.CreateFor"/>, against the type of the property: the
    /// numeric types accept anything that widens into them without loss, an
    /// enumeration or a set of flags accepts any managed
    /// <see cref="System.Enum"/> whose value fits as well as a plain number —
    /// which is how the enumeration of a plugin, whose managed type no binding
    /// declares, is written — and an object, a boxed value or a mini object
    /// accepts its wrapper, of which the value takes a copy, so the caller stays
    /// the owner of what it holds.
    /// </para>
    /// <para>
    /// <b>A property that cannot be written is refused here rather than in
    /// GLib.</b> <c>g_object_set_property</c> answers a read only property, and
    /// a construct only property once the object is built, with a warning on the
    /// console and a write that never happens — a failure an application cannot
    /// see. Both are an <see cref="ArgumentException"/> instead;
    /// <see cref="FindProperty(string)"/> is where the flags can be asked in
    /// advance.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The object has no such property, the property cannot be written at all or
    /// cannot be written any more, or <paramref name="content"/> does not fit
    /// the type the property declares.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public unsafe void SetProperty(string name, object? content)
    {
        ArgumentNullException.ThrowIfNull(name);

        nint handle = Handle;
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);

        // The first word of a GTypeInstance is its class, which is what
        // G_OBJECT_GET_CLASS reads; GetProperty takes the same route. The
        // specification belongs to the class and is borrowed, so there is
        // nothing here to release.
        nint pspec = GObjectNative.ObjectClassFindProperty(*(nint*)handle, scope.Pointer);
        if (pspec == nint.Zero)
        {
            throw new ArgumentException($"\"{name}\" is not a property of {NativeType.Name}.", nameof(name));
        }

        GType expected = ParamSpec.ValueTypeOf(pspec);
        ParamFlags flags = ParamSpec.FlagsOf(pspec);

        if ((flags & ParamFlags.Writable) == 0)
        {
            throw new ArgumentException(
                $"The property \"{name}\" of {NativeType.Name} cannot be written.",
                nameof(name));
        }

        if ((flags & ParamFlags.ConstructOnly) != 0)
        {
            throw new ArgumentException(
                $"The property \"{name}\" of {NativeType.Name} can only be given to the constructor.",
                nameof(name));
        }

        using Value value = Value.CreateFor(content, expected);

        // The specification is only valid for as long as the class that owns it,
        // and the class only for as long as the object. The write below is a use
        // of this wrapper, so it is what keeps it alive across everything above:
        // a wrapper that native code does not hold besides us is held weakly, and
        // the collector may take one whose last use has passed. See GetProperty.
        // The core skips the guards of the public overload, which all ran here
        // - looking the specification up a second time would answer the same.
        SetPropertyCore(name, value);
    }

    /// <summary>
    /// Reads a property by name and hands its content back as a managed type.
    /// </summary>
    /// <typeparam name="T">
    /// The managed type to read the property as; see the remarks for the ones a
    /// property can supply.
    /// </typeparam>
    /// <param name="name">The name of the property.</param>
    /// <returns>
    /// The content of the property, or <see langword="null"/> when the property
    /// holds nothing and <typeparamref name="T"/> is a reference type.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <see cref="SetProperty(string, object?)"/> read backwards, and
    /// the same one line:
    /// </para>
    /// <code>
    /// int bitrate = encoder.GetProperty&lt;int&gt;("bitrate");
    /// bool live = source.GetProperty&lt;bool&gt;("is-live");
    /// using Caps? current = filter.GetProperty&lt;Caps&gt;("caps");
    /// </code>
    /// <para>
    /// <typeparamref name="T"/> may be:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="bool"/> for a <c>gboolean</c> property and
    /// <see cref="string"/> for a <c>gchararray</c> one, which reads as
    /// <see langword="null"/> when it holds no string;
    /// </description></item>
    /// <item><description>
    /// <see cref="sbyte"/>, <see cref="byte"/>, <see cref="int"/>,
    /// <see cref="uint"/>, <see cref="long"/> or <see cref="ulong"/> for an
    /// integer, enumeration or flags property whose <em>declared</em> type
    /// widens into it without loss. A <c>gint</c> property reads as an
    /// <see cref="int"/> or a <see cref="long"/>, a <c>guint</c> one as a
    /// <see cref="uint"/>, a <see cref="long"/> or a <see cref="ulong"/>, and
    /// neither reads as the other. An enumeration carries a <c>gint</c> and a
    /// set of flags a <c>guint</c>, so reading the enumeration of a plugin as
    /// an <see cref="int"/> is the counterpart of writing one as a number;
    /// </description></item>
    /// <item><description>
    /// <see cref="float"/> for a <c>gfloat</c> property and
    /// <see cref="double"/> for a <c>gfloat</c> or a <c>gdouble</c> one. An
    /// integer property is read with an integer type, and a <c>gdouble</c>
    /// property is not narrowed into a <see cref="float"/>;
    /// </description></item>
    /// <item><description>
    /// any managed <see cref="System.Enum"/> whose underlying type holds the
    /// number, for an enumeration or a flags property. The <c>GType</c> of the
    /// property is not compared against the managed type — the generated
    /// enumerations are not known down here — so it is the caller who says
    /// which enumeration the number belongs to, exactly as on the way in;
    /// </description></item>
    /// <item><description>
    /// the wrapper type of an object, a boxed value or a mini object:
    /// <c>GetProperty&lt;Gst.Element&gt;("parent")</c>,
    /// <c>GetProperty&lt;Gst.Structure&gt;("stats")</c>,
    /// <c>GetProperty&lt;Gst.Caps&gt;("caps")</c>.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>What the property declares is what decides, never the number it
    /// happens to hold.</b> A <c>gint64</c> property is refused for an
    /// <see cref="int"/> even while its value would fit in one, so a call that
    /// works once works always. This is the one place the pair is asymmetric:
    /// <see cref="SetProperty(string, object?)"/> takes any literal that fits,
    /// because a write is checked with the caller standing right there, while a
    /// read that succeeded in testing and threw in production would not be.
    /// </para>
    /// <para>
    /// <b>A boxed value and a mini object come back as an owner of their own,
    /// which the caller has to dispose</b>, exactly as
    /// <see cref="Value.GetBoxed{T}"/> and <see cref="Value.GetMiniObject{T}"/>
    /// hand them out, and for the same reason: the value the property was read
    /// into is released as this returns. An object is not — GObject wrappers are
    /// interned and the one that comes back is the wrapper the application
    /// already has. A property that holds nothing reads as <see langword="null"/>
    /// for all three.
    /// </para>
    /// <para>
    /// A boxed value or a mini object also needs the module that binds its type
    /// to have registered, which naming its wrapper type does not by itself do;
    /// see <see cref="Value.GetBoxed{T}"/> for that rule and for the call that
    /// satisfies it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The object has no such property, or the property cannot be read.
    /// </exception>
    /// <exception cref="InvalidCastException">
    /// The property cannot supply a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for the boxed type of the property, which
    /// normally means that the module that binds it has not been initialised.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public T GetProperty<T>(string name)
    {
        Value value = GetProperty(name);

        try
        {
            if (TryExtract(in value, out T result))
            {
                return result;
            }

            throw new InvalidCastException(
                $"The property \"{name}\" of {NativeType.Name} holds a {value.Type.Name}, " +
                $"which cannot be read as a {typeof(T)}.");
        }
        finally
        {
            value.Dispose();
        }
    }

    /// <summary>
    /// Looks one property up by name and hands back its description, or
    /// <see langword="null"/> when the class declares no property under that
    /// name.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>
    /// The <see cref="ParamSpec"/> of the property, which the caller has to
    /// dispose, or <see langword="null"/> when there is no such property.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>g_object_class_find_property</c>, the lookup that
    /// <see cref="GetProperty(string)"/> and
    /// <see cref="SetProperty(string, object?)"/> run before they touch the
    /// property — published so that the questions those calls answer with an
    /// exception can be asked in advance. Absence is an answer here rather than
    /// a throw, because asking is the point:
    /// </para>
    /// <code>
    /// using ParamSpec? property = sink.FindProperty("last-sample");
    ///
    /// if (property is not null &amp;&amp; (property.Flags &amp; ParamFlags.Readable) != 0)
    /// {
    ///     using Sample? last = sink.GetProperty&lt;Sample&gt;("last-sample");
    /// }
    /// </code>
    /// <para>
    /// <see cref="ParamFlags.Writable"/> and
    /// <see cref="ParamFlags.ConstructOnly"/> are the flags
    /// <see cref="SetProperty(string, object?)"/> enforces, and
    /// <see cref="ParamSpec.ValueType"/> is the type a value has to fit. The
    /// question is about the class rather than the instance — every
    /// <c>fakesink</c> answers the same — and, as with
    /// <see cref="ListProperties"/>, the specification that comes back holds a
    /// reference of its own: it stays valid whatever happens to the object, and
    /// it is the caller's to dispose.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public unsafe ParamSpec? FindProperty(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        nint handle = Handle;
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);

        // The first word of a GTypeInstance is its class, which is what
        // G_OBJECT_GET_CLASS reads; GetProperty takes the same route. The
        // specification the class answers with is borrowed, and the wrapper
        // takes a reference of its own in its constructor.
        nint pspec = GObjectNative.ObjectClassFindProperty(*(nint*)handle, scope.Pointer);

        // Handle was the last use of this wrapper, and the class is only known
        // to be alive for as long as the object is.
        GC.KeepAlive(this);

        return pspec == nint.Zero ? null : new ParamSpec(pspec, Transfer.None);
    }

    /// <summary>
    /// Creates the value a property is written with: empty, and of the type the
    /// specification of that property declares.
    /// </summary>
    /// <param name="name">The name of the property.</param>
    /// <returns>The value to fill in, which the caller has to dispose.</returns>
    /// <remarks>
    /// <para>
    /// This is the first half of the setter of a generated property that no C
    /// accessor backs, and <see cref="SetPropertyValue(string, in Value)"/> is
    /// the second: the pair is what
    /// <see cref="SetProperty(string, in Value)"/> does, split so that the
    /// declared type is known before the value is filled in. The type is what
    /// the generated setter needs and cannot compute: the binding declares no
    /// <c>GType</c> of its own for a generated enumeration, and
    /// <c>g_value_set_enum</c> asserts that the value it is given already holds
    /// one.
    /// </para>
    /// <para>
    /// Every guard of the public overload runs here, before anything is
    /// allocated: an unknown name, a property that cannot be written and one
    /// that can only be given to the constructor are the same three
    /// <see cref="ArgumentException"/>s, with the same messages. What the
    /// second half no longer has to check is the pair of types, because a value
    /// created here holds exactly what the property declares.
    /// </para>
    /// <para>
    /// It is <c>internal</c> on purpose. The two halves are only correct
    /// together, and the generator is the one caller that always writes both;
    /// <see cref="SetProperty(string, object?)"/> and
    /// <see cref="SetProperty(string, in Value)"/> are the public ways to write
    /// a property in one call.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The object has no such property, or the property cannot be written at
    /// all or cannot be written any more.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    internal unsafe Value NewPropertyValue(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        nint handle = Handle;
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);

        // The first word of a GTypeInstance is its class, which is what
        // G_OBJECT_GET_CLASS reads; GetProperty takes the same route. The
        // specification belongs to the class and is borrowed, so there is
        // nothing here to release.
        nint pspec = GObjectNative.ObjectClassFindProperty(*(nint*)handle, scope.Pointer);
        if (pspec == nint.Zero)
        {
            throw new ArgumentException($"\"{name}\" is not a property of {NativeType.Name}.", nameof(name));
        }

        ParamFlags flags = ParamSpec.FlagsOf(pspec);

        if ((flags & ParamFlags.Writable) == 0)
        {
            throw new ArgumentException(
                $"The property \"{name}\" of {NativeType.Name} cannot be written.",
                nameof(name));
        }

        if ((flags & ParamFlags.ConstructOnly) != 0)
        {
            throw new ArgumentException(
                $"The property \"{name}\" of {NativeType.Name} can only be given to the constructor.",
                nameof(name));
        }

        Value value = Value.New(ParamSpec.ValueTypeOf(pspec));

        // The specification is only valid for as long as the class that owns
        // it, and the class only for as long as the object; Handle was the last
        // use of this wrapper. See GetProperty.
        GC.KeepAlive(this);
        return value;
    }

    /// <summary>
    /// Writes a value that <see cref="NewPropertyValue(string)"/> created for
    /// this property and this object.
    /// </summary>
    /// <param name="name">The name of the property, the one the value was created for.</param>
    /// <param name="value">The value to write.</param>
    /// <remarks>
    /// The second half of the pair, which skips the guards because they all ran
    /// in the first: looking the specification up a second time would answer
    /// the same, the value is not empty because it was created with a type, and
    /// no transformation is needed because that type is the declared one. This
    /// is the same split that <see cref="SetProperty(string, object?)"/> makes
    /// between its own guards and the write it ends with.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    internal void SetPropertyValue(string name, in Value value) => SetPropertyCore(name, value);

    /// <summary>
    /// Reads the content of a value as <typeparamref name="T"/>, without
    /// reflection and without a table: the type argument is compared against the
    /// managed types a property can supply, and the fundamental type of the
    /// value says whether it can supply that one.
    /// </summary>
    /// <typeparam name="T">The managed type to read the value as.</typeparam>
    /// <param name="value">The value to read.</param>
    /// <param name="result">The content of the value.</param>
    /// <returns>
    /// <see langword="true"/> when the value can supply a
    /// <typeparamref name="T"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The value holds a boxed value whose type is not registered.
    /// </exception>
    private static bool TryExtract<T>(in Value value, out T result)
    {
        GType type = value.Type;
        nuint fundamental = type.Fundamental.Value;

        if (typeof(T) == typeof(bool))
        {
            if (fundamental == GType.BooleanValue)
            {
                result = (T)(object)value.GetBoolean();
                return true;
            }
        }
        else if (typeof(T) == typeof(string))
        {
            if (fundamental == GType.StringValue)
            {
                string? text = value.GetString();
                result = text is null ? default! : (T)(object)text;
                return true;
            }
        }
        else if (typeof(T) == typeof(sbyte))
        {
            if (Widens(fundamental, GType.CharValue) && TryReadInt64(value, fundamental, out long number))
            {
                result = (T)(object)(sbyte)number;
                return true;
            }
        }
        else if (typeof(T) == typeof(byte))
        {
            if (Widens(fundamental, GType.UCharValue) && TryReadUInt64(value, fundamental, out ulong number))
            {
                result = (T)(object)(byte)number;
                return true;
            }
        }
        else if (typeof(T) == typeof(int))
        {
            if (Widens(fundamental, GType.IntValue) && TryReadInt64(value, fundamental, out long number))
            {
                result = (T)(object)(int)number;
                return true;
            }
        }
        else if (typeof(T) == typeof(uint))
        {
            if (Widens(fundamental, GType.UIntValue) && TryReadUInt64(value, fundamental, out ulong number))
            {
                result = (T)(object)(uint)number;
                return true;
            }
        }
        else if (typeof(T) == typeof(long))
        {
            if (Widens(fundamental, GType.Int64Value) && TryReadInt64(value, fundamental, out long number))
            {
                result = (T)(object)number;
                return true;
            }
        }
        else if (typeof(T) == typeof(ulong))
        {
            if (Widens(fundamental, GType.UInt64Value) && TryReadUInt64(value, fundamental, out ulong number))
            {
                result = (T)(object)number;
                return true;
            }
        }
        else if (typeof(T) == typeof(float))
        {
            if (fundamental == GType.FloatValue)
            {
                result = (T)(object)value.GetFloat();
                return true;
            }
        }
        else if (typeof(T) == typeof(double))
        {
            if (fundamental == GType.FloatValue)
            {
                result = (T)(object)(double)value.GetFloat();
                return true;
            }

            if (fundamental == GType.DoubleValue)
            {
                result = (T)(object)value.GetDouble();
                return true;
            }
        }
        else if (typeof(T).IsEnum)
        {
            // The GType of a generated enumeration is not known here, so the
            // member travels as its number and the caller is the one who says
            // which enumeration it belongs to. That is the same trade
            // Value.CreateFor makes on the way in.
            if (TryReadInt64(value, fundamental, out long number) && FitsEnum<T>(number))
            {
                result = (T)Enum.ToObject(typeof(T), number);
                return true;
            }
        }
        else if (fundamental == GType.ObjectValue ||
            (fundamental == GType.InterfaceValue && type.IsA(GType.Object)))
        {
            // Interned, and borrowed from the value: the wrapper the application
            // already has is the one that comes back, so it is not the caller's
            // to dispose and a mismatch must not dispose it either.
            Object? instance = value.GetObject();
            if (instance is null)
            {
                return TryNull(out result);
            }

            if (instance is T typed)
            {
                result = typed;
                return true;
            }
        }
        else if (fundamental == GType.BoxedValue)
        {
            return TryExtractBoxed(value, type, out result);
        }

        result = default!;
        return false;
    }

    /// <summary>
    /// Reads a boxed value — which is what a mini object is, as far as GObject
    /// is concerned — as its registered wrapper.
    /// </summary>
    /// <typeparam name="T">The wrapper type to read the value as.</typeparam>
    /// <param name="value">The value to read.</param>
    /// <param name="type">The type of the value, which says what its pointer is.</param>
    /// <param name="result">The wrapper.</param>
    /// <returns>
    /// <see langword="true"/> when the wrapper is a <typeparamref name="T"/>.
    /// </returns>
    /// <remarks>
    /// This is the route <see cref="Value.GetBoxed{T}"/> and
    /// <see cref="Value.GetMiniObject{T}"/> take, without their type constraint:
    /// the wrapper is built with <see cref="Transfer.None"/>, so a
    /// <see cref="Boxed"/> takes a copy and a <see cref="Gst.MiniObject"/> takes
    /// a reference, and either way the caller becomes an owner. The two calls
    /// are two calls because their type constraints are two constraints; here
    /// <typeparamref name="T"/> carries none and the question is asked once.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for <paramref name="type"/>.
    /// </exception>
    private static bool TryExtractBoxed<T>(in Value value, GType type, out T result)
    {
        nint boxed = value.GetBoxed();
        if (boxed == nint.Zero)
        {
            return TryNull(out result);
        }

        if (!TypeRegistry.TryCreateWrapper(type, boxed, Transfer.None, out object? wrapper))
        {
            throw new InvalidOperationException(
                $"No wrapper is registered for the boxed type {type.Name}. " +
                "Initialise the binding module that covers it — for example " +
                "Gst.Video.GstVideo.Initialize() — before reading the property.");
        }

        if (wrapper is T typed)
        {
            result = typed;
            return true;
        }

        // The factory built a copy — a reference, for a mini object — of its
        // own, and nothing else holds it.
        (wrapper as IDisposable)?.Dispose();

        result = default!;
        return false;
    }

    /// <summary>
    /// Hands a property that holds nothing back as <see langword="null"/>, which
    /// only a type that has a null value can take.
    /// </summary>
    /// <typeparam name="T">The managed type the caller asked for.</typeparam>
    /// <param name="result">Always the default of <typeparamref name="T"/>.</param>
    /// <returns>
    /// <see langword="true"/> when <typeparamref name="T"/> can be
    /// <see langword="null"/>.
    /// </returns>
    private static bool TryNull<T>(out T result)
    {
        result = default!;
        return default(T) is null;
    }

    /// <summary>
    /// Tells whether a number survives being stored in an enumeration of
    /// <typeparamref name="T"/>, which <c>Enum.ToObject</c> would otherwise
    /// truncate to fit.
    /// </summary>
    /// <typeparam name="T">The enumeration type.</typeparam>
    /// <param name="number">The number to store.</param>
    /// <returns><see langword="true"/> when the underlying type holds it.</returns>
    private static bool FitsEnum<T>(long number)
    {
        Type underlying = Enum.GetUnderlyingType(typeof(T));

        if (underlying == typeof(int))
        {
            return number is >= int.MinValue and <= int.MaxValue;
        }

        if (underlying == typeof(uint))
        {
            return number is >= 0 and <= uint.MaxValue;
        }

        if (underlying == typeof(long))
        {
            return true;
        }

        if (underlying == typeof(ulong))
        {
            return number >= 0;
        }

        if (underlying == typeof(short))
        {
            return number is >= short.MinValue and <= short.MaxValue;
        }

        if (underlying == typeof(ushort))
        {
            return number is >= 0 and <= ushort.MaxValue;
        }

        if (underlying == typeof(sbyte))
        {
            return number is >= sbyte.MinValue and <= sbyte.MaxValue;
        }

        return underlying == typeof(byte) && number is >= 0 and <= byte.MaxValue;
    }

    /// <summary>
    /// Tells whether every value of one fundamental type fits another one, which
    /// is the question the numeric reads are gated on.
    /// </summary>
    /// <param name="source">The fundamental type of the value.</param>
    /// <param name="target">
    /// The fundamental type that stands for the managed type the caller asked
    /// for: <see cref="GType.IntValue"/> for an <see cref="int"/>, and so on.
    /// </param>
    /// <returns><see langword="true"/> when the conversion loses nothing.</returns>
    /// <remarks>
    /// <para>
    /// The question is about the two types and never about the number the value
    /// currently holds, so a call answers the same way every time it is made.
    /// That is deliberately stricter than the range checks
    /// <see cref="Value.CreateFor"/> makes on the way in: a write is a literal
    /// that either fits or does not, and the caller is standing right there,
    /// while a read that succeeds for the values seen in testing and throws for
    /// the one seen in production is the failure this rules out.
    /// </para>
    /// <para>
    /// A C <c>long</c> is 32 bits on Windows and 64 elsewhere, so it is only
    /// taken as widening into the 64 bit types, where both are true.
    /// </para>
    /// </remarks>
    private static bool Widens(nuint source, nuint target) => target switch
    {
        GType.CharValue => source is GType.CharValue,
        GType.UCharValue => source is GType.UCharValue,

        // A GObject enumeration carries a gint and a set of flags a guint, which
        // is what puts each of them on one side only.
        GType.IntValue => source is GType.CharValue or GType.UCharValue or GType.IntValue or GType.EnumValue,
        GType.UIntValue => source is GType.UCharValue or GType.UIntValue or GType.FlagsValue,
        GType.Int64Value => source is GType.CharValue or GType.UCharValue or GType.IntValue or GType.UIntValue
            or GType.LongValue or GType.Int64Value or GType.EnumValue or GType.FlagsValue,
        GType.UInt64Value => source is GType.UCharValue or GType.UIntValue or GType.ULongValue
            or GType.UInt64Value or GType.FlagsValue,
        _ => false,
    };

    /// <summary>
    /// Reads any integral, enumeration or flags value that fits a signed 64 bit
    /// integer, which is the mirror of <c>Value.TryToInt64</c>.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="fundamental">The fundamental type of the value.</param>
    /// <param name="result">The number the value holds.</param>
    /// <returns><see langword="true"/> when the conversion is possible.</returns>
    private static bool TryReadInt64(in Value value, nuint fundamental, out long result)
    {
        switch (fundamental)
        {
            case GType.CharValue:
                result = value.GetSChar();
                return true;
            case GType.UCharValue:
                result = value.GetUChar();
                return true;
            case GType.IntValue:
                result = value.GetInt();
                return true;
            case GType.UIntValue:
                result = value.GetUInt();
                return true;
            case GType.LongValue:
                result = value.GetLong();
                return true;
            case GType.Int64Value:
                result = value.GetInt64();
                return true;
            case GType.EnumValue:
                result = value.GetEnum();
                return true;
            case GType.FlagsValue:
                result = value.GetFlags();
                return true;

            case GType.ULongValue:
            {
                nuint number = value.GetULong();
                if (number <= long.MaxValue)
                {
                    result = (long)number;
                    return true;
                }

                break;
            }

            case GType.UInt64Value:
            {
                ulong number = value.GetUInt64();
                if (number <= long.MaxValue)
                {
                    result = (long)number;
                    return true;
                }

                break;
            }
        }

        result = 0;
        return false;
    }

    /// <summary>
    /// Reads any integral, enumeration or flags value that fits an unsigned 64
    /// bit integer, which is the mirror of <c>Value.TryToUInt64</c>.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="fundamental">The fundamental type of the value.</param>
    /// <param name="result">The number the value holds.</param>
    /// <returns><see langword="true"/> when the conversion is possible.</returns>
    private static bool TryReadUInt64(in Value value, nuint fundamental, out ulong result)
    {
        switch (fundamental)
        {
            case GType.ULongValue:
                result = value.GetULong();
                return true;
            case GType.UInt64Value:
                result = value.GetUInt64();
                return true;
            default:
                if (TryReadInt64(value, fundamental, out long signed) && signed >= 0)
                {
                    result = (ulong)signed;
                    return true;
                }

                result = 0;
                return false;
        }
    }
}
