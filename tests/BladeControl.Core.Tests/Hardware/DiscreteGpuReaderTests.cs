using BladeControl.Core.Hardware;
using BladeControl.Core.Platform;

namespace BladeControl.Core.Tests.Hardware;

public class DiscreteGpuReaderTests
{
    [Fact]
    public void ParsesAProcessMemoryInstanceName()
    {
        var ok = DiscreteGpuReader.TryParseInstanceName("pid_18284_luid_0x00000000_0x00012352_phys_0", out var pid, out var luid);

        Assert.True(ok);
        Assert.Equal(18284, pid);
        Assert.Equal(0x12352L, luid);
    }

    [Fact]
    public void ParsesAnEngineInstanceName()
    {
        var ok = DiscreteGpuReader.TryParseInstanceName("pid_4_luid_0x00000000_0x0001201B_phys_0_eng_0_engtype_3D", out var pid, out var luid);

        Assert.True(ok);
        Assert.Equal(4, pid);
        Assert.Equal(0x1201BL, luid);
    }

    [Fact]
    public void KeepsTheHighPartOfTheLuid()
    {
        DiscreteGpuReader.TryParseInstanceName("pid_1_luid_0x00000001_0x00000002_phys_0", out _, out var luid);

        Assert.Equal((1L << 32) | 2, luid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("_Total")]
    [InlineData("pid_abc_luid_0x00000000_0x00012352_phys_0")]
    [InlineData("pid_5_luid_0x00000000_phys_0")]
    public void RejectsNamesThatAreNotProcessInstances(string name) =>
        Assert.False(DiscreteGpuReader.TryParseInstanceName(name, out _, out _));

    [Theory]
    [InlineData(0x10DEu, 7948L, true)]  // NVIDIA card with its own memory.
    [InlineData(0x8086u, 128L, false)]  // Intel integrated graphics.
    [InlineData(0x1414u, 0L, false)]    // Microsoft's software renderer.
    [InlineData(0x1414u, 8000L, false)] // Never the software renderer, whatever it reports.
    public void OnlyACardWithItsOwnMemoryIsDiscrete(uint vendor, long megabytes, bool expected) =>
        Assert.Equal(expected, new GpuAdapter("test", vendor, 0x12352, megabytes * 1024 * 1024).IsDiscrete);

    [Fact]
    public void AnAdapterWithoutALuid_IsNotDiscrete() =>
        Assert.False(new GpuAdapter("duplicate entry", 0x10DE, 0, 8L * 1024 * 1024 * 1024).IsDiscrete);

    private static readonly GpuAdapter LaptopCard = new("NVIDIA GeForce RTX 5090 Laptop GPU", 0x10DE, 0x12352, 24137L << 20, 0x2C58);
    private static readonly GpuAdapter ExternalCard = new("NVIDIA GeForce RTX 3090", 0x10DE, 0x5A1B0, 24322L << 20, 0x2204);

    private static DedicatedGpu Device(uint? vendorId, uint? deviceId) =>
        new(@"PCI\VEN_10DE&DEV_2C58", "NVIDIA GeForce RTX 5090 Laptop GPU", Enabled: true, VendorId: vendorId, DeviceId: deviceId);

    [Fact]
    public void OnlyTheDedicatedGpusAdapter_IsKept() =>
        Assert.Equal([LaptopCard], DiscreteGpuReader.MatchDedicatedGpu([ExternalCard, LaptopCard], Device(0x10DE, 0x2C58)));

    [Fact]
    public void NoAdapterMatching_KeepsNone() =>
        Assert.Empty(DiscreteGpuReader.MatchDedicatedGpu([ExternalCard, LaptopCard], Device(0x10DE, 0x28A0)));

    [Fact]
    public void TheLaptopsCardOff_WithOnlyTheEgpuListed_KeepsNone() =>
        Assert.Empty(DiscreteGpuReader.MatchDedicatedGpu([ExternalCard], Device(0x10DE, 0x2C58)));

    [Fact]
    public void AdaptersWithoutADeviceId_AreAllKept()
    {
        var laptopWithoutId = LaptopCard with { DeviceId = 0 };
        var externalWithoutId = ExternalCard with { DeviceId = 0 };

        Assert.Equal([externalWithoutId, laptopWithoutId],
            DiscreteGpuReader.MatchDedicatedGpu([externalWithoutId, laptopWithoutId], Device(0x10DE, 0x2C58)));
    }

    [Fact]
    public void ADedicatedGpuWithOnlyAVendorId_KeepsThemAll() =>
        Assert.Equal([ExternalCard, LaptopCard], DiscreteGpuReader.MatchDedicatedGpu([ExternalCard, LaptopCard], Device(0x10DE, null)));

    [Fact]
    public void NoDedicatedGpu_KeepsThemAll() =>
        Assert.Equal([ExternalCard, LaptopCard], DiscreteGpuReader.MatchDedicatedGpu([ExternalCard, LaptopCard], null));

    [Fact]
    public void ADedicatedGpuWithoutIds_KeepsThemAll() =>
        Assert.Equal([ExternalCard, LaptopCard], DiscreteGpuReader.MatchDedicatedGpu([ExternalCard, LaptopCard], Device(null, null)));

    [Fact]
    public void TheSameModelInsideAndOutside_KeepsBoth()
    {
        var externalTwin = LaptopCard with { Luid = 0x5A1B0 };

        Assert.Equal([LaptopCard, externalTwin], DiscreteGpuReader.MatchDedicatedGpu([LaptopCard, externalTwin], Device(0x10DE, 0x2C58)));
    }
}
