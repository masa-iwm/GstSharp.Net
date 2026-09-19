using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// The result of one generator run.
/// </summary>
/// <param name="Files">The generated files, ordered by relative path.</param>
/// <param name="Diagnostics">Everything the run reported.</param>
/// <param name="Census">What the run emitted and what it left out.</param>
internal sealed record GenerationResult(
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<Diagnostic> Diagnostics,
    EmissionCensus Census)
{
    /// <summary>Gets the Markdown listing of every symbol the run left out.</summary>
    internal string SkipReport => Census.SkipReport();
}

/// <summary>
/// Runs the whole generator: gir parsing, semantic analysis and emission.
/// </summary>
internal static class GenerationPipeline
{
    /// <summary>Name of the sub directory holding the gir files.</summary>
    internal const string ReferenceDirectoryName = "reference";

    /// <summary>Name of the sub directory holding the overlays.</summary>
    internal const string OverlayDirectoryName = "overlays";

    /// <summary>
    /// Name of the listing of the symbols a run left out, written next to the
    /// gir files it was derived from.
    /// </summary>
    internal const string SkipReportFileName = "skip-report.md";

    /// <summary>Parses, analyses and emits every generated module.</summary>
    /// <param name="girDirectory">Directory holding <c>reference/</c> and <c>overlays/</c>.</param>
    /// <returns>The generated files and the diagnostics of the run.</returns>
    internal static GenerationResult Run(string girDirectory)
    {
        string referenceDirectory = Path.Combine(girDirectory, ReferenceDirectoryName);
        if (!Directory.Exists(referenceDirectory))
        {
            throw new DirectoryNotFoundException(
                FormattableString.Invariant($"The gir directory {referenceDirectory} does not exist."));
        }

        Repository repository = Repository.Load(referenceDirectory);
        Overlays overlays = Overlays.Load(Path.Combine(girDirectory, OverlayDirectoryName));
        return Execute(repository, overlays);
    }

    /// <summary>
    /// Emits every generated module of an already loaded repository. The tests
    /// use this with a repository that was parsed from a string.
    /// </summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="overlays">The corrections to apply.</param>
    /// <returns>The generated files and the diagnostics of the run.</returns>
    internal static GenerationResult Execute(Repository repository, Overlays overlays)
    {
        DiagnosticBag diagnostics = new();

        // The renames the mapper answered with, recorded here rather than in
        // the overlays, which stay immutable: Overlays.Empty is shared by every
        // fixture of the test suite.
        HashSet<string> consumedRenames = new(StringComparer.Ordinal);
        NameMapper names = new(overlays, diagnostics, consumedRenames);
        Classifier classifier = new(repository, overlays, diagnostics);

        // Classify everything once so that the diagnostics of a run do not
        // depend on which emitters happen to touch which type.
        foreach (GirNamespace ns in repository.Namespaces)
        {
            foreach (GirRecord record in ns.Records)
            {
                classifier.Classify(record);
            }
        }

        // The class structs are paired with their virtual methods before any
        // emitter plans a callable: the pairing is what stamps a virtual method
        // with the key an annotation correction addresses it by, so a model
        // built later would leave those corrections unapplied and, worse,
        // reported stale.
        Dictionary<string, HashSet<string>> emittedVirtuals = new(StringComparer.Ordinal);
        SubclassModel subclasses = SubclassModel.Build(repository, overlays, diagnostics);
        ReportStaleVirtualKeys(subclasses, overlays, diagnostics);

        TypeMap types = new(repository, classifier, names, diagnostics);
        SkipRules skipRules = new(overlays);
        EmissionCensus census = new(overlays);
        EnumEmitter enumEmitter = new(names, overlays, diagnostics, census);
        Dictionary<string, List<string>> inherited = new(StringComparer.Ordinal);

        // The array corrections are consumed by a planner that is built per
        // module, and reported once for the whole run, so the record of what
        // was applied belongs here. The overlays themselves stay immutable:
        // Overlays.Empty is shared by every fixture of the test suite.
        HashSet<string> consumedArrayOverrides = new(StringComparer.Ordinal);
        HashSet<string> consumedAnnotationOverrides = new(StringComparer.Ordinal);
        HashSet<string> consumedInstanceKeyedCallbacks = new(StringComparer.Ordinal);
        HashSet<string> consumedDocNotes = new(StringComparer.Ordinal);
        HashSet<string> consumedSignalDocNotes = new(StringComparer.Ordinal);
        HashSet<string> consumedPreconditions = new(StringComparer.Ordinal);
        HashSet<string> consumedHandOverRefusals = new(StringComparer.Ordinal);
        HashSet<string> consumedDocStrips = new(StringComparer.Ordinal);
        HashSet<string> consumedSiblingArguments = new(StringComparer.Ordinal);
        HashSet<string> lentOpaqueRecords = new(StringComparer.Ordinal);

        // The skip entries the planner matched against a type nothing else
        // reads: a callback type has no emitter loop of its own. They are
        // folded into the census below, so that the stale report has one place
        // to ask.
        HashSet<string> consumedSkips = new(StringComparer.Ordinal);

        List<GeneratedFile> files = [];
        foreach (ModuleInfo module in ModuleMap.Modules)
        {
            if (!module.IsGenerated)
            {
                continue;
            }

            GirNamespace? ns = repository.FindNamespace(module.GirNamespace);
            if (ns is null)
            {
                diagnostics.Warn(
                    "GEN0005",
                    $"No gir file was found for the '{module.GirNamespace}' namespace; the module is skipped.");
                continue;
            }

            files.AddRange(EmitModule(
                new ModuleEmitters(
                    repository,
                    classifier,
                    names,
                    types,
                    overlays,
                    skipRules,
                    census,
                    diagnostics,
                    enumEmitter,
                    inherited,
                    consumedArrayOverrides,
                    consumedAnnotationOverrides,
                    consumedInstanceKeyedCallbacks,
                    consumedDocNotes,
                    consumedSignalDocNotes,
                    consumedPreconditions,
                    consumedHandOverRefusals,
                    consumedDocStrips,
                    consumedSiblingArguments,
                    lentOpaqueRecords,
                    consumedSkips,
                    subclasses,
                    emittedVirtuals),
                module,
                ns));
        }

        // An array correction that matched nothing is a statement about a gir
        // that has moved on. Reporting it is what keeps the overlays from
        // accumulating entries that describe a symbol which no longer exists,
        // or a parameter that is no array.
        List<string> stale = [];
        foreach (string key in overlays.ArrayOverrideKeys)
        {
            if (!consumedArrayOverrides.Contains(key))
            {
                stale.Add(key);
            }
        }

        stale.Sort(StringComparer.Ordinal);
        foreach (string key in stale)
        {
            diagnostics.Warn(
                "GEN0020",
                $"The array override '{key}' matched no array parameter or return value; the entry is stale.");
        }

        // An annotation correction is read wherever the planner asks for one,
        // so a key that was never read matched no callable, no parameter and
        // no signal argument of this run. Silently ignoring it is what lets
        // the overlays describe a gir that has moved on, which the corrections
        // of an array are already protected from.
        List<string> staleAnnotations = [];
        foreach (string key in overlays.AnnotationOverrideKeys)
        {
            if (!consumedAnnotationOverrides.Contains(key))
            {
                staleAnnotations.Add(key);
            }
        }

        staleAnnotations.Sort(StringComparer.Ordinal);
        foreach (string key in staleAnnotations)
        {
            diagnostics.Warn(
                "GEN0024",
                $"The annotation override '{key}' matched no callable, parameter or signal argument; "
                + "the entry is stale.");
        }

        // An instance keyed entry the run never read names a callback that no
        // longer exists or one no module emits, which would leave a callback
        // whose state has nowhere to live looking as though it were handled.
        List<string> staleInstanceKeyed = [];
        foreach (string key in overlays.InstanceKeyedCallbackKeys)
        {
            if (!consumedInstanceKeyedCallbacks.Contains(key))
            {
                staleInstanceKeyed.Add(key);
            }
        }

        staleInstanceKeyed.Sort(StringComparer.Ordinal);
        foreach (string key in staleInstanceKeyed)
        {
            diagnostics.Warn(
                "GEN0041",
                $"The instance keyed callback '{key}' names no emitted callback; the entry is stale.");
        }

        // And the notes, for the same reason: a note keyed by an identifier the
        // run never planned is a sentence nothing says.
        List<string> staleDocNotes = [];
        foreach (string key in overlays.DocNoteKeys)
        {
            if (!consumedDocNotes.Contains(key))
            {
                staleDocNotes.Add(key);
            }
        }

        staleDocNotes.Sort(StringComparer.Ordinal);
        foreach (string key in staleDocNotes)
        {
            diagnostics.Warn(
                "GEN0042",
                $"The documentation note '{key}' names no planned callable; the entry is stale.");
        }

        // A note on a signal is consumed where the signal was planned, so one
        // that named no planned signal is a sentence nothing says either.
        List<string> staleSignalDocNotes = [];
        foreach (string key in overlays.SignalDocNoteKeys)
        {
            if (!consumedSignalDocNotes.Contains(key))
            {
                staleSignalDocNotes.Add(key);
            }
        }

        staleSignalDocNotes.Sort(StringComparer.Ordinal);
        foreach (string key in staleSignalDocNotes)
        {
            diagnostics.Warn(
                "GEN0048",
                $"The documentation note '{key}' names no planned signal; the entry is stale.");
        }

        // A precondition is consumed where the callable it guards was
        // rendered, so a key nothing consumed guards nothing: a misspelled
        // c:identifier, one the overlays skip, one a hand written member
        // replaced, or one that names a slot or a signal rather than a
        // callable. The member it was written for would go on calling the C
        // that the entry exists to keep it out of.
        List<string> stalePreconditions = [];
        foreach (string key in overlays.PreconditionKeys)
        {
            if (!consumedPreconditions.Contains(key))
            {
                stalePreconditions.Add(key);
            }
        }

        stalePreconditions.Sort(StringComparer.Ordinal);
        foreach (string key in stalePreconditions)
        {
            diagnostics.Warn(
                "GEN0049",
                $"The preconditions of '{key}' name a callable that was not rendered by this run; "
                + "the entry is stale.");
        }

        // A hand over refusal is read where the callable it releases for was
        // planned, so a key nothing read releases nothing: the member it was
        // written for goes on leaving the reference minted for its argument
        // with no owner on the refusal path, which is the leak the entry
        // exists to close.
        List<string> staleHandOverRefusals = [];
        foreach (string key in overlays.HandOverRefusalKeys)
        {
            if (!consumedHandOverRefusals.Contains(key))
            {
                staleHandOverRefusals.Add(key);
            }
        }

        staleHandOverRefusals.Sort(StringComparer.Ordinal);
        foreach (string key in staleHandOverRefusals)
        {
            diagnostics.Warn(
                "GEN0051",
                $"The hand over refusal entry '{key}' names a callable that was not rendered by "
                + "this run; the entry is stale.");
        }

        // And a documentation strip that was read nowhere is a sentence the
        // generated member goes on carrying: the upstream rule that is not the
        // rule of the binding, standing above the remark that contradicts it.
        List<string> staleDocStrips = [];
        foreach (string key in overlays.DocStripKeys)
        {
            if (!consumedDocStrips.Contains(key))
            {
                staleDocStrips.Add(key);
            }
        }

        staleDocStrips.Sort(StringComparer.Ordinal);
        foreach (string key in staleDocStrips)
        {
            diagnostics.Warn(
                "GEN0053",
                $"The documentation strip entry '{key}' names a callable that was not rendered by "
                + "this run; the entry is stale.");
        }

        // A sibling argument entry is only consumed where it named the shape it
        // describes: a GObject a slot is lent. One that named no parameter of a
        // slot at all, or one whose parameter turned out to be of another
        // shape, would silently leave that parameter on the borrowing bucket,
        // which is the very projection the entry exists to replace.
        List<string> staleSiblings = [];
        foreach (string key in overlays.VfuncSiblingArgumentKeys)
        {
            if (!consumedSiblingArguments.Contains(key))
            {
                staleSiblings.Add(key);
            }
        }

        staleSiblings.Sort(StringComparer.Ordinal);
        foreach (string key in staleSiblings)
        {
            diagnostics.Warn(
                "GEN0044",
                $"The sibling argument '{key}' names no parameter of a slot that is lent a GObject; "
                + "the entry is stale.");
        }

        // The wrapper of a lent opaque record forgets its pointer when the call
        // returns, and that is a decision about the record: it is taken while
        // the record is written out, long before the slots that lend it are
        // planned. The list is therefore stated rather than derived, and both
        // ways of it going wrong are reported here. A record a slot lends and
        // the list does not name would keep a wrapper alive over an address
        // that means nothing after the call, which is why that half is an
        // error rather than a warning.
        List<string> unlisted = [];
        foreach (string name in lentOpaqueRecords)
        {
            if (!overlays.IsLentOpaqueRecord(name))
            {
                unlisted.Add(name);
            }
        }

        unlisted.Sort(StringComparer.Ordinal);
        foreach (string name in unlisted)
        {
            diagnostics.Error(
                "GEN0045",
                $"A virtual method is lent a '{name}', which 'lentOpaqueRecords' does not name, so its "
                + "wrapper is not detached when the call returns. Add it to girs/overlays/fixups.json.");
        }

        List<string> unlent = [];
        foreach (string name in overlays.LentOpaqueRecordKeys)
        {
            if (!lentOpaqueRecords.Contains(name))
            {
                unlent.Add(name);
            }
        }

        unlent.Sort(StringComparer.Ordinal);
        foreach (string name in unlent)
        {
            diagnostics.Warn(
                "GEN0046",
                $"The lent opaque record '{name}' is lent by no slot of a subclassable class; "
                + "the entry is stale.");
        }

        // A field skip the run never matched names a field that no longer
        // exists, one of a record that is not emitted, or a misspelling; an
        // entry that states neither an exposing member nor that the field is
        // hand written says nothing about it at all, and one that states both
        // says two different things about who answers it. Any of the three
        // would leave the ledger quiet about a field on the strength of a claim
        // nothing checks, which is what the report exists to prevent.
        List<string> staleFields = [];
        foreach (string key in overlays.FieldSkipKeys)
        {
            if (!census.FieldSkipKeys.Contains(key) || overlays.GetFieldSkip(key) is not { IsStated: true })
            {
                staleFields.Add(key);
            }
        }

        staleFields.Sort(StringComparer.Ordinal);
        foreach (string key in staleFields)
        {
            diagnostics.Warn(
                "GEN0025",
                $"The field skip '{key}' matched no field of an emitted record, or states neither "
                + "'exposedBy' nor 'handBound', or states both; the entry is stale.");
        }

        // A field annotation the run never applied names a field that no longer
        // exists, one of a record that is not emitted, or a misspelling; an
        // entry that states nothing, or that states the default the emitters
        // already use, corrects nothing. Either way the overlays would carry a
        // claim about the C implementation that nothing acts on, which reads as
        // a decision that was taken when none was.
        List<(string Key, string Fault)> staleAnnotatedFields = [];
        foreach (string key in overlays.FieldAnnotationKeys)
        {
            // The shape of the entry is read first, so that one that cannot be
            // acted on at all is reported for what is wrong with it rather than
            // for the run never having applied it, which is only the
            // consequence.
            if (overlays.GetFieldAnnotation(key)?.ShapeFault is { } fault)
            {
                staleAnnotatedFields.Add((key, fault));
            }
            else if (census.RedundantFieldNames.Contains(key))
            {
                staleAnnotatedFields.Add((key, "names the member the field derives anyway"));
            }
            else if (!census.FieldAnnotationKeys.Contains(key))
            {
                staleAnnotatedFields.Add((key, "was applied to no field of an emitted record"));
            }
        }

        staleAnnotatedFields.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        foreach ((string key, string fault) in staleAnnotatedFields)
        {
            diagnostics.Warn("GEN0026", $"The field annotation '{key}' {fault}; the entry is stale.");
        }

        // A hand bound entry the run never saw skipped names a symbol that is
        // generated after all, or one that no longer exists, or a misspelling.
        // Any of the three makes the ledger claim something about the bindings
        // that is not true, which is exactly what the annotation exists to
        // prevent.
        List<string> unseen = [];
        foreach (string identifier in overlays.HandBoundIdentifiers)
        {
            if (!census.HandBoundSymbols.Contains(identifier))
            {
                unseen.Add(identifier);
            }
        }

        unseen.Sort(StringComparer.Ordinal);
        foreach (string identifier in unseen)
        {
            diagnostics.Warn(
                "GEN0023",
                $"The hand bound entry '{identifier}' was not skipped by this run; the entry is stale.");
        }

        // A skip entry spelled as a signal - Ns.Type::signal, the one skip key
        // that names neither a callable, nor a type, nor a property - is read
        // in the signal loop alone, and nowhere else reads a key of that
        // shape. One the loop never matched names a signal that no longer
        // exists, one of a type that is not emitted, or a misspelling, and the
        // event it was written to keep out is generated again: the shape the
        // entry calls wrong returns to the surface beside the hand written
        // member that answers the signal, under a name that is then declared
        // twice.
        List<string> staleSignalSkips = [];
        foreach (string key in overlays.SkippedIdentifiers)
        {
            if (key.Contains("::", StringComparison.Ordinal) && !census.SignalSkipKeys.Contains(key))
            {
                staleSignalSkips.Add(key);
            }
        }

        staleSignalSkips.Sort(StringComparer.Ordinal);
        foreach (string key in staleSignalSkips)
        {
            diagnostics.Warn(
                "GEN0055",
                $"The skipped signal '{key}' matched no signal of an emitted type; the entry is stale.");
        }

        // Every other shape of skip entry - the c:identifier of a callable, a
        // qualified type name, and the Gst.Type:property spelling - is read
        // where the thing it names would otherwise be emitted, and the run
        // records the key it matched there. One that matched nothing keeps
        // nothing off the surface: it names a symbol the gir no longer
        // declares, a type of a module this run does not generate, or a
        // misspelling, and what it was written to hold back is either gone or
        // generated anyway. The signal shape is left to GEN0055 above, which
        // says the same thing about it in the words of a signal.
        foreach (string key in consumedSkips)
        {
            census.SkippedOverlayKey(key);
        }

        List<string> staleSkips = [];
        foreach (string key in overlays.SkippedIdentifiers)
        {
            if (!key.Contains("::", StringComparison.Ordinal) && !census.OverlaySkipKeys.Contains(key))
            {
                staleSkips.Add(key);
            }
        }

        staleSkips.Sort(StringComparer.Ordinal);
        foreach (string key in staleSkips)
        {
            diagnostics.Warn(
                "GEN0056",
                $"The skip entry '{key}' matched no callable, type or property of this run; the entry is stale.");
        }

        // A rename the mapper never answered with names nothing this run emits.
        // The name it decided is therefore not the name anything carries, and
        // the entry reads as a decision that is in force when it is not: the
        // next reader of the overlays has no way of telling the two apart.
        List<string> staleRenames = [];
        foreach (string key in overlays.RenameKeys)
        {
            if (!consumedRenames.Contains(key))
            {
                staleRenames.Add(key);
            }
        }

        staleRenames.Sort(StringComparer.Ordinal);
        foreach (string key in staleRenames)
        {
            diagnostics.Warn(
                "GEN0057",
                $"The rename '{key}' named nothing this run emitted; the entry is stale.");
        }

        files.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return new GenerationResult(files, diagnostics.Items, census);
    }

    /// <summary>Emits one module.</summary>
    /// <param name="shared">The analysis that every module shares.</param>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">Its gir namespace.</param>
    /// <returns>The files of the module.</returns>
    private static IReadOnlyList<GeneratedFile> EmitModule(ModuleEmitters shared, ModuleInfo module, GirNamespace ns)
    {
        // The planner is built per module, because it collects the callbacks
        // that the members of this module hand to native code.
        MarshalPlanner planner = new(
            shared.Repository,
            shared.Classifier,
            shared.Names,
            shared.Types,
            shared.Overlays,
            shared.SkipRules,
            shared.Diagnostics,
            shared.ConsumedArrayOverrides,
            shared.ConsumedAnnotationOverrides,
            shared.ConsumedInstanceKeyedCallbacks,
            shared.ConsumedDocNotes,
            shared.ConsumedSignalDocNotes,
            shared.ConsumedPreconditions,
            shared.ConsumedHandOverRefusals,
            shared.ConsumedDocStrips,
            shared.ConsumedSiblingArguments,
            shared.LentOpaqueRecords,
            shared.ConsumedSkips);

        SurfaceBuilder surfaces = new(
            planner,
            shared.Names,
            shared.Types,
            shared.Overlays,
            shared.Census,
            shared.Diagnostics);
        List<RegistryEntry> registry = [];
        RecordEmitter recordEmitter = new(
            shared.Repository,
            shared.Classifier,
            shared.Names,
            shared.Types,
            shared.Overlays,
            shared.SkipRules,
            shared.Diagnostics,
            surfaces,
            shared.Census,
            registry);

        ClassEmitter classEmitter = new(
            shared.Repository,
            shared.Classifier,
            shared.Names,
            surfaces,
            shared.Overlays,
            shared.Census,
            shared.Diagnostics,
            registry,
            shared.Inherited);

        List<InterfaceRegistryEntry> interfaceRegistry = [];
        InterfaceEmitter interfaceEmitter = new(
            shared.Names,
            surfaces,
            shared.Overlays,
            shared.Census,
            interfaceRegistry);

        CallbackEmitter callbackEmitter = new(planner, shared.Census);
        RegistryEmitter registryEmitter = new();

        List<GeneratedFile> files = [];
        if (shared.Enums.Emit(module, ns) is { } enumFile)
        {
            files.Add(enumFile);
        }

        ClassStructEmitter classStructEmitter = new(shared.Repository, shared.Census, shared.Diagnostics);
        files.AddRange(classStructEmitter.Emit(module, ns, shared.Subclasses));

        VfuncEmitter vfuncEmitter = new(
            planner,
            shared.Census,
            shared.Overlays,
            shared.Diagnostics,
            shared.EmittedVirtuals);
        files.AddRange(vfuncEmitter.Emit(module, ns, shared.Subclasses));
        files.AddRange(recordEmitter.Emit(module, ns));
        files.AddRange(classEmitter.Emit(module, ns));
        files.AddRange(interfaceEmitter.Emit(module, ns));
        files.AddRange(classEmitter.EmitEnumFunctions(module, ns));

        if (classEmitter.EmitGlobal(module, ns) is { } globalFile)
        {
            files.Add(globalFile);
        }

        // A callback type whose only consumers are hand bound is reached by
        // nothing the emitters planned, and would leave the module although
        // the bindings do hand it out. The ledger names those consumers, so
        // they claim their callback types here.
        planner.PlanHandBoundCallbacks(module, ns);

        // Every emitter has run, so the set of reachable callbacks is complete.
        if (callbackEmitter.Emit(module, ns) is { } callbackFile)
        {
            files.Add(callbackFile);
        }

        // The events of the module share one holder of the connected handlers,
        // which is only worth emitting when the module has events at all.
        if (shared.Census.EmittedCount(module.GirNamespace, "signal") > 0)
        {
            files.Add(SignalEmitter.EmitConnections(module, ns));
        }

        files.Add(registryEmitter.Emit(module, ns, registry, interfaceRegistry));
        return files;
    }

    /// <summary>
    /// The analysis and the emitters that every module of a run shares.
    /// </summary>
    /// <param name="Repository">The loaded gir repository.</param>
    /// <param name="Classifier">The type classifier.</param>
    /// <param name="Names">The name mapper.</param>
    /// <param name="Types">The type map.</param>
    /// <param name="Overlays">The overlay configuration.</param>
    /// <param name="SkipRules">The skip rules.</param>
    /// <param name="Census">The census of the run.</param>
    /// <param name="Diagnostics">The diagnostic sink.</param>
    /// <param name="Enums">The enumeration emitter.</param>
    /// <param name="Inherited">
    /// The members of every class the run has emitted so far, keyed by
    /// qualified gir name and shared by every module.
    /// </param>
    /// <param name="ConsumedArrayOverrides">
    /// The keys of the array corrections the run has applied, shared by every
    /// module so that the stale ones can be reported once.
    /// </param>
    /// <param name="ConsumedAnnotationOverrides">
    /// The keys of the annotation corrections the run has read, shared for the
    /// same reason.
    /// </param>
    /// <param name="ConsumedInstanceKeyedCallbacks">
    /// The keys of the instance keyed callback entries the run has read,
    /// shared for the same reason.
    /// </param>
    /// <param name="ConsumedDocNotes">
    /// The keys of the documentation notes the run has attached, shared for
    /// the same reason.
    /// </param>
    /// <param name="ConsumedSignalDocNotes">
    /// The keys of the signal documentation notes the run has attached, shared
    /// for the same reason.
    /// </param>
    /// <param name="ConsumedPreconditions">
    /// The keys of the precondition entries the run has emitted, shared for
    /// the same reason.
    /// </param>
    /// <param name="ConsumedHandOverRefusals">
    /// The keys of the hand over refusal entries the run has read, shared for
    /// the same reason.
    /// </param>
    /// <param name="ConsumedDocStrips">
    /// The keys of the documentation strip entries the run has read, shared for
    /// the same reason.
    /// </param>
    /// <param name="ConsumedSiblingArguments">
    /// The keys of the sibling argument entries the run has matched, shared
    /// for the same reason.
    /// </param>
    /// <param name="LentOpaqueRecords">
    /// The gir names of the opaque records the run has seen a slot lent, shared
    /// for the same reason.
    /// </param>
    /// <param name="ConsumedSkips">
    /// The skip entries the planner of a module matched against a type it was
    /// asked to project, shared for the same reason.
    /// </param>
    private sealed record ModuleEmitters(
        Repository Repository,
        Classifier Classifier,
        NameMapper Names,
        TypeMap Types,
        Overlays Overlays,
        SkipRules SkipRules,
        EmissionCensus Census,
        DiagnosticBag Diagnostics,
        EnumEmitter Enums,
        Dictionary<string, List<string>> Inherited,
        HashSet<string> ConsumedArrayOverrides,
        HashSet<string> ConsumedAnnotationOverrides,
        HashSet<string> ConsumedInstanceKeyedCallbacks,
        HashSet<string> ConsumedDocNotes,
        HashSet<string> ConsumedSignalDocNotes,
        HashSet<string> ConsumedPreconditions,
        HashSet<string> ConsumedHandOverRefusals,
        HashSet<string> ConsumedDocStrips,
        HashSet<string> ConsumedSiblingArguments,
        HashSet<string> LentOpaqueRecords,
        HashSet<string> ConsumedSkips,
        SubclassModel Subclasses,
        Dictionary<string, HashSet<string>> EmittedVirtuals);

    /// <summary>
    /// Reports the overlay entries about virtual methods that name no slot of
    /// a subclassable class.
    /// </summary>
    /// <param name="subclasses">The class structs of the run.</param>
    /// <param name="overlays">The corrections to check.</param>
    /// <param name="diagnostics">Where a stale entry is reported.</param>
    /// <remarks>
    /// A misspelled key is worse here than elsewhere: a <c>vfuncDefaults</c>
    /// entry that lands nowhere silently turns a slot with a documented default
    /// into one whose chain-up throws, and a <c>skipVirtuals</c> entry that
    /// lands nowhere silently emits a slot the ledger claims was left out.
    /// </remarks>
    private static void ReportStaleVirtualKeys(
        SubclassModel subclasses,
        Overlays overlays,
        DiagnosticBag diagnostics)
    {
        Report(
            "GEN0029",
            overlays.SkippedVirtualKeys,
            subclasses.VirtualMethodKeys,
            "The skipped virtual method '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0030",
            overlays.VfuncDefaultKeys,
            subclasses.VirtualMethodKeys,
            "The virtual method default '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0037",
            overlays.VfuncDocNoteKeys,
            subclasses.VirtualMethodKeys,
            "The virtual method note '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0036",
            overlays.VfuncNonNullReturnKeys,
            subclasses.VirtualMethodKeys,
            "The non null return '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0038",
            overlays.VfuncFailureValueKeys,
            subclasses.VirtualMethodKeys,
            "The virtual method failure value '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0039",
            overlays.VfuncSpanKeys,
            subclasses.VirtualMethodParameterKeys,
            "The read only block '{0}' names no parameter of a slot of a subclassable class; "
            + "the entry is stale.");
        Report(
            "GEN0031",
            overlays.VfuncIdentityBufferKeys,
            subclasses.VirtualMethodParameterKeys,
            "The identity buffer '{0}' names no parameter of a slot of a subclassable class; "
            + "the entry is stale.");

        void Report(string code, IReadOnlyCollection<string> keys, IReadOnlySet<string> known, string message)
        {
            List<string> stale = [];
            foreach (string key in keys)
            {
                if (!known.Contains(key))
                {
                    stale.Add(key);
                }
            }

            stale.Sort(StringComparer.Ordinal);
            foreach (string key in stale)
            {
                diagnostics.Warn(code, string.Format(System.Globalization.CultureInfo.InvariantCulture, message, key));
            }
        }
    }
}
