using GstSharp.Generator.GirParsing;
using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Symbol table, include closure and alias resolution.
/// </summary>
public sealed class RepositoryTests
{
    [Fact]
    public void QualifiedReferencesResolveAcrossNamespaces()
    {
        GirSymbol symbol = GirFixture.Symbol("GObject.Object");

        Assert.Equal("GObject", symbol.Namespace.Name);
        Assert.Equal(GirSymbolKind.Class, symbol.Kind);
    }

    [Fact]
    public void GstAppResolvesGObjectThroughTheIncludeClosure()
    {
        Repository repository = GirFixture.Repository;
        GirNamespace app = GirFixture.Namespace("GstApp");

        // GstApp includes Gst and GstBase only; GObject and GLib are reached
        // through the closure of those includes.
        GirRepository file = repository.Files.Single(
            f => f.Namespaces.Any(ns => string.Equals(ns.Name, "GstApp", StringComparison.Ordinal)));
        Assert.Equal(["Gst", "GstBase"], file.Includes.Select(static include => include.Name).ToArray());

        string[] closure = repository.IncludeClosure(app).Select(static ns => ns.Name).ToArray();
        Assert.Contains("GObject", closure);
        Assert.Contains("GLib", closure);

        GirSymbol? gobject = repository.Resolve("GObject.Object", app);
        Assert.NotNull(gobject);
        Assert.Equal("GObject.Object", gobject!.QualifiedName);
    }

    [Fact]
    public void UnqualifiedReferencesPreferTheDeclaringNamespace()
    {
        Repository repository = GirFixture.Repository;
        GirNamespace gst = GirFixture.Namespace("Gst");
        GirNamespace app = GirFixture.Namespace("GstApp");

        Assert.Equal("Gst.Buffer", repository.Resolve("Buffer", gst)?.QualifiedName);

        // GstApp declares no Buffer of its own, so the include closure supplies it.
        Assert.Equal("Gst.Buffer", repository.Resolve("Buffer", app)?.QualifiedName);
    }

    [Fact]
    public void ReferencesResolveEvenWhenTheIncludeListIsNotClosed()
    {
        // The gir include lists cannot be trusted, so resolution falls back to
        // every loaded namespace.
        GirRepository lonely = GirReader.ReadXml(
            """
            <repository xmlns="http://www.gtk.org/introspection/core/1.0" xmlns:c="http://www.gtk.org/introspection/c/1.0" xmlns:glib="http://www.gtk.org/introspection/glib/1.0" version="1.2">
              <namespace name="Lonely" version="1.0" c:identifier-prefixes="Lonely" c:symbol-prefixes="lonely">
                <record name="Holder" c:type="LonelyHolder">
                  <field name="buffer"><type name="Buffer" c:type="GstBuffer*"/></field>
                </record>
              </namespace>
            </repository>
            """,
            "lonely.gir");

        List<GirRepository> files = [.. GirFixture.Repository.Files, lonely];
        Repository repository = Repository.FromRepositories(files);
        GirNamespace ns = repository.FindNamespace("Lonely")!;

        Assert.Empty(repository.IncludeClosure(ns));
        Assert.Equal("Gst.Buffer", repository.Resolve("Buffer", ns)?.QualifiedName);
    }

    [Fact]
    public void ClockTimeAliasResolvesToGuint64()
    {
        Repository repository = GirFixture.Repository;
        GirNamespace gst = GirFixture.Namespace("Gst");

        GirSymbol alias = GirFixture.Symbol("Gst.ClockTime");
        Assert.Equal(GirSymbolKind.Alias, alias.Kind);
        Assert.Equal("guint64", Assert.IsType<GirAlias>(alias.Declaration).Target.Name);

        Assert.Equal("guint64", repository.ResolveAliasedName("ClockTime", gst));
        Assert.Equal("gpointer", repository.ResolveAliasedName("ClockID", gst));
    }

    [Fact]
    public void PrimitiveNamesAreNotSymbols()
    {
        Assert.True(Repository.IsPrimitive("guint64"));
        Assert.True(Repository.IsPrimitive("utf8"));
        Assert.False(Repository.IsPrimitive("Gst.Buffer"));
        Assert.Null(GirFixture.Repository.Resolve("guint64", GirFixture.Namespace("Gst")));
    }
}
