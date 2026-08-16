using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What a deprecated gir symbol does to the emitted output: it is bound like
/// any other one, it carries an <c>[Obsolete]</c> attribute, and everything the
/// generator writes around it still compiles under warnings as errors.
/// </summary>
/// <remarks>
/// <para>
/// The two shapes pinned here are the ones a deprecation reaches from outside
/// the file of the deprecated symbol. Inside a deprecated type the compiler asks
/// no questions, because a use of an obsolete member inside an obsolete member
/// is not reported; the type table of the module and a property that delegates
/// to a deprecated accessor are the two places that sit outside.
/// </para>
/// <para>
/// The vendored girs happen to declare neither shape, so nothing in the
/// committed output would notice a regression here. The fixtures are the whole
/// statement of the feature.
/// </para>
/// </remarks>
public sealed class DeprecationTests
{
    /// <summary>The deprecation attributes of a gir element.</summary>
    private const string Since120 = "deprecated=\"1\" deprecated-version=\"1.20\"";

    /// <summary>The deprecation attributes of an element that went first.</summary>
    private const string Since118 = "deprecated=\"1\" deprecated-version=\"1.18\"";

    /// <summary>The deprecation text of an accessor.</summary>
    private const string AccessorDoc =
        "<doc-deprecated xml:space=\"preserve\">Use the child property instead.</doc-deprecated>";

    /// <summary>The deprecation text of the property itself.</summary>
    private const string PropertyDoc =
        "<doc-deprecated xml:space=\"preserve\">The property went away.</doc-deprecated>";

    /// <summary>
    /// The type table names a deprecated wrapper behind a pragma: the wrapper
    /// has to stay registered, so the file says once why it may name it.
    /// </summary>
    [Fact]
    public void TheModuleTableRegistersADeprecatedWrapperBehindAPragma()
    {
        FixtureRun run = Fixture.Run(
            """
                <class name="Legacy" c:type="GstLegacy" parent="GObject.Object" glib:type-name="GstLegacy" glib:get-type="gst_legacy_get_type" deprecated="1" deprecated-version="1.28">
                  <doc-deprecated xml:space="preserve">Use Modern instead.</doc-deprecated>
                </class>
                <class name="Modern" c:type="GstModern" parent="GObject.Object" glib:type-name="GstModern" glib:get-type="gst_modern_get_type">
                </class>
            """);

        Assert.Contains(
            "[Obsolete(\"Use Modern instead. (deprecated since 1.28)\")]\npublic unsafe partial class Legacy",
            run.File("Legacy.cs"),
            StringComparison.Ordinal);

        Assert.Contains(
            """
                // The module registers its deprecated wrappers too: the deprecation is a
                // message to the callers of a type, while the registry still has to map
                // the GType of every wrapper native code can hand back.
                #pragma warning disable CS0618
                /// <summary>Builds the type table of the module.</summary>
                /// <returns>One entry per generated wrapper that GObject knows a type for.</returns>
                internal static Gst.Interop.ModuleTypeEntry[] CreateEntries() =>
                [
                    new Gst.Interop.ModuleTypeEntry(&Gst.Legacy.GetGType, &Gst.Legacy.CreateWrapper),
                    new Gst.Interop.ModuleTypeEntry(&Gst.Modern.GetGType, &Gst.Modern.CreateWrapper),
                ];
                #pragma warning restore CS0618
            """,
            run.File("_Module.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A module without a deprecated wrapper gets no pragma at all: a pragma
    /// that suppresses nothing would be a comment justifying nothing.
    /// </summary>
    [Fact]
    public void TheModuleTableOfANonDeprecatedModuleCarriesNoPragma()
    {
        FixtureRun run = Fixture.Run(
            """
                <class name="Modern" c:type="GstModern" parent="GObject.Object" glib:type-name="GstModern" glib:get-type="gst_modern_get_type">
                </class>
            """);

        string source = run.File("_Module.cs");

        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Modern.GetGType, &Gst.Modern.CreateWrapper),",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("#pragma", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property whose getter is deprecated is deprecated itself: its body is a
    /// call of that getter, which would not compile without saying so.
    /// </summary>
    [Fact]
    public void APropertyInheritsTheDeprecationOfItsGetter()
    {
        FixtureRun run = Fixture.Run(Transition(
            property: string.Empty,
            propertyDoc: string.Empty,
            getter: Since120,
            getterDoc: AccessorDoc,
            setter: string.Empty,
            setterDoc: string.Empty));

        Assert.Contains(
            """
                /// <summary>The <c>border</c> property.</summary>
                [Obsolete("Use the child property instead. (deprecated since 1.20)")]
                public int Border
            """,
            run.File("Transition.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The setter counts as well: the <c>set</c> accessor calls it, so a
    /// property with a deprecated setter is obsolete as a whole.
    /// </summary>
    [Fact]
    public void APropertyInheritsTheDeprecationOfItsSetter()
    {
        FixtureRun run = Fixture.Run(Transition(
            property: string.Empty,
            propertyDoc: string.Empty,
            getter: string.Empty,
            getterDoc: string.Empty,
            setter: Since120,
            setterDoc: AccessorDoc));

        Assert.Contains(
            """
                /// <summary>The <c>border</c> property.</summary>
                [Obsolete("Use the child property instead. (deprecated since 1.20)")]
                public int Border
            """,
            run.File("Transition.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A property that is deprecated together with its accessors gets exactly
    /// one attribute, and it is the one the gir wrote on the property: a second
    /// <c>[Obsolete]</c> attribute is an error of its own.
    /// </summary>
    [Fact]
    public void APropertyDeprecatedWithItsAccessorsGetsOneAttribute()
    {
        FixtureRun run = Fixture.Run(Transition(
            property: Since118,
            propertyDoc: PropertyDoc,
            getter: Since120,
            getterDoc: AccessorDoc,
            setter: Since120,
            setterDoc: AccessorDoc));

        Assert.Contains(
            """
                /// <summary>The <c>border</c> property.</summary>
                [Obsolete("The property went away. (deprecated since 1.18)")]
                public int Border
            """,
            run.File("Transition.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A property of current accessors carries no attribute, which is what says
    /// that the tests above pin a propagation and not a constant.
    /// </summary>
    [Fact]
    public void APropertyOfCurrentAccessorsIsNotDeprecated()
    {
        FixtureRun run = Fixture.Run(Transition(
            property: string.Empty,
            propertyDoc: string.Empty,
            getter: string.Empty,
            getterDoc: string.Empty,
            setter: string.Empty,
            setterDoc: string.Empty));

        Assert.Contains(
            """
                /// <summary>The <c>border</c> property.</summary>
                public int Border
            """,
            run.File("Transition.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A class with one read write property, whose deprecations the caller
    /// chooses. It is modelled on <c>GESVideoTransition:border</c>, the property
    /// whose getter made the propagation necessary.
    /// </summary>
    /// <param name="property">The deprecation attributes of the property.</param>
    /// <param name="propertyDoc">The <c>doc-deprecated</c> element of the property.</param>
    /// <param name="getter">The deprecation attributes of the getter.</param>
    /// <param name="getterDoc">The <c>doc-deprecated</c> element of the getter.</param>
    /// <param name="setter">The deprecation attributes of the setter.</param>
    /// <param name="setterDoc">The <c>doc-deprecated</c> element of the setter.</param>
    /// <returns>The body of the fixture namespace.</returns>
    private static string Transition(
        string property,
        string propertyDoc,
        string getter,
        string getterDoc,
        string setter,
        string setterDoc) =>
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
                {setterDoc}
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
                {propertyDoc}
                <type name="gint" c:type="gint"/>
              </property>
            </class>
        """;
}
