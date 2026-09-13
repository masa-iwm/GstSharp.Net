using Gst;
using Gst.App;
using Gst.GLib;
using Gst.Rtsp;
using Gst.RtspServer;
using Xunit;
using Xunit.Abstractions;
using DateTime = System.DateTime;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The authentication objects of the RtspServer module on their own, and then
/// all of them together in front of a mount that a real client reaches only
/// with the credentials the server was given.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RTSPAuth.Check"/> is deliberately absent. It reads the RTSP
/// context of the request being served out of thread local storage and logs a
/// critical where there is none, so it is only callable from inside a request,
/// which is what the last test exercises through a real client instead.
/// </para>
/// <para>
/// The field names of a token and the permissions of a media factory are
/// spelled out as strings because the module binds no constants for them:
/// <c>GST_RTSP_TOKEN_MEDIA_FACTORY_ROLE</c> and its neighbours are C macros
/// that the gir does not carry.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspServerAuthTests
{
    private const string Launch = "( audiotestsrc is-live=true ! audioconvert ! rtpL16pay name=pay0 pt=96 )";

    private const string RoleField = "media.factory.role";
    private const string AccessPermission = "media.factory.access";
    private const string ConstructPermission = "media.factory.construct";
    private const string Role = "user";

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtspServerAuthTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A token answers the fields it was given, says nothing about the ones it
    /// was not, and carries both in the structure behind it.
    /// </summary>
    [Fact]
    public void ATokenAnswersItsFieldsAndDeniesWhatItDoesNotHold()
    {
        using RTSPToken token = RTSPToken.New();

        token.SetString(RoleField, Role);

        Assert.Equal(Role, token.GetString(RoleField));
        Assert.Null(token.GetString("absent"));
        Assert.False(token.IsAllowed("absent"));

        using Structure structure = token.GetStructure();
        _output.WriteLine(structure.ToString());

        Assert.True(structure.HasField(RoleField));
        Assert.Equal(Role, structure.GetString(RoleField));
    }

    /// <summary>
    /// Permissions are answered per role: the role that holds one gets it, the
    /// role that was told no does not, and a role the object never heard of
    /// gets nothing at all.
    /// </summary>
    /// <remarks>
    /// <c>gst_rtsp_permissions_get_role</c> answers NULL for a role it does not
    /// know, which the overlays correct on
    /// <c>gst_rtsp_permissions_get_role#return</c> in
    /// <c>girs/overlays/fixups.json</c>; without that the miss below would be
    /// an exception.
    /// </remarks>
    [Fact]
    public void PermissionsAreKeyedByRole()
    {
        using RTSPPermissions permissions = RTSPPermissions.New();

        permissions.AddRole(Role);
        permissions.AddPermissionForRole(Role, AccessPermission, true);
        permissions.AddPermissionForRole(Role, ConstructPermission, false);

        Assert.True(permissions.IsAllowed(Role, AccessPermission));
        Assert.False(permissions.IsAllowed(Role, ConstructPermission));
        Assert.False(permissions.IsAllowed("nobody", AccessPermission));

        Structure? held = permissions.GetRole(Role);
        Assert.NotNull(held);
        _output.WriteLine(held.ToString());

        Assert.Null(permissions.GetRole("nobody"));

        permissions.RemoveRole(Role);
        Assert.Null(permissions.GetRole(Role));
    }

    /// <summary>
    /// An authentication object keeps the basic entries, the realm and the
    /// methods it was given, and gives an entry up again when it is removed.
    /// </summary>
    [Fact]
    public void AnAuthKeepsItsBasicEntriesRealmAndMethods()
    {
        string basic = RTSPAuth.MakeBasic(Role, "pass");
        Assert.Equal("dXNlcjpwYXNz", basic);

        using RTSPAuth auth = RTSPAuth.New();
        using RTSPToken token = RTSPToken.New();
        token.SetString(RoleField, Role);

        auth.AddBasic(basic, token);

        auth.SetDefaultToken(token);

        // A token is a boxed type rather than a GObject, so no wrapper is
        // interned for it: the getter mints a second wrapper of the same
        // storage, which owns a reference of its own and is disposed here.
        using RTSPToken? fallback = auth.GetDefaultToken();
        Assert.NotNull(fallback);
        Assert.Equal(token.Handle, fallback.Handle);

        auth.SetRealm("GstSharp");
        Assert.Equal("GstSharp", auth.GetRealm());

        auth.SetSupportedMethods(RTSPAuthMethod.Basic);
        Assert.Equal(RTSPAuthMethod.Basic, auth.GetSupportedMethods());

        // Taking the entry away again is the only observable half of the pair
        // from outside a request.
        auth.RemoveBasic(basic);
    }

    /// <summary>
    /// A client that presents the credentials of the server reaches the mount
    /// and receives data; the same client without them is refused, and says so
    /// on its bus as a resource error that names the refusal.
    /// </summary>
    /// <remarks>
    /// <c>rtspsrc</c> turns a 401 into
    /// <c>GST_RESOURCE_ERROR_NOT_AUTHORIZED</c>
    /// (gst-plugins-good/gst/rtsp/gstrtspsrc.c:7445-7447, the
    /// <c>GST_RTSP_STS_UNAUTHORIZED</c> case of the response switch; the same
    /// case stands on the 1.24 floor, at :7007). Only the domain and the code are
    /// asserted: the text of the message differs between releases.
    /// </remarks>
    [RequiresElementFact("rtspsrc", "rtpL16pay", "audiotestsrc", "audioconvert")]
    public void ABasicCredentialOpensAProtectedMountAndItsAbsenceIsRefused()
    {
        using MainContext context = MainContext.New();
        using RTSPServer server = RTSPServer.New();

        server.SetAddress("127.0.0.1");
        server.SetService("0");

        RTSPThreadPool? threadPool = server.GetThreadPool();
        Assert.NotNull(threadPool);
        threadPool.SetMaxThreads(0);

        RTSPMountPoints? mounts = server.GetMountPoints();
        Assert.NotNull(mounts);

        using RTSPMediaFactory factory = RTSPMediaFactory.New();
        factory.SetLaunch(Launch);
        factory.SetShared(true);

        using RTSPPermissions permissions = RTSPPermissions.New();
        permissions.AddRole(Role);
        permissions.AddPermissionForRole(Role, AccessPermission, true);
        permissions.AddPermissionForRole(Role, ConstructPermission, true);
        factory.SetPermissions(permissions);

        using RTSPAuth auth = RTSPAuth.New();
        using RTSPToken token = RTSPToken.New();
        token.SetString(RoleField, Role);
        auth.AddBasic(RTSPAuth.MakeBasic(Role, "pass"), token);
        server.SetAuth(auth);

        int configured = 0;
        factory.MediaConfigure += (_, _) => Interlocked.Increment(ref configured);

        mounts.AddFactory("/test", factory);

        uint sourceId = server.Attach(context);
        Assert.True(sourceId > 0, "Attach answered 0, which is its failure.");

        int port = server.GetBoundPort();
        Assert.True(port > 0, $"bound port {port} is not a port.");

        // The refusal first: it is the cheaper half and it proves the gate is
        // shut before anything tries to walk through it.
        RefusedWithoutCredentials(context, port);

        AdmittedWithCredentials(context, port, () => Volatile.Read(ref configured) > 0);

        // 1. Stop accepting.
        Assert.True(server.Detach(sourceId, context));

        // 2. Close every connection.
        Assert.Empty(server.ClientFilter((_, _) => RTSPFilterResult.Remove));

        // 3. Drop the sessions.
        RTSPSessionPool? sessionPool = server.GetSessionPool();
        Assert.NotNull(sessionPool);
        Assert.Empty(sessionPool.Filter((_, _) => RTSPFilterResult.Remove));

        // 4. Wait for the asynchronous half of the close.
        Assert.True(
            PumpUntil(context, () => DisposeAll(server.ClientFilter(null)) == 0),
            "a client was still managed after the filter removed it.");
    }

    /// <summary>
    /// Runs a client without credentials against the mount and asserts that the
    /// refusal reaches its bus as a resource error.
    /// </summary>
    /// <param name="context">The context that drives the server.</param>
    /// <param name="port">The port the server bound.</param>
    private void RefusedWithoutCredentials(MainContext context, int port)
    {
        using Element client = Global.ParseLaunch(
            $"rtspsrc location=rtsp://127.0.0.1:{port}/test latency=0 ! fakesink sync=false");

        using Bus bus = Assert.IsAssignableFrom<Pipeline>(client).GetBus();

        GException? failure = null;

        try
        {
            client.SetState(State.Playing);

            Assert.True(
                PumpUntil(context, () =>
                {
                    using Message? message = bus.PopFiltered(MessageType.Error);

                    if (message is null)
                    {
                        return false;
                    }

                    failure = message.ParseError().Error;
                    return true;
                }),
                "a client without credentials was never refused.");
        }
        finally
        {
            StopFromAnotherThread(context, client);
        }

        Assert.NotNull(failure);

        _output.WriteLine($"refused with domain={failure.Domain} code={failure.Code}: {failure.Message}");

        Assert.Equal(ResourceErrorExtensions.Quark(), failure.Domain);
        Assert.Equal((int)ResourceError.NotAuthorized, failure.Code);
    }

    /// <summary>
    /// Runs a client with the credentials of the server against the mount and
    /// asserts that it gets in.
    /// </summary>
    /// <param name="context">The context that drives the server.</param>
    /// <param name="port">The port the server bound.</param>
    /// <param name="constructed">Answers whether the mount built a medium.</param>
    private void AdmittedWithCredentials(MainContext context, int port, Func<bool> constructed)
    {
        // GstApp has to have run its module initialiser before the sink of
        // the pipeline resolves as an AppSink; see Gst.App.GstApp.
        GstApp.Initialize();

        using Pipeline client = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            $"rtspsrc location=rtsp://127.0.0.1:{port}/test latency=0 user-id={Role} user-pw=pass ! " +
            "appsink name=sink sync=false emit-signals=true"));

        using Bus bus = client.GetBus();

        using Element? sinkElement = client.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(sinkElement);

        int received = 0;

        // The handler runs on the streaming thread of the sink, never on the
        // thread that pumps the server.
        FlowReturn OnNewSample(object? sender, EventArgs args)
        {
            using Sample? sample = sink.PullSample();

            if (sample is null)
            {
                return FlowReturn.Eos;
            }

            Interlocked.Increment(ref received);
            return FlowReturn.Ok;
        }

        sink.NewSample += OnNewSample;

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, client.SetState(State.Playing));

            Assert.True(
                PumpUntil(context, () =>
                {
                    using Message? message = bus.PopFiltered(MessageType.Error);
                    Assert.Null(message);

                    return constructed() && Volatile.Read(ref received) > 0;
                }),
                "a client with the credentials of the server never received data from the mount.");

            _output.WriteLine($"the credentials opened the mount and {Volatile.Read(ref received)} samples arrived");
        }
        finally
        {
            sink.NewSample -= OnNewSample;
            StopFromAnotherThread(context, client);
        }
    }

    /// <summary>
    /// Takes a client pipeline to <see cref="State.Null"/> from a thread that
    /// is not the one pumping the server.
    /// </summary>
    /// <param name="context">The context that drives the server.</param>
    /// <param name="client">The client pipeline.</param>
    /// <remarks>
    /// <c>rtspsrc</c> joins its own task on the way down, and that task is
    /// waiting for the server this thread is the engine of, so the state change
    /// cannot be made from this thread.
    /// </remarks>
    private static void StopFromAnotherThread(MainContext context, Element client)
    {
        System.Threading.Tasks.Task down =
            System.Threading.Tasks.Task.Run(() => client.SetState(State.Null));

        Assert.True(PumpUntil(context, () => down.IsCompleted), "the client never reached NULL.");
        down.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Disposes every wrapper of a transfer full list and answers how many
    /// there were.
    /// </summary>
    /// <typeparam name="T">The wrapper type of the list.</typeparam>
    /// <param name="owned">The list a filter answered.</param>
    /// <returns>The number of items the list held.</returns>
    private static int DisposeAll<T>(IReadOnlyList<T> owned)
        where T : Gst.GObject.Object
    {
        foreach (T item in owned)
        {
            item.Dispose();
        }

        return owned.Count;
    }

    /// <summary>
    /// Iterates <paramref name="context"/> until <paramref name="done"/>
    /// answers <see langword="true"/> or <see cref="Deadline"/> passes.
    /// </summary>
    /// <param name="context">The context that drives the server.</param>
    /// <param name="done">The condition that ends the wait.</param>
    /// <returns>
    /// <see langword="true"/> when the condition was met before the deadline.
    /// </returns>
    private static bool PumpUntil(MainContext context, Func<bool> done)
    {
        DateTime end = DateTime.UtcNow + Deadline;

        while (true)
        {
            while (context.Iteration(false))
            {
                if (done())
                {
                    return true;
                }
            }

            if (done())
            {
                return true;
            }

            if (DateTime.UtcNow >= end)
            {
                return false;
            }

            Thread.Sleep(5);
        }
    }
}
