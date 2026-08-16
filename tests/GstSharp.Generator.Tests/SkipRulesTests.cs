using GstSharp.Generator.GirParsing;
using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The rules that keep unbindable callables out of the emitters.
/// </summary>
public sealed class SkipRulesTests
{
    private static readonly SkipRules Subject = new(Overlays.Empty);

    [Fact]
    public void VarArgsConstructorsAreSkipped()
    {
        GirCallable callable = GirFixture.Callable("gst_caps_new_simple");

        Assert.True(callable.HasVarArgs);
        Assert.Equal(SkipReason.VarArgs, Subject.GetSkipReason(callable));
    }

    [Fact]
    public void ShadowedCallablesAreReplacedByTheShadowingVariant()
    {
        GirCallable shadowed = GirFixture.Callable("gst_bus_add_watch");
        GirCallable shadowing = GirFixture.Callable("gst_bus_add_watch_full");

        // gst_bus_add_watch takes a GDestroyNotify-free callback and is not
        // introspectable; the full variant is generated under the clean name.
        Assert.Equal(SkipReason.ShadowedBy, Subject.GetSkipReason(shadowed));
        Assert.Equal(SkipReason.None, Subject.GetSkipReason(shadowing));
        Assert.Equal("add_watch", SkipRules.EffectiveGirName(shadowing));
        Assert.Equal("add_watch", shadowed.Name);
    }

    [Fact]
    public void MovedToFunctionsAreSkipped()
    {
        // The namespace level gst_buffer_replace was moved onto the record; the
        // record keeps a second declaration with the same c:identifier.
        GirFunction moved = GirFixture.Namespace("Gst").Functions.Single(
            static function => string.Equals(function.Name, "buffer_replace", StringComparison.Ordinal));

        Assert.Equal("gst_buffer_replace", moved.CIdentifier);
        Assert.Equal("Buffer.replace", moved.MovedTo);
        Assert.Equal(SkipReason.MovedTo, Subject.GetSkipReason(moved));
    }

    [Fact]
    public void NonIntrospectableCallablesAreSkipped()
    {
        GirCallable callable = GirFixture.Callable("gst_debug_log_valist");

        Assert.False(callable.IsIntrospectable);
        Assert.Equal(SkipReason.NotIntrospectable, Subject.GetSkipReason(callable));
    }

    [Fact]
    public void VtableSlotCallbacksAreSkipped()
    {
        GirRecord elementClass = Assert.IsType<GirRecord>(GirFixture.Symbol("Gst.ElementClass").Declaration);
        GirField slot = elementClass.Fields.First(static field => field.Callback is not null);

        Assert.Equal(SkipReason.FieldSlotCallback, Subject.GetSkipReason(slot.Callback!));
    }

    [Fact]
    public void NamespaceLevelCallbacksAreKept()
    {
        GirCallback callback = Assert.IsType<GirCallback>(GirFixture.Symbol("Gst.PadProbeCallback").Declaration);

        Assert.False(callback.IsFieldSlot);
        Assert.Equal(SkipReason.None, Subject.GetSkipReason(callback));
    }

    [Fact]
    public void OverlaySkipListWins()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                """
                {
                  "$comment": "unknown keys must be tolerated",
                  "skip": [ "gst_element_link" ],
                  "rename": { "Gst.MessageType": "MessageKind" },
                  "annotationOverrides": { "gst_element_link#return": { "transfer": "none", "nullable": true } }
                }
                """);

            Overlays overlays = Overlays.Load(directory);
            SkipRules rules = new(overlays);

            Assert.Equal(SkipReason.OverlaySkip, rules.GetSkipReason(GirFixture.Callable("gst_element_link")));
            Assert.True(overlays.TryGetRename("Gst.MessageType", out string? renamed));
            Assert.Equal("MessageKind", renamed);

            AnnotationOverride? annotation = overlays.GetAnnotationOverride("gst_element_link#return");
            Assert.NotNull(annotation);
            Assert.Equal("none", annotation!.Transfer);
            Assert.True(annotation.Nullable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingOverlayFilesAreTreatedAsEmpty()
    {
        Overlays overlays = Overlays.Load(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

        Assert.Empty(overlays.SkippedIdentifiers);
        Assert.False(overlays.TryGetRename("Gst.MessageType", out _));
    }

    [Fact]
    public void CommittedOverlaysLoad()
    {
        // Every skip of the committed fixups is a symbol whose C contract the
        // gir does not describe, or one that hand written glue has taken over,
        // so the list is asserted whole: a symbol that is added or dropped is a
        // decision, not a detail.
        Assert.Equal(
            [
                "ges_deinit",
                "gst_adapter_map",
                "gst_adapter_take",
                "gst_adapter_unmap",
                "gst_audio_ring_buffer_commit",
                "gst_buffer_remove_meta",
                "gst_caps_features_add_static_str",
                "gst_caps_features_new_single_static_str",
                "gst_caps_new_static_str_empty_simple",
                "gst_id_str_set_static_str",
                "gst_id_str_set_static_str_with_len",
                "gst_meta_info_register",
                "gst_structure_new_static_str_empty",
                "gst_structure_set_name_static_str",
                "gst_type_find_peek",
            ],
            GirFixture.Overlays.SkippedIdentifiers.Order(StringComparer.Ordinal).ToArray());

        Assert.True(GirFixture.Overlays.TryGetReturnTypeOverride("gst_pipeline_new", out string? pipeline));
        Assert.Equal("Gst.Pipeline", pipeline);
        Assert.Null(GirFixture.Overlays.GetPlatformSupport("gst_macos_main"));
    }

    [Fact]
    public void CallablesWithoutACIdentifierAreSkipped()
    {
        GirNamespace ns = GirReader.ReadXml(
            """
            <repository xmlns="http://www.gtk.org/introspection/core/1.0" xmlns:c="http://www.gtk.org/introspection/c/1.0" xmlns:glib="http://www.gtk.org/introspection/glib/1.0" version="1.2">
              <namespace name="Test" version="1.0" c:identifier-prefixes="Test" c:symbol-prefixes="test">
                <function name="broken">
                  <return-value transfer-ownership="none"><type name="none" c:type="void"/></return-value>
                </function>
              </namespace>
            </repository>
            """,
            "fixture.gir").Namespaces[0];

        Assert.Equal(SkipReason.NoCIdentifier, Subject.GetSkipReason(ns.Functions[0]));
    }
}
