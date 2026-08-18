using Gst;
using Gst.Interop;
using Xunit;
using Object = Gst.GObject.Object;
using Uri = Gst.Uri;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The members that return a <c>GList</c>, against the library that is
/// installed: the spine is walked and released correctly in each of its three
/// ownership shapes, and the device enumeration that the feature exists for
/// round trips without corrupting anything.
/// </summary>
/// <remarks>
/// The device tests tolerate a machine with no capture hardware and a build
/// with no device plugins: an empty list is a valid answer everywhere, and what
/// is asserted then is that the call round trips at all. Everything that has to
/// hold on every installation is asserted through the registry instead, which
/// is never empty.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ListReturnTests
{
    /// <summary>
    /// How many device providers are started at most. Probing hardware costs
    /// real time, and one provider that answers is enough to exercise the path.
    /// </summary>
    private const int ProviderBudget = 4;

    /// <summary>
    /// <c>gst_type_find_factory_get_list</c> transfers the spine and the
    /// elements. Every installation has type find factories, so this is the
    /// case that has to hold everywhere: the list comes back populated and its
    /// elements are live objects rather than pointers into a freed spine.
    /// </summary>
    [Fact]
    public void ATransferredListComesBackPopulatedAndUsable()
    {
        IReadOnlyList<TypeFindFactory> factories = TypeFindFactory.GetList();

        try
        {
            Assert.NotEmpty(factories);

            foreach (TypeFindFactory factory in factories)
            {
                // Reading through each wrapper is what would fault if the
                // elements had been adopted out of a spine that was already
                // gone, or if the same node had been freed twice.
                Assert.False(string.IsNullOrEmpty(factory.Name));
            }
        }
        finally
        {
            foreach (TypeFindFactory factory in factories)
            {
                factory.Dispose();
            }
        }
    }

    /// <summary>
    /// <c>gst_element_get_pad_template_list</c> transfers nothing: the list
    /// belongs to the element class and is still there afterwards, which is
    /// only true because the spine of a <c>transfer-ownership="none"</c> list is
    /// never freed. Asking twice is how a spine that was freed anyway would
    /// show up.
    /// </summary>
    [Fact]
    public void AListTheLibraryOwnsSurvivesBeingRead()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "templates"));

        IReadOnlyList<PadTemplate> templates = element.GetPadTemplateList();
        IReadOnlyList<PadTemplate> again = element.GetPadTemplateList();

        try
        {
            Assert.NotEmpty(templates);
            Assert.Equal(templates.Count, again.Count);

            for (int i = 0; i < templates.Count; i++)
            {
                // GObject wrappers are interned, so the second walk of the same
                // spine hands the same wrappers back rather than building a
                // second one per object.
                Assert.Same(templates[i], again[i]);
                Assert.False(string.IsNullOrEmpty(templates[i].Name));
            }
        }
        finally
        {
            foreach (PadTemplate template in templates)
            {
                template.Dispose();
            }
        }
    }

    /// <summary>
    /// <c>gst_element_factory_get_static_pad_templates</c> is the other shape a
    /// borrowed list can have: its elements are records rather than objects, and
    /// the wrapper of one is a bare pointer into the static storage of the
    /// factory. Nothing about the list is owned here, which is what asking twice
    /// and reading through the first answer afterwards checks.
    /// </summary>
    /// <remarks>
    /// <c>videotestsrc</c> declares exactly one template, an always-present
    /// source named <c>src</c>, and it is in the base plugin set that every leg
    /// of the matrix installs. Its caps come out of the cache the factory holds:
    /// <c>gst_static_pad_template_get_caps</c> transfers them, so they are
    /// disposed, while the template itself is not.
    /// </remarks>
    [RequiresElementFact("videotestsrc")]
    public void TheStaticPadTemplatesOfAFactoryAreBorrowed()
    {
        using ElementFactory? factory = ElementFactory.Find("videotestsrc");

        Assert.NotNull(factory);

        IReadOnlyList<StaticPadTemplate> templates = factory.GetStaticPadTemplates();
        IReadOnlyList<StaticPadTemplate> again = factory.GetStaticPadTemplates();

        // The templates live in the static storage of the plugin, so the same
        // addresses come back every time. A freed spine or a copied element
        // would show up here.
        Assert.Equal(templates.Count, again.Count);
        Assert.Equal((int)factory.GetNumPadTemplates(), templates.Count);

        StaticPadTemplate source = Assert.Single(templates);

        Assert.Equal(source.Handle, again[0].Handle);

        // The three fields of the structure are not on the wrapper; the pad
        // template built from it carries them as construct-only properties.
        using (PadTemplate built = Assert.IsAssignableFrom<PadTemplate>(source.Get()))
        {
            using Gst.GObject.Value direction = built.GetProperty("direction");
            using Gst.GObject.Value presence = built.GetProperty("presence");
            using Gst.GObject.Value name = built.GetProperty("name-template");

            Assert.Equal(PadDirection.Src, (PadDirection)direction.GetEnum());
            Assert.Equal(PadPresence.Always, (PadPresence)presence.GetEnum());
            Assert.Equal("src", name.GetString());
        }

        // gst_static_caps_get parses on the first call and hands the cached caps
        // out afterwards, with a reference of its own each time. Both are
        // released here, and the second read is what a snapshot of the cache
        // rather than the cache itself would have got wrong: the two calls see
        // one cached GstCaps, not two freshly parsed ones.
        using (Caps caps = source.GetCaps())
        using (Caps twice = source.GetCaps())
        {
            Assert.False(caps.IsEmpty());
            Assert.Equal(caps.Handle, twice.Handle);
            Assert.Contains("video/x-raw", caps.ToString(), StringComparison.Ordinal);
        }

        // Nothing above owned the templates, so the factory still has them.
        Assert.Equal(templates.Count, factory.GetStaticPadTemplates().Count);
    }

    /// <summary>
    /// The two string shapes: <c>gst_uri_get_path_segments</c> owns its strings
    /// and <c>gst_uri_get_query_keys</c> only owns the spine. Both come back
    /// decoded, and the second one must not free strings that still belong to
    /// the URI, which is what reading the URI again afterwards checks.
    /// </summary>
    [Fact]
    public void AStringListIsDecodedInBothOwnershipShapes()
    {
        using Uri? uri = Uri.FromString("http://example.com/alpha/beta?first=1&second=2");

        Assert.NotNull(uri);

        IReadOnlyList<string> segments = uri.GetPathSegments();
        IReadOnlyList<string> keys = uri.GetQueryKeys();

        Assert.Contains("alpha", segments);
        Assert.Contains("beta", segments);
        Assert.Contains("first", keys);
        Assert.Contains("second", keys);

        // The keys are borrowed, so the URI still holds them.
        Assert.Equal("1", uri.GetQueryValue("first"));
        Assert.Contains("second", uri.GetQueryKeys());
    }

    /// <summary>
    /// The path this feature exists for: a provider is started, its devices are
    /// enumerated and it is stopped again. A machine without capture hardware
    /// answers with an empty list, which is a valid answer and not a failure;
    /// every device that is there has to be usable.
    /// </summary>
    [Fact]
    public void DeviceEnumerationRoundTripsThroughAProvider()
    {
        List<string> names = [];

        foreach (DeviceProvider provider in StartedProviders())
        {
            try
            {
                foreach (Device device in provider.GetDevices())
                {
                    names.Add(device.DisplayName);

                    using (Caps? caps = device.GetCaps())
                    using (Structure? properties = device.GetProperties())
                    {
                        // Reading both is what would fault on a device that was
                        // adopted out of a spine that had already been freed.
                        _ = caps?.Handle;
                        _ = properties?.Handle;
                    }

                    device.Dispose();
                }
            }
            finally
            {
                provider.Stop();
                provider.Dispose();
            }
        }

        // Nothing is asserted about the count: a headless runner has no devices
        // at all, and an empty list is the right answer there. What every device
        // that is present has to have is a name.
        Assert.All(names, name => Assert.False(string.IsNullOrEmpty(name)));
    }

    /// <summary>
    /// Enumerating a started provider twice hands the same <c>GstDevice</c>
    /// handles out twice. The interned wrapper absorbs the duplicate reference
    /// and hands the existing wrapper back, so the second walk produces the same
    /// instances rather than a second wrapper per object, which would install a
    /// second toggle reference on it.
    /// </summary>
    /// <remarks>
    /// The boxed half is checked here as well: two independently obtained
    /// <c>GetProperties</c> results are two distinct <c>GstStructure</c>
    /// instances, and disposing both is two <c>g_boxed_free</c> calls on
    /// <c>GST_TYPE_STRUCTURE</c>. That is the empirical proof that the boxed
    /// free of a structure is <c>gst_structure_free</c>, which no code in this
    /// repository can state on its own.
    /// </remarks>
    [Fact]
    public void RepeatedEnumerationHandsOutTheSameWrappers()
    {
        foreach (DeviceProvider provider in StartedProviders())
        {
            try
            {
                IReadOnlyList<Device> first = provider.GetDevices();
                IReadOnlyList<Device> second = provider.GetDevices();

                try
                {
                    Assert.Equal(first.Count, second.Count);

                    for (int i = 0; i < first.Count; i++)
                    {
                        Assert.Same(first[i], second[i]);
                    }

                    foreach (Device device in first)
                    {
                        using Structure? one = device.GetProperties();
                        using Structure? other = device.GetProperties();

                        if (one is not null && other is not null)
                        {
                            Assert.NotEqual(one.Handle, other.Handle);
                        }
                    }
                }
                finally
                {
                    // The second walk returned the very same wrappers, so each
                    // one is disposed exactly once.
                    foreach (Device device in first)
                    {
                        device.Dispose();
                    }
                }
            }
            finally
            {
                provider.Stop();
                provider.Dispose();
            }
        }
    }

    /// <summary>
    /// Materializing the same lists over and over, with collections in between,
    /// must not report anything from a finalizer and must not fault. The
    /// registry list is the part that runs on every machine; the device list
    /// joins in when there is a provider.
    /// </summary>
    [Fact]
    public void RepeatedMaterializationReportsNothingFromAFinalizer()
    {
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            for (int round = 0; round < 20; round++)
            {
                foreach (TypeFindFactory factory in TypeFindFactory.GetList())
                {
                    factory.Dispose();
                }

                if (round % 5 == 4)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    Object.DrainPendingReleases();
                }
            }

            foreach (DeviceProvider provider in StartedProviders())
            {
                try
                {
                    for (int round = 0; round < 5; round++)
                    {
                        foreach (Device device in provider.GetDevices())
                        {
                            device.Dispose();
                        }
                    }
                }
                finally
                {
                    provider.Stop();
                    provider.Dispose();
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Object.DrainPendingReleases();
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Yields the device providers of this machine, started and ready to be
    /// enumerated. The caller stops and disposes each one.
    /// </summary>
    /// <returns>
    /// The providers that could be created and started, at most
    /// <see cref="ProviderBudget"/> of them, and none at all on a build without
    /// device plugins.
    /// </returns>
    /// <remarks>
    /// The factories come from <c>gst_device_provider_factory_list_get_device_providers</c>,
    /// which is one of the members this feature added, so the helper exercises
    /// the static list on the way to the instance one. Naming a provider instead
    /// would tie the test to one platform and to the plugin set of one build.
    /// </remarks>
    private static IEnumerable<DeviceProvider> StartedProviders()
    {
        IReadOnlyList<DeviceProviderFactory> factories =
            DeviceProviderFactory.ListGetDeviceProviders(Rank.None);

        int started = 0;

        try
        {
            foreach (DeviceProviderFactory factory in factories)
            {
                if (started == ProviderBudget)
                {
                    break;
                }

                DeviceProvider? provider = factory.Get();
                if (provider is null)
                {
                    continue;
                }

                if (!provider.Start())
                {
                    provider.Dispose();
                    continue;
                }

                started++;
                yield return provider;
            }
        }
        finally
        {
            foreach (DeviceProviderFactory factory in factories)
            {
                factory.Dispose();
            }
        }
    }
}
