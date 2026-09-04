using Gst;
using Gst.Base;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What a chain-up of <c>PushSrc.OnAlloc</c> answers. No class of the library
/// sets the <c>alloc</c> slot of <c>GstPushSrcClass</c>, so the chain-up always
/// takes the NULL slot branch, and that branch has to do what
/// <c>gst_push_src_alloc</c> does: reach
/// <c>GST_BASE_SRC_CLASS (parent_class)-&gt;alloc</c>, which is the pooled
/// allocation of <c>gst_base_src_default_alloc</c>.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class SubclassPushSrcAllocTests
{
    /// <summary>
    /// A push source that declares <c>alloc</c> and does nothing with it but
    /// chain up, which is what a subclass wanting the pooled allocation writes.
    /// </summary>
    private sealed class AllocSrc : PushSrc
    {
        internal const string GTypeName = "GstSharpTestAllocSrc";

        private static readonly PadTemplate SrcTemplate = NewSrcTemplate();

        private static readonly SubclassType Definition = DefineSubclass(
            GTypeName,
            ConfigureClass,
            AllocOverride);

        internal AllocSrc()
            : base(Definition.NewInstance())
        {
        }

        /// <summary>Allocates one buffer through the override and its chain-up.</summary>
        /// <param name="buffer">The buffer the base class allocated, if any.</param>
        /// <returns>What the base class answered.</returns>
        internal FlowReturn Allocate(out Gst.Buffer? buffer) => OnAlloc(out buffer);

        /// <inheritdoc/>
        protected override FlowReturn OnAlloc(out Gst.Buffer? buf) => ChainUpAlloc(out buf);

        private static void ConfigureClass(ClassConfig config)
        {
            config.SetMetadata(
                "GstSharp alloc source",
                "Source/Testing",
                "Chains up to the default allocation of GstBaseSrc",
                "GstSharp.Net integration tests");

            config.AddPadTemplate(SrcTemplate);
        }

        private static PadTemplate NewSrcTemplate()
        {
            using Caps caps = Caps.NewAny();

            return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
                ?? throw new InvalidOperationException("The source pad template could not be created.");
        }
    }

    /// <summary>
    /// The buffer is the size the source asks for: with no pool and no
    /// allocator negotiated, <c>gst_base_src_default_alloc</c> allocates
    /// <c>blocksize</c> bytes from the system allocator. Before the default was
    /// written this answered <c>NotSupported</c> and no buffer at all.
    /// </summary>
    [Fact]
    public void ChainingUpAllocAnswersABufferOfTheRequestedSize()
    {
        using AllocSrc source = new() { Blocksize = 1234 };

        FlowReturn result = source.Allocate(out Gst.Buffer? buffer);

        using (buffer)
        {
            Assert.Equal(FlowReturn.Ok, result);
            Assert.NotNull(buffer);
            Assert.Equal(1234UL, buffer.GetSize());
        }
    }
}
