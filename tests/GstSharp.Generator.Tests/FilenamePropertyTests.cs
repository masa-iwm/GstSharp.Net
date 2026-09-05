using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>filename</c> properties. A file name crosses the GObject property
/// system as the string it is - <c>G_TYPE_STRING</c> holds it either way - so
/// it is planned through the very accessors a <c>utf8</c> property uses, and
/// only the encoding of the bytes tells the two apart.
/// </summary>
public sealed class FilenamePropertyTests
{
    /// <summary>
    /// One class carrying a file name in the two shapes a property has: the
    /// writable one and the construct-only one.
    /// </summary>
    private const string Body =
        """
            <class name="Store" c:type="GstStore" parent="GObject.Object" glib:type-name="GstStore" glib:get-type="gst_store_get_type">
              <property name="index-path" writable="1" transfer-ownership="none">
                <doc xml:space="preserve">the file the index is kept in</doc>
                <type name="filename" c:type="gchar*"/>
              </property>
              <property name="device-path" writable="1" construct-only="1" transfer-ownership="none">
                <type name="filename" c:type="gchar*"/>
              </property>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A writable file name is the same property a writable string is, down to
    /// the nullability: the holder answers the null pointer as
    /// <see langword="null"/> whichever of the two the specification declares.
    /// </summary>
    [Fact]
    public void AFilenamePropertyIsPlannedLikeAString()
    {
        Assert.Equal(
            """
            public string? IndexPath
            {
                get
                {
                    using Gst.GObject.Value holder = GetProperty("index-path");
                    return holder.GetString();
                }

                set
                {
                    using Gst.GObject.Value holder = NewPropertyValue("index-path");
                    holder.SetString(value);
                    SetPropertyValue("index-path", in holder);
                }
            }
            """,
            Run.Member("Store.cs", "public string? IndexPath"),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Construct-only leaves a file name read only, exactly as it leaves every
    /// other value backed property: what refuses the write is the property
    /// system rather than the type of the value.
    /// </summary>
    [Fact]
    public void AConstructOnlyFilenamePropertyIsGetOnly()
    {
        Assert.Equal(
            """
            public string? DevicePath
            {
                get
                {
                    using Gst.GObject.Value holder = GetProperty("device-path");
                    return holder.GetString();
                }
            }
            """,
            Run.Member("Store.cs", "public string? DevicePath"),
            StringComparer.Ordinal);
    }
}
