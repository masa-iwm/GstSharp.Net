using GstSharp.Generator.Emit;
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
        // decision, not a detail. The fifteen consuming calls that shipped in
        // 1.28.2 as hand written members joined the list when the generator
        // learned the consuming argument kind, so that the hand written surface
        // stays canonical; gst_allocator_free joined it for the opposite
        // reason, because the consuming recipe would free a memory block the
        // wrapper still references. The toc append pair and
        // gst_buffer_pool_release_buffer joined for a third: the callee keys
        // on the writability of its consumed argument, which the reference the
        // recipe mints makes fail on every call - the toc entry is never
        // appended and the released buffer is never requeued. The GValue
        // carrying calls joined when the planner learned the GValue
        // projection, for the same hand-written-stays-canonical reason as the
        // consuming fifteen, plus the three *_value_static_str entries of the
        // static string family and gst_iterator_next, whose out value would
        // bind beside the typed Iterator.Items&lt;T&gt;. Eighteen more joined
        // with the caller allocated out parameters: six whose storage is a
        // scope, a span or a range check and is hand written for it, and
        // twelve whose whole job is to initialise a record the binding already
        // allocates through a _new or _new_from_caps sibling. The last two
        // joined with the holders of the fundamental value containers:
        // gst_value_unique_list_prepend_value can only raise a critical in
        // 1.28, and gst_flagset_register is the one function of a fundamental
        // that the containers were not designed for. Twelve joined with the
        // callback scopes: four whose managed state has a lifetime no emitted
        // shape expresses and that are hand written for it, three whose
        // lifetime is not derivable at all, and five that are skipped on
        // value rather than for a missing mechanism. Five joined with the
        // metadata attachment cluster: gst_buffer_foreach_meta, whose GstMeta**
        // is a keep, remove or stop decision rather than an inout parameter,
        // gst_meta_serialize_simple, whose GByteArray sink the hand written
        // Meta.Serialize owns, gst_buffer_add_audio_meta, whose offsets array
        // is sized by a field of another argument, and the
        // gst_meta_api_type_aggregate_params pair, whose GstStructure** the gir
        // declares as a GstStructure* and which no direction override can
        // correct on a boxed record. Five more joined with the list arguments:
        // three *_list_free calls whose whole body is the release of a list
        // managed code never holds, gst_iterator_new_list, which keeps the
        // address of the caller's own list variable, and
        // gst_event_new_select_streams, which is hand bound and is the one call
        // of the family that refuses an empty list. One joined on a leak the
        // generated shape cannot see: gst_clock_id_wait_async only takes the
        // destroy notification of its callback over once it has written it onto
        // the entry, so the hand written Gst.Clock.IdWaitAsync releases the
        // state on the refusals in front of that and leaves it with the entry on
        // the ones that come back from the clock, and gst_meta_register_custom
        // joined it for the same reason: the C function only takes the state of
        // its transform function over once the registration is accepted, so the
        // hand written Gst.Meta.RegisterCustom releases it before it reports a
        // refusal. gst_caps_fixate joined for a fault of its own: it is the one
        // conversion of the self consuming family that does not consume what it
        // is given on every path, because it refuses ANY caps before the
        // reference reaches the conversion, so the hand written Gst.Caps.Fixate
        // makes that check before it mints anything. The element factory pair is
        // the last: gst_element_factory_create_with_properties and
        // gst_element_factory_make_with_properties take the names and the values
        // of the construction properties as parallel arrays of which the second
        // has a bare GValue as its element type, and the type each value has to
        // hold is the one the property beside it declares, which only the class
        // of a loaded factory can answer, so
        // Gst.ElementFactory.MakeWithProperties and .CreateWithProperties ask it
        // and build the two arrays by hand.
        // The transcoder pair is the newest: gst_transcoder_message_parse_error
        // and gst_transcoder_message_parse_warning read a details field that
        // the transcoder does not attach to the errors it raises itself, and
        // the macro they read their fields through calls g_error() on a miss,
        // which aborts the process. Both are hand written in
        // Gst.Transcoder.TranscoderMessageExtensions instead, where a message
        // with no details answers null.
        // The play group is the newest and is thirteen entries. Three of them
        // are the first properties the list carries.
        // GstPlay.PlayVideoOverlayVideoRenderer:video-sink has a C setter that
        // assigns a ref_sink over the field without releasing the sink that was
        // there, so the sink is a construction argument and the read only half
        // of the property is hand written. GstPlay.Play:video-renderer is
        // declared readable and has no get case in gst_play_get_property, so a
        // read is a g_warning and nothing else. GstPlay.PlaySignalAdapter:play
        // reads a field the adapter never referenced, which dangles once the
        // play is disposed, and it goes with the getter beside it. The other
        // ten are callables: the three
        // namespace level twins of gst_play_media_info_get_*_streams, which
        // put the same three lists on the surface a second time as statics of
        // Play; gst_play_set_config, whose C half only honours the ownership
        // it documents on success and leaks the structure it was handed when
        // the play is not stopped; the two missing plugin parses, whose C half
        // compares an uninitialised message kind when it is handed a message
        // of another bus; and the three signal adapter factories, which store
        // the play without referencing it, so the wrapper is what has to keep
        // it alive. All nine are hand written in src/GstSharp.Net.Play/Custom.
        // The two type entries at the head are callback types whose delegate is
        // written by hand: a hand bound consumer keeps its callback type
        // generated, and these two are the ones whose hand written declaration
        // stays the source of truth, so the emitter is told not to declare them
        // a second time.
        // The RTP group is the newest and is six entries. The eighteen
        // gst_rtcp_packet_xr_get_* readers of an XR report block were the rest
        // of it until the direction correction learned to reach a pointer to a
        // scalar, which is the shape their fifty one out parameters have: they
        // are generated now, with the direction the gir omits stated in the
        // overlays. The six that stay have a shape the generator
        // binds and binds wrongly: gst_rtcp_packet_app_get_name and
        // gst_rtcp_packet_app_set_name read and write four bytes that are not a
        // C string, gst_buffer_add_rtp_source_meta and
        // gst_rtp_source_meta_set_ssrc take a nullable guint32* the gir spells
        // as a value, and gst_rtp_hdrext_set_ntp_56 and
        // gst_rtp_hdrext_set_ntp_64 write into a block the gir spells as a bare
        // gpointer beside its size, which no array override may turn into an
        // array. All six are hand written in src/GstSharp.Net.Rtp/Custom
        // beside the two guint8* returns the planner already refuses.
        // The RTSP server group is six entries. gst_rtsp_client_set_send_func
        // and gst_rtsp_client_set_send_messages_func are unreachable on every
        // path that exists: the attach of a client installs the send functions
        // of the server over whatever was there, and the client-connected
        // signal that would hand a caller the client fires before that attach,
        // so the first call always reaches its g_assert and the second is
        // overwritten a moment later. gst_rtsp_token_writable_structure hands
        // the structure of a token out to be written through, which a value
        // projection cannot carry; the typed setters are the writing path.
        // gst_rtsp_thread_pool_get_thread and gst_rtsp_thread_new mint a
        // GstRTSPThread whose loop is only ever stopped by
        // gst_rtsp_thread_stop, the release the LifetimePrimitive rule keeps
        // out, so a wrapper that only unrefs on Dispose leaks one OS thread per
        // call. gst_rtsp_mount_points_add_factory consumes the factory it is
        // handed, and the generated shape would dispose the wrapper whose
        // signal handlers the caller has just attached; it is hand written in
        // src/GstSharp.Net.RtspServer/Custom.
        Assert.Equal(
            [
                "Gst.BusSyncHandler",
                "Gst.MetaClearFunction",
                "Gst.MetaDeserializeFunction",
                "Gst.MetaFreeFunction",
                "Gst.MetaInitFunction",
                "Gst.MetaSerializeFunction",
                "Gst.MetaTransformFunction",
                "Gst.ParamSpecArray",
                "Gst.ParamSpecFraction",
                "GstBase.BitReader",
                "GstBase.BitWriter",
                "GstBase.ByteReader",
                "GstBase.ByteWriter",
                "GstPbutils.InstallPluginsResultFunc",
                "GstPlay.Play:video-renderer",
                "GstPlay.PlaySignalAdapter:play",
                "GstPlay.PlayVideoOverlayVideoRenderer:video-sink",
                "GstRtsp.RTSPWatch",
                "GstRtsp.RTSPWatchFuncs",
                "GstVideo.VideoGLTextureUpload",
                "ges_deinit",
                "ges_timeline_element_get_child_property",
                "ges_timeline_element_set_child_property",
                "ges_track_element_lookup_child",
                "gst_adapter_map",
                "gst_adapter_take",
                "gst_adapter_unmap",
                "gst_allocator_free",
                "gst_app_sink_set_simple_callbacks",
                "gst_app_src_push_buffer",
                "gst_app_src_set_simple_callbacks",
                "gst_audio_base_sink_set_custom_slaving_callback",
                "gst_audio_buffer_map",
                "gst_audio_buffer_unmap",
                "gst_audio_info_from_caps",
                "gst_audio_info_init",
                "gst_audio_ring_buffer_commit",
                "gst_audio_ring_buffer_read",
                "gst_buffer_add_audio_meta",
                "gst_buffer_add_rtp_source_meta",
                "gst_buffer_add_video_gl_texture_upload_meta",
                "gst_buffer_extract",
                "gst_buffer_foreach_meta",
                "gst_buffer_new_wrapped_full",
                "gst_buffer_pool_release_buffer",
                "gst_buffer_pool_set_config",
                "gst_buffer_remove_meta",
                "gst_bus_set_sync_handler",
                "gst_caps_features_add_static_str",
                "gst_caps_features_new_single_static_str",
                "gst_caps_fixate",
                "gst_caps_new_static_str_empty_simple",
                "gst_caps_set_value_static_str",
                "gst_clock_id_wait_async",
                "gst_collect_pads_add_pad",
                "gst_debug_remove_log_function",
                "gst_discoverer_stream_info_list_free",
                "gst_dsd_info_from_caps",
                "gst_dsd_info_init",
                "gst_element_factory_create_with_properties",
                "gst_element_factory_make_with_properties",
                "gst_element_post_message",
                "gst_element_send_event",
                "gst_encoding_container_profile_add_profile",
                "gst_event_new_custom",
                "gst_event_new_select_streams",
                "gst_flagset_register",
                "gst_id_str_set_static_str",
                "gst_id_str_set_static_str_with_len",
                "gst_install_plugins_async",
                "gst_iterator_new_list",
                "gst_iterator_next",
                "gst_memory_new_wrapped",
                "gst_message_new_application",
                "gst_message_new_custom",
                "gst_meta_api_type_aggregate_params",
                "gst_meta_api_type_set_params_aggregator",
                "gst_meta_info_register",
                "gst_meta_register_custom",
                "gst_meta_serialize_simple",
                "gst_mini_object_set_qdata",
                "gst_pad_push_event",
                "gst_pad_send_event",
                "gst_pad_set_event_full_function_full",
                "gst_play_get_audio_streams",
                "gst_play_get_subtitle_streams",
                "gst_play_get_video_streams",
                "gst_play_message_parse_error_missing_plugin",
                "gst_play_message_parse_warning_missing_plugin",
                "gst_play_set_config",
                "gst_play_signal_adapter_get_play",
                "gst_play_signal_adapter_new",
                "gst_play_signal_adapter_new_sync_emit",
                "gst_play_signal_adapter_new_with_main_context",
                "gst_plugin_feature_list_free",
                "gst_plugin_list_free",
                "gst_plugin_register_static",
                "gst_promise_reply",
                "gst_query_new_custom",
                "gst_query_parse_nth_allocation_param",
                "gst_rtcp_packet_app_get_name",
                "gst_rtcp_packet_app_set_name",
                "gst_rtp_hdrext_set_ntp_56",
                "gst_rtp_hdrext_set_ntp_64",
                "gst_rtp_source_meta_set_ssrc",
                "gst_rtsp_auth_credentials_free",
                "gst_rtsp_client_set_send_func",
                "gst_rtsp_client_set_send_messages_func",
                "gst_rtsp_mount_points_add_factory",
                "gst_rtsp_range_free",
                "gst_rtsp_range_parse",
                "gst_rtsp_thread_new",
                "gst_rtsp_thread_pool_get_thread",
                "gst_rtsp_token_writable_structure",
                "gst_rtsp_transport_init",
                "gst_rtsp_transport_parse",
                "gst_sdp_media_init",
                "gst_sdp_message_init",
                "gst_structure_get_value",
                "gst_structure_new_static_str_empty",
                "gst_structure_set_name_static_str",
                "gst_structure_set_value",
                "gst_structure_set_value_static_str",
                "gst_structure_take_value_static_str",
                "gst_tag_list_add_value",
                "gst_tag_list_copy_value",
                "gst_tag_list_get_value_index",
                "gst_task_pool_push",
                "gst_toc_append_entry",
                "gst_toc_entry_append_sub_entry",
                "gst_tracing_register_hook",
                "gst_transcoder_message_parse_error",
                "gst_transcoder_message_parse_warning",
                "gst_type_find_peek",
                "gst_util_array_binary_search",
                "gst_value_compare",
                "gst_value_serialize",
                "gst_value_unique_list_prepend_value",
                "gst_video_blend_scale_linear_RGBA",
                "gst_video_codec_frame_set_user_data",
                "gst_video_frame_map",
                "gst_video_frame_map_id",
                "gst_video_frame_unmap",
                "gst_video_gl_texture_upload_meta_upload",
                "gst_video_info_dma_drm_from_caps",
                "gst_video_info_dma_drm_init",
                "gst_video_info_from_caps",
                "gst_video_info_init",
                "gst_webrtc_session_description_new",
            ],
            GirFixture.Overlays.SkippedIdentifiers.Order(StringComparer.Ordinal).ToArray());

        // The hand bound ledger carries the same weight as the skip list, from
        // the other side: every entry states that the managed surface of the
        // symbol exists, hand written, so the skip report may stop counting it
        // among the bindings that are missing. An entry that is added or
        // dropped moves the measured gap, which is a decision rather than a
        // detail, and an entry the generator never sees skipped is reported as
        // GEN0023 instead of quietly overstating what is bound.
        // The six newest are the RTP module's: the two guint8* returns the
        // planner refuses on their shape, the two four byte APP name calls and
        // the two nullable guint32* ssrc calls, all six written by hand in
        // src/GstSharp.Net.Rtp/Custom.
        // The newest entry is the RTSP server's gst_rtsp_mount_points_add_factory,
        // which consumes the media factory it is handed and would take the
        // wrapper the caller keeps its signal handlers on down with it, so
        // src/GstSharp.Net.RtspServer/Custom takes a reference of its own and
        // hands that one over.
        // ges_asset_extract joins them as the one call a managed GES subclass
        // cannot do without: the planner refuses it for its GError, and
        // GES.Asset.Extract<T> in src/GstSharp.Net.GES/Custom is what a clip
        // builds its children with.
        // gst_param_spec_array and gst_param_spec_fraction are the two
        // namespace scope factories of the parameter specifications that
        // src/GstSharp.Net/Custom writes by hand rather than generating: the
        // ledger had them under the catch all reason, which read as a gap
        // where there is a binding.
        // gst_mini_object_get_qdata and gst_mini_object_steal_qdata are read
        // and stolen by MiniObject.GetQData and MiniObject.StealQData in
        // src/GstSharp.Net/Custom; the setter beside them stays skipped,
        // because its destroy notification belongs to whoever stored the
        // pointer and that is native code.
        // Gst.Bus:enable-async is the one property of the list. It is
        // construct only and write only, so no accessor of a wrapper can carry
        // it; Bus.New(bool) in src/GstSharp.Net/Custom gives it its value
        // while the bus is being built, which is the only moment there is.
        // gst_rtsp_message_append_headers writes into a GString, which is a
        // buffer of GLib the caller has to allocate and free and which no
        // managed signature should carry; RTSPMessage.AppendHeaders in
        // src/GstSharp.Net.Rtsp/Custom owns one for the length of the call and
        // appends what it holds to a StringBuilder.
        Assert.Equal(
            [
                "Gst.Bus:enable-async",
                "GstWebRTC.WebRTCDataChannel::on-message-data",
                "ges_asset_extract",
                "ges_asset_request_async",
                "ges_asset_request_finish",
                "ges_timeline_element_get_child_property",
                "ges_timeline_element_set_child_property",
                "ges_uri_clip_asset_finish",
                "ges_uri_clip_asset_new",
                "gst_adapter_map",
                "gst_adapter_unmap",
                "gst_app_sink_set_simple_callbacks",
                "gst_app_sink_simple_callbacks_ref",
                "gst_app_src_push_buffer",
                "gst_app_src_set_simple_callbacks",
                "gst_app_src_simple_callbacks_ref",
                "gst_audio_base_sink_set_custom_slaving_callback",
                "gst_audio_buffer_map",
                "gst_audio_buffer_unmap",
                "gst_audio_ring_buffer_read",
                "gst_buffer_add_audio_downmix_meta",
                "gst_buffer_add_audio_meta",
                "gst_buffer_add_rtp_source_meta",
                "gst_buffer_add_video_gl_texture_upload_meta",
                "gst_buffer_copy",
                "gst_buffer_extract",
                "gst_buffer_foreach_meta",
                "gst_buffer_iterate_meta",
                "gst_buffer_iterate_meta_filtered",
                "gst_buffer_new_wrapped_full",
                "gst_buffer_pool_set_config",
                "gst_buffer_remove_meta",
                "gst_bus_set_sync_handler",
                "gst_caps_fixate",
                "gst_clock_id_wait_async",
                "gst_element_factory_create_with_properties",
                "gst_element_factory_make_with_properties",
                "gst_element_post_message",
                "gst_element_send_event",
                "gst_encoding_container_profile_add_profile",
                "gst_event_new_custom",
                "gst_event_new_select_streams",
                "gst_event_parse_select_streams",
                "gst_init_check",
                "gst_install_plugins_async",
                "gst_iterator_next",
                "gst_memory_new_wrapped",
                "gst_message_copy",
                "gst_message_new_application",
                "gst_message_new_custom",
                "gst_message_parse_error",
                "gst_message_parse_info",
                "gst_message_parse_property_notify",
                "gst_message_parse_warning",
                "gst_meta_api_type_aggregate_params",
                "gst_meta_info_new",
                "gst_meta_info_register",
                "gst_meta_register",
                "gst_meta_register_custom",
                "gst_meta_serialize_simple",
                "gst_mini_object_get_qdata",
                "gst_mini_object_is_writable",
                "gst_mini_object_make_writable",
                "gst_mini_object_ref",
                "gst_mini_object_steal_qdata",
                "gst_mini_object_unref",
                "gst_pad_push_event",
                "gst_pad_send_event",
                "gst_pad_set_event_full_function_full",
                "gst_param_spec_array",
                "gst_param_spec_fraction",
                "gst_play_message_parse_error",
                "gst_play_message_parse_error_missing_plugin",
                "gst_play_message_parse_warning",
                "gst_play_message_parse_warning_missing_plugin",
                "gst_play_new",
                "gst_play_set_config",
                "gst_play_signal_adapter_get_play",
                "gst_play_signal_adapter_new",
                "gst_play_signal_adapter_new_sync_emit",
                "gst_play_signal_adapter_new_with_main_context",
                "gst_play_video_overlay_video_renderer_new",
                "gst_play_video_overlay_video_renderer_new_with_sink",
                "gst_play_visualizations_free",
                "gst_play_visualizations_get",
                "gst_promise_reply",
                "gst_query_new_custom",
                "gst_query_parse_nth_allocation_param",
                "gst_rtcp_packet_app_get_data",
                "gst_rtcp_packet_app_get_name",
                "gst_rtcp_packet_app_set_name",
                "gst_rtcp_packet_fb_get_fci",
                "gst_rtp_hdrext_set_ntp_56",
                "gst_rtp_hdrext_set_ntp_64",
                "gst_rtp_source_meta_set_ssrc",
                "gst_rtsp_message_append_headers",
                "gst_rtsp_mount_points_add_factory",
                "gst_rtsp_transport_parse",
                "gst_structure_get_value",
                "gst_structure_set_value",
                "gst_tag_list_add_value",
                "gst_tag_list_copy_value",
                "gst_tag_list_get_value_index",
                "gst_transcoder_message_parse_error",
                "gst_transcoder_message_parse_warning",
                "gst_type_find_peek",
                "gst_value_compare",
                "gst_value_serialize",
                "gst_video_codec_frame_set_user_data",
                "gst_video_frame_map",
                "gst_video_frame_map_id",
                "gst_video_frame_unmap",
                "gst_video_gl_texture_upload_meta_upload",
                "gst_webrtc_data_channel_send_data_full",
                "gst_webrtc_session_description_new",
            ],
            GirFixture.Overlays.HandBoundIdentifiers.Order(StringComparer.Ordinal).ToArray());

        Assert.True(GirFixture.Overlays.TryGetReturnTypeOverride("gst_pipeline_new", out string? pipeline));
        Assert.Equal("Gst.Pipeline", pipeline);
        Assert.Null(GirFixture.Overlays.GetPlatformSupport("gst_macos_main"));

        // The opaque list carries the same weight as the skip list: whether a
        // record is copied by value or addressed through a pointer is the shape
        // of its public type, so an entry that is added or dropped is a
        // decision, not a detail.
        Assert.Equal(
            [
                "GES.FrameCompositionMeta",
                "Gst.CustomMeta",
                "Gst.DebugCategory",
                "Gst.Meta",
                "Gst.ParentBufferMeta",
                "Gst.ProtectionMeta",
                "Gst.ReferenceTimestampMeta",
                "Gst.StaticCaps",
                "Gst.StaticPadTemplate",
                "GstAudio.AudioCdSrcTrack",
                "GstAudio.AudioClippingMeta",
                "GstAudio.AudioDownmixMeta",
                "GstAudio.AudioLevelMeta",
                "GstAudio.DsdPlaneOffsetMeta",
                "GstNet.NetAddressMeta",
                "GstNet.NetControlMessageMeta",
                "GstRtsp.RTSPTransport",
                "GstSdp.SDPAttribute",
                "GstSdp.SDPBandwidth",
                "GstSdp.SDPConnection",
                "GstSdp.SDPKey",
                "GstSdp.SDPMedia",
                "GstSdp.SDPOrigin",
                "GstSdp.SDPTime",
                "GstSdp.SDPZone",
                "GstVideo.AncillaryMeta",
                "GstVideo.VideoAFDMeta",
                "GstVideo.VideoAffineTransformationMeta",
                "GstVideo.VideoBarMeta",
                "GstVideo.VideoCaptionMeta",
                "GstVideo.VideoCodecAlphaMeta",
                "GstVideo.VideoCropMeta",
                "GstVideo.VideoOverlayCompositionMeta",
                "GstVideo.VideoRegionOfInterestMeta",
                "GstVideo.VideoResampler",
                "GstVideo.VideoSEIUserDataUnregisteredMeta",
                "GstVideo.VideoTimeCodeConfig",
            ],
            GirFixture.Overlays.OpaqueRecords.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AnOverlaySkipKeepsAPropertyOffBothRoutesOfTheSurface()
    {
        // A property whose C implementation is wrong in a way no annotation
        // describes has to be removable by name, exactly as a callable is. The
        // entry that this exists for is
        // GstPlay.PlayVideoOverlayVideoRenderer:video-sink, whose setter leaks
        // the sink it replaces, and the read only half of it is hand written.
        // Both routes of the surface builder are covered: 'pulse' is delegated
        // to a bound C getter and 'glow' is read through the GObject property
        // system, and the skip list is asked before either of them decides.
        FixtureRun before = RunWithOverlay("{}", PropertyBody);

        Assert.Contains("public int Pulse", before.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains("public bool Glow", before.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(0, before.Result.Census.SkippedCount("Gst", SkipReason.OverlaySkip));

        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": [ "Gst.Widget:pulse", "Gst.Widget:glow" ]
            }
            """,
            PropertyBody);

        string source = run.File("Widget.cs");
        Assert.DoesNotContain("public int Pulse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool Glow", source, StringComparison.Ordinal);

        // The C getter the gir named is a method of its own and stays bound:
        // the entry names the property, not the call behind it.
        Assert.Contains("public int GetPulse()", source, StringComparison.Ordinal);

        Assert.Equal(2, run.Result.Census.SkippedCount("Gst", SkipReason.OverlaySkip));
        Assert.Contains(
            "### OverlaySkip (2)\n\n- `Gst.Widget:glow`\n- `Gst.Widget:pulse`\n",
            run.Result.SkipReport,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AHandBoundEntryIsReportedUnderItsOwnReason()
    {
        // The skip list is what keeps the symbol out; the hand bound entry
        // only says that the managed surface exists all the same, so the
        // report files it apart from the skips that are a real gap.
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": [ "gst_widget_pack" ],
              "handBound": [ "gst_widget_pack" ]
            }
            """);

        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.HandBound));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.OverlaySkip));
        Assert.Contains("### HandBound (1)\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0023", StringComparison.Ordinal));
    }

    [Fact]
    public void AHandBoundSignalIsNamedByItsGObjectSpelling()
    {
        // A signal has no c:identifier, so the ledger names it the way the
        // census and the skip report print it. GstWebRTCDataChannel's
        // on-message-data is the entry this exists for: its argument is a
        // GBytes, which the signal planner has no rule for, and the event, its
        // arguments class and its trampoline are written by hand instead.
        // Without the entry the signal is a plain gap, filed under the reason
        // that says the planner has no rule for it.
        FixtureRun before = RunWithOverlay("{}", SignalBody);
        Assert.Equal(1, before.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));

        FixtureRun run = RunWithOverlay(
            """
            {
              "handBound": [ "Gst.Widget::packed" ]
            }
            """,
            SignalBody);

        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.HandBound));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.Contains(
            "### HandBound (1)\n\n- `Gst.Widget::packed`\n",
            run.Result.SkipReport,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0023", StringComparison.Ordinal));
    }

    [Fact]
    public void ACallbackTypeGoesWithTheOnlyCallThatTakesIt()
    {
        // A delegate nothing can be handed to is dead surface, so a callback
        // type is emitted because a planned callable takes one. Skipping that
        // callable takes the type down with it.
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": [ "gst_widget_watch" ]
            }
            """,
            CallbackBody);

        Assert.False(run.HasFile("Callbacks.cs"));
    }

    [Fact]
    public void AHandBoundConsumerKeepsItsCallbackTypeGenerated()
    {
        // The hand bound ledger says the call exists all the same, so the
        // callback type it takes is generated and the hand written member
        // binds the generated delegate rather than a copy of it. This is what
        // Gst.ClockCallback and Gst.CustomMetaTransformFunction had to be
        // copied into Custom/ for.
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": [ "gst_widget_watch" ],
              "handBound": [ "gst_widget_watch" ]
            }
            """,
            CallbackBody);

        string source = run.File("Callbacks.cs");
        Assert.Contains(
            "public delegate void PulseFunc(Gst.Widget widget);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "internal static unsafe class PulseFuncTrampoline",
            source,
            StringComparison.Ordinal);

        // The scope of the consumer decides the epilogue here as anywhere
        // else: a notified site releases the state through its destroy
        // notification, so the trampoline frees nothing.
        Assert.DoesNotContain("FromUserData(userData).Free()", source, StringComparison.Ordinal);

        // The call itself is still not generated, and the ledger still files
        // it under the reason that says the bindings do cover it.
        Assert.DoesNotContain("Watch", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.HandBound));
    }

    [Fact]
    public void AHandBoundEntryThatNamesNoSkippedSymbolIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "handBound": [ "gst_widget_unpack" ]
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0023", StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "The hand bound entry 'gst_widget_unpack' was not skipped by this run",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void AHandBoundEntryOnAGeneratedSymbolIsReportedAsStale()
    {
        // Nothing keeps gst_widget_pack out of the bindings here, so the
        // entry claims a hand binding of a member the generator emits: two
        // ways to call the same function, which is what the ledger exists to
        // rule out.
        FixtureRun run = RunWithOverlay(
            """
            {
              "handBound": [ "gst_widget_pack" ]
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0023", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack", StringComparison.Ordinal));
    }

    [Fact]
    public void AHandBoundEntryWhoseOnlySkipIsAMovedToTwinIsReportedAsStale()
    {
        // The gir declares a handful of entry points twice: once inside the
        // type they belong to, which is the declaration the generator binds,
        // and once at namespace level with a moved-to pointing back at it. The
        // twin is skipped, but for a reason that says the callable is emitted
        // under another declaration rather than absent, so the ledger may not
        // read it as the skip that a hand binding replaces - it would satisfy
        // the entry and leave two ways to call the function unreported.
        FixtureRun run = RunWithOverlay(
            """
            {
              "handBound": [ "gst_widget_pack" ]
            }
            """,
            Body + "\n" + MovedToTwin);

        Assert.Contains("Pack()", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.MovedTo));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.HandBound));
        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0023", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack", StringComparison.Ordinal));
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

    /// <summary>
    /// One class with one bindable method, which is enough to state that a
    /// symbol is skipped, hand bound, both or neither.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="pack" c:identifier="gst_widget_pack">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </class>
        """;

    /// <summary>
    /// One class with one signal the planner cannot bind: its argument is a
    /// fixed size array, which is one of the array shapes a signal argument
    /// never is - only the NULL terminated vector of strings is planned. It
    /// stands for the shape the ledger has to name, a signal whose managed
    /// surface is hand written.
    /// </summary>
    private const string SignalBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="packed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="names" transfer-ownership="none">
                    <array c:type="gchar**" fixed-size="4">
                      <type name="utf8" c:type="gchar*"/>
                    </array>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// One class with two properties: <c>pulse</c>, which the gir backs with a
    /// bound C getter, and <c>glow</c>, which has no accessor at all and is
    /// therefore read through the GObject property system. The two take the two
    /// different routes of the surface builder, which is what makes the pair
    /// the fixture of the overlay skip of a property.
    /// </summary>
    private const string PropertyBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="get_pulse" c:identifier="gst_widget_get_pulse">
                <return-value transfer-ownership="none">
                  <type name="gint" c:type="gint"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <property name="pulse" readable="1" transfer-ownership="none" getter="get_pulse">
                <type name="gint" c:type="gint"/>
              </property>
              <property name="glow" readable="1" writable="1" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
            </class>
        """;

    /// <summary>
    /// The namespace level twin of <c>gst_widget_pack</c>: the same C entry
    /// point declared a second time, with a <c>moved-to</c> that names the
    /// method the generator binds. The reference girs carry a handful of
    /// these.
    /// </summary>
    private const string MovedToTwin =
        """
            <function name="widget_pack" c:identifier="gst_widget_pack" moved-to="Widget.pack">
              <return-value transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
              </parameters>
            </function>
        """;

    /// <summary>
    /// A callback type and the one call that takes it, which is the shape a
    /// hand binding would otherwise remove from the module.
    /// </summary>
    private const string CallbackBody =
        """
            <callback name="PulseFunc" c:type="GstPulseFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="watch" c:identifier="gst_widget_watch">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="notified" closure="1" destroy="2">
                    <type name="PulseFunc" c:type="GstPulseFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                  <parameter name="notify" transfer-ownership="none" scope="async">
                    <type name="GLib.DestroyNotify" c:type="GDestroyNotify"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    private static FixtureRun RunWithOverlay(string fixups) => RunWithOverlay(fixups, Body);

    private static FixtureRun RunWithOverlay(string fixups, string body)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
