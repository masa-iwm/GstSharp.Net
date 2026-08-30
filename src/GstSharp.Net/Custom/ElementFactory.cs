using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The two factory calls that carry the properties of the new element with
/// them. The generator does not emit either: their names and values are two
/// parallel arrays of which the second has a bare <c>GValue</c> as its element
/// type, and nothing in the gir says what type each of those values has to
/// hold - the answer is the property the name beside it picks out, which is
/// known only once the plugin of the factory is loaded.
/// </content>
public unsafe partial class ElementFactory
{
    /// <summary>
    /// Creates an element from the factory of the given name and gives it its
    /// properties while it is built.
    /// </summary>
    /// <param name="factoryName">The name of the factory, as the registry knows it.</param>
    /// <param name="properties">
    /// The properties to give the new element, by name. An empty dictionary is
    /// allowed and creates the element with its defaults.
    /// </param>
    /// <returns>
    /// The new element, or <see langword="null"/> when the registry has no such
    /// factory or the element could not be created. <b>Both are normal
    /// answers</b>, and the same ones <see cref="Make(string, string?)"/>
    /// gives: an application that names an element a plugin set does not carry
    /// has to see that as a value rather than as an exception.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_element_factory_make</c> with the construction arguments
    /// of the element, which is the one thing the plain call cannot express:
    /// </para>
    /// <code>
    /// using Element? source = ElementFactory.MakeWithProperties(
    ///     "videotestsrc",
    ///     new Dictionary&lt;string, object?&gt;
    ///     {
    ///         ["name"] = "bars",
    ///         ["num-buffers"] = 100,
    ///     });
    /// </code>
    /// <para>
    /// Each value is converted against the type the property declares, by the
    /// contract of <see cref="Gst.GObject.Value.CreateFor"/> that
    /// <see cref="Gst.GObject.Object.SetProperty(string, object?)"/> already
    /// uses: the numeric types accept anything that widens into them without
    /// loss, an enumeration or a set of flags accepts any managed
    /// <see cref="System.Enum"/> whose value fits as well as a plain number,
    /// and an object, a boxed value or a mini object accepts its wrapper, of
    /// which the value takes a copy.
    /// </para>
    /// <para>
    /// <b>A property that can only be given to the constructor is what this
    /// call is for.</b>
    /// <see cref="Gst.GObject.Object.SetProperty(string, object?)"/> refuses
    /// one, because writing it after the object is built never happens; here it
    /// is written while the element is built, which is the only moment it can
    /// be. What is still refused is a name the element does not declare and a
    /// property that cannot be written at all: GLib answers either with a
    /// message on the console and a property that stays at its default, which
    /// is a failure an application cannot see.
    /// </para>
    /// <para>
    /// The properties are written in the order the dictionary enumerates them,
    /// and each one has to appear once. GLib reads a name the way it declares
    /// it, so <c>max_size_buffers</c> and <c>max-size-buffers</c> are the same
    /// property under two spellings: giving both writes the property twice,
    /// which for an ordinary property is the second write winning and for one
    /// that is given to the constructor is a message on the console and the
    /// second value dropped. Neither is refused here, because each of the two
    /// names finds the property and passes the guards on its own.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="factoryName"/> or <paramref name="properties"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The element has no property of one of the given names, one of them
    /// cannot be written, or one of the values does not fit the type its
    /// property declares.
    /// </exception>
    public static Gst.Element? MakeWithProperties(
        string factoryName,
        System.Collections.Generic.IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(factoryName);
        ArgumentNullException.ThrowIfNull(properties);

        System.Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(factoryName, buffer);

        // The body of gst_element_factory_make_with_properties: look the
        // factory up, hand it to create_with_properties, and let the reference
        // the lookup took go again. The entry point itself is not imported,
        // because the element type has to be resolved from the factory before
        // the values can be built and a second lookup by name would only answer
        // the factory this one already holds.
        nint factory = GstElementFactoryFind(scope.Pointer);
        if (factory == nint.Zero)
        {
            return null;
        }

        try
        {
            return CreateWithProperties(factory, properties);
        }
        finally
        {
            GObjectNative.ObjectUnref(factory);
        }
    }

    /// <summary>
    /// Creates an element from this factory and gives it its properties while
    /// it is built.
    /// </summary>
    /// <param name="properties">
    /// The properties to give the new element, by name. An empty dictionary is
    /// allowed and creates the element with its defaults.
    /// </param>
    /// <returns>
    /// The new element, or <see langword="null"/> when it could not be created:
    /// the plugin of the factory failed to load, or the factory carries no
    /// type. <b>Both are normal answers</b>, the same ones
    /// <see cref="Create(string?)"/> gives.
    /// </returns>
    /// <remarks>
    /// <see cref="MakeWithProperties(string, System.Collections.Generic.IReadOnlyDictionary{string, object?})"/>
    /// is this call reached by the name of the factory, and states the
    /// conversion contract of the values and what is refused.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The element has no property of one of the given names, one of them
    /// cannot be written, or one of the values does not fit the type its
    /// property declares.
    /// </exception>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public Gst.Element? CreateWithProperties(
        System.Collections.Generic.IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        nint factory = Handle;

        try
        {
            return CreateWithProperties(factory, properties);
        }
        finally
        {
            // Reading Handle is the last use of this wrapper, so without this
            // the collector may finalize it while the call runs.
            System.GC.KeepAlive(this);
        }
    }

    /// <summary>
    /// Creates the element of a native factory with the properties of a
    /// dictionary.
    /// </summary>
    /// <param name="factory">The factory, borrowed for the length of the call.</param>
    /// <param name="properties">The properties to give the new element.</param>
    /// <returns>The new element, or <see langword="null"/>.</returns>
    private static Gst.Element? CreateWithProperties(
        nint factory,
        System.Collections.Generic.IReadOnlyDictionary<string, object?> properties)
    {
        if (properties.Count == 0)
        {
            // No property is no type to look up. The C call loads the feature
            // itself, and g_object_new_with_properties reads the two null
            // arrays as "none", which is what gst_element_factory_create does
            // for an element that is given no name either.
            return Gst.GObject.Object.FromNative<Gst.Element>(
                GstElementFactoryCreateWithProperties(factory, 0, null, null),
                Transfer.None);
        }

        // Every value has to hold the type its property declares before the
        // call, and a factory only knows the type of its elements once its
        // plugin is loaded: gst_element_factory_get_element_type answers 0
        // until then. This is the load that
        // gst_element_factory_create_with_properties opens with, and it may
        // answer a different feature object than the one the registry handed
        // out - which is why the create below is given the loaded one, exactly
        // as the C function does.
        nint loaded = GstPluginFeatureLoad(factory);
        if (loaded == nint.Zero)
        {
            return null;
        }

        try
        {
            Gst.GObject.GType elementType = new(GstElementFactoryGetElementType(loaded));

            return elementType.IsValid ? CreateWithProperties(loaded, elementType, properties) : null;
        }
        finally
        {
            GObjectNative.ObjectUnref(loaded);
        }
    }

    /// <summary>
    /// Builds the two arrays the call takes and makes the element.
    /// </summary>
    /// <param name="factory">The loaded factory, borrowed for the length of the call.</param>
    /// <param name="elementType">The type of the elements of <paramref name="factory"/>.</param>
    /// <param name="properties">The properties to give the new element.</param>
    /// <returns>The new element, or <see langword="null"/>.</returns>
    private static Gst.Element? CreateWithProperties(
        nint factory,
        Gst.GObject.GType elementType,
        System.Collections.Generic.IReadOnlyDictionary<string, object?> properties)
    {
        int count = properties.Count;
        string[] names = new string[count];
        Gst.GObject.GValueNative[] values = new Gst.GObject.GValueNative[count];

        // The specifications belong to the class and are borrowed, so the class
        // has to be held for as long as they are read. Referencing it is also
        // what makes them exist: a class that was never instantiated has run no
        // class_init and declares no property yet.
        nint elementClass = GObjectNative.TypeClassRef(elementType.Value);

        try
        {
            System.Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
            int index = 0;

            foreach (System.Collections.Generic.KeyValuePair<string, object?> property in properties)
            {
                using Utf8Scope scope = GMarshal.StackUtf8(property.Key, buffer);
                nint pspec = GObjectNative.ObjectClassFindProperty(elementClass, scope.Pointer);

                if (pspec == nint.Zero)
                {
                    throw new ArgumentException(
                        $"\"{property.Key}\" is not a property of {elementType.Name}.",
                        nameof(properties));
                }

                if ((Gst.GObject.ParamSpec.FlagsOf(pspec) & Gst.GObject.ParamFlags.Writable) == 0)
                {
                    throw new ArgumentException(
                        $"The property \"{property.Key}\" of {elementType.Name} cannot be written.",
                        nameof(properties));
                }

                // A construct only property is deliberately not refused here,
                // which is the one guard of Gst.GObject.Object.SetProperty that
                // this call does not repeat: giving one its value while the
                // element is built is what the call exists for.
                names[index] = property.Key;

                Gst.GObject.GType declared = Gst.GObject.ParamSpec.ValueTypeOf(pspec);

                try
                {
                    // The converted value moves into the array, which is
                    // from here on its only owner and is what unsets it again
                    // in the exit below - the hand over that
                    // Gst.GObject.Object.EmitSignal makes for the arguments of
                    // a signal. The array is only read by the call: GObject
                    // takes the values as const and copies out of them, so
                    // nothing writes back into it.
                    values[index] = Gst.GObject.Value.CreateFor(property.Value, declared).NativeValue;
                }
                catch (ArgumentException exception)
                {
                    // The conversion knows the type it refused and not the
                    // property it was refused for, which is the half a caller
                    // needs; EmitSignal names the argument of the signal the
                    // same way.
                    throw new ArgumentException(
                        $"The property \"{property.Key}\" of {elementType.Name} holds {declared.Name}: " +
                        exception.Message,
                        nameof(properties),
                        exception);
                }

                index++;
            }

            using StrvScope nameScope = GMarshal.AllocStrv(names);

            fixed (Gst.GObject.GValueNative* first = values)
            {
                nint element = GstElementFactoryCreateWithProperties(
                    factory,
                    (uint)count,
                    (byte**)nameScope.Pointer,
                    first);

                return Gst.GObject.Object.FromNative<Gst.Element>(element, Transfer.None);
            }
        }
        finally
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].TypeValue != Gst.GObject.GType.InvalidValue)
                {
                    GObjectNative.ValueUnset(ref values[i]);
                }
            }

            GObjectNative.TypeClassUnref(elementClass);
        }
    }

    /// <summary>The <c>gst_element_factory_create_with_properties</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_element_factory_create_with_properties")]
    private static partial nint GstElementFactoryCreateWithProperties(
        nint factory,
        uint n,
        byte** names,
        Gst.GObject.GValueNative* values);

    /// <summary>The <c>gst_plugin_feature_load</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_plugin_feature_load")]
    private static partial nint GstPluginFeatureLoad(nint feature);
}
