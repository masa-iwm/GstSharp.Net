using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The parts of the fabrication of a managed subclass wrapper that decide
/// before anything native is touched: what the arguments of an adopted instance
/// say about ownership, what the guards of the two entry points refuse, and how
/// the two class configuration facades relate.
/// </summary>
/// <remarks>
/// Everything that actually builds a wrapper needs a registered <c>GType</c>,
/// which needs an installed GStreamer; those are the integration tests. What is
/// pinned here is the decision table around them, which is where a mistake
/// would be silent rather than loud.
/// </remarks>
public class SubclassFabricationTests
{
    /// <summary>
    /// A handle that stands for a native object without being one. Nothing in
    /// the paths under test dereferences it.
    /// </summary>
    private const nint Sentinel = 0x1000;

    [Fact]
    public void ConstructionArgumentsHandOverTheReferenceOfObjectNew()
    {
        SubclassCtorArgs args = new(Sentinel);

        Assert.False(args.IsAdopted);
        Assert.Equal(Transfer.Full, args.Transfer);
    }

    [Fact]
    public void AdoptedArgumentsTransferNothing()
    {
        SubclassCtorArgs args = SubclassCtorArgs.Adopt(Sentinel);

        Assert.True(args.IsAdopted);

        // The instance belongs to whoever created it and may still be floating,
        // so the wrapper takes a reference of its own and sinks nothing.
        Assert.Equal(Transfer.None, args.Transfer);
    }

    [Fact]
    public void NothingIsFabricatedForANullHandle()
    {
        Assert.False(TypeRegistry.TryFabricate(nint.Zero, out Gst.GObject.Object? wrapper));
        Assert.Null(wrapper);
        Assert.Null(Gst.GObject.Object.TryGetOrFabricate(nint.Zero));
    }

    [Fact]
    public void AnUnregisteredInstanceIsNotFabricated()
    {
        // The table of the managed subclasses is asked before the type of the
        // instance is read, so a handle that stands for nothing is answered
        // without being dereferenced.
        Assert.False(TypeRegistry.TryFabricate(Sentinel, out Gst.GObject.Object? wrapper));
        Assert.Null(wrapper);
    }

    [Fact]
    public void ANullHandleCarriesNoDisposedMarker() =>
        Assert.False(Gst.GObject.Object.WasSubclassDisposed(nint.Zero));

    [Fact]
    public void AFactoryIsRequiredToRegisterASubclass() =>
        Assert.Throws<ArgumentNullException>(
            static () => TypeRegistry.RegisterSubclass(GType.Invalid, null!));

    [Fact]
    public void NothingIsBeingConstructedOnAFreshThread() =>
        Assert.False(SubclassRegistry.IsConstructing(GType.Invalid));

    [Fact]
    public void ConstructionPropertiesAreRequired() =>
        Assert.Throws<ArgumentNullException>(
            static () => SubclassRegistry.NewInstance(GType.Invalid, null!));

    [Fact]
    public void ConstructionNeedsAValidType() =>
        Assert.Throws<ArgumentException>(
            static () => SubclassRegistry.NewInstance(
                GType.Invalid,
                new Dictionary<string, object?> { ["name"] = "sink_0" }));

    [Fact]
    public void TheElementFacadeIsAClassConfigurationOfItsOwn()
    {
        // A class that is not an element is configured through the GObject
        // level facade, and the generated DefineSubclass of such a class asks
        // for it by its parameter type; the element one has to stay usable
        // wherever it was before, which is what deriving rather than replacing
        // buys.
        Assert.True(typeof(ClassConfig).IsSubclassOf(typeof(ObjectClassConfig)));
    }
}
