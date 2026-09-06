using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using GES;
using Gst;
using Gst.GLib;
using Gst.Interop;
using Gst.Pbutils;

/// <summary>
/// The run of the sample: it builds or loads the project, waits for it to be
/// loaded, and then saves, plays or renders the timeline it carries.
/// </summary>
internal static class Launcher
{
    /// <summary>The encoding profile the C tool falls back to.</summary>
    private const string FallbackFormat = "application/ogg:video/x-theora:audio/x-vorbis";

    /// <summary>How long one turn of the load-wait loop sleeps when nothing was pending.</summary>
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// Runs the sample.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>
    /// 0 on end of stream and on the runs that only print or save, 1 on any
    /// error, 2 when the run did not finish within the timeout.
    /// </returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            Options options = Options.Parse(arguments);

            if (options.Help)
            {
                Options.PrintUsage();
                return 0;
            }

            // The C tool prints its own help and fails when it was given
            // neither a project to load nor a description to build.
            if (!options.ListTransitions && options.LoadPath is null && options.TimelineDescription is null)
            {
                Options.PrintUsage();
                return 1;
            }

            // Initialising through the module rather than through GstSharp is
            // what runs ges_init, which the project, the timeline and the
            // formatter behind the "ges:" scheme all need.
            GstGES.Initialize(options.Native);

            if (options.ListTransitions)
            {
                return ListTransitions() ? 0 : 1;
            }

            return Load(options);
        }
        catch (OptionException exception)
        {
            Console.Error.WriteLine($"GesLaunch: {exception.Message}");
            Options.PrintUsage();
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GesLaunch: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Prints the nick of every transition type, which is what
    /// <c>print_enum</c> of <c>utils.c:186-198</c> does.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the enumeration could not be read, so that
    /// a run which printed nothing fails rather than exiting 0.
    /// </returns>
    private static bool ListTransitions()
    {
        Gst.GObject.GType type = Gst.GObject.GType.FromName("GESVideoStandardTransitionType");

        if (!type.IsValid)
        {
            // The enumeration is registered with the type system the first time
            // something asks for it, and nothing has yet. A transition clip is
            // the cheapest thing that carries the type as a property.
            using TransitionClip? probe = TransitionClip.New(VideoStandardTransitionType.Crossfade);

            type = Gst.GObject.GType.FromName("GESVideoStandardTransitionType");
        }

        if (!type.IsValid)
        {
            Console.Error.WriteLine("GesLaunch: the transition types are not registered.");
            return false;
        }

        int printed = 0;

        foreach (Gst.GObject.EnumValue value in type.GetEnumValues())
        {
            Console.WriteLine(value.Nick ?? value.Name);
            printed++;
        }

        if (printed == 0)
        {
            Console.Error.WriteLine("GesLaunch: the transition types carry no values.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates the project, extracts its timeline and waits for the load to
    /// finish, then hands over to the save or the playback.
    /// </summary>
    /// <param name="options">The command line.</param>
    /// <returns>The exit code of the run.</returns>
    private static int Load(Options options)
    {
        string uri = options.LoadPath is string load
            ? EnsureUri(load)
            : options.TimelineDescription!;

        if (options.LoadPath is not null)
        {
            Console.WriteLine($"Loading project from : {uri}");
        }

        // The project is created here and is the asset of the timeline it
        // extracts, so it is disposed here.
        using Project project = Project.New(uri);

        bool loaded = false;
        string? failure = null;

        // The handlers only record what happened. The library emits them from
        // inside the load - "error-loading-asset" synchronously for a "ges:"
        // description, everything else from the iteration below - and touching
        // the timeline from there would run inside a load that has not
        // finished.
        project.Loaded += (_, _) => loaded = true;
        project.ErrorLoading += (_, arguments) => failure ??= arguments.Error.Message;
        project.ErrorLoadingAsset += (_, arguments) =>
            failure ??= $"asset {arguments.Id}: {arguments.Error?.Message ?? "no reason given"}";

        // The C tool has no relocation table either: an asset whose uri is
        // gone stays gone, and the load reports it.
        project.MissingUri += (_, _) => null;

        Timeline extracted;

        try
        {
            // The timeline is created by the extraction, so it is disposed
            // here. A project that is not there, and a "ges:" description that
            // cannot discover one of its clips, both report their error from
            // inside this call and throw.
            extracted = project.Extract<Timeline>();
        }
        catch (GException exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ERROR: Could not create timeline because: {exception.Message}");
            Console.Error.WriteLine();
            return 1;
        }

        using Timeline timeline = extracted;

        Stopwatch elapsed = Stopwatch.StartNew();

        // The load-wait loop. It iterates the *default* main context, without
        // pushing one of its own, on the thread that called Extract; see the
        // header of Program.cs for why all three of those matter.
        while (!loaded && failure is null && Within(elapsed, options.Timeout))
        {
            if (!MainContext.Default.Iteration(mayBlock: false))
            {
                Thread.Sleep(IdleWait);
            }
        }

        if (failure is not null)
        {
            Console.Error.WriteLine($"GesLaunch: error loading timeline: {failure}");
            return 1;
        }

        if (!loaded)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"GesLaunch: the project was not loaded within {options.Timeout.TotalSeconds:F0} s."));
            return 2;
        }

        if (options.SavePath is not null || options.SaveOnlyPath is not null)
        {
            if (!Save(project, timeline, options))
            {
                return 1;
            }
        }

        if (options.SaveOnlyPath is not null)
        {
            return 0;
        }

        return Play(project, timeline, options, elapsed);
    }

    /// <summary>
    /// Saves the project, which is what the <c>loaded</c> handler of the C tool
    /// does at <c>ges-launcher.c:887-916</c>.
    /// </summary>
    /// <param name="project">The project that was loaded.</param>
    /// <param name="timeline">The timeline it carries.</param>
    /// <param name="options">The command line.</param>
    /// <returns><see langword="false"/> when the project was not saved.</returns>
    private static bool Save(Project project, Timeline timeline, Options options)
    {
        string path = options.SavePath ?? options.SaveOnlyPath!;

        // "+r" is the C tool's spelling of "back to where it came from".
        string? uri = string.Equals(path, "+r", StringComparison.Ordinal)
            ? project.GetUri()
            : EnsureUri(path);

        if (uri is null)
        {
            Console.Error.WriteLine($"GesLaunch: could not create a uri for \"{path}\".");
            return false;
        }

        if (options.EmbedNesteds)
        {
            // The C tool does this from _save_timeline, which runs before the
            // pipeline is built rather than from the save this port keeps
            // (ges-launcher.c:1123-1143). It is the same project object either
            // way, so the assets it registers are in whichever save runs.
            int embedded = EmbedNestedTimelines(project);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Embedded nested projects: {embedded}"));
        }

        Console.WriteLine($"Saving project to {uri}");

        // A refusal comes back as false, a failure as a GException; an
        // unwritable destination is the second one.
        try
        {
            if (!project.Save(timeline, uri, null, true))
            {
                Console.Error.WriteLine($"GesLaunch: the project was not saved to {uri}.");
                return false;
            }
        }
        catch (GException exception)
        {
            Console.Error.WriteLine(
                $"GesLaunch: the project was not saved to {uri}: {exception.Message}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the pipeline around the timeline and runs it to the end.
    /// </summary>
    /// <param name="project">The project the timeline came from.</param>
    /// <param name="timeline">The timeline to play or to render.</param>
    /// <param name="options">The command line.</param>
    /// <param name="elapsed">How long the run has taken so far.</param>
    /// <returns>The exit code of the run.</returns>
    private static int Play(Project project, Timeline timeline, Options options, Stopwatch elapsed)
    {
        // The pipeline is created here and is disposed here, after it is back
        // in NULL.
        using GES.Pipeline pipeline = GES.Pipeline.New();

        try
        {
            if (options.OutputUri is not null)
            {
                // No preview at all while rendering, which is what the C tool
                // asks for at ges-launcher.c:1239-1240 before it builds
                // anything else.
                pipeline.SetMode(0);
            }

            if (!SetSinks(pipeline, options))
            {
                return 1;
            }

            if (!pipeline.SetTimeline(timeline))
            {
                Console.Error.WriteLine("GesLaunch: the pipeline refused the timeline.");
                return 1;
            }

            // The order is the C tool's: the user options and then the
            // rendering details, both after the pipeline has the timeline
            // (ges-launcher.c:932-937), and the commit and the state changes
            // after those (ges-launcher.c:943-955). Nothing here depends on
            // it - ges_pipeline_set_mode does its work on the tracks under an
            // "if (pipeline->priv->timeline)" and is a no-op without one - so
            // the order is kept only to stay readable next to the C.
            if (!SetUserOptions(timeline, options))
            {
                return 1;
            }

            if (!SetRenderingDetails(pipeline, project, timeline, options))
            {
                return 1;
            }

            timeline.Commit();

            // READY first, because the elements of the render bin are built on
            // the way there and an error is reported before anything rolls.
            if (pipeline.SetState(State.Ready) == StateChangeReturn.Failure
                || pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("GesLaunch: the pipeline refused to start.");
                return 1;
            }

            return new Playback(pipeline, options).Run(elapsed);
        }
        finally
        {
            // Back to NULL before anything is released: a pipeline that is
            // still PLAYING when its last reference goes away leaves its
            // streaming threads running.
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Sets the preview sinks, which is <c>ges-launcher.c:1262-1268</c> for
    /// <c>-m</c> and <c>_set_sink</c> at <c>ges-launcher.c:1009-1028</c> for
    /// <c>-v</c> and <c>-a</c>.
    /// </summary>
    /// <param name="pipeline">The pipeline that previews the timeline.</param>
    /// <param name="options">The command line.</param>
    /// <returns><see langword="false"/> when a sink could not be built.</returns>
    private static bool SetSinks(GES.Pipeline pipeline, Options options)
    {
        if (options.Mute)
        {
            // The C tool names these two factories and has no fallback. It
            // hands GES whatever gst_element_factory_make answered, including
            // nothing; this reports the missing factory instead.
            using Element? audio = ElementFactory.Make("fakeaudiosink", null);
            using Element? video = ElementFactory.Make("fakevideosink", null);

            if (audio is null || video is null)
            {
                Console.Error.WriteLine("GesLaunch: --mute needs fakeaudiosink and fakevideosink.");
                return false;
            }

            pipeline.PreviewSetAudioSink(audio);
            pipeline.PreviewSetVideoSink(video);
        }

        return SetSink(options.VideoSink, pipeline.PreviewSetVideoSink)
            && SetSink(options.AudioSink, pipeline.PreviewSetAudioSink);
    }

    /// <summary>Builds one sink out of a description and hands it to the pipeline.</summary>
    /// <param name="description">The description, or <see langword="null"/> for no sink.</param>
    /// <param name="set">What the pipeline does with the sink.</param>
    /// <returns><see langword="false"/> when the description could not be built.</returns>
    private static bool SetSink(string? description, Action<Element?> set)
    {
        if (description is null)
        {
            return true;
        }

        try
        {
            // The bin is built here, so it is disposed here; the pipeline takes
            // its own reference on it.
            using Element sink = Global.ParseBinFromDescriptionFull(
                description,
                ghostUnlinkedPads: true,
                null,
                ParseFlags.NoSingleElementBins | ParseFlags.PlaceInBin);

            set(sink);
            return true;
        }
        catch (GException exception)
        {
            Console.Error.WriteLine(
                $"GesLaunch: could not create the requested sink {description}: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Applies the track options to the loaded timeline, which is
    /// <c>_timeline_set_user_options</c> at <c>ges-launcher.c:764-862</c>.
    /// </summary>
    /// <param name="timeline">The timeline that was loaded.</param>
    /// <param name="options">The command line.</param>
    /// <returns><see langword="false"/> when an option could not be applied.</returns>
    private static bool SetUserOptions(Timeline timeline, Options options)
    {
        // Before anything else, because the tracks the rest of this method
        // works on are the ones it builds (ges-launcher.c:773-810).
        if (options.ProfileFrom is string named && !RebuildTracksFrom(timeline, named))
        {
            return false;
        }

        // The track wrappers are interned: the timeline owns the tracks and
        // this only looks them up, so none of them is disposed here.
        foreach (Track track in timeline.GetTracks())
        {
            // Smart rendering cannot work in a track that mixes.
            if (options.DisableMixing || options.SmartRendering)
            {
                track.SetMixing(false);
            }

            // The C tool skips this filter when --profile-from rebuilt the
            // tracks (ges-launcher.c:822-828): they came from the streams of a
            // clip rather than from -t, and trimming them here would undo it.
            if (options.ProfileFrom is null && (track.TrackType & options.TrackTypes) == 0)
            {
                timeline.RemoveTrack(track);
                continue;
            }

            string? caps = track.TrackType switch
            {
                TrackType.Video => options.VideoCaps,
                TrackType.Audio => options.AudioCaps,
                _ => null,
            };

            if (caps is not null)
            {
                // The caps are parsed here, so they are disposed here; the
                // track takes its own reference.
                using Caps? restriction = Caps.FromString(caps);

                if (restriction is null)
                {
                    // The C tool ends the run here: _set_track_restriction_caps
                    // calls g_error at ges-launcher.c:349-353, which aborts.
                    Console.Error.WriteLine($"GesLaunch: \"{caps}\" are not caps.");
                    return false;
                }

                track.SetRestrictionCaps(restriction);
            }

            if (options.ForwardTags)
            {
                ForwardTags(track);
            }
        }

        if (options.SmartRendering && !options.DisableMixing)
        {
            Console.WriteLine("**Mixing is disabled for smart rendering to work**");
        }

        return true;
    }

    /// <summary>
    /// Lets the compositions of a track pass the tags of their sources on,
    /// which is <c>_set_tracks_forward_tags</c> at <c>ges-launcher.c:380-410</c>.
    /// </summary>
    /// <param name="track">The track to open up.</param>
    private static void ForwardTags(Track track)
    {
        // The iterator is created here and is disposed here; what it yields is
        // owned by the track.
        using Iterator compositions = track.IterateAllByElementFactoryName("nlecomposition");

        foreach (Element composition in compositions.Items<Element>())
        {
            composition.SetProperty("drop-tags", false);
        }
    }

    /// <summary>
    /// Decides between preview and render, which is
    /// <c>_set_rendering_details</c> at <c>ges-launcher.c:589-747</c>.
    /// </summary>
    /// <param name="pipeline">The pipeline to configure.</param>
    /// <param name="project">The project, which may carry profiles of its own.</param>
    /// <param name="timeline">
    /// The timeline, which <c>--profile-from</c> and the smart profile read the
    /// clips and the tracks of. It has been through
    /// <see cref="SetUserOptions"/> already, so the track counts the smart
    /// profile compares against are the final ones, which is the order of the C
    /// tool at <c>ges-launcher.c:932-937</c>.
    /// </param>
    /// <param name="options">The command line.</param>
    /// <returns><see langword="false"/> when there is nothing to render with.</returns>
    private static bool SetRenderingDetails(
        GES.Pipeline pipeline,
        Project project,
        Timeline timeline,
        Options options)
    {
        if (options.OutputUri is null)
        {
            pipeline.SetMode(GES.PipelineFlags.FullPreview);
            return true;
        }

        EncodingProfile? profile = null;

        // A profile the project carries is owned by the project; one parsed
        // here is owned here. Only the second is disposed, which is what this
        // flag is for.
        EncodingProfile? created = null;

        // Which of the paths below answered. Every one of them ends in a
        // profile and a rendered file, so a run says out loud which one it
        // took rather than leaving that to be guessed from the output.
        string source = "--format";

        try
        {
            string? format = options.Format;

            if (format is null)
            {
                // Not what -e reads like, but what the C tool does with it: it
                // names one of the profiles the loaded project already carries.
                IReadOnlyList<EncodingProfile> carried = project.ListEncodingProfiles();

                if (carried.Count > 0)
                {
                    profile = carried[0];
                    source = "the project";

                    if (options.EncodingProfile is not null)
                    {
                        foreach (EncodingProfile candidate in carried)
                        {
                            if (string.Equals(candidate.GetName(), options.EncodingProfile, StringComparison.Ordinal))
                            {
                                profile = candidate;
                            }
                        }
                    }
                }
            }

            if (profile is null)
            {
                if (format is null)
                {
                    // The chain of ges-launcher.c:628-638: the named clip
                    // first, then the smart profile, and only then the
                    // extension of the output file.
                    if (options.ProfileFrom is string named)
                    {
                        profile = created = ProfileFromNamedClip(timeline, named);
                        source = $"--profile-from {named}";
                    }
                    else if (options.SmartRendering)
                    {
                        profile = created = SmartProfile(timeline, out int candidates);
                        source = string.Create(
                            CultureInfo.InvariantCulture,
                            $"smart rendering ({candidates} candidate profiles)");
                    }

                    if (profile is null)
                    {
                        format = FileExtension(options.OutputUri);
                        profile = created = format is null ? null : ParseEncodingProfile(format);
                        source = "the output file extension";
                    }
                }
                else
                {
                    profile = created = ParseEncodingProfile(format);

                    if (profile is null)
                    {
                        Console.Error.WriteLine($"GesLaunch: invalid format specified: {format}");
                        return false;
                    }
                }

                if (profile is null)
                {
                    Console.Error.WriteLine(
                        "GesLaunch: no format specified and none found from the output file extension, "
                        + "falling back to theora+vorbis in ogg.");

                    format = FallbackFormat;
                    profile = created = ParseEncodingProfile(format);
                    source = "the theora+vorbis in ogg default";
                }

                if (profile is null)
                {
                    Console.Error.WriteLine($"GesLaunch: could not find any encoding format for {format}");
                    return false;
                }

                if (options.ContainerProfile is string container)
                {
                    if (ReParent(profile, container) is not { } reparented)
                    {
                        return false;
                    }

                    // Both arms of ReParent settle the profile that went in:
                    // the container took its children over and its shell was
                    // dropped, or it became the sole child of the container
                    // and the call that took it disposed the wrapper.
                    profile = created = reparented;

                    Console.WriteLine($"Re-parented the encoding profile into --container-profile {container}");
                }

                Console.WriteLine();
                Console.WriteLine("Encoding details:");
                Console.WriteLine("================");
                Console.WriteLine($"  -> Output file: {options.OutputUri}");
                Console.WriteLine($"  -> Profile: {profile.GetName() ?? format}");
                Console.WriteLine();

                project.AddEncodingProfile(profile);
            }

            Console.WriteLine($"Encoding profile from: {source}");

            string outputUri = EnsureUri(options.OutputUri);

            if (!pipeline.SetRenderSettings(outputUri, profile))
            {
                Console.Error.WriteLine($"GesLaunch: the pipeline refused to render into {outputUri}.");
                return false;
            }

            if (!pipeline.SetMode(options.SmartRendering ? GES.PipelineFlags.SmartRender : GES.PipelineFlags.Render))
            {
                Console.Error.WriteLine("GesLaunch: the pipeline refused the render mode.");
                return false;
            }

            return true;
        }
        finally
        {
            created?.Dispose();
        }
    }

    /// <summary>
    /// Rebuilds the tracks of the timeline out of the streams of a named clip,
    /// which is the <c>--profile-from</c> branch of
    /// <c>_timeline_set_user_options</c> at <c>ges-launcher.c:773-810</c>.
    /// </summary>
    /// <param name="timeline">The timeline whose tracks are replaced.</param>
    /// <param name="name">The name the clip was given in the description.</param>
    /// <returns><see langword="false"/> when there is no such clip.</returns>
    private static bool RebuildTracksFrom(Timeline timeline, string name)
    {
        if (AssetForNamedClip(timeline, name) is not { } asset)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                $"ERROR: can't create profile from named clip, no such clip {name}");
            Console.Error.WriteLine();
            return false;
        }

        // Every track goes, whatever its type: from here on the streams of the
        // clip decide the topology.
        foreach (Track track in timeline.GetTracks())
        {
            timeline.RemoveTrack(track);
        }

        // The discoverer information belongs to the asset, and the stream
        // information objects belong to it in turn, so none of those is
        // disposed; see docs/ownership.md on GObject wrappers.
        DiscovererInfo info = asset.GetInfo();

        // The tracks, on the other hand, are built here, so they are disposed
        // here: the track parameter of ges_timeline_add_track is transfer none
        // and the timeline takes a reference of its own.
        for (int i = info.GetAudioStreams().Count; i > 0; i--)
        {
            using Track track = AudioTrack.New();
            timeline.AddTrack(track);
        }

        for (int i = info.GetVideoStreams().Count; i > 0; i--)
        {
            using Track track = VideoTrack.New();
            timeline.AddTrack(track);
        }

        return true;
    }

    /// <summary>
    /// Answers the asset of the uri clip that carries a name, which is
    /// <c>_asset_for_named_clip</c> at <c>ges-launcher.c:465-489</c>.
    /// </summary>
    /// <param name="timeline">The timeline to walk.</param>
    /// <param name="name">The name to look for.</param>
    /// <returns>The asset, or <see langword="null"/> when there is no such clip.</returns>
    /// <remarks>
    /// The layers, the clips and their assets are all owned by the timeline and
    /// the project, so none of the wrappers here is disposed.
    /// </remarks>
    private static UriClipAsset? AssetForNamedClip(Timeline timeline, string name)
    {
        foreach (Layer layer in timeline.GetLayers())
        {
            foreach (Clip clip in layer.GetClips())
            {
                if (clip is UriClip && string.Equals(clip.GetName(), name, StringComparison.Ordinal))
                {
                    return clip.GetAsset() as UriClipAsset;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Answers the asset of every uri clip on the timeline, which is
    /// <c>_timeline_assets</c> at <c>ges-launcher.c:445-463</c>.
    /// </summary>
    /// <param name="timeline">The timeline to walk.</param>
    /// <returns>The assets, in the order the C tool collects them.</returns>
    private static List<UriClipAsset> TimelineAssets(Timeline timeline)
    {
        List<UriClipAsset> assets = [];

        foreach (Layer layer in timeline.GetLayers())
        {
            foreach (Clip clip in layer.GetClips())
            {
                if (clip is UriClip && clip.GetAsset() is UriClipAsset asset)
                {
                    assets.Add(asset);
                }
            }
        }

        return assets;
    }

    /// <summary>
    /// Reads the encoding profile out of a named clip, which is
    /// <c>_get_profile_from</c> at <c>ges-launcher.c:491-505</c>.
    /// </summary>
    /// <param name="timeline">The timeline the clip is on.</param>
    /// <param name="name">The name of the clip.</param>
    /// <returns>The profile, which the caller owns.</returns>
    /// <remarks>
    /// The clip is known to be there: <see cref="SetUserOptions"/> ran first
    /// and ended the run when it was not, which is what the <c>g_assert</c> of
    /// the C tool stands for.
    /// </remarks>
    private static EncodingProfile? ProfileFromNamedClip(Timeline timeline, string name) =>
        AssetForNamedClip(timeline, name) is { } asset
            ? EncodingProfile.FromDiscoverer(asset.GetInfo())
            : null;

    /// <summary>
    /// Builds the encoding profile <c>--smart-rendering</c> renders with when
    /// no format was named, which is <c>get_smart_profile</c> at
    /// <c>ges-launcher.c:507-575</c>.
    /// </summary>
    /// <param name="timeline">The timeline whose clips are read.</param>
    /// <param name="candidates">How many distinct profiles qualified.</param>
    /// <returns>
    /// The profile, which the caller owns, or <see langword="null"/> when no
    /// clip of the timeline carries enough streams for its tracks.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The profile this picks is the least common one</b>, not the most
    /// common one: <c>sort_encoding_profiles</c> (<c>ges-launcher.c:428-443</c>)
    /// orders the candidates by how many assets carried them <i>ascending</i>,
    /// and the C tool takes the head of that list. That reads like a bug and is
    /// left alone here, because this is a port and not a rewrite.
    /// </para>
    /// <para>
    /// The C tool keeps the count on the profile object itself, in a
    /// <c>"__n_instances"</c> qdata (<c>ges-launcher.c:427</c>); the counts are
    /// kept beside the profiles here instead, which needs no qdata primitive
    /// and says the same thing.
    /// </para>
    /// <para>
    /// The <c>--profile-from</c> re-check the C function opens with
    /// (<c>ges-launcher.c:514-526</c>) is not ported: its only call site is the
    /// <c>else if</c> of an <c>if (profile_from)</c>, so it is unreachable.
    /// </para>
    /// </remarks>
    private static EncodingProfile? SmartProfile(Timeline timeline, out int candidates)
    {
        (int audio, int video) = TrackCounts(timeline);

        List<(EncodingProfile Profile, int Count)> possible = [];

        foreach (UriClipAsset asset in TimelineAssets(timeline))
        {
            DiscovererInfo info = asset.GetInfo();

            // Enough streams of each kind to feed every track of that kind.
            if (info.GetAudioStreams().Count < audio || info.GetVideoStreams().Count < video)
            {
                continue;
            }

            if (EncodingProfile.FromDiscoverer(info) is not { } built)
            {
                continue;
            }

            int known = possible.FindIndex(entry => entry.Profile.IsEqual(built));

            if (known >= 0)
            {
                // The same profile a second time. The one just built is a
                // duplicate nothing else holds, so it goes.
                built.Dispose();
                possible[known] = (possible[known].Profile, possible[known].Count + 1);
                continue;
            }

            // Prepended, because the C tool prepends and then sorts with the
            // stable sort of GLib, which leaves the last of a tie at the head.
            possible.Insert(0, (built, 1));
        }

        candidates = possible.Count;

        if (possible.Count == 0)
        {
            return null;
        }

        List<(EncodingProfile Profile, int Count)> sorted = [.. possible.OrderBy(entry => entry.Count)];

        // Everything that was built here and not chosen is disposed, which is
        // the g_list_free_full of ges-launcher.c:572.
        for (int i = 1; i < sorted.Count; i++)
        {
            sorted[i].Profile.Dispose();
        }

        return sorted[0].Profile;
    }

    /// <summary>
    /// Counts the audio and the video tracks of the timeline, which is
    /// <c>_check_has_audio_video</c> at <c>ges-launcher.c:413-425</c>.
    /// </summary>
    /// <param name="timeline">The timeline to count.</param>
    /// <returns>How many tracks of each kind it has.</returns>
    private static (int Audio, int Video) TrackCounts(Timeline timeline)
    {
        int audio = 0;
        int video = 0;

        foreach (Track track in timeline.GetTracks())
        {
            if (track.TrackType == TrackType.Video)
            {
                video++;
            }
            else if (track.TrackType == TrackType.Audio)
            {
                audio++;
            }
        }

        return (audio, video);
    }

    /// <summary>
    /// Re-parents a profile tree into a container profile, which is the
    /// <c>--container-profile</c> block of <c>_set_rendering_details</c> at
    /// <c>ges-launcher.c:665-710</c>.
    /// </summary>
    /// <param name="profile">
    /// The profile that was resolved so far. It is spent whatever the answer
    /// is: its children were taken over and its shell disposed, or it became
    /// the sole child of the new container, which consumed it.
    /// </param>
    /// <param name="container">
    /// The serialised muxer profile, which has to be a bare container - one
    /// with no <c>:</c> sub profiles.
    /// </param>
    /// <returns>
    /// The new top level container, which the caller owns, or
    /// <see langword="null"/> when the option is not a bare container profile.
    /// </returns>
    private static EncodingContainerProfile? ReParent(EncodingProfile profile, string container)
    {
        if (ParseEncodingProfile(container) is not { } parsed)
        {
            Console.Error.WriteLine($"GesLaunch: failed to parse container profile {container}");
            return null;
        }

        if (parsed is not EncodingContainerProfile target)
        {
            Console.Error.WriteLine("GesLaunch: top level profile should be container profile");
            parsed.Dispose();
            return null;
        }

        if (target.GetProfiles().Count > 0)
        {
            Console.Error.WriteLine("GesLaunch: --container-profile cannot contain children profiles");
            target.Dispose();
            return null;
        }

        if (profile is EncodingContainerProfile existing)
        {
            // The children move under the new container and the old shell is
            // dropped. AddProfile consumes the wrapper it is handed, which is
            // exactly the gst_encoding_profile_ref the C tool takes before it
            // adds each child (ges-launcher.c:696-702); the reference the old
            // container still holds on them goes away with it.
            foreach (EncodingProfile child in existing.GetProfiles())
            {
                target.AddProfile(child);
            }

            existing.Dispose();
        }
        else
        {
            // A single elementary stream profile becomes the sole child. The
            // call consumes it, so there is nothing left to dispose.
            target.AddProfile(profile);
        }

        return target;
    }

    /// <summary>
    /// Registers the nested timelines of the project as sub project assets, so
    /// that saving writes them out rather than only pointing at their files.
    /// This is the <c>--embed-nesteds</c> block of <c>_save_timeline</c> at
    /// <c>ges-launcher.c:1123-1143</c>.
    /// </summary>
    /// <param name="project">The project that is about to be saved.</param>
    /// <returns>How many nested projects were embedded.</returns>
    /// <remarks>
    /// <c>GES_TYPE_URI_CLIP</c> and <c>GES_TYPE_TIMELINE</c> are looked up by
    /// name, the way <see cref="EncodingProfileType"/> does, because the
    /// <c>GetGType()</c> of the generated classes is internal to the binding. A
    /// name the type system does not know yet is not an error here: nothing has
    /// made a clip or a timeline of that type, so there is no such asset on the
    /// project either and there is nothing to embed.
    /// </remarks>
    private static int EmbedNestedTimelines(Project project)
    {
        Gst.GObject.GType uriClip = Gst.GObject.GType.FromName("GESUriClip");
        Gst.GObject.GType timeline = Gst.GObject.GType.FromName("GESTimeline");

        if (!uriClip.IsValid || !timeline.IsValid)
        {
            return 0;
        }

        int embedded = 0;

        // The assets of the project are the project's, so they are not disposed
        // here; the filter is the extractable type a uri clip asset produces.
        foreach (Asset listed in project.ListAssets(uriClip))
        {
            if (listed is not UriClipAsset asset || !asset.IsNestedTimeline)
            {
                continue;
            }

            Asset? subProject;

            try
            {
                subProject = Asset.Request(timeline, asset.GetId());
            }
            catch (GException exception)
            {
                // The C tool hands ges_asset_request no error to fill in and
                // then adds whatever came back, including nothing.
                Console.Error.WriteLine(
                    $"GesLaunch: could not embed {asset.GetId()}: {exception.Message}");
                continue;
            }

            if (subProject is null)
            {
                continue;
            }

            // Requested here, so disposed here: the asset parameter of
            // ges_project_add_asset is transfer none and the project takes a
            // reference of its own. The C tool never gives this one back, which
            // leaks one reference per nested project; this port does, the same
            // deliberate divergence the -m/--mute handling is.
            using (subProject)
            {
                if (project.AddAsset(subProject))
                {
                    embedded++;
                }
            }
        }

        return embedded;
    }

    /// <summary>
    /// Parses one serialised encoding profile, which is what
    /// <c>gst_encoding_profile_from_string</c> does for the C tool at
    /// <c>ges-launcher.c:638</c>.
    /// </summary>
    /// <param name="format">The serialisation, such as
    /// <c>application/ogg:audio/x-vorbis</c>.</param>
    /// <returns>The profile, which the caller owns, or <see langword="null"/>
    /// when the string is not a profile.</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>GstEncodingProfile</c> is not registered with the type system.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The route is the <c>GValue</c> one and not <c>EncodingProfile.FromString</c>
    /// on purpose: <c>gst_encoding_profile_from_string</c> is only 1.26 and up,
    /// while the deserializer it is a wrapper over is much older —
    /// <c>gst_encoding_profile_get_type</c> registers it with
    /// <c>gst_value_register</c> (<c>encoding-profile.c:383-412</c>), so a
    /// registered type always has its deserializer and
    /// <c>gst_value_deserialize</c> into a value of the profile type parses the
    /// same strings on every 1.x. It is
    /// the only path here, on every version, rather than a fallback behind a
    /// try/catch that only an old library would ever run and that nothing else
    /// would exercise.
    /// </para>
    /// <para>
    /// The deserializer <c>g_value_take_object</c>s the profile into the value,
    /// which owns that reference; <c>GetObject</c> hands out the wrapper of the
    /// type the registry knows for the instance — the container profile of a
    /// serialisation that names a muxer — and takes a reference of its own, so
    /// unsetting the value below leaves the caller holding one profile to
    /// dispose.
    /// </para>
    /// </remarks>
    private static EncodingProfile? ParseEncodingProfile(string format)
    {
        Gst.GObject.Value value = Gst.GObject.Value.New(EncodingProfileType());

        try
        {
            if (!Global.ValueDeserialize(ref value, format))
            {
                return null;
            }

            return value.GetObject() as EncodingProfile
                ?? throw new InvalidOperationException(
                    "the deserialised profile did not come back as an encoding profile.");
        }
        finally
        {
            value.Dispose();
        }
    }

    /// <summary>
    /// Answers the type to deserialize a profile into.
    /// </summary>
    /// <returns>The <c>GstEncodingProfile</c> type.</returns>
    /// <exception cref="InvalidOperationException">The type is not registered.</exception>
    /// <remarks>
    /// A type is registered with the type system by its own <c>get_type</c>, and
    /// naming <see cref="EncodingProfile"/> in managed code does not call one.
    /// <see cref="Gst.GObject.TypeRegistry.Freeze"/> is what runs the
    /// <c>get_type</c> of every entry of every registered module, which is where
    /// <c>gst_encoding_profile_get_type</c> is finally called. The managed
    /// registry already holds the Pbutils module by then: the sweep
    /// <c>GstGES.Initialize</c> runs takes in every loaded binding assembly and
    /// keeps watching for later ones (<c>GstSharp.cs:261-277</c>), so the
    /// <see cref="GstPbutils.Initialize"/> above it is belt and braces —
    /// harmless, idempotent, and it says out loud which module has to be there.
    /// Deserializing into a value of an unregistered type would only warn and
    /// fail, which would read here as an invalid format.
    /// </remarks>
    private static Gst.GObject.GType EncodingProfileType()
    {
        Gst.GObject.GType type = Gst.GObject.GType.FromName("GstEncodingProfile");

        if (!type.IsValid)
        {
            GstPbutils.Initialize();
            Gst.GObject.TypeRegistry.Freeze();

            type = Gst.GObject.GType.FromName("GstEncodingProfile");
        }

        return type.IsValid
            ? type
            : throw new InvalidOperationException("GstEncodingProfile is not registered with the type system.");
    }

    /// <summary>
    /// Answers the extension of a location, which is
    /// <c>get_file_extension</c> at <c>utils.c:269-288</c>.
    /// </summary>
    /// <param name="uri">The location to look at.</param>
    /// <returns>The extension without its dot, or <see langword="null"/>.</returns>
    private static string? FileExtension(string uri)
    {
        int dot = uri.LastIndexOf('.');

        return dot <= 0 ? null : uri[(dot + 1)..];
    }

    /// <summary>
    /// Turns a location into a uri, which is <c>ensure_uri</c> at
    /// <c>utils.c:175-182</c>.
    /// </summary>
    /// <param name="location">A uri or a file name.</param>
    /// <returns>The uri.</returns>
    /// <exception cref="OptionException">The file name has no uri.</exception>
    private static string EnsureUri(string location) =>
        Gst.Uri.IsValid(location)
            ? location
            : Global.FilenameToUri(location)
                ?? throw new OptionException($"could not create a uri for \"{location}\".");

    /// <summary>Answers whether a bounded run still has time left.</summary>
    /// <param name="elapsed">How long the run has taken.</param>
    /// <param name="timeout">The bound, zero for none.</param>
    /// <returns><see langword="true"/> while the run may go on.</returns>
    private static bool Within(Stopwatch elapsed, TimeSpan timeout) =>
        timeout <= TimeSpan.Zero || elapsed.Elapsed < timeout;

    /// <summary>
    /// The playing pipeline: the polled bus and, when a person is at the
    /// terminal, the keyboard controls of <c>ges-launcher.c:1635-1697</c>.
    /// </summary>
    /// <param name="pipeline">The pipeline that is playing.</param>
    /// <param name="options">The command line.</param>
    private sealed class Playback(GES.Pipeline pipeline, Options options)
    {
        /// <summary>How long one poll of the bus waits.</summary>
        private static readonly ClockTime PollInterval = ClockTime.FromMilliseconds(100);

        /// <summary>How far the right arrow seeks, as a share of the duration.</summary>
        private const double ForwardStep = 0.08;

        /// <summary>How far the left arrow seeks, as a share of the duration.</summary>
        private const double BackwardStep = -0.01;

        /// <summary>The rate the last seek asked for.</summary>
        private double _rate = 1.0;

        /// <summary>The trick mode the last seek asked for.</summary>
        private TrickMode _trickMode = TrickMode.None;

        /// <summary>The state the keyboard last asked for.</summary>
        private State _desiredState = State.Playing;

        /// <summary>Whether the keyboard is read at all.</summary>
        private readonly bool _interactive =
            options.Interactive && options.OutputUri is null && !Console.IsInputRedirected;

        /// <summary>Whether an interrupt asked the run to end.</summary>
        /// <remarks>
        /// Written on the thread the console raises the event on and read on
        /// the thread that polls the bus, so it is volatile.
        /// </remarks>
        private volatile bool _interrupted;

        /// <summary>
        /// What a seek does to the buffers it asks for, the values of
        /// <c>GstPlayTrickMode</c> the C tool cycles through with <c>t</c>.
        /// </summary>
        private enum TrickMode
        {
            /// <summary>Normal playback, trick modes disabled.</summary>
            None,

            /// <summary>Trick mode: default.</summary>
            Default,

            /// <summary>Trick mode: default, no audio.</summary>
            DefaultNoAudio,

            /// <summary>Trick mode: key frames only.</summary>
            KeyUnits,

            /// <summary>Trick mode: key frames only, no audio.</summary>
            KeyUnitsNoAudio,

            /// <summary>One past the last mode.</summary>
            Last,
        }

        /// <summary>Polls the bus until the run ends.</summary>
        /// <param name="elapsed">How long the run has taken so far.</param>
        /// <returns>The exit code of the run.</returns>
        internal int Run(Stopwatch elapsed)
        {
            // The bus wrapper is an interned GObject wrapper, shared with every
            // other lookup of the same bus, so it is not disposed here.
            Bus bus = pipeline.GetBus();

            if (_interactive)
            {
                Console.WriteLine("Press 'k' to see a list of keyboard shortcuts.");
            }

            // Ctrl+C would otherwise end the process where it stands, without
            // the NULL that the caller's finally does - and a render that is
            // stopped that way is left truncated. The C tool takes the same
            // way out through intr_handler at ges-launcher.c:1104-1116, which
            // only asks the application to quit.
            ConsoleCancelEventHandler cancel = OnCancelKeyPress;
            Console.CancelKeyPress += cancel;

            try
            {
                while (!_interrupted && Within(elapsed, options.Timeout))
                {
                    using (Message? message = bus.TimedPopFiltered(
                        PollInterval,
                        MessageType.Error | MessageType.Warning | MessageType.Eos))
                    {
                        if (message is not null && Handle(message) is int code)
                        {
                            return code;
                        }
                    }

                    GstSharp.DrainPendingReleases();

                    if (!ReadKey())
                    {
                        return 0;
                    }
                }

                if (_interrupted)
                {
                    // The C tool quits its application here and ends with the
                    // code it would have ended with anyway, which is 0.
                    return 0;
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"GesLaunch: the run did not finish within {options.Timeout.TotalSeconds:F0} s."));

            return 2;
        }

        /// <summary>Turns Ctrl+C into the end of the poll loop.</summary>
        /// <param name="sender">The console.</param>
        /// <param name="e">Whether the process is allowed to end.</param>
        /// <remarks>
        /// The first interrupt belongs to the run, so that the pipeline is
        /// brought back to NULL before anything is released; a second one is
        /// the way out of a pipeline that will not stop, and letting it
        /// through ends the process.
        /// </remarks>
        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = !_interrupted;

            if (e.Cancel)
            {
                Console.WriteLine();
                Console.WriteLine("interrupt received.");
                _interrupted = true;
            }
        }

        /// <summary>Acts on one message of the bus.</summary>
        /// <param name="message">The message that was popped.</param>
        /// <returns>The exit code when the run ends here, otherwise null.</returns>
        private int? Handle(Message message)
        {
            switch (message.Type)
            {
                case MessageType.Eos:
                    if (options.IgnoreEos)
                    {
                        return null;
                    }

                    Console.WriteLine();
                    Console.WriteLine("Done");
                    return 0;

                case MessageType.Warning:
                    (GException warning, string? warningDebug) = message.ParseWarning();
                    Console.Error.WriteLine($"WARNING from element {message.SourceName ?? "?"}: {warning.Message}");
                    Console.Error.WriteLine($"Debugging info: {warningDebug ?? "none"}");
                    return null;

                default:
                    (GException error, string? debug) = message.ParseError();
                    Console.Error.WriteLine($"ERROR from element {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"Debugging info: {debug ?? "none"}");
                    return 1;
            }
        }

        /// <summary>
        /// Reads one key, when the run is interactive and a person is at the
        /// terminal.
        /// </summary>
        /// <returns><see langword="false"/> when the key says to stop.</returns>
        private bool ReadKey()
        {
            // KeyAvailable throws when stdin is a pipe rather than a console,
            // which is what an unattended run has.
            if (!_interactive || !Console.KeyAvailable)
            {
                return true;
            }

            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.RightArrow:
                    RelativeSeek(ForwardStep);
                    return true;

                case ConsoleKey.LeftArrow:
                    RelativeSeek(BackwardStep);
                    return true;

                case ConsoleKey.Escape:
                    return false;

                default:
                    return Act(char.ToLowerInvariant(key.KeyChar));
            }
        }

        /// <summary>Acts on one printable key.</summary>
        /// <param name="key">The character that was typed.</param>
        /// <returns><see langword="false"/> when the key says to stop.</returns>
        private bool Act(char key)
        {
            switch (key)
            {
                case 'k':
                    PrintKeyboardHelp();
                    break;

                case ' ':
                    TogglePaused();
                    break;

                case 'q':
                    return false;

                case '+':
                    SetRelativeRate(Math.Abs(_rate) switch
                    {
                        < 2.0 => 0.1,
                        < 4.0 => 0.5,
                        _ => 1.0,
                    });
                    break;

                case '-':
                    SetRelativeRate(Math.Abs(_rate) switch
                    {
                        <= 2.0 => -0.1,
                        <= 4.0 => -0.5,
                        _ => -1.0,
                    });
                    break;

                case 't':
                    SwitchTrickMode();
                    break;

                case '0':
                    Seek(0, _rate, _trickMode);
                    break;

                default:
                    break;
            }

            return true;
        }

        /// <summary>Puts the pipeline into the state it is not in.</summary>
        private void TogglePaused()
        {
            _desiredState = _desiredState == State.Playing ? State.Paused : State.Playing;
            pipeline.SetState(_desiredState);
        }

        /// <summary>Seeks by a share of the duration.</summary>
        /// <param name="share">How far to seek, between -1 and 1.</param>
        private void RelativeSeek(double share)
        {
            if (!pipeline.QueryPosition(Format.Time, out long position)
                || !pipeline.QueryDuration(Format.Time, out long duration))
            {
                Console.WriteLine();
                Console.WriteLine("Could not seek.");
                return;
            }

            long step = (long)(duration * share);

            if (Math.Abs(step) < (long)ClockTime.NanosecondsPerSecond)
            {
                step = share < 0
                    ? -(long)ClockTime.NanosecondsPerSecond
                    : (long)ClockTime.NanosecondsPerSecond;
            }

            position += step;

            if (position > duration)
            {
                return;
            }

            Seek(Math.Max(position, 0), _rate, _trickMode);
        }

        /// <summary>Changes the playback rate by a step.</summary>
        /// <param name="step">What to add to the rate.</param>
        private void SetRelativeRate(double step)
        {
            double rate = _rate + step;

            if (!pipeline.QueryPosition(Format.Time, out long position) || !Seek(position, rate, _trickMode))
            {
                Console.WriteLine();
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not change playback rate to {rate:F2}."));
                return;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Playback rate: {rate:F2}"));
        }

        /// <summary>Moves to the next trick mode.</summary>
        private void SwitchTrickMode()
        {
            TrickMode mode = _trickMode + 1;

            if (mode == TrickMode.Last)
            {
                mode = TrickMode.None;
            }

            string description = mode switch
            {
                TrickMode.None => "normal playback, trick modes disabled",
                TrickMode.Default => "trick mode: default",
                TrickMode.DefaultNoAudio => "trick mode: default, no audio",
                TrickMode.KeyUnits => "trick mode: key frames only",
                _ => "trick mode: key frames only, no audio",
            };

            if (!pipeline.QueryPosition(Format.Time, out long position) || !Seek(position, _rate, mode))
            {
                Console.WriteLine();
                Console.WriteLine($"Could not change trick mode to {description}.");
                return;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Rate: {_rate:F2} ({description})"));
        }

        /// <summary>
        /// Sends the flushing seek of <c>play_do_seek</c> at
        /// <c>ges-launcher.c:80-138</c>. The instant rate change of that
        /// function is not reachable from the keyboard, which never asks for
        /// the trick mode flag that turns it on.
        /// </summary>
        /// <param name="position">Where to seek to.</param>
        /// <param name="rate">The rate to play at.</param>
        /// <param name="mode">The trick mode to play in.</param>
        /// <returns><see langword="false"/> when the pipeline refused the seek.</returns>
        private bool Seek(long position, double rate, TrickMode mode)
        {
            if (rate == 0)
            {
                return false;
            }

            SeekFlags flags = SeekFlags.Flush | SeekFlags.Accurate | mode switch
            {
                TrickMode.Default => SeekFlags.Trickmode,
                TrickMode.DefaultNoAudio => SeekFlags.Trickmode | SeekFlags.TrickmodeNoAudio,
                TrickMode.KeyUnits => SeekFlags.TrickmodeKeyUnits,
                TrickMode.KeyUnitsNoAudio => SeekFlags.TrickmodeKeyUnits | SeekFlags.TrickmodeNoAudio,
                _ => SeekFlags.None,
            };

            // A backwards seek plays the segment that ends where the stream is
            // from its beginning, which is what the negative rate reverses.
            using Event seek = rate >= 0
                ? Event.NewSeek(rate, Format.Time, flags, SeekType.Set, position, SeekType.Set, -1)
                : Event.NewSeek(rate, Format.Time, flags, SeekType.Set, 0, SeekType.Set, position);

            // SendEvent consumes the event. The `using` stays correct because
            // Dispose is idempotent, and it is what releases the event on the
            // paths that return before the send.
            if (!pipeline.SendEvent(seek))
            {
                return false;
            }

            _rate = rate;
            _trickMode = mode;
            return true;
        }

        /// <summary>Prints the keys the run listens to.</summary>
        private static void PrintKeyboardHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Interactive mode - keyboard controls:");
            Console.WriteLine();
            Console.WriteLine("        space  pause/unpause");
            Console.WriteLine("     q or ESC  quit");
            Console.WriteLine("  right arrow  seek forward");
            Console.WriteLine("   left arrow  seek backward");
            Console.WriteLine("            +  increase playback rate");
            Console.WriteLine("            -  decrease playback rate");
            Console.WriteLine("            t  enable/disable trick modes");
            Console.WriteLine("            0  seek to beginning");
            Console.WriteLine("            k  show keyboard shortcuts");
            Console.WriteLine();
        }
    }
}
