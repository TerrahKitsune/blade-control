using BladeControl.Core.Platform;

namespace BladeControl.Core.Tests.Platform;

public class DedicatedGpuDeviceTests
{
    // Shaped like a Blade 16 (2025)'s display devices with an RTX 3090 in a Thunderbolt enclosure.
    private const string Radeon = @"PCI\VEN_1002&DEV_150E&SUBSYS_02C61A58&REV_C1\4&2C5B1D07&0&0041";
    private const string LaptopNvidia = @"PCI\VEN_10DE&DEV_2C58&SUBSYS_02C61A58&REV_A1\4&1A6FF3E6&0&0009";
    private const string ExternalNvidia = @"PCI\VEN_10DE&DEV_2204&SUBSYS_38833842&REV_A1\6&10F9D3B1&0&00000019";

    [Fact]
    public void TheLaptopsOwnCard_ListedFirst_IsChosen() =>
        Assert.Equal(1, DedicatedGpuDevice.IndexOfDedicatedGpu([(Radeon, false), (LaptopNvidia, false), (ExternalNvidia, true)]));

    [Fact]
    public void AnExternalCard_ListedFirst_IsPassedOver() =>
        Assert.Equal(2, DedicatedGpuDevice.IndexOfDedicatedGpu([(Radeon, false), (ExternalNvidia, true), (LaptopNvidia, false)]));

    [Fact]
    public void OnlyAnExternalCard_IsNoDedicatedGpu() =>
        Assert.Equal(-1, DedicatedGpuDevice.IndexOfDedicatedGpu([(Radeon, false), (ExternalNvidia, true)]));

    [Fact]
    public void ACardWindowsDoesNotPlace_CountsAsTheMachinesOwn() =>
        Assert.Equal(1, DedicatedGpuDevice.IndexOfDedicatedGpu([(Radeon, null), (ExternalNvidia, null)]));

    [Fact]
    public void NoNvidiaCard_IsNoDedicatedGpu() =>
        Assert.Equal(-1, DedicatedGpuDevice.IndexOfDedicatedGpu([(Radeon, false)]));

    [Fact]
    public void ReadsThePciIdsOutOfAnInstanceId() =>
        Assert.Equal((0x10DEu, 0x2C58u), DedicatedGpuDevice.ReadPciIds(LaptopNvidia));

    [Fact]
    public void ReadsLowerCasePciIds() =>
        Assert.Equal((0x10DEu, 0x2204u), DedicatedGpuDevice.ReadPciIds(@"pci\ven_10de&dev_2204&subsys_38833842"));

    [Theory]
    [InlineData("")]
    [InlineData(@"ROOT\BasicDisplay\0000")]
    [InlineData(@"PCI\VEN_10DE\4&1A6FF3E6&0&0009")]
    public void AnInstanceIdWithoutPciIds_GivesNone(string instanceId) =>
        Assert.Equal(((uint?)null, (uint?)null), DedicatedGpuDevice.ReadPciIds(instanceId));
}
