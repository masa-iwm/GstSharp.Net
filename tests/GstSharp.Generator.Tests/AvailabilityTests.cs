using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What the <c>version</c> attribute of a gir element does to the emitted
/// documentation: a member that needs a GStreamer newer than the supported
/// floor says so, and every other member stays silent.
/// </summary>
/// <remarks>
/// <para>
/// The floor is what makes the line worth reading. Every member the binding
/// emits is available in the oldest GStreamer it supports or in a newer one, so
/// a line on all of them would carry no information; a line on the few hundred
/// that arrived later is the warning that <c>gst_structure_is_writable</c> and
/// <c>gst_state_get_name</c> did not carry when this repository called them
/// against a library that did not have them yet.
/// </para>
/// <para>
/// The silent half of these tests is the half that pins the floor: without it
/// an emitter that wrote the line unconditionally would still pass.
/// </para>
/// </remarks>
public sealed class AvailabilityTests
{
    /// <summary>The documentation of the getter, in one paragraph.</summary>
    private const string OneParagraph = "<doc xml:space=\"preserve\">Reads the border.</doc>";

    /// <summary>The documentation of the getter, in two paragraphs.</summary>
    private const string TwoParagraphs =
        "<doc xml:space=\"preserve\">Reads the border.\n\nThe border is measured in pixels.</doc>";

    /// <summary>
    /// A member that arrived after the floor carries the line, as the only
    /// paragraph of a remarks block the gir did not ask for.
    /// </summary>
    [Fact]
    public void AMemberNewerThanTheFloorNamesItsVersion()
    {
        FixtureRun run = Fixture.Run(Transition(getter: "version=\"1.26\""));

        Assert.Contains(
            """
                /// <summary>Reads the border.</summary>
                /// <remarks>
                /// <para>Available since GStreamer 1.26.</para>
                /// </remarks>
            """,
            run.File("Transition.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A member of the floor version itself says nothing: it is available
    /// wherever the binding runs at all.
    /// </summary>
    [Fact]
    public void AMemberOfTheFloorStaysSilent()
    {
        FixtureRun run = Fixture.Run(Transition(getter: "version=\"1.24\""));

        Assert.DoesNotContain("Available since GStreamer", run.File("Transition.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The three component spelling of the floor is the floor: the gir writes
    /// <c>1.24.0</c> for a few members, and a comparison on the text alone
    /// would read it as something newer.
    /// </summary>
    [Fact]
    public void TheThreeComponentSpellingOfTheFloorStaysSilent()
    {
        FixtureRun run = Fixture.Run(Transition(getter: "version=\"1.24.0\""));

        Assert.DoesNotContain("Available since GStreamer", run.File("Transition.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A member older than the floor says nothing either, which is what the
    /// bulk of the emitted surface looks like.
    /// </summary>
    [Fact]
    public void AMemberOlderThanTheFloorStaysSilent()
    {
        FixtureRun run = Fixture.Run(Transition(getter: "version=\"1.20\""));

        Assert.DoesNotContain("Available since GStreamer", run.File("Transition.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A member the gir wrote no version for says nothing, which is what makes
    /// the line a statement rather than a decoration.
    /// </summary>
    [Fact]
    public void AMemberWithoutAVersionStaysSilent()
    {
        FixtureRun run = Fixture.Run(Transition(getter: string.Empty));

        Assert.DoesNotContain("Available since GStreamer", run.File("Transition.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The line joins the remarks the gir already wrote rather than opening a
    /// second block, and it comes last: it is the generator talking about the
    /// library, after the library has talked about itself.
    /// </summary>
    [Fact]
    public void TheLineIsAppendedToTheRemarksOfTheGir()
    {
        FixtureRun run = Fixture.Run(Transition(getter: "version=\"1.28\"", getterDoc: TwoParagraphs));

        Assert.Contains(
            """
                /// <summary>Reads the border.</summary>
                /// <remarks>
                /// <para>The border is measured in pixels.</para>
                /// <para>Available since GStreamer 1.28.</para>
                /// </remarks>
            """,
            run.File("Transition.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A property takes the newest version of its parts. It is a call of its
    /// accessors, so it is there once the gir property and both accessors are.
    /// </summary>
    [Fact]
    public void APropertyTakesTheNewestVersionOfItsParts()
    {
        FixtureRun run = Fixture.Run(Transition(
            getter: "version=\"1.26\"",
            setter: "version=\"1.28\"",
            property: "version=\"1.2\""));

        Assert.Contains(
            """
                /// <summary>The <c>border</c> property.</summary>
                /// <remarks>
                /// <para>Available since GStreamer 1.28.</para>
                /// </remarks>
                public int Border
            """,
            run.File("Transition.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A class with one read write property whose versions the caller chooses.
    /// It is the shape of <c>DeprecationTests</c>, for the same reason: the
    /// property is the one member built out of three gir elements.
    /// </summary>
    /// <param name="getter">The version attribute of the getter, if any.</param>
    /// <param name="getterDoc">The <c>doc</c> element of the getter.</param>
    /// <param name="setter">The version attribute of the setter, if any.</param>
    /// <param name="property">The version attribute of the property, if any.</param>
    /// <returns>The body of the fixture namespace.</returns>
    private static string Transition(
        string getter,
        string getterDoc = OneParagraph,
        string setter = "",
        string property = "") =>
        $"""
            <class name="Transition" c:type="GstTransition" parent="GObject.Object" glib:type-name="GstTransition" glib:get-type="gst_transition_get_type">
              <method name="get_border" c:identifier="gst_transition_get_border" {getter}>
                {getterDoc}
                <return-value transfer-ownership="none">
                  <type name="gint" c:type="gint"/>
                </return-value>
                <parameters>
                  <instance-parameter name="self" transfer-ownership="none">
                    <type name="Transition" c:type="GstTransition*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_border" c:identifier="gst_transition_set_border" {setter}>
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="self" transfer-ownership="none">
                    <type name="Transition" c:type="GstTransition*"/>
                  </instance-parameter>
                  <parameter name="border" transfer-ownership="none">
                    <type name="gint" c:type="gint"/>
                  </parameter>
                </parameters>
              </method>
              <property name="border" writable="1" transfer-ownership="none" getter="get_border" setter="set_border" default-value="0" {property}>
                <type name="gint" c:type="gint"/>
              </property>
            </class>
        """;
}
