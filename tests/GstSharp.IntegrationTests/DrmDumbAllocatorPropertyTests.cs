using Gst.Allocators;
using Gst.GObject;
using Xunit;
using Object = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>drm-device-path</c> property of the DRM dumb allocator, which is the
/// one property of the whole corpus the gir spells as a <c>filename</c>. It is
/// asked of the class rather than of an instance: a machine without a DRM
/// device builds no allocator, while the class installs its properties
/// wherever the module is loaded.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class DrmDumbAllocatorPropertyTests
{
    /// <summary>
    /// The class carries the property under the name the generated member is
    /// derived from.
    /// </summary>
    [Fact]
    public void TheAllocatorClassInstallsTheDevicePathProperty()
    {
        ParamSpec[] properties = Object.ListProperties(new GType(DRMDumbAllocator.GetGType()));

        try
        {
            Assert.Contains(properties, spec => spec.Name == "drm-device-path");
        }
        finally
        {
            foreach (ParamSpec spec in properties)
            {
                spec.Dispose();
            }
        }
    }

    /// <summary>
    /// What holds the file name is a string specification, which is what says
    /// that the value backed property reads and writes it through the string
    /// accessors of the holder: the encoding of the bytes is the only thing a
    /// <c>filename</c> adds, and <c>GValue</c> knows nothing of it.
    /// </summary>
    [Fact]
    public void TheDevicePathIsAConstructOnlyStringSpecification()
    {
        using ParamSpec? found = FindDevicePath();
        ParamSpecString spec = Assert.IsType<ParamSpecString>(found);

        Assert.Equal("GParamString", spec.NativeType.Name);
        Assert.Equal(GType.String, spec.ValueType);
        Assert.Equal("GstDRMDumbAllocator", spec.OwnerType.Name);

        // Construct-only is what leaves the generated member read only, so the
        // flags of the specification are the reason the property has no setter.
        Assert.True(spec.Flags.HasFlag(ParamFlags.ConstructOnly));
        Assert.True(spec.Flags.HasFlag(ParamFlags.Readable));
    }

    /// <summary>Takes the specification of the property out of the listing.</summary>
    /// <returns>The specification, which the caller owns.</returns>
    private static ParamSpec? FindDevicePath()
    {
        ParamSpec[] properties = Object.ListProperties(new GType(DRMDumbAllocator.GetGType()));
        ParamSpec? found = null;

        foreach (ParamSpec spec in properties)
        {
            if (found is null && spec.Name == "drm-device-path")
            {
                found = spec;
                continue;
            }

            spec.Dispose();
        }

        return found;
    }
}
