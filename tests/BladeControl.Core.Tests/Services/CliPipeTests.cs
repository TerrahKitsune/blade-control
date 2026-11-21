using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class CliPipeTests
{
    private static string TestPipeName() => $"BladeControl.Cli.Test.{Guid.NewGuid():N}";

    [Fact]
    public void ARequest_ReachesTheHandler_AndItsReplyComesBack()
    {
        var pipeName = TestPipeName();
        CliRequest? received = null;
        var status = new CliStatus("Razer Blade 16 (2025)", true, PerformanceMode.Balanced, null, null, null, null, 80);
        using var server = new CliPipeServer(pipeName, request =>
        {
            received = request;
            return Task.FromResult<CliReply>(status);
        });
        server.Start();

        var reply = new CliPipeClient(pipeName, () => { }).Send(CliJson.Write(CliRequest.Status()));

        Assert.Equal(CliRequest.Status(), received);
        Assert.Equal(CliJson.Write(status), reply);
    }

    [Fact]
    public void AHandlerThatThrows_GivesOkFalse()
    {
        var pipeName = TestPipeName();
        using var server = new CliPipeServer(pipeName, _ => throw new InvalidOperationException("broken"));
        server.Start();

        var reply = new CliPipeClient(pipeName, () => { }).Send(CliJson.Write(CliRequest.SetChargeLimit(60)));

        Assert.NotNull(reply);
        Assert.False(CliJson.IsOk(reply));
    }

    [Fact]
    public void SomethingThatIsNotARequest_IsRefused_WithoutReachingTheHandler()
    {
        var pipeName = TestPipeName();
        var called = false;
        using var server = new CliPipeServer(pipeName, _ =>
        {
            called = true;
            return Task.FromResult<CliReply>(new CliMessage(true, "done"));
        });
        server.Start();

        var reply = new CliPipeClient(pipeName, () => { }).Send("""{"action":"SetMode","mode":"Gaming"}""");

        Assert.NotNull(reply);
        Assert.False(CliJson.IsOk(reply));
        Assert.False(called);
    }

    [Fact]
    public void AClientThatConnectsAndLeavesAtOnce_DoesNotStopTheServer()
    {
        var pipeName = TestPipeName();
        using var server = new CliPipeServer(pipeName, _ => Task.FromResult<CliReply>(new CliMessage(true, "Active")));
        server.Start();

        using (var dropped = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut))
            dropped.Connect(5000);

        var reply = new CliPipeClient(pipeName, () => { }).Send(CliJson.Write(CliRequest.Status()));

        Assert.NotNull(reply);
        Assert.True(CliJson.IsOk(reply));
    }

    [Fact]
    public void WithNoServer_TheAppIsStartedOnce_AndNoReplyWhenNothingEverListens()
    {
        var started = 0;
        var client = new CliPipeClient(TestPipeName(), () => started++,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        Assert.Null(client.Send(CliJson.Write(CliRequest.Status())));
        Assert.Equal(1, started);
    }

    [Fact]
    public void WithNoServer_TheAppItStarts_IsReached()
    {
        var pipeName = TestPipeName();
        CliPipeServer? server = null;
        var client = new CliPipeClient(pipeName, () =>
            {
                server = new CliPipeServer(pipeName, _ => Task.FromResult<CliReply>(new CliMessage(true, "Active")));
                server.Start();
            },
            TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        try
        {
            Assert.True(CliJson.IsOk(client.Send(CliJson.Write(CliRequest.Status()))!));
        }
        finally
        {
            server?.Dispose();
        }
    }

    [Fact]
    public void AHandlerThatIsSlowerThanTheReplyWait_GivesNoReply()
    {
        var pipeName = TestPipeName();
        using var server = new CliPipeServer(pipeName, async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            return new CliMessage(true, "late");
        });
        server.Start();
        var client = new CliPipeClient(pipeName, () => { }, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(200));

        Assert.Null(client.Send(CliJson.Write(CliRequest.Status())));
    }
}
