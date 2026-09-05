using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESVideoSource</c> whose <c>create_element</c> override refuses
/// to build an element, either by answering none or by throwing.
/// </summary>
/// <remarks>
/// The sibling of <see cref="ProbeNullVideoSource"/> one slot down.
/// <c>GESSource</c> implements <c>create_element</c> and calls
/// <c>create_source</c> from it, so a subclass that declares
/// <c>create_element</c> takes over the whole of it; the guard the trampoline
/// carries is the same one, and so is what it protects against — the library
/// releases an nleobject it no longer owns when the slot answers nothing
/// (<c>ges-track-element.c:1022</c>, <c>1066-1070</c>).
/// </remarks>
internal sealed class ProbeNullElementVideoSource : GES.VideoSource, IManagedSubclass<ProbeNullElementVideoSource>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesNullElementVideoSource";

    /// <summary>The message the throwing mode raises.</summary>
    internal const string RefusalMessage = "The probe refuses to build an element.";

    private static readonly SubclassType Definition = DefineSubclass<ProbeNullElementVideoSource>(
        GTypeName,
        null,
        CreateElementOverride);

    private static int _throws;

    private ProbeNullElementVideoSource(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the source.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>
    /// Gets or sets a value indicating whether the override throws instead of
    /// answering nothing.
    /// </summary>
    internal static bool Throws
    {
        get => Volatile.Read(ref _throws) != 0;
        set => Volatile.Write(ref _throws, value ? 1 : 0);
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeNullElementVideoSource CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override Gst.Element OnCreateElement()
    {
        if (Throws)
        {
            throw new InvalidOperationException(RefusalMessage);
        }

        // The slot answers a non nullable element, so this is what an override
        // that answers nothing anyway looks like: the trampoline finds the null
        // and reports it.
        return null!;
    }
}
