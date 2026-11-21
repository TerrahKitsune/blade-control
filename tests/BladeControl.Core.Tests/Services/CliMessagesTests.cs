using BladeControl.Core.Models;
using BladeControl.Core.Services;

namespace BladeControl.Core.Tests.Services;

public sealed class CliMessagesTests
{
    [Fact]
    public void EveryRequest_ReadsBackAsItWasWritten()
    {
        CliRequest[] requests =
        [
            CliRequest.Status(),
            CliRequest.SetMode(PerformanceMode.Silent),
            CliRequest.SetMode(PerformanceMode.Performance),
            CliRequest.SetChargeLimit(60),
            CliRequest.SetChargeLimit(100)
        ];

        foreach (var request in requests)
            Assert.Equal(request, CliJson.ReadRequest(CliJson.Write(request)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"action":"Explode"}""")]
    [InlineData("""{"action":"SetMode"}""")]                         // no mode
    [InlineData("""{"action":"SetMode","mode":"Gaming"}""")]         // not one the command line offers
    [InlineData("""{"action":"SetChargeLimit","chargeLimit":40}""")]
    [InlineData("""{"action":"Status","chargeLimit":80}""")]
    public void AnythingTheCommandLineCouldNotHaveSent_IsNoRequest(string? line) =>
        Assert.Null(CliJson.ReadRequest(line));

    [Fact]
    public void AMessage_ReadsBackAsItWasWritten_WithOkFirst()
    {
        var message = new CliMessage(false, "Needs to be plugged in");

        var line = CliJson.Write(message);

        Assert.Equal("""{"ok":false,"message":"Needs to be plugged in"}""", line);
        Assert.Equal(message, CliJson.Read<CliMessage>(line));
    }

    [Fact]
    public void TheStatus_HasTheDocumentedShape_AndReadsBack()
    {
        var status = new CliStatus("Razer Blade 16 (2025)", true, PerformanceMode.Balanced, null, null, null, new CliFanRpm(3900, 3700), 60);

        var line = CliJson.Write(status);

        Assert.Equal(
            """{"ok":true,"model":"Razer Blade 16 (2025)","pluggedIn":true,"mode":"Balanced","cpuBoost":null,"gpuBoost":null,"maxFan":null,"fanRpm":{"cpu":3900,"gpu":3700},"chargeLimit":60}""",
            line);
        Assert.Equal(status, CliJson.Read<CliStatus>(line));
    }

    [Fact]
    public void TheStatus_InCustom_NamesTheLevels()
    {
        var status = new CliStatus(null, false, PerformanceMode.Custom, CpuBoost.Boost, GpuBoost.High, true, null, null);

        var line = CliJson.Write(status);

        Assert.Contains("""
            "model":null
            """, line);
        Assert.Contains("""
            "cpuBoost":"Boost","gpuBoost":"High","maxFan":true,"fanRpm":null,"chargeLimit":null
            """, line);
        Assert.Equal(status, CliJson.Read<CliStatus>(line));
    }

    [Theory]
    [InlineData("""{"ok":true,"message":"Active"}""", true)]
    [InlineData("""{"ok":false,"message":"Busy"}""", false)]
    [InlineData("""{"message":"no ok"}""", false)]
    [InlineData("not json", false)]
    public void IsOk_ReadsTheReplysOk(string line, bool ok) =>
        Assert.Equal(ok, CliJson.IsOk(line));
}
