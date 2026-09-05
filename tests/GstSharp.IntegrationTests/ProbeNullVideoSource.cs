using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESVideoSource</c> whose <c>create_source</c> override refuses
/// to build an element, either by answering none or by throwing.
/// </summary>
/// <remarks>
/// Both are the same answer as far as the library is concerned: the trampoline
/// reports the exception and hands the slot a substitute. Without that
/// substitute GES 1.28.6 releases the nlesource while it is still floating and
/// frees it under the track element (<c>ges-track-element.c:1022</c>,
/// <c>1066-1070</c>), which the process rarely survives.
/// </remarks>
internal sealed class ProbeNullVideoSource : GES.VideoSource, IManagedSubclass<ProbeNullVideoSource>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesNullVideoSource";

    /// <summary>The message the throwing mode raises.</summary>
    internal const string RefusalMessage = "The probe refuses to build a source.";

    private static readonly SubclassType Definition = DefineSubclass<ProbeNullVideoSource>(
        GTypeName,
        null,
        CreateSourceOverride);

    private static int _throws;

    private ProbeNullVideoSource(SubclassCtorArgs args)
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
    public static ProbeNullVideoSource CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override Gst.Element OnCreateSource()
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
