using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// A type registered through the two argument <see cref="ModuleTypeEntry"/>
/// constructor carries no borrowing factory, and the fallback that stands
/// behind <see cref="TypeRegistry.TryCreateBorrowedWrapper"/> is what a
/// dynamically connected signal handler is given for it.
/// </summary>
/// <remarks>
/// The arm itself is what is called here, through
/// <see cref="DynamicSignalClosure.ReadBoxed"/>: the borrowing factory is asked
/// first and answers nothing, and
/// <see cref="TypeRegistry.TryCreateMiniObjectWrapper"/> with
/// <see cref="Transfer.None"/> stands behind it. Without that second arm a
/// module compiled against an earlier version of this binding — or one outside
/// this repository, which cannot build a borrowing wrapper at all — would see
/// its mini objects arrive as a raw handle instead of as their wrapper.
/// </remarks>
public sealed class BorrowlessModuleEntryTests
{
    /// <summary>
    /// The type this test registers. Nothing native answers it: the registry
    /// reads the number the entry hands it and never asks GObject about it.
    /// </summary>
    private const nuint TypeId = 0x6d69_6e69;

    /// <summary>The handle the factory is given. It is never dereferenced.</summary>
    private const nint Instance = 0x1000;

    private static nuint GetTypeOfTheFixture() => TypeId;

    private static object CreateOwningWrapper(nint handle, Transfer transfer)
    {
        _ = transfer;
        return new FixtureMiniObject(handle);
    }

    [Fact]
    public unsafe void AnEntryWithoutABorrowingFactoryStillBuildsItsMiniObjectWrapper()
    {
        ModuleTypeEntry entry = new(&GetTypeOfTheFixture, &CreateOwningWrapper);
        Assert.True(entry.BorrowedFactory is null, "the two argument constructor leaves it unset.");

        TypeRegistry.RegisterModule(new NativeModule("GstBorrowlessTest", [entry]));

        GType type = new(TypeId);

        // The primary mechanism has nothing to call for this entry.
        Assert.False(TypeRegistry.TryCreateBorrowedWrapper(type, Instance, out object? lent));
        Assert.Null(lent);

        // The arm of the closure marshaller, which is what the handler is
        // given: the fallback behind the borrowing factory builds the wrapper
        // and enters it among the wrappers the emission disposes.
        List<IDisposable>? borrowed = null;
        object? wrapper = DynamicSignalClosure.ReadBoxed(type, Instance, ref borrowed);

        using FixtureMiniObject built = Assert.IsType<FixtureMiniObject>(wrapper);
        Assert.Equal(Instance, built.Handle);
        Assert.NotNull(borrowed);
        Assert.Same(built, Assert.Single(borrowed));
    }

    /// <summary>
    /// A mini object wrapper that touches nothing native: the base constructor
    /// is handed <see cref="Transfer.Full"/> so that it adopts the handle
    /// rather than referencing it, and the disposal is overridden so that the
    /// number standing in for an instance is never handed to
    /// <c>gst_mini_object_unref</c>.
    /// </summary>
    private sealed class FixtureMiniObject : MiniObject
    {
        internal FixtureMiniObject(nint handle)
            : base(handle, Transfer.Full) => GC.SuppressFinalize(this);

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) => _ = disposing;
    }
}
