using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;
using BladeControl.Core.Platform;

namespace BladeControl.Core.Hardware;

/// <summary>A graphics adapter Windows knows about, identified by its LUID (the id its performance counters use).</summary>
internal sealed record GpuAdapter(string Description, uint VendorId, long Luid, long DedicatedBytes, uint DeviceId = 0)
{
    private const uint MicrosoftBasicRenderVendorId = 0x1414;
    private const long MinimumDedicatedBytes = 512L * 1024 * 1024;

    /// <summary>
    /// A separate graphics card: real video memory of its own. Integrated
    /// graphics share system memory and the software renderer has none.
    /// </summary>
    public bool IsDiscrete =>
        Luid != 0 && VendorId != MicrosoftBasicRenderVendorId && DedicatedBytes >= MinimumDedicatedBytes;
}

/// <summary>
/// Reads which processes hold video memory on the dedicated GPU. Only reads:
/// the registry's adapter list and Windows' per-process GPU counters, the
/// same numbers Task Manager shows. It never touches the GPU itself.
/// </summary>
internal static partial class DiscreteGpuReader
{
    private const string AdapterListKey = @"SOFTWARE\Microsoft\DirectX";

    // Counter instances look like "pid_18284_luid_0x00000000_0x00012352_phys_0".
    [GeneratedRegex(@"^pid_(\d+)_luid_0x([0-9a-fA-F]+)_0x([0-9a-fA-F]+)_", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceNamePattern();

    /// <summary>The discrete adapters, or an empty list on a laptop without one or if Windows will not say.</summary>
    public static IReadOnlyList<GpuAdapter> ReadDiscreteAdapters()
    {
        var adapters = new List<GpuAdapter>();

        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(AdapterListKey);

            foreach (var name in root?.GetSubKeyNames() ?? [])
            {
                using var key = root!.OpenSubKey(name);

                if (key?.GetValue("Description") is not string description ||
                    key.GetValue("AdapterLuid") is not long luid ||
                    key.GetValue("DedicatedVideoMemory") is not long dedicated)
                {
                    continue;
                }

                var vendor = key.GetValue("VendorId") is int vendorId ? unchecked((uint)vendorId) : 0u;
                var device = key.GetValue("DeviceId") is int deviceId ? unchecked((uint)deviceId) : 0u;
                var adapter = new GpuAdapter(description, vendor, luid, dedicated, device);

                if (adapter.IsDiscrete)
                    adapters.Add(adapter);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Error("Could not read the list of graphics adapters.", exception);
        }

        return adapters;
    }

    /// <summary>
    /// The discrete adapters that are the dedicated GPU, the laptop's own card,
    /// leaving out an external one (an eGPU).
    /// </summary>
    public static IReadOnlyList<GpuAdapter> ReadDedicatedGpuAdapters() =>
        ReadDedicatedGpuAdapters(DedicatedGpuDevice.Find());

    /// <inheritdoc cref="ReadDedicatedGpuAdapters()"/>
    public static IReadOnlyList<GpuAdapter> ReadDedicatedGpuAdapters(DedicatedGpu? gpu) =>
        MatchDedicatedGpu(ReadDiscreteAdapters(), gpu);

    /// <summary>
    /// The adapters with the dedicated GPU's vendor and device ids, none when
    /// no adapter has them (the card is off, or only an eGPU is listed). When
    /// the card or its ids are not known, or the adapters carry no device id,
    /// all of them. An internal and an external card of the very same model
    /// look alike here, so both are kept.
    /// </summary>
    internal static IReadOnlyList<GpuAdapter> MatchDedicatedGpu(IReadOnlyList<GpuAdapter> adapters, DedicatedGpu? gpu)
    {
        if (gpu is not { VendorId: { } vendorId, DeviceId: { } deviceId } || adapters.All(adapter => adapter.DeviceId == 0))
            return adapters;

        return adapters.Where(adapter => adapter.VendorId == vendorId && adapter.DeviceId == deviceId).ToList();
    }

    /// <summary>Video memory each process holds on the given adapters, in bytes, summed per process.</summary>
    public static IReadOnlyList<GpuProcessUsage> ReadProcessUsage(IReadOnlyCollection<long> adapterLuids)
    {
        var bytesByProcess = new Dictionary<int, long>();

        try
        {
            var category = new PerformanceCounterCategory("GPU Process Memory");
            var dedicatedUsage = category.ReadCategory()["Dedicated Usage"];

            foreach (InstanceData instance in dedicatedUsage.Values)
            {
                if (!TryParseInstanceName(instance.InstanceName, out var processId, out var luid) ||
                    !adapterLuids.Contains(luid))
                {
                    continue;
                }

                bytesByProcess[processId] = bytesByProcess.GetValueOrDefault(processId) + instance.RawValue;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Older Windows has no GPU counters; nothing to report then.
            AppLog.Error("Could not read the GPU process counters.", exception);
        }

        return bytesByProcess
            .Where(pair => pair.Value > 0)
            .Select(pair => new GpuProcessUsage(pair.Key, pair.Value))
            .ToList();
    }

    internal static bool TryParseInstanceName(string instanceName, out int processId, out long luid)
    {
        processId = 0;
        luid = 0;

        var match = InstanceNamePattern().Match(instanceName);

        if (!match.Success ||
            !int.TryParse(match.Groups[1].ValueSpan, out processId) ||
            !uint.TryParse(match.Groups[2].ValueSpan, System.Globalization.NumberStyles.HexNumber, null, out var high) ||
            !uint.TryParse(match.Groups[3].ValueSpan, System.Globalization.NumberStyles.HexNumber, null, out var low))
        {
            return false;
        }

        luid = ((long)high << 32) | low;
        return true;
    }
}
