using GES;
using Gst;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The six callables that speak <c>GDate</c>, against the installed libraries:
/// three on <see cref="Gst.Structure"/> and <see cref="Gst.TagList"/>, three on
/// the GES meta container.
/// </summary>
/// <remarks>
/// <para>
/// No <c>GDate</c> crosses the boundary. A date goes in as a
/// <see cref="DateOnly"/> and comes back as a <c>DateOnly?</c>, and what is
/// measured here is that the conversion is faithful in both directions and that
/// the null half of the answer means what the documentation says it means.
/// </para>
/// <para>
/// Every entry point called below is GStreamer 1.0 or 1.2 and every GES one is
/// 1.0, so the file runs on the 1.24 floor of the Linux leg and needs no
/// availability gate.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GDateProjectionTests
{
    /// <summary>
    /// A date field of a structure, written by the serialisation of GStreamer
    /// itself, is read back as the day it spells.
    /// </summary>
    [Fact]
    public void ADateFieldOfAStructureIsReadAsADateOnly()
    {
        using Structure? structure = Structure.NewFromString("s, d=(date)2024-05-06");
        Assert.NotNull(structure);

        Assert.True(structure.GetDate("d", out DateOnly? value));
        Assert.Equal(new DateOnly(2024, 5, 6), value);
    }

    /// <summary>
    /// A field that is not there is a false answer and no date, and so is a
    /// field of another type.
    /// </summary>
    [Fact]
    public void AMissingDateFieldIsFalseAndNull()
    {
        using Structure? structure = Structure.NewFromString("s, d=(date)2024-05-06, n=(int)7");
        Assert.NotNull(structure);

        Assert.False(structure.GetDate("absent", out DateOnly? missing));
        Assert.Null(missing);

        Assert.False(structure.GetDate("n", out DateOnly? wrongType));
        Assert.Null(wrongType);
    }

    /// <summary>
    /// A generic structure may hold a date field whose value is <c>NULL</c>,
    /// and reading it is a true answer with no date. That is the case the
    /// nullable projection exists for.
    /// </summary>
    /// <remarks>
    /// <c>gst_structure_validate_field_value</c> refuses a NULL <c>GDate</c> in
    /// a tag list and allows one in every other structure, which is why the
    /// same shape cannot be built on <see cref="Gst.TagList"/>. The type is
    /// taken off a field that was parsed rather than looked up by name, so the
    /// test does not depend on when <c>g_date_get_type</c> was first called.
    /// </remarks>
    [Fact]
    public void ANullDateInAStructureIsTrueAndNull()
    {
        using Structure? structure = Structure.NewFromString("s, d=(date)2024-05-06");
        Assert.NotNull(structure);

        GType dateType = structure.GetFieldType("d");
        using (Value empty = Value.New(dateType))
        {
            // A freshly initialised boxed value holds the null pointer.
            Assert.Equal(nint.Zero, empty.GetBoxed());
            structure.SetValue("d", in empty);
        }

        Assert.True(structure.GetDate("d", out DateOnly? value));
        Assert.Null(value);
    }

    /// <summary>
    /// The date of a tag list is read both by tag and by index, and both agree.
    /// </summary>
    [Fact]
    public void TheDateOfATagListIsReadByTagAndByIndex()
    {
        using TagList? tags = TagList.NewFromString("taglist, date=(date)2020-01-02");
        Assert.NotNull(tags);

        Assert.True(tags.GetDate("date", out DateOnly? first));
        Assert.Equal(new DateOnly(2020, 1, 2), first);

        Assert.True(tags.GetDateIndex("date", 0, out DateOnly? indexed));
        Assert.Equal(first, indexed);

        Assert.False(tags.GetDateIndex("date", 1, out DateOnly? beyond));
        Assert.Null(beyond);

        Assert.False(tags.GetDate("title", out DateOnly? absent));
        Assert.Null(absent);
    }

    /// <summary>
    /// A date set on a meta container comes back as the day it was set to, and
    /// a container that never had one answers false with no date.
    /// </summary>
    [Fact]
    public void ADateSetOnAMetaContainerRoundTrips()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();

        Assert.False(timeline.GetDate("shot-on", out DateOnly? before));
        Assert.Null(before);

        Assert.True(timeline.SetDate("shot-on", new DateOnly(1999, 12, 31)));
        Assert.True(timeline.GetDate("shot-on", out DateOnly? after));
        Assert.Equal(new DateOnly(1999, 12, 31), after);
    }

    /// <summary>
    /// Registering a field as a date sets it and pins its type: a later date is
    /// accepted and a value of another type is refused.
    /// </summary>
    [Fact]
    public void ARegisteredDateFieldKeepsItsType()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();

        Assert.True(timeline.RegisterMetaDate(
            MetaFlag.Readwrite, "released", new DateOnly(2001, 2, 3)));
        Assert.True(timeline.GetDate("released", out DateOnly? registered));
        Assert.Equal(new DateOnly(2001, 2, 3), registered);

        Assert.True(timeline.SetDate("released", new DateOnly(2002, 3, 4)));
        Assert.True(timeline.GetDate("released", out DateOnly? updated));
        Assert.Equal(new DateOnly(2002, 3, 4), updated);

        // The field only holds dates now, so a string is refused and the date
        // that is there survives.
        Assert.False(timeline.SetString("released", "not a date"));
        Assert.True(timeline.GetDate("released", out DateOnly? kept));
        Assert.Equal(new DateOnly(2002, 3, 4), kept);
    }
}
