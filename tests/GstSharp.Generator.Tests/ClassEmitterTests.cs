using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What the emitters make of the real <c>Gst</c> gir: the shape of the
/// hierarchy, a few members that have to be there, and the frozen counts of
/// what is emitted and what is left out.
/// </summary>
public sealed class ClassEmitterTests
{
    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GirFixture.RunWithoutErrors(),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    [Theory]
    [InlineData("Object.cs", "public abstract unsafe partial class Object : Gst.GObject.InitiallyUnowned")]
    [InlineData("Element.cs", "public abstract unsafe partial class Element : Gst.Object")]
    [InlineData("Bin.cs", "public unsafe partial class Bin : Gst.Element, Gst.IChildProxy")]
    [InlineData("Pipeline.cs", "public unsafe partial class Pipeline : Gst.Bin, Gst.IChildProxy")]
    [InlineData("SystemClock.cs", "public unsafe partial class SystemClock : Gst.Clock")]
    public void TheHierarchyFollowsTheGir(string fileName, string declaration)
    {
        Assert.Contains(declaration + "\n", Source(fileName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Element.cs", "public Gst.StateChangeReturn SetState(Gst.State state)")]
    [InlineData("Element.cs", "public Gst.StateChangeReturn GetState(out Gst.State state, out Gst.State pending, Gst.ClockTime timeout)")]
    [InlineData("Object.cs", "public string? GetName()")]
    [InlineData("Bin.cs", "public bool Add(Gst.Element element)")]
    [InlineData("Buffer.cs", "public static Gst.Buffer New()")]
    [InlineData("ElementFactory.cs", "public static Gst.Element? Make(string factoryname, string? name)")]
    [InlineData("Caps.cs", "public static Gst.Caps NewEmpty()")]
    [InlineData("Pad.cs", "public Gst.PadLinkReturn Link(Gst.Pad sinkpad)")]
    [InlineData("DeviceProvider.cs", "public System.Collections.Generic.IReadOnlyList<Gst.Device> GetDevices()")]
    [InlineData("TypeFindFactory.cs", "public static System.Collections.Generic.IReadOnlyList<Gst.TypeFindFactory> GetList()")]
    [InlineData("Uri.cs", "public System.Collections.Generic.IReadOnlyList<string> GetQueryKeys()")]
    public void TheExpectedMembersAreEmitted(string fileName, string signature)
    {
        Assert.Contains("    " + signature + "\n", Source(fileName), StringComparison.Ordinal);
    }

    [Fact]
    public void GstObjectExposesItsNameAsAProperty()
    {
        // gst_object_set_name returns a gboolean, so the property is read only
        // and the setter stays a method of its own.
        string source = Source("Object.cs");

        Assert.Contains("public string? Name => GetName();", source, StringComparison.Ordinal);
        Assert.Contains("public bool SetName(string? name)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNameOfAControlBindingIsTheControlledProperty()
    {
        // GstControlBinding installs a construct-only "name" of its own that
        // holds the name of the property it controls, which is not what
        // GstObject means by "name" and not what Gst.Object.Name already binds.
        // The rename of fixups.json is what binds it under a name of its own;
        // without it the property would be dropped for hiding the inherited
        // member.
        string source = Source("ControlBinding.cs");

        Assert.Contains("public string? PropertyName\n", source, StringComparison.Ordinal);
        Assert.Contains("GetProperty(\"name\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public string? Name\n", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GstSharp.Net/Generated/Stream.cs", "Caps", "public Gst.Caps? GetCaps()")]
    [InlineData("GstSharp.Net/Generated/Stream.cs", "Tags", "public Gst.TagList? GetTags()")]
    [InlineData("GstSharp.Net/Generated/Device.cs", "Caps", "public Gst.Caps? GetCaps()")]
    [InlineData("GstSharp.Net/Generated/PadTemplate.cs", "Caps", "public Gst.Caps GetCaps()")]
    [InlineData("GstSharp.Net.App/Generated/AppSrc.cs", "Caps", "public Gst.Caps? GetCaps()")]
    [InlineData("GstSharp.Net.App/Generated/AppSink.cs", "Caps", "public Gst.Caps? GetCaps()")]
    [InlineData("GstSharp.Net.Base/Generated/BaseSink.cs", "Stats", "public Gst.Structure GetStats()")]
    [InlineData("GstSharp.Net.GES/Generated/Track.cs", "Caps", "public Gst.Caps? GetCaps()")]
    public void AWrapperValuedPropertyIsAMethodInstead(string path, string propertyName, string getter)
    {
        // Reading one of these builds a mini object or a boxed wrapper that
        // owns a reference and has to be disposed, so a property would leak one
        // per evaluation, and GST0001 does not look at property reads. The
        // getter stays, under a name that says something is produced.
        string source = SourceOf(path);

        Assert.DoesNotContain("    public Gst.Caps? " + propertyName + "\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" " + propertyName + " => Get" + propertyName + "();", source, StringComparison.Ordinal);
        Assert.Contains("    " + getter + "\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public void APropertyWhoseValueIsNotAWrapperStays()
    {
        // The rule keys on the wrapper flavour of the getter, not on the fact
        // that a getter exists: a blittable value like GstClockTime and an
        // interned GObject are still read as values.
        Assert.Contains(
            "    public Gst.ClockTime Delay\n",
            Source("Pipeline.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "    public Gst.Object? Parent => GetParent();\n",
            Source("Object.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ParseLaunchThrowsTheErrorItIsGiven()
    {
        string source = Source("Global.cs");

        Assert.Contains("public static unsafe partial class Global\n", source, StringComparison.Ordinal);
        Assert.Contains("public static Gst.Element ParseLaunch(string pipelineDescription)", source, StringComparison.Ordinal);
        Assert.Contains("Gst.GLib.GException.ThrowIfSet(ref errorNative);", source, StringComparison.Ordinal);
        Assert.Contains(
            "[LibraryImport(\"Gst\", EntryPoint = \"gst_parse_launch\")]",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFactoryThatABaseClassAlsoCarriesHidesItOnPurpose()
    {
        // gst_pipeline_new and gst_bin_new have the same signature, so the
        // derived one has to say that it hides the inherited one. Both are
        // narrowed onto the type that declares them by fixups.json.
        Assert.Contains(
            "public static new Gst.Pipeline New(string? name)",
            Source("Pipeline.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public static Gst.Bin New(string? name)",
            Source("Bin.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryInterfaceIsEmittedWithItsExtensions()
    {
        foreach (string name in new[] { "IChildProxy", "IPreset", "ITagSetter", "ITocSetter", "IURIHandler" })
        {
            string source = Source(name + ".cs");
            Assert.Contains("public interface " + name + "\n", source, StringComparison.Ordinal);
            Assert.Contains("public static unsafe partial class " + name[1..] + "Extensions\n", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheModuleRegistersTheWrappersOfTheModule()
    {
        string source = Source("_Module.cs");
        int entries = source.Split("new Gst.Interop.ModuleTypeEntry(").Length - 1;

        // Every class, mini object and boxed record of the module that GObject
        // knows a type for.
        Assert.Equal(57, entries);
        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Element.GetGType, &Gst.Element.CreateWrapper),",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Buffer.GetGType, &Gst.Buffer.CreateWrapper, "
            + "&Gst.Buffer.BorrowWrapper),",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Gst", 35, 49, 5, 39, 18, 1450, 29, 23, 71)]
    [InlineData("GstBase", 11, 4, 0, 7, 0, 176, 31, 2, 10)]
    [InlineData("GstApp", 2, 2, 0, 8, 0, 62, 36, 8, 0)]
    [InlineData("GstAudio", 14, 17, 1, 1, 2, 212, 33, 0, 48)]
    [InlineData("GstVideo", 12, 42, 5, 0, 10, 379, 14, 2, 122)]
    [InlineData("GstPbutils", 14, 1, 0, 0, 1, 180, 5, 5, 0)]
    [InlineData("GstSdp", 1, 21, 0, 0, 0, 168, 0, 0, 51)]
    [InlineData("GstWebRTC", 9, 4, 0, 1, 2, 35, 38, 8, 21)]
    [InlineData("GstNet", 5, 3, 0, 1, 0, 25, 17, 0, 4)]
    [InlineData("GstRtsp", 1, 10, 1, 1, 2, 114, 0, 1, 28)]
    [InlineData("GstRtp", 5, 5, 0, 0, 0, 188, 21, 2, 9)]
    [InlineData("GstRtspServer", 19, 6, 0, 8, 0, 384, 58, 40, 21)]
    [InlineData("GstAllocators", 6, 0, 1, 0, 0, 23, 2, 0, 0)]
    [InlineData("GstTag", 3, 0, 1, 0, 0, 46, 0, 0, 0)]
    [InlineData("GstTranscoder", 2, 0, 0, 0, 3, 26, 9, 6, 0)]
    [InlineData("GstPlay", 8, 1, 1, 0, 5, 120, 17, 13, 0)]
    [InlineData("GES", 56, 2, 2, 0, 3, 384, 77, 39, 7)]
    public void TheEmissionCensusIsStable(
        string module,
        int classes,
        int records,
        int interfaces,
        int callbacks,
        int enumHolders,
        int methods,
        int properties,
        int signals,
        int fieldAccessors)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(classes, census.EmittedCount(module, "class"));
        Assert.Equal(records, census.EmittedCount(module, "record"));
        Assert.Equal(interfaces, census.EmittedCount(module, "interface"));
        Assert.Equal(callbacks, census.EmittedCount(module, "callback"));
        Assert.Equal(enumHolders, census.EmittedCount(module, "enum holder"));
        Assert.Equal(methods, census.EmittedCount(module, "method"));
        Assert.Equal(properties, census.EmittedCount(module, "property"));
        Assert.Equal(signals, census.EmittedCount(module, "signal"));

        // A get only property per field of a mini object, a boxed record or an
        // opaque record that the mirror of its native layout projects onto a
        // value. GstApp and GstPbutils declare no such field at all.
        Assert.Equal(fieldAccessors, census.EmittedCount(module, "field accessor"));
    }

    [Theory]
    // gst_pad_sticky_events_foreach and gst_buffer_list_foreach left the
    // unsupported signatures for the hand bound ledger when the two walks over
    // a lent slot were written.
    [InlineData("Gst", 1, 90, 53, 110, 28, 10)]
    [InlineData("GstBase", 0, 11, 0, 20, 3, 0)]
    [InlineData("GstApp", 1, 0, 0, 2, 0, 1)]
    [InlineData("GstAudio", 0, 22, 0, 7, 1, 0)]
    [InlineData("GstVideo", 0, 96, 1, 6, 2, 0)]
    [InlineData("GstPbutils", 0, 1, 0, 0, 0, 0)]
    [InlineData("GstSdp", 0, 8, 0, 0, 0, 0)]
    [InlineData("GstWebRTC", 0, 2, 0, 0, 0, 0)]
    [InlineData("GstNet", 0, 3, 0, 0, 0, 0)]
    [InlineData("GstRtsp", 0, 13, 0, 0, 10, 0)]
    // The two extensions properties left the unsupported signatures for the
    // hand bound ledger when the Extensions property of each class was written.
    [InlineData("GstRtp", 2, 22, 1, 0, 0, 0)]
    [InlineData("GstRtspServer", 2, 1, 1, 3, 13, 0)]
    [InlineData("GstAllocators", 0, 0, 0, 0, 0, 0)]
    [InlineData("GstTag", 0, 0, 0, 0, 0, 0)]
    [InlineData("GstTranscoder", 0, 7, 0, 0, 0, 0)]
    [InlineData("GstPlay", 0, 23, 0, 0, 0, 0)]
    // ges_meta_container_foreach left the unsupported signatures for the hand
    // bound ledger when the walk over the metadata of a container was written.
    [InlineData("GES", 6, 3, 4, 10, 4, 2)]
    public void TheSkipCensusIsStable(
        string module,
        int shadowed,
        int movedTo,
        int varArgs,
        int notIntrospectable,
        int unsupported,
        int collisions)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(shadowed, census.SkippedCount(module, SkipReason.ShadowedBy));
        Assert.Equal(movedTo, census.SkippedCount(module, SkipReason.MovedTo));
        Assert.Equal(varArgs, census.SkippedCount(module, SkipReason.VarArgs));
        Assert.Equal(notIntrospectable, census.SkippedCount(module, SkipReason.NotIntrospectable));
        Assert.Equal(unsupported, census.SkippedCount(module, SkipReason.UnsupportedSignature));
        Assert.Equal(collisions, census.SkippedCount(module, SkipReason.NameCollision));
        Assert.Equal(0, census.SkippedCount(module, SkipReason.NoCIdentifier));
        Assert.Equal(0, census.SkippedCount(module, SkipReason.FieldSlotCallback));
    }

    [Fact]
    public void AWritableTargetSaysSoInItsRemarks()
    {
        // The C side asserts writability and the generated member deliberately
        // carries no guard — gst_caps_append_structure shipped with the same
        // assert and none — so C parity is stated in the documentation, and
        // this pins the note: nothing else fails when the entry point list in
        // CallableRenderer.WritableTargets is dropped.
        Assert.Contains(
            """
                /// <remarks>
                /// <para>
                /// The caps have to be writable. Like the C API, the call raises a warning
                /// and writes nothing otherwise.
                /// </para>
                /// </remarks>
                /// <param name="field">name of the field to set</param>
            """,
            Source("Caps.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "/// The structure has to be writable. Like the C API, the call raises a warning",
            Source("Structure.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AStolenReferenceClaimIsCorrectedInTheRemarks()
    {
        // gst_video_time_code_new and gst_video_time_code_init both carry the
        // C sentence "@latest_daiy_jam reference is stolen from caller.", which
        // the generator copies verbatim onto the member. It is false at 1.28.6
        // - gst_video_time_code_init takes a reference of its own - so the two
        // members would otherwise ship a sentence that contradicts the
        // consuming contract of the binding. This pins the correction: nothing
        // else fails when the entry point list in
        // CallableRenderer.StolenReferenceTargets is dropped.
        const string correction =
            """
                /// <para>
                /// The documentation above says that the reference of the daily jam is stolen
                /// from the caller. It is not: the C function takes a reference of its own, so
                /// the caller keeps the value it passes and disposes it as usual.
                /// </para>
            """;

        string source = SourceOf("GstSharp.Net.Video/Generated/VideoTimeCode.cs");

        // Once on the constructor and once on the initializer, and nowhere
        // else: the four members that take a dt rather than a daily jam say
        // nothing about a stolen reference, because their gir does not either.
        Assert.Equal(2, source.Split(correction).Length - 1);
        Assert.Contains(
            "public static Gst.Video.VideoTimeCode New(uint fpsN, uint fpsD, Gst.GLib.DateTime? latestDailyJam,",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public void Init(uint fpsN, uint fpsD, Gst.GLib.DateTime? latestDailyJam,",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnAdoptedWrapperSaysWhoOwnsIt()
    {
        // The gir documents the borrowed pointer the C function returns. The
        // wrapper is not borrowed: it references a mini object and it copies a
        // boxed value, so the caller has to dispose it.
        Assert.Contains(
            """
                /// The wrapper owns a reference of its own, which is a copy for a boxed type:
                /// dispose it when you are done, and note that changes made to a copy of a
                /// boxed value are not written back.
            """,
            Source("Buffer.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AConsumedParameterSaysItIsConsumed()
    {
        // The wording is the contract of the hand written consuming members:
        // the parameter note, the transfer statement with the idempotence
        // sentence in the remarks, and the two exceptions.
        string source = Source("Caps.cs");

        Assert.Contains(
            """
                /// <param name="caps2">
                /// the #GstCaps to append
                /// The call consumes it: <paramref name="caps2"/> is disposed when this
                /// method returns, and using it afterwards throws <see cref="ObjectDisposedException"/>.
                /// </param>
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
                /// The <c>caps2</c> parameter is <c>transfer-ownership="full"</c>: the call is
                /// handed a reference of its own and the wrapper is disposed afterwards, which
                /// leaves the native reference count exactly where the C call leaves it.
                /// <see cref="Gst.MiniObject.Dispose()"/> is idempotent, so a <c>using</c>
                /// declaration around the argument stays correct.
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
                /// <exception cref="ArgumentNullException">
                /// <paramref name="caps2"/> is <see langword="null"/>.
                /// </exception>
                /// <exception cref="ObjectDisposedException">
                /// This wrapper or <paramref name="caps2"/> was disposed.
                /// </exception>
            """,
            source,
            StringComparison.Ordinal);

        // A GObject argument is handed over rather than consumed: the remark
        // says what the call takes and what the wrapper keeps. The upstream
        // sentence that tells a C caller to drop the argument is taken out of
        // the documentation by the docStrip overlay where a member carries one,
        // so nothing is left for the remark to argue against.
        string streamCollection = Source("StreamCollection.cs");

        Assert.Contains(
            """
                /// The call takes <paramref name="stream"/> over: the library is handed a
                /// reference of its own, minted for this call, and keeps it for as long as it
                /// needs the object. This wrapper keeps the reference it holds, so it stays
                /// usable after the call and the handlers connected to it keep firing.
            """,
            streamCollection,
            StringComparison.Ordinal);
        Assert.Contains(
            """
                /// <param name="stream">
                /// the #GstStream to add
                /// The call is handed a reference of its own: <paramref name="stream"/> stays
                /// usable after this method returns.
                /// </param>
            """,
            streamCollection,
            StringComparison.Ordinal);

        // The body mints the reference the call is handed and leaves the
        // wrapper alone, which is the whole of the handover: no dispose, and
        // the barrier that replaces it last.
        Assert.Contains(
            """
                public bool AddStream(Gst.Stream stream)
                {
                    ArgumentNullException.ThrowIfNull(stream);
                    nint instanceHandle = Handle;
                    nint streamNative = stream.Handle;
                    nint streamOwned = Gst.Interop.GObjectNative.ObjectRef(streamNative);
                    int nativeResult = GstStreamCollectionAddStream(instanceHandle, streamOwned);
                    bool result = nativeResult != 0;
                    System.GC.KeepAlive(this);
                    System.GC.KeepAlive(stream);
                    return result;
                }
            """,
            streamCollection,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AHandedOverArgumentIsKeptAliveAcrossAThrowingMember()
    {
        // ges_project_save is handed a reference of its own for the formatter
        // asset and reports errors through a GError. The asset is a GObject, so
        // nothing disposes the wrapper on either path, and the barrier that
        // replaces the dispose sits with the other barriers, after the throw —
        // pinned adjacent, because nothing else in the suite pins this path.
        Assert.Contains(
            """
                    int nativeResult = GesProjectSave(instanceHandle, timelineNative, uriScope.Pointer, formatterAssetOwned, overwrite ? 1 : 0, &errorNative);
                    Gst.GLib.GException.ThrowIfSet(ref errorNative);
                    bool result = nativeResult != 0;
                    System.GC.KeepAlive(this);
                    System.GC.KeepAlive(timeline);
                    System.GC.KeepAlive(formatterAsset);
            """,
            SourceOf("GstSharp.Net.GES/Generated/Project.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AGeneratedClassOpensItsConstructorToBindingModules()
    {
        // The constructor is the one thing about a generated class that is
        // open, and it is deliberate public surface: it is where a module
        // written against the package attaches its wrappers to the generated
        // hierarchy. Everything else stays internal.
        string source = Source("ControlSource.cs");

        Assert.Contains(
            "    protected ControlSource(nint handle, Gst.Interop.Transfer transfer)\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "/// This is where a binding module attaches its own wrappers: derive from",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "    internal static new object CreateWrapper(nint handle, Gst.Interop.Transfer transfer)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("    internal static new partial nuint GetGType();\n", source, StringComparison.Ordinal);

        // The private concrete subclass reaches the constructor by nesting, so
        // its own accessibility is unaffected.
        Assert.Contains("    private sealed class Concrete : ControlSource\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "        internal Concrete(nint handle, Gst.Interop.Transfer transfer)\n",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGeneratedClassOpensItsConstructorAndEveryRecordDoesNot()
    {
        // A class is never sealed, so protected is always legal on it. A mini
        // object, a boxed value and an opaque record are sealed and keep the
        // internal constructor that only a generated factory calls.
        int classes = 0;
        int records = 0;

        foreach (GeneratedFile file in Generated.Files)
        {
            string content = file.Content;

            if (content.Contains("\npublic abstract unsafe partial class ", StringComparison.Ordinal)
                || content.Contains("\npublic unsafe partial class ", StringComparison.Ordinal))
            {
                classes++;
                Assert.Contains(
                    "\n    protected ",
                    content,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "\n    internal " + Path.GetFileNameWithoutExtension(file.RelativePath) + "(nint handle",
                    content,
                    StringComparison.Ordinal);
                continue;
            }

            if (!content.Contains("\npublic sealed unsafe partial class ", StringComparison.Ordinal))
            {
                continue;
            }

            records++;
            Assert.DoesNotContain("\n    protected ", content, StringComparison.Ordinal);
        }

        // 122 rather than 106 since the field accessors of a boxed or opaque
        // record landed: sixteen wrappers whose gir declares no callable, from
        // Gst.ValueTable to GstSdp.MIKEYPayloadT, now read their fields through
        // a mirror and are therefore emitted with the unsafe modifier the
        // counting here keys on. The eighteen the RTSP server adds are its
        // classes: the nineteenth it emits is RtspServerGlobal, the static
        // holder of the namespace level calls, whose declaration carries the
        // static modifier the pattern here does not match.
        // Thirty six more since the subclassing surface landed: the
        // `*.Subclass.cs` partial of each allowlisted class opens with the same
        // `public unsafe partial class` the counting keys on, and the allowlist
        // holds thirty six classes - fourteen from stage 2a, the five codec
        // bases of stage 2b, Gst.Pad and GstBase.AggregatorPad of stage 3a, the
        // seven GES classes of stage 3c, Gst.DeviceProvider and Gst.Device, and
        // the six GES effect classes of stage 3d.
        // GES.Container is not among them: it is on the chain of GES.Clip, so
        // it gets a mirror and no managed surface of its own.
        Assert.Equal(226, classes);

        // 127 rather than 123 since the field accessors of a string and of a
        // handle landed: GstSdp.SDPKey, GstSdp.SDPOrigin and
        // GstVideo.VideoCodecState declare no callable that reads a handle, so
        // the accessors of their fields are the first members of each to
        // dereference the mirror. The last one is Gst.Rtp.RTPSourceMeta, the
        // only sealed class the RTP module emits. The four the RTSP server adds
        // are RTSPAddress, RTSPPermissions, RTSPThread and RTSPToken, the boxed
        // and mini object records it wraps behind a mirror of their native
        // layout; RTSPContext and SDPInfo are plain structures and are counted
        // by neither half of this test.
        Assert.Equal(131, records);
    }

    [Fact]
    public void AFailedCallReleasesTheResultItStillTransferred()
    {
        // gst_discoverer_discover_uri sets its GError whenever the run saw an
        // error message on the bus and returns the information object all the
        // same. The throw puts that object out of reach, so it is released
        // first; without this the call leaks one GObject per failed discovery.
        Assert.Contains(
            """
                    nint nativeResult = GstDiscovererDiscoverUri(Handle, uriScope.Pointer, &errorNative);
                    if (errorNative != 0 && nativeResult != 0)
                    {
                        // The call failed and transferred a value all the same. The throw
                        // below puts it out of reach, so it is released rather than leaked.
                        Gst.Interop.GObjectNative.ObjectUnref(nativeResult);
                    }
                    Gst.GLib.GException.ThrowIfSet(ref errorNative);
                    Gst.Pbutils.DiscovererInfo result = Gst.GObject.Object.FromNative<Gst.Pbutils.DiscovererInfo>(nativeResult, Gst.Interop.Transfer.Full)
                        ?? throw new InvalidOperationException("gst_discoverer_discover_uri returned no value.");
                    System.GC.KeepAlive(this);
            """,
            SourceOf("GstSharp.Net.Pbutils/Generated/Discoverer.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseOfAFailedResultFollowsItsKind()
    {
        // A mini object and a boxed value are not interned, so the wrapper is
        // the release: it adopts what the call transferred and hands it back.
        Assert.Contains(
            "Gst.Sdp.MIKEYMessage.FromNative(nativeResult, Gst.Interop.Transfer.Full)?.Dispose();",
            SourceOf("GstSharp.Net.Sdp/Generated/MIKEYMessage.cs"),
            StringComparison.Ordinal);

        // A transferred string is memory of GLib and nothing else.
        Assert.Contains(
            "Gst.Interop.GMarshal.Free(nativeResult);",
            Source("Global.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ABorrowedResultIsNotReleasedOnTheThrowPath()
    {
        // gst_parse_launch returns a floating reference the wrapper sinks, and
        // gst_element_make_from_uri returns transfer none. Releasing either
        // would be releasing something this call was never given.
        Assert.Contains(
            """
                    nint nativeResult = GstElementMakeFromUri((int)type, uriScope.Pointer, elementnameScope.Pointer, &errorNative);
                    Gst.GLib.GException.ThrowIfSet(ref errorNative);
            """,
            Source("Element.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryThrowingCallableThatOwnsItsResultReleasesIt()
    {
        // The guard is emitted for a transferred handle and a transferred
        // string, which is every owned return the bound surface throws with,
        // and for a list, which no bound symbol pairs with a GError yet but
        // which the emitter covers all the same (ListReturnTests holds that
        // arm honest). A new one of a kind the emitter does not cover — an
        // opaque record, a string vector, an array — would leak silently, so
        // the count is frozen here and a change to it has to be looked at.
        int guards = 0;
        foreach (GeneratedFile file in Generated.Files)
        {
            guards += file.Content.Split("// The call failed and transferred a value all the same.").Length - 1;
        }

        // Fifteen since the GBytes bridge landed: the thirteen of before -
        // RTSPServer.CreateSocket among them, which takes a GError and answers
        // a GSocket it owns - plus MIKEYMessage.NewFromBytes and
        // MIKEYMessage.ToBytes, which throw and own the message and the block
        // they answer.
        Assert.Equal(15, guards);
    }

    [Fact]
    public void TheSkipReportListsEverySkippedSymbolAndIsDeterministic()
    {
        string report = Generated.SkipReport;

        Assert.StartsWith("<!-- Generated by GstSharp.Generator. Do not edit. -->\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", report, StringComparison.Ordinal);
        Assert.Contains("## Gst\n", report, StringComparison.Ordinal);
        // Nothing is rejected for caller allocated storage any more: a boxed
        // record is allocated from its own constructor, and everything else
        // is hand written or answered by a sibling, so the section is gone
        // from the report and the entry points the overlays took over are
        // named under the overlay skips instead.
        Assert.DoesNotContain("### CallerAllocates", report, StringComparison.Ordinal);
        Assert.Contains("### OverlaySkip (27)\n", report, StringComparison.Ordinal);
        Assert.Contains("- `GstApp.AppSrc::push-buffer`\n", report, StringComparison.Ordinal);

        // The hand bound ledger takes precedence over the reason that kept a
        // symbol out, so gst_video_frame_map is filed under the hand bound
        // section of its own module - GstVideo, not the Gst one - and not
        // under the overlay skips it is also listed in. The whole section is
        // anchored, because a lone "- `symbol`" line matches under any reason
        // and in any module.
        Assert.Contains("### HandBound (73)\n", report, StringComparison.Ordinal);
        Assert.Contains(
            "### HandBound (12)\n\n"
            + "- `gst_buffer_add_video_gl_texture_upload_meta`\n"
            + "- `gst_video_codec_frame_set_user_data`\n"
            + "- `gst_video_frame_map`\n"
            + "- `gst_video_frame_map_id`\n"
            + "- `gst_video_frame_unmap`\n"
            + "- `gst_video_gl_texture_upload_meta_upload`\n"
            + "- `gst_video_vbi_encoder_copy`\n"
            + "- `gst_video_vbi_encoder_new`\n"
            + "- `gst_video_vbi_encoder_write_line`\n"
            + "- `gst_video_vbi_parser_add_line`\n"
            + "- `gst_video_vbi_parser_copy`\n"
            + "- `gst_video_vbi_parser_new`\n",
            report,
            StringComparison.Ordinal);

        Assert.Equal(report, GenerationPipeline.Run(GirFixture.GirDirectory).SkipReport, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Gst", 48)]
    [InlineData("GstBase", 0)]
    [InlineData("GstAudio", 15)]
    [InlineData("GstVideo", 33)]
    [InlineData("GstSdp", 33)]
    [InlineData("GstWebRTC", 0)]
    [InlineData("GstNet", 2)]
    [InlineData("GstRtsp", 1)]
    [InlineData("GstRtp", 3)]
    [InlineData("GstRtspServer", 5)]
    [InlineData("GstAllocators", 0)]
    [InlineData("GstTag", 0)]
    [InlineData("GstTranscoder", 0)]
    [InlineData("GstPlay", 0)]
    [InlineData("GES", 1)]
    [InlineData("GstApp", 0)]
    [InlineData("GstPbutils", 0)]
    public void TheFieldLedgerIsStable(string module, int fields)
    {
        // Public record fields that carry API in C and none in C#. The ledger
        // exists because a field has no skip reason of its own: without it a
        // record whose methods are bound reads as fully bound however many of
        // its fields are missing, which is how the fixed size fields of
        // GstVideoInfo went unnoticed. GstApp, GstBase, GstPbutils, GstPlay and
        // GstWebRTC declare no record field that is left out at all.
        Assert.Equal(fields, Generated.Census.DroppedFieldCount(module));
    }

    [Fact]
    public void TheSkipReportCarriesTheFieldLedger()
    {
        string report = Generated.SkipReport;

        Assert.Equal(141, Generated.Census.DroppedFieldCount());
        Assert.Contains("## Fields (141)\n", report, StringComparison.Ordinal);
        Assert.Contains("### GstVideo (33)\n", report, StringComparison.Ordinal);

        // One entry per shape that keeps a field out. The fixed size fields of
        // GstVideoInfo are bound and are therefore absent; the ones whose
        // elements are pointers or structures are not.
        // The pool of a buffer is bound: the field holds a reference of its own
        // and only the disposal of the buffer clears it, so a holder of the
        // buffer never reads a pool that is gone. The one the overlays still
        // hold back is the iterator a parent iterator pushed, which a copy of
        // the parent would alias and free twice.
        Assert.DoesNotContain("- `Buffer.pool`", report, StringComparison.Ordinal);
        Assert.Contains("- `Iterator.pushed` \u2014 Pointer\n", report, StringComparison.Ordinal);
        Assert.Contains("- `Buffer.mini_object` \u2014 EmbeddedStruct\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `VideoInfo.colorimetry`", report, StringComparison.Ordinal);
        Assert.Contains("- `Iterator.next` \u2014 Callback\n", report, StringComparison.Ordinal);
        // The variant union of GstRTSPMessage still stops the layout and is
        // still listed under its own name. A reserved ABI union is laid out
        // instead, so what is listed of one is its members: none of the
        // strings behind the reserve of GstWebRTCICECandidateStats, which the
        // accessors of a string read, and the four members GstVideoCodecFrame
        // keeps to its own implementation as Private.
        Assert.Contains("- `RTSPMessage.type_data` \u2014 Union\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `VideoInfo.ABI`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `VideoInfo.multiview_mode`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `WebRTCICECandidateStats.foundation`", report, StringComparison.Ordinal);
        Assert.Contains("- `VideoCodecFrame.ts` \u2014 Private\n", report, StringComparison.Ordinal);
        Assert.Contains(
            "- `VideoFormatInfo.tile_info` \u2014 InlineArray(struct element)\n",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "- `VideoFrame.data` \u2014 InlineArray(pointer element)\n",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain("- `VideoInfo.stride`", report, StringComparison.Ordinal);

        // A value projected structure declares its fields itself, so a typed
        // public field of one is bound although no accessor reads it:
        // GstRTSPTimeRange embeds four GstRTSPTime by value and hands all four
        // out. A field that lands on a machine address is not bound, and
        // neither is one that only a hand written member reads through, which
        // is what keeps the two ends of the rule from drifting. The nick of a
        // format definition is off the ledger all the same: the address stayed
        // where it is and the string accessor beside it is what binds it.
        Assert.DoesNotContain("- `RTSPTimeRange.min`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `VideoMetaTransformMatrix.in_rectangle`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `FormatDefinition.nick`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `VideoMetaTransform.in_info`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `VideoInfo.finfo` \u2014 Pointer", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `AudioInfo.finfo` \u2014 Pointer", report, StringComparison.Ordinal);
        Assert.Contains(
            "- `MapInfo.user_data` \u2014 InlineArray(pointer element)\n",
            report,
            StringComparison.Ordinal);

        // The catch all is split by the cause a field reaches it through: a
        // wrapper the generator never asks for accessors, and a record whose
        // mirror collapsed and has no storage to read one out of. The second
        // half is empty since the two GstParamSpec shells were skipped: their
        // fields were the only ones the generator had no layout for. The
        // catch all is read on this ledger alone, because the one below
        // refines nothing and answers for the shapes no rule of the layout
        // ever looked at.
        string ledger = FieldLedger(report);
        Assert.DoesNotContain("\u2014 Other\n", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2014 NoLayout\n", ledger, StringComparison.Ordinal);
        Assert.Contains("- `MiniObject.type` \u2014 HandWritten\n", report, StringComparison.Ordinal);

        // An enumeration another generated module declares is handed out typed,
        // so the field it sits on is bound rather than counted.
        Assert.DoesNotContain("- `AudioClippingMeta.format`", report, StringComparison.Ordinal);

        // A pointer to a plain structure is copied out on read rather than left
        // on the ledger, which is what binds the HDR metadata of a codec state.
        Assert.DoesNotContain("- `VideoCodecState.content_light_level`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `VideoCodecState.mastering_display_info`", report, StringComparison.Ordinal);

        // A field that arrived after the support floor carries no accessor
        // whatever its shape, because the structure of an older library is not
        // long enough to hold it; the line says which version put it there.
        // The one such field a hand written accessor now answers behind a
        // runtime version check, ReferenceTimestampMeta.info (handed out as
        // GetInfoStructure()), is off this ledger and on the one below, the
        // way every hand written field is.
        Assert.Contains("- `ValueTable.hash` \u2014 Callback, since 1.28\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "- `ReferenceTimestampMeta.info` \u2014 Pointer, since 1.28\n",
            report,
            StringComparison.Ordinal);

        // Padding is off the ledger whether or not the gir annotates it.
        Assert.DoesNotContain("_gst_reserved", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSkipReportCarriesTheFieldsThatAreAnsweredElsewhere()
    {
        // A field the overlays name a member for is not a gap, so it is kept
        // out of the ledger above and listed here with what answers it. The
        // flow return of a pad probe is one: the C function
        // gst_pad_probe_info_get_flow_return reads that very field, and the
        // generated pair is what a caller uses. The others are the fields a
        // hand written member of Custom/ reads through and the ones a generated
        // C accessor already answers.
        string report = Generated.SkipReport;

        Assert.Equal(23, Generated.Census.ExposedFieldCount());
        Assert.Contains("## Fields exposed elsewhere (23)\n", report, StringComparison.Ordinal);
        Assert.Contains(
            "### Gst (7)\n\n- `CustomMeta.structure` — GetStructure\n"
            + "- `Message.src` — hand written\n"
            + "- `Meta.info` — hand written\n"
            + "- `PadProbeInfo.data` — GetBuffer, GetBufferList, GetEvent and GetQuery\n"
            + "- `PadProbeInfo.flow_ret` — GetFlowReturn\n"
            + "- `ReferenceTimestampMeta.info` — hand written\n"
            + "- `StaticCaps.caps` — Get\n",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "### GstPlay (2)\n\n- `PlayVisualization.description` — hand written\n"
            + "- `PlayVisualization.name` — hand written\n",
            report,
            StringComparison.Ordinal);

        // The buffer of a collect data is the other shape an entry answers: a
        // generated member of another wrapper reads the field, under the lock
        // the C accessor takes, so the field carries no accessor of its own.
        Assert.Contains(
            "### GstBase (1)\n\n- `CollectData.buffer` — CollectPads.Peek\n",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "### GstAudio (3)\n\n- `AudioBuffer.buffer` — hand written\n"
            + "- `AudioBuffer.info` — hand written\n"
            + "- `AudioInfo.finfo` — hand written\n",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain("- `PadProbeInfo.flow_ret` — Other", report, StringComparison.Ordinal);

        // An instance field of a class is answered the same way, and the three
        // that are come from the one wrapper that mirrors an instance head by
        // hand: the label and the range of a colour balance channel.
        Assert.Contains(
            "- `ColorBalanceChannel.label` — hand written\n"
            + "- `ColorBalanceChannel.max_value` — hand written\n"
            + "- `ColorBalanceChannel.min_value` — hand written\n",
            report,
            StringComparison.Ordinal);
        Assert.DoesNotContain("- `ColorBalanceChannel.", ClassFieldLedger(report), StringComparison.Ordinal);

        // The accessor is not emitted either, which is what lets an entry
        // answer a name a hand written member already carries.
        Assert.DoesNotContain("public Gst.FlowReturn FlowRet", Source("PadProbeInfo.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Gst", 69)]
    [InlineData("GstBase", 38)]
    [InlineData("GstAudio", 36)]
    [InlineData("GstVideo", 8)]
    [InlineData("GstSdp", 0)]
    [InlineData("GstWebRTC", 11)]
    [InlineData("GstNet", 0)]
    [InlineData("GstRtsp", 0)]
    [InlineData("GstRtp", 10)]
    [InlineData("GstRtspServer", 2)]
    [InlineData("GstAllocators", 0)]
    [InlineData("GstTag", 0)]
    [InlineData("GstTranscoder", 0)]
    [InlineData("GstPlay", 0)]
    [InlineData("GES", 24)]
    [InlineData("GstApp", 0)]
    [InlineData("GstPbutils", 3)]
    public void TheClassFieldLedgerIsStable(string module, int fields)
    {
        // The public instance fields of the emitted classes. A wrapper holds a
        // native instance and mirrors no part of the structure, so every one of
        // them is unprojected and the ledger counts the whole of them rather
        // than what a rule dropped. The modules at zero declare classes that
        // are opaque to the gir, carry nothing but padding, or declare no
        // class at all.
        Assert.Equal(fields, Generated.Census.ClassFieldCount(module));
    }

    [Fact]
    public void TheSkipReportCarriesTheClassFieldLedger()
    {
        string report = Generated.SkipReport;
        string ledger = ClassFieldLedger(report);

        Assert.Equal(201, Generated.Census.ClassFieldCount());
        Assert.Contains("## Class fields (201)\n", report, StringComparison.Ordinal);
        Assert.Contains("### Gst (69)\n", ledger, StringComparison.Ordinal);

        // One line per shape the ledger measures. The shapes are what says how
        // much an exposure would have to marshal: a plain value, a machine
        // address, a structure laid into the instance, a function pointer slot
        // the class carries itself, and a union the C structure grew by.
        Assert.Contains("- `AudioRingBuffer.acquired` — Scalar\n", ledger, StringComparison.Ordinal);
        Assert.Contains("- `Object.name` — Pointer\n", ledger, StringComparison.Ordinal);
        Assert.Contains("- `AudioRingBuffer.spec` — EmbeddedStruct\n", ledger, StringComparison.Ordinal);
        Assert.Contains("- `Allocator.mem_map` — Callback\n", ledger, StringComparison.Ordinal);
        Assert.Contains("- `Pad.ABI` — Union\n", ledger, StringComparison.Ordinal);

        // A lock a class embeds sits in the instance the way any structure laid
        // into it does, whether GLib spells it a union (GMutex) or a record
        // (GRecMutex), and the ledger reads the two the same. Nothing is left
        // for the catch all: every field of the section names a shape.
        Assert.Contains("- `Object.lock` — EmbeddedStruct\n", ledger, StringComparison.Ordinal);
        Assert.Contains("- `Element.state_lock` — EmbeddedStruct\n", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("— Other\n", ledger, StringComparison.Ordinal);

        // What the hand written mirror of an instance head reads internally
        // stays on the ledger: the ring buffer checks its own acquired flag and
        // the channel count of its spec, and neither is a member of the binding.
        Assert.DoesNotContain("- `AudioRingBuffer.acquired` —", FieldLedger(report), StringComparison.Ordinal);

        // The instance structure of the base class is the inheritance chain and
        // not a member, whatever the gir names it, and padding and the fields
        // the gir keeps to the C implementation carry no API in C either.
        Assert.DoesNotContain("- `Element.object`", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Bin.element`", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("- `TimelineElement.parent_instance`", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("- `NtpClock.clock`", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("_gst_reserved", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Pad.stream_rec_lock`", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Bin.priv`", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommittedSkipReportIsUpToDate()
    {
        string committed = File.ReadAllText(
            Path.Combine(GirFixture.GirDirectory, GenerationPipeline.SkipReportFileName));

        Assert.Equal(
            Generated.SkipReport,
            committed.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheCommittedOverlaysCarryNoStaleEntry()
    {
        // Fourteen of the seventeen name an overlay entry that matched nothing: an
        // array correction on no array (GEN0020), a hand bound ledger entry
        // the run never saw skipped (GEN0023), an annotation override on no
        // callable, parameter or signal argument (GEN0024), a field skip on no
        // field of an emitted record or class (GEN0025), a field annotation that
        // corrected no field (GEN0026), a documentation note on no rendered
        // callable (GEN0042) or on no rendered signal (GEN0048), and a
        // precondition on no rendered callable (GEN0049), and a hand over
        // refusal on no rendered callable (GEN0051), and a documentation
        // strip on no rendered callable (GEN0053), and a skip entry spelled as
        // a signal that matched no signal of an emitted type (GEN0055), a skip
        // entry of any other shape - the c:identifier of a callable, a
        // qualified type name, a property - that matched nothing either
        // (GEN0056), a rename the run never looked up (GEN0057), and a floating
        // return on no slot of a subclassable class (GEN0058). Every one of
        // them describes a gir that has moved on, and every one of them is a
        // warning, which the verbs do not fail on - so this is what holds the
        // committed overlays to them.
        //
        // GEN0060 is the one of the eighteen that is an error rather than a
        // warning: an instance field entry that matched nothing is the only
        // reason a public accessor of that field exists, so the verbs do fail on
        // it. It is asserted here beside the others so that the suite names the
        // entry rather than only the exit code.
        //
        // GEN0058 is the one vfunc key of its family asserted here, because it
        // is the one whose loss is silent in a new way: the slot keeps its
        // member and its census row, and what disappears is the reference the
        // trampoline mints, which turns a floating hand out back into a borrow
        // the caller of the slot owns nothing of.
        //
        // GEN0064 is the other error of the family, and it covers both halves at
        // once: a type documentation replacement whose 'old' no longer stands in
        // the documentation of the class exactly once, and one that names no
        // class this run rendered. Either way the generated type carries the
        // upstream sentence the entry exists to rewrite - a documented string
        // grammar the library does not write that way - so the run fails rather
        // than publishing it.
        //
        // The seventeenth is not stale but illegal: a 'borrow' on a key that is
        // no argument of a signal at all - a parameter of a method or of a
        // callback, an argument of a virtual method, a return - or on a signal
        // argument the planner does not project onto a mini object or a boxed
        // wrapper (GEN0054). It is an error rather than a warning, so the verbs
        // do fail on it, and it is asserted here beside the others so that the
        // suite names the entry rather than only the exit code.
        //
        // The other two are the entries a gir refresh silences without
        // renaming the key at all: a hand over refusal whose callable no
        // longer has the shape the release is written for (GEN0050), and a
        // documentation strip whose sentence upstream reworded (GEN0052). The
        // key is still read, so no stale report follows it; what disappears is
        // the release, or the sentence returns to the member. Neither would be
        // caught by the thirteen above, which is why both are asserted here
        // too.
        //
        // GEN0049, GEN0050 and GEN0051 are the ones with teeth. A key a gir
        // refresh renamed or a member whose shape it changed stops being
        // applied, and the member is regenerated without the statement that
        // kept it off a NULL dereference or off a bus that cannot deliver, or
        // without the release that kept its refusal path from leaking a
        // reference. Nothing else moves: the helper in Custom/ is still there,
        // so the build stays warning free, and verify is clean the moment the
        // new output is committed.
        //
        // GEN0026 is the one that goes wrong most quietly. A stale
        // 'nullable: false' key - a field a gir refresh renamed or removed -
        // stops being applied, and the accessor that was a non nullable string
        // becomes a 'string?'. Nothing else moves: the field is still bound, so
        // no census number changes and no ledger line appears, and the first
        // report of it would be the surface check failing on a package that is
        // already being published.
        foreach (Diagnostic diagnostic in Generated.Diagnostics)
        {
            Assert.NotEqual("GEN0020", diagnostic.Code);
            Assert.NotEqual("GEN0023", diagnostic.Code);
            Assert.NotEqual("GEN0024", diagnostic.Code);
            Assert.NotEqual("GEN0025", diagnostic.Code);
            Assert.NotEqual("GEN0026", diagnostic.Code);
            Assert.NotEqual("GEN0042", diagnostic.Code);
            Assert.NotEqual("GEN0048", diagnostic.Code);
            Assert.NotEqual("GEN0049", diagnostic.Code);
            Assert.NotEqual("GEN0050", diagnostic.Code);
            Assert.NotEqual("GEN0051", diagnostic.Code);
            Assert.NotEqual("GEN0052", diagnostic.Code);
            Assert.NotEqual("GEN0053", diagnostic.Code);
            Assert.NotEqual("GEN0054", diagnostic.Code);
            Assert.NotEqual("GEN0055", diagnostic.Code);
            Assert.NotEqual("GEN0056", diagnostic.Code);
            Assert.NotEqual("GEN0057", diagnostic.Code);
            Assert.NotEqual("GEN0058", diagnostic.Code);
            Assert.NotEqual("GEN0060", diagnostic.Code);
            Assert.NotEqual("GEN0064", diagnostic.Code);
        }
    }

    /// <summary>
    /// Exactly one committed <c>skipVirtuals</c> key names a class that is not
    /// subclassable: the chain-only mirror whose slot is hand bound for the
    /// classes below it.
    /// </summary>
    /// <remarks>
    /// GEN0029 cannot report this. It holds the skipped keys against the slots
    /// the mirrors lay out, and a class taken off the subclassable list keeps
    /// its mirror, so its entries go on reading as ledger reasons for a surface
    /// that is gone. Pinning the set is the report the run has no way to write:
    /// the moment a class with skip entries leaves the allowlist, this fails and
    /// its reasons are re-read by hand.
    /// </remarks>
    [Fact]
    public void OneSkippedVirtualNamesAClassThatIsNotSubclassable()
    {
        List<string> chainOnly = [];
        foreach (string key in GirFixture.Overlays.SkippedVirtualKeys)
        {
            int separator = key.IndexOf("::", StringComparison.Ordinal);
            Assert.True(separator > 0, "The skipped virtual key " + key + " names no class.");

            if (!GirFixture.Overlays.IsSubclassable(key[..separator]))
            {
                chainOnly.Add(key);
            }
        }

        Assert.Equal("GES.Container::ungroup", Assert.Single(chainOnly));
    }

    [Fact]
    public void EveryGeneratedFileHasItsOwnPath()
    {
        HashSet<string> paths = new(StringComparer.Ordinal);

        foreach (GeneratedFile file in Generated.Files)
        {
            Assert.True(paths.Add(file.RelativePath), "Two emitters claim " + file.RelativePath + ".");
        }
    }

    [Fact]
    public void TheOutputIsDeterministic()
    {
        GenerationResult second = GenerationPipeline.Run(GirFixture.GirDirectory);

        Assert.Equal(Generated.Files.Count, second.Files.Count);
        for (int i = 0; i < second.Files.Count; i++)
        {
            Assert.Equal(Generated.Files[i].RelativePath, second.Files[i].RelativePath, StringComparer.Ordinal);
            Assert.Equal(Generated.Files[i].Content, second.Files[i].Content, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void NothingIsEmittedForAFundamentalWithoutFunctionsOrForAVirtualMethod()
    {
        // A GType fundamental has no instance structure to wrap, so nothing is
        // emitted for one that declares no function of its own - GstFraction,
        // GstBitmask and the range types are read and written through the
        // gst_value_* family instead. Vfuncs need subclassing support that does
        // not exist yet.
        Assert.False(HasFile("Fraction.cs"));
        Assert.False(HasFile("Bitmask.cs"));
        Assert.False(HasFile("IntRange.cs"));
        Assert.DoesNotContain("virtual", Source("Element.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFundamentalsThatDeclareFunctionsBecomeStaticHolders()
    {
        // Exactly the three value containers. Any other fundamental that grows
        // a function - or one whose functions stop being skipped, which is what
        // gst_flagset_register is held back by - is a surface nobody decided
        // on, so the count is frozen rather than derived.
        Assert.Equal(3, Generated.Census.EmittedCount("Gst", "value container"));
        foreach (string name in new[] { "ValueArray", "ValueList", "ValueUniqueList" })
        {
            Assert.Contains(
                "public static unsafe partial class " + name + "\n",
                Source(name + ".cs"),
                StringComparison.Ordinal);
        }

        // The holder is not a wrapper, so it is not in the type table.
        Assert.DoesNotContain("Gst.ValueList.GetGType", Source("_Module.cs"), StringComparison.Ordinal);
        Assert.False(HasFile("FlagSet.cs"));
    }

    private static bool HasFile(string fileName) =>
        Generated.Files.Any(file => file.RelativePath.EndsWith("/" + fileName, StringComparison.Ordinal));

    /// <summary>
    /// The overlay entries of the RTSP server, read off the surface they
    /// produce rather than off the file that asks for them.
    /// </summary>
    [Fact]
    public void TheRtspServerOverlaysReachTheEmittedSurface()
    {
        // The three returns that answer NULL on ordinary input are nullable,
        // so a caller reads the answer instead of catching an exception.
        Assert.Contains(
            "public Gst.RtspServer.RTSPMediaFactory? Match(string path, out int matched)",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPMountPoints.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public Gst.Structure? GetRole(string role)",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPPermissions.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public string? GetUri()",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPMediaFactoryURI.cs"),
            StringComparison.Ordinal);

        // gst_rtsp_media_new takes its element with transfer none after the
        // correction, so the wrapper the caller passed stays alive.
        string media = SourceOf("GstSharp.Net.RtspServer/Generated/RTSPMedia.cs");
        Assert.Contains(
            "public static Gst.RtspServer.RTSPMedia New(Gst.Element element)",
            media,
            StringComparison.Ordinal);
        Assert.DoesNotContain("element.Dispose();", media, StringComparison.Ordinal);

        // The three ONVIF factories are narrowed onto the type they construct.
        Assert.Contains(
            "public static new Gst.RtspServer.RTSPOnvifServer New()",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPOnvifServer.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public static new Gst.RtspServer.RTSPOnvifClient New()",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPOnvifClient.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public static new Gst.RtspServer.RTSPOnvifMediaFactory New()",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPOnvifMediaFactory.cs"),
            StringComparison.Ordinal);

        // The signal the rename was written for is skipped by the overlays
        // now - the gir names an argument type the C never registered - so
        // nothing of it is generated and the event is written by hand under
        // the name the rename decided. The method whose name it had taken
        // stands, and the two send function setters are gone.
        string client = SourceOf("GstSharp.Net.RtspServer/Generated/RTSPClient.cs");
        Assert.DoesNotContain("SendingMessage", client, StringComparison.Ordinal);
        Assert.DoesNotContain("send-message", client, StringComparison.Ordinal);
        Assert.Contains(
            "public Gst.Rtsp.RTSPResult SendMessage(",
            client,
            StringComparison.Ordinal);
        Assert.DoesNotContain("public void SetSendFunc(", client, StringComparison.Ordinal);
        Assert.DoesNotContain("public void SetSendMessagesFunc(", client, StringComparison.Ordinal);

        // The mount of a factory is generated again, now that a transfer full
        // GObject argument is handed over rather than consumed, and it carries
        // the path check the C answers with a g_return_if_fail as a
        // precondition of the overlays.
        string mountPoints = SourceOf("GstSharp.Net.RtspServer/Generated/RTSPMountPoints.cs");

        Assert.Contains(
            "public void AddFactory(string path, Gst.RtspServer.RTSPMediaFactory factory)",
            mountPoints,
            StringComparison.Ordinal);
        Assert.Contains(
            """
                    if (path.Length == 0 || path[0] != '/') { throw new ArgumentException("A mount point has to begin with '/'.", nameof(path)); }
            """,
            mountPoints,
            StringComparison.Ordinal);
        Assert.DoesNotContain("factory.Dispose();", mountPoints, StringComparison.Ordinal);

        // The rest of the skip group leaves no member behind either.
        Assert.DoesNotContain(
            "WritableStructure",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPToken.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public Gst.RtspServer.RTSPThread? GetThread(",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPThreadPool.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public static Gst.RtspServer.RTSPThread New(",
            SourceOf("GstSharp.Net.RtspServer/Generated/RTSPThread.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>The section of the report that lists the record fields.</summary>
    /// <param name="report">The whole report.</param>
    /// <returns>The text of the section.</returns>
    private static string FieldLedger(string report) =>
        Section(report, "## Fields (", "## Fields exposed elsewhere (");

    /// <summary>The section of the report that lists the instance fields of the classes.</summary>
    /// <param name="report">The whole report.</param>
    /// <returns>The text of the section, to the end of the report.</returns>
    private static string ClassFieldLedger(string report) => Section(report, "## Class fields (", null);

    /// <summary>
    /// One section of the report, so that an assertion about what a section
    /// does not carry reads the section and not the whole document.
    /// </summary>
    /// <param name="report">The whole report.</param>
    /// <param name="heading">The heading the section starts at.</param>
    /// <param name="next">The heading that ends it, or <see langword="null"/> for the last one.</param>
    /// <returns>The text of the section.</returns>
    private static string Section(string report, string heading, string? next)
    {
        int start = report.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, "The report carries no '" + heading + "' section.");

        if (next is null)
        {
            return report[start..];
        }

        int end = report.IndexOf(next, start, StringComparison.Ordinal);
        Assert.True(end >= 0, "The report carries no '" + next + "' section.");
        return report[start..end];
    }

    private static string SourceOf(string path)
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException("The run produced no '" + path + "'.");
    }

    private static string Source(string fileName)
    {
        string path = "GstSharp.Net/Generated/" + fileName;
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException("The run produced no " + path + ".");
    }

    /// <summary>
    /// The counts of the rules that this milestone added. They are frozen
    /// separately from the older ones, because each of them stands for a
    /// binding that used to be emitted and corrupt memory.
    /// </summary>
    /// <param name="module">The gir namespace to read.</param>
    /// <param name="overlaySkip">Callables that fixups.json lists and the hand
    /// bound ledger does not claim. Every count of this theory is of gir
    /// declarations rather than of identifiers, so a function the gir declares
    /// twice - once inside the record it belongs to and once at namespace
    /// level, where it used to be counted under MovedTo - is counted twice
    /// here while skip-report.md lists it once. gst_rtsp_range_parse,
    /// gst_rtsp_range_free and gst_meta_api_type_set_params_aggregator are of
    /// that shape, which is why these numbers run above the counts of the
    /// report; the same holds for the hand bound ones, where
    /// gst_meta_api_type_aggregate_params, gst_meta_register_custom,
    /// gst_tag_list_copy_value, gst_audio_buffer_map, gst_video_frame_map,
    /// gst_video_frame_map_id, gst_rtsp_transport_parse,
    /// gst_transcoder_message_parse_error,
    /// gst_transcoder_message_parse_warning,
    /// gst_play_message_parse_error_missing_plugin and
    /// gst_play_message_parse_warning_missing_plugin are declared twice. That
    /// is the whole of the difference: Gst, GstAudio, GstVideo, GstRtsp,
    /// GstTranscoder and GstPlay each count more hand bound declarations than
    /// the report lists symbols, by the twins named above, and every other
    /// module counts the same on both sides.
    /// All eleven are on the skip list, so both of their declarations are
    /// rejected as an overlay skip and the ledger claims both; a twin that is
    /// only kept out by its own moved-to is left under MovedTo instead, which
    /// is what lets GEN0023 see a ledger entry on a generated
    /// symbol.</param>
    /// <param name="callerAllocates">Callables with unusable caller allocated storage.</param>
    /// <param name="lifetime">Callables that release or reference their instance.</param>
    /// <param name="instanceTransfer">Callables that consume their instance and replace it.</param>
    /// <param name="actionSignals">Signals that are a call API rather than a notification.</param>
    /// <param name="owningProperties">Properties whose value is a wrapper the reader would have to dispose.</param>
    /// <param name="handBound">Callables the hand written surface already covers. They are counted here rather
    /// than under the reason that kept them out of the emitters, which is why the overlay skips of a module
    /// fall by the number of its hand bound entries that reach the census through the skip list.</param>
    [Theory]
    // The hand bound ledger grew by the two walks over a lent slot, the sticky
    // events of a pad and the buffers of a list.
    [InlineData("Gst", 28, 0, 21, 0, 0, 5, 76)]
    [InlineData("GstBase", 2, 0, 4, 0, 0, 2, 3)]
    [InlineData("GstApp", 0, 0, 2, 0, 9, 2, 7)]
    [InlineData("GstAudio", 9, 0, 4, 0, 0, 0, 9)]
    [InlineData("GstVideo", 9, 0, 10, 0, 0, 0, 14)]
    [InlineData("GstPbutils", 1, 0, 1, 0, 0, 1, 3)]
    [InlineData("GstSdp", 4, 0, 1, 0, 0, 0, 2)]
    [InlineData("GstWebRTC", 1, 0, 4, 0, 4, 0, 6)]
    [InlineData("GstNet", 0, 0, 1, 0, 0, 0, 0)]
    [InlineData("GstRtsp", 8, 0, 3, 0, 0, 0, 4)]
    [InlineData("GstRtp", 2, 0, 0, 0, 0, 0, 16)]
    [InlineData("GstRtspServer", 4, 0, 1, 0, 0, 0, 5)]
    [InlineData("GstAllocators", 0, 0, 0, 0, 0, 0, 0)]
    [InlineData("GstTag", 0, 0, 0, 0, 0, 0, 0)]
    [InlineData("GstTranscoder", 0, 0, 0, 0, 0, 0, 4)]
    [InlineData("GstPlay", 6, 0, 1, 0, 0, 0, 16)]
    // The hand bound ledger grew by the walk over the metadata of a container.
    [InlineData("GES", 3, 0, 0, 0, 0, 2, 9)]
    public void TheRejectionCensusIsStable(
        string module,
        int overlaySkip,
        int callerAllocates,
        int lifetime,
        int instanceTransfer,
        int actionSignals,
        int owningProperties,
        int handBound)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(overlaySkip, census.SkippedCount(module, SkipReason.OverlaySkip));
        Assert.Equal(callerAllocates, census.SkippedCount(module, SkipReason.CallerAllocates));
        Assert.Equal(lifetime, census.SkippedCount(module, SkipReason.LifetimePrimitive));
        Assert.Equal(instanceTransfer, census.SkippedCount(module, SkipReason.InstanceTransferFull));
        Assert.Equal(actionSignals, census.SkippedCount(module, SkipReason.ActionSignal));
        Assert.Equal(owningProperties, census.SkippedCount(module, SkipReason.OwningProperty));
        Assert.Equal(handBound, census.SkippedCount(module, SkipReason.HandBound));
    }

    [Theory]
    [InlineData("GstSharp.Net.Base/Generated/BaseSink.cs", "public abstract unsafe partial class BaseSink : Gst.Element")]
    [InlineData("GstSharp.Net.Base/Generated/PushSrc.cs", "public unsafe partial class PushSrc : Gst.Base.BaseSrc")]
    [InlineData("GstSharp.Net.App/Generated/AppSink.cs", "public unsafe partial class AppSink : Gst.Base.BaseSink, Gst.IURIHandler")]
    [InlineData("GstSharp.Net.App/Generated/AppSrc.cs", "public unsafe partial class AppSrc : Gst.Base.BaseSrc, Gst.IURIHandler")]
    [InlineData("GstSharp.Net.Video/Generated/VideoSink.cs", "public unsafe partial class VideoSink : Gst.Base.BaseSink")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioClock.cs", "public unsafe partial class AudioClock : Gst.SystemClock")]
    [InlineData("GstSharp.Net.Pbutils/Generated/AudioVisualizer.cs", "public abstract unsafe partial class AudioVisualizer : Gst.Element")]
    [InlineData("GstSharp.Net.GES/Generated/Timeline.cs", "public unsafe partial class Timeline : Gst.Bin, GES.IExtractable, GES.IMetaContainer, Gst.IChildProxy")]
    [InlineData("GstSharp.Net.GES/Generated/Pipeline.cs", "public unsafe partial class Pipeline : Gst.Pipeline, Gst.IChildProxy, Gst.Video.IVideoOverlay")]
    public void AClassDerivesAcrossModuleBoundaries(string path, string declaration)
    {
        Assert.Contains(declaration + "\n", SourceOf(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Gst", "GstSharp.Net")]
    [InlineData("GstBase", "GstSharp.Net.Base")]
    [InlineData("GstApp", "GstSharp.Net.App")]
    [InlineData("GstAudio", "GstSharp.Net.Audio")]
    [InlineData("GstVideo", "GstSharp.Net.Video")]
    [InlineData("GstPbutils", "GstSharp.Net.Pbutils")]
    [InlineData("GstSdp", "GstSharp.Net.Sdp")]
    [InlineData("GstWebRTC", "GstSharp.Net.WebRTC")]
    [InlineData("GstNet", "GstSharp.Net.Net")]
    [InlineData("GstRtsp", "GstSharp.Net.Rtsp")]
    [InlineData("GstRtp", "GstSharp.Net.Rtp")]
    [InlineData("GstRtspServer", "GstSharp.Net.RtspServer")]
    [InlineData("GstAllocators", "GstSharp.Net.Allocators")]
    [InlineData("GstTag", "GstSharp.Net.Tag")]
    [InlineData("GstTranscoder", "GstSharp.Net.Transcoder")]
    [InlineData("GstPlay", "GstSharp.Net.Play")]
    [InlineData("GES", "GstSharp.Net.GES")]
    public void EveryModuleEmitsItsOwnTypeTable(string module, string projectDirectory)
    {
        string source = SourceOf(projectDirectory + "/Generated/_Module.cs");

        Assert.Contains(
            "internal static unsafe partial class " + module + "Module\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains("internal static Gst.Interop.ModuleTypeEntry[] CreateEntries() =>", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GstSharp.Net/Generated/StateExtensions.cs", "public static string GetName(Gst.State state)")]
    [InlineData("GstSharp.Net/Generated/EventTypeExtensions.cs", "public static string GetName(Gst.EventType type)")]
    [InlineData("GstSharp.Net.Video/Generated/VideoFormatExtensions.cs", "public static string ToString(Gst.Video.VideoFormat format)")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioFormatExtensions.cs", "public static string ToString(Gst.Audio.AudioFormat format)")]
    [InlineData("GstSharp.Net.Pbutils/Generated/InstallPluginsReturnExtensions.cs", "public static string GetName(Gst.Pbutils.InstallPluginsReturn ret)")]
    public void TheFunctionsOfAnEnumerationLandOnAHolderNamedAfterIt(string path, string signature)
    {
        Assert.Contains(signature + "\n", SourceOf(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GstSharp.Net/Generated/Global.cs", "public static unsafe partial class Global")]
    [InlineData("GstSharp.Net.Base/Generated/BaseGlobal.cs", "public static unsafe partial class BaseGlobal")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioGlobal.cs", "public static unsafe partial class AudioGlobal")]
    [InlineData("GstSharp.Net.Video/Generated/VideoGlobal.cs", "public static unsafe partial class VideoGlobal")]
    [InlineData("GstSharp.Net.Pbutils/Generated/PbutilsGlobal.cs", "public static unsafe partial class PbutilsGlobal")]
    [InlineData("GstSharp.Net.Sdp/Generated/SdpGlobal.cs", "public static unsafe partial class SdpGlobal")]
    [InlineData("GstSharp.Net.Net/Generated/NetGlobal.cs", "public static unsafe partial class NetGlobal")]
    [InlineData("GstSharp.Net.Rtsp/Generated/RtspGlobal.cs", "public static unsafe partial class RtspGlobal")]
    [InlineData("GstSharp.Net.GES/Generated/GESGlobal.cs", "public static unsafe partial class GESGlobal")]
    public void TheGlobalHolderOfAnExtensionModuleCarriesItsModuleName(string path, string declaration)
    {
        // Nine types named Global, one per module, read as one type that keeps
        // changing shape once several modules are referenced together. Only the
        // core module keeps the plain name. The editing services are the one
        // module whose C# namespace has no dot in it, and the last segment of
        // it is the whole of it, so the holder is GESGlobal.
        Assert.Contains(declaration + "\n", SourceOf(path), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryInstanceMemberKeepsItsWrapperAliveAcrossTheCall()
    {
        // The call takes the raw handle out of the wrapper and nothing mentions
        // the wrapper afterwards, so without the barrier the finalizer may
        // release the instance while the call is still running.
        int calls = 0;
        int barriers = 0;
        foreach (GeneratedFile file in Generated.Files)
        {
            calls += file.Content.Split("(Handle, ").Length - 1;
            calls += file.Content.Split("(Handle)").Length - 1;
            barriers += file.Content.Split("System.GC.KeepAlive(").Length - 1;
        }

        Assert.True(calls > 0);
        Assert.True(
            barriers >= calls,
            $"{calls} call(s) take the raw handle of an instance but only {barriers} barrier(s) are emitted.");
    }

    [Theory]
    [InlineData(
        "GstSharp.Net.App/Generated/AppSrcSimpleCallbacks.cs",
        "    public void SetNeedData(Gst.App.AppSrcNeedDataCallback needDataCb)\n"
        + "    {\n"
        + "        ArgumentNullException.ThrowIfNull(needDataCb);\n"
        + "        nint instanceHandle = Handle;\n"
        + "        Gst.Interop.CallbackHandle needDataCbState = Gst.Interop.CallbackHandle.Alloc(needDataCb);\n")]
    [InlineData(
        "GstSharp.Net.App/Generated/AppSinkSimpleCallbacks.cs",
        "    public void SetNewSample(Gst.App.AppSinkNewSampleCallback newSampleCb)\n"
        + "    {\n"
        + "        ArgumentNullException.ThrowIfNull(newSampleCb);\n"
        + "        nint instanceHandle = Handle;\n"
        + "        Gst.Interop.CallbackHandle newSampleCbState = Gst.Interop.CallbackHandle.Alloc(newSampleCb);\n")]
    [InlineData(
        "GstSharp.Net/Generated/Pad.cs",
        "        ArgumentNullException.ThrowIfNull(callback);\n"
        + "        nint instanceHandle = Handle;\n"
        + "        Gst.Interop.CallbackHandle callbackState = Gst.Interop.CallbackHandle.Alloc(callback);\n"
        + "        System.Runtime.InteropServices.CULong nativeResult = GstPadAddProbe(instanceHandle, ")]
    public void AMemberThatTakesACallbackReadsItsHandleBeforeItAllocatesTheState(string path, string body)
    {
        // The GCHandle of a callback is freed by the destroy notification of
        // the native call and by nothing else, so a read of Handle that throws
        // ObjectDisposedException after the allocation would pin the delegate
        // and everything its closure captured for the life of the process. A
        // member that takes a callback therefore runs every guard first, reads
        // the handle of every wrapper next and allocates last; that order is
        // what this pins, with the three lines asserted adjacent.
        Assert.Contains(body, SourceOf(path), StringComparison.Ordinal);
    }

    [Fact]
    public void AMemberThatTakesACallbackGuardsEveryParameterBeforeItAllocatesTheState()
    {
        // gst_plugin_register_static_full takes a callback in the middle of
        // eight strings. The state used to be allocated where the callback
        // sits in the gir, so the guard of a later string threw with the
        // GCHandle already taken, and so did the UTF-8 copy of a string with
        // an embedded NUL. Every guard now runs first and the allocation is
        // the last statement before the call.
        string source = Source("Plugin.cs");
        int guard = source.IndexOf("ArgumentNullException.ThrowIfNull(origin);", StringComparison.Ordinal);
        int copy = source.IndexOf(
            "using Gst.Interop.Utf8Scope originScope = Gst.Interop.GMarshal.StackUtf8(origin, originBuffer);",
            StringComparison.Ordinal);
        int alloc = source.IndexOf(
            "Gst.Interop.CallbackHandle initFullFuncState = Gst.Interop.CallbackHandle.Alloc(initFullFunc);",
            StringComparison.Ordinal);

        Assert.True(guard > 0 && copy > 0 && alloc > 0);
        Assert.True(guard < copy, "A guard runs after the UTF-8 copy of an earlier parameter.");
        Assert.True(copy < alloc, "The callback state is allocated before a prologue that can throw.");
        Assert.Contains(
            """
                    Gst.Interop.CallbackHandle initFullFuncState = Gst.Interop.CallbackHandle.Alloc(initFullFunc);
                    try
            """,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMemberThatTakesACallbackAllocatesTheStateAsItsLastPrologue()
    {
        // gst_type_find_register takes a callback between a string it has to
        // copy and a nullable one, so the allocation used to sit between the
        // two copies. The whole prologue is pinned here: every guard, then the
        // handle of every wrapper, then the copies, then the allocation.
        Assert.Contains(
            """
                    ArgumentNullException.ThrowIfNull(name);
                    ArgumentNullException.ThrowIfNull(func);
                    nint pluginNative = plugin is null ? 0 : plugin.Handle;
                    nint possibleCapsNative = possibleCaps is null ? 0 : possibleCaps.Handle;
                    System.Span<byte> nameBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
                    using Gst.Interop.Utf8Scope nameScope = Gst.Interop.GMarshal.StackUtf8(name, nameBuffer);
                    System.Span<byte> extensionsBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
                    using Gst.Interop.Utf8Scope extensionsScope = Gst.Interop.GMarshal.StackUtf8(extensions, extensionsBuffer);
                    Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);
                    int nativeResult = GstTypeFindRegister(pluginNative,
            """,
            Source("TypeFind.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMemberThatTakesAnOwnedStringGuardsAndReadsItsHandleFirst()
    {
        // gst_element_message_full takes two owned strings, whose UTF-8 copies
        // only the call releases. The member used to allocate the first before
        // the guard of a later parameter ran, so a throwing guard leaked it.
        // The order pinned here is the fix: every guard, then the handle read,
        // then the first allocation, asserted adjacent.
        Assert.Contains(
            """
                    ArgumentNullException.ThrowIfNull(file);
                    ArgumentNullException.ThrowIfNull(function);
                    nint instanceHandle = Handle;
                    nint textNative = Gst.Interop.GMarshal.StringToUtf8Ptr(text);
            """,
            Source("Element.cs"),
            StringComparison.Ordinal);
    }
}
