using Gst;
using Gst.GObject;
using GObjectObject = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed element whose one property the test disposes the caller's
/// specification wrapper of, to prove that the class keeps the property.
/// </summary>
/// <remarks>
/// It is a type of its own rather than a second property on
/// <see cref="ProbePropertyElement"/> because disposing a specification is not
/// something the other facts of that element should have to work around.
/// </remarks>
internal sealed class ProbeDisposableSpecElement : Element, IManagedSubclass<ProbeDisposableSpecElement>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestDisposableSpecElement";

    /// <summary>The identifier of the <c>value</c> property.</summary>
    internal const uint ValueId = 1;

    private const ParamFlags ReadWrite = ParamFlags.Readable | ParamFlags.Writable;

    private static readonly ParamSpecInt ValueSpec =
        ParamSpecInt.New("value", "Value", "An integer", 0, 100, 0, ReadWrite);

    // The handle is read while the wrapper still holds it: Dispose zeroes it,
    // and the reference count has to be read from the same pointer afterwards.
    private static readonly nint SpecHandle = ValueSpec.Handle;

    private static readonly SubclassType Definition = DefineSubclass<ProbeDisposableSpecElement>(
        GTypeName,
        static config => config.InstallProperty(ValueId, ValueSpec),
        GObjectObject.SetPropertyOverride,
        GObjectObject.GetPropertyOverride);

    private int _value;

    /// <summary>Creates an element of the type.</summary>
    internal ProbeDisposableSpecElement()
        : base(Definition.NewInstance())
    {
    }

    private ProbeDisposableSpecElement(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the pointer the specification was installed from.</summary>
    internal static nint SpecificationHandle => SpecHandle;

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets what the last write stored.</summary>
    internal int Value => Volatile.Read(ref _value);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeDisposableSpecElement CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <summary>Releases the wrapper the installation was performed from.</summary>
    internal static void DisposeSpecification() => ValueSpec.Dispose();

    /// <inheritdoc/>
    protected override void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
    {
        if (propertyId == ValueId)
        {
            Volatile.Write(ref _value, value.GetInt());
            return;
        }

        base.OnSetProperty(propertyId, value, pspec);
    }

    /// <inheritdoc/>
    protected override void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
    {
        if (propertyId == ValueId)
        {
            value.SetInt(Volatile.Read(ref _value));
            return;
        }

        base.OnGetProperty(propertyId, value, pspec);
    }
}
