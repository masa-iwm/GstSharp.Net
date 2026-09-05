using Gst;
using Gst.GObject;
using Xunit;
using Object = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The class level property listing: what a type declares, asked without an
/// instance of it.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class ClassPropertyListingTests
{
    /// <summary>
    /// A pad type answers its own properties although nothing ever creates a
    /// bare <c>GstPad</c> to ask.
    /// </summary>
    [Fact]
    public void APadTypeListsThePropertiesOfItsClass()
    {
        string[] names = NamesOf(Pad.GetGType());

        Assert.Contains("direction", names);
        Assert.Contains("template", names);
        Assert.Contains("caps", names);
    }

    /// <summary>
    /// The pad type of a request template is the one gst-inspect prints the
    /// pad properties of, and it carries more than <c>GstPad</c> does.
    /// </summary>
    [Fact]
    public void ThePadTypeOfARequestTemplateListsItsOwnProperties()
    {
        using Element multiqueue = ElementFactory.Make("multiqueue", "class-properties")
            ?? throw new InvalidOperationException("multiqueue is missing.");

        PadTemplate template = multiqueue.GetPadTemplate("src_%u")
            ?? throw new InvalidOperationException("multiqueue has no src_%u template.");

        GType padType = template.Gtype;
        Assert.True(padType.IsValid);
        Assert.NotEqual(Pad.GetGType(), padType.Value);

        string[] names = NamesOf(padType);
        string[] padNames = NamesOf(Pad.GetGType());

        Assert.Contains("current-level-buffers", names);
        Assert.DoesNotContain("current-level-buffers", padNames);

        // The derived type carries everything the base type does.
        foreach (string name in padNames)
        {
            Assert.Contains(name, names);
        }
    }

    /// <summary>A type that is not a class has no properties to list.</summary>
    [Fact]
    public void ATypeThatIsNotAClassIsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Object.ListProperties(new GType(Caps.GetGType())));

        Assert.Contains("does not derive from GObject", error.Message, StringComparison.Ordinal);
    }

    private static string[] NamesOf(nuint type) => NamesOf(new GType(type));

    private static string[] NamesOf(GType type)
    {
        ParamSpec[] properties = Object.ListProperties(type);

        try
        {
            string[] names = new string[properties.Length];
            for (int i = 0; i < properties.Length; i++)
            {
                names[i] = properties[i].Name;
            }

            return names;
        }
        finally
        {
            foreach (ParamSpec property in properties)
            {
                property.Dispose();
            }
        }
    }
}
