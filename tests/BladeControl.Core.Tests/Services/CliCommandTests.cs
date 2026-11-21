using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class CliCommandTests
{
    // A client whose app is never reached: the tests that must not get that far fail if it is.
    private static CliPipeClient NoApp(Action? startApp = null) =>
        new($"BladeControl.Cli.Test.{Guid.NewGuid():N}", startApp ?? (() => { }),
            TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));

    // ---- Recognising a command-line request ---------------------------------

    [Theory]
    [InlineData]                              // no arguments: a normal launch
    [InlineData("--tray")]
    [InlineData("--tray", "--status")]        // only the first argument counts
    [InlineData("--STATUS")]                  // the switch is exact
    [InlineData("--hibernation", "off")]
    public void OtherArguments_AreNotACommand_SoTheAppStartsNormally(params string[] args)
    {
        var started = false;
        var output = new StringWriter();

        Assert.Null(CliCommand.TryRun(args, NoApp(() => started = true), output));
        Assert.False(started);
        Assert.Empty(output.ToString());
    }

    // ---- The valid forms -----------------------------------------------------

    [Fact]
    public void Status_IsAStatusRequest() =>
        Assert.Equal(CliRequest.Status(), CliCommand.Parse(["--status"], out _));

    [Theory]
    [InlineData("silent", (byte)PerformanceMode.Silent)]
    [InlineData("Balanced", (byte)PerformanceMode.Balanced)]
    [InlineData("PERFORMANCE", (byte)PerformanceMode.Performance)]
    [InlineData("custom", (byte)PerformanceMode.Custom)]
    public void SetMode_TakesTheFourModes_InAnyCase(string name, byte mode) =>
        Assert.Equal(CliRequest.SetMode((PerformanceMode)mode), CliCommand.Parse(["--set-mode", name], out _));

    [Theory]
    [InlineData("60", 60)]
    [InlineData("80", 80)]
    [InlineData("100", 100)]   // no limit
    public void SetChargeLimit_TakesSixtyToOneHundred(string text, int percent) =>
        Assert.Equal(CliRequest.SetChargeLimit(percent), CliCommand.Parse(["--set-charge-limit", text], out _));

    // ---- The bad forms -------------------------------------------------------

    [Theory]
    [InlineData("--status", "extra")]
    [InlineData("--set-mode")]                          // missing value
    [InlineData("--set-mode", "nonsense")]              // unknown mode
    [InlineData("--set-mode", "gaming")]                // a mode the command line does not offer
    [InlineData("--set-mode", "2")]                     // not by number
    [InlineData("--set-mode", "silent", "extra")]
    [InlineData("--set-charge-limit")]
    [InlineData("--set-charge-limit", "40")]            // below the range
    [InlineData("--set-charge-limit", "101")]           // above it
    [InlineData("--set-charge-limit", "eighty")]        // not a number
    [InlineData("--set-charge-limit", "80.5")]          // not whole
    [InlineData("--set-charge-limit", "-80")]
    [InlineData("--set-charge-limit", "80", "extra")]
    public void ABadCommand_IsRefusedWithExitCodeTwo_AndNeverStartsTheApp(params string[] args)
    {
        var started = false;
        var output = new StringWriter();

        var exitCode = CliCommand.TryRun(args, NoApp(() => started = true), output);

        Assert.Equal(CliCommand.BadArguments, exitCode);
        Assert.False(started);
        var reply = CliJson.Read<CliMessage>(output.ToString());
        Assert.NotNull(reply);
        Assert.False(reply.Ok);
        Assert.NotEmpty(reply.Message);
    }

    // ---- Running against the app -------------------------------------------

    [Fact]
    public void WithNoAppListening_ItIsStarted_AndExitCodeThreeWhenItNeverAnswers()
    {
        var started = 0;
        var output = new StringWriter();

        var exitCode = CliCommand.TryRun(["--status"], NoApp(() => started++), output);

        Assert.Equal(CliCommand.AppUnreachable, exitCode);
        Assert.Equal(1, started);
        Assert.False(CliJson.IsOk(output.ToString()));
    }

    [Theory]
    [InlineData(true, CliCommand.Done)]
    [InlineData(false, CliCommand.Refused)]
    public void TheAppsReply_IsPrintedAsItCame_AndItsOkGivesTheExitCode(bool ok, int expectedExitCode)
    {
        var pipeName = $"BladeControl.Cli.Test.{Guid.NewGuid():N}";
        using var server = new CliPipeServer(pipeName, _ => Task.FromResult<CliReply>(new CliMessage(ok, "Active")));
        server.Start();
        var output = new StringWriter();

        var exitCode = CliCommand.TryRun(["--set-mode", "silent"], new CliPipeClient(pipeName, () => { }), output);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(CliJson.Write(new CliMessage(ok, "Active")), output.ToString().TrimEnd());
    }
}
