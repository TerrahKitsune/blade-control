using System.Text.Json;
using System.Text.Json.Serialization;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

internal enum CliAction
{
    Status,
    SetMode,
    SetChargeLimit
}

/// <summary>What the command line asks the running app, sent over <see cref="CliPipeServer"/>'s pipe.</summary>
internal sealed record CliRequest(CliAction Action, PerformanceMode? Mode = null, int? ChargeLimit = null)
{
    public static CliRequest Status() => new(CliAction.Status);

    public static CliRequest SetMode(PerformanceMode mode) => new(CliAction.SetMode, Mode: mode);

    public static CliRequest SetChargeLimit(int percent) => new(CliAction.SetChargeLimit, ChargeLimit: percent);

    /// <summary>Only the requests the command line itself can make: the four modes it names and a limit of 60 to 100.</summary>
    public bool IsValid => this switch
    {
        { Action: CliAction.Status, Mode: null, ChargeLimit: null } => true,
        { Action: CliAction.SetMode, Mode: { } mode, ChargeLimit: null } => CliCommand.Modes.Values.Contains(mode),
        { Action: CliAction.SetChargeLimit, Mode: null, ChargeLimit: { } percent } => BatteryLimitRange.IsValid(percent),
        _ => false
    };
}

/// <summary>The one JSON line the app answers with, and the command line prints.</summary>
internal abstract record CliReply([property: JsonPropertyOrder(-1)] bool Ok);

/// <summary>The answer to a "set": whether it was done, and the text the app shows for the same action.</summary>
internal sealed record CliMessage(bool Ok, string Message) : CliReply(Ok);

/// <summary>
/// The answer to <c>--status</c>. Null means the laptop did not say (or has no
/// such thing); the charge limit is the one saved in the settings, 100 for none.
/// </summary>
internal sealed record CliStatus(
    string? Model,
    bool PluggedIn,
    PerformanceMode? Mode,
    CpuBoost? CpuBoost,
    GpuBoost? GpuBoost,
    bool? MaxFan,
    CliFanRpm? FanRpm,
    int? ChargeLimit) : CliReply(Ok: true);

internal sealed record CliFanRpm(int Cpu, int Gpu);

/// <summary>The command line's messages as single JSON lines.</summary>
internal static class CliJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write(CliRequest request) => JsonSerializer.Serialize(request, Options);

    public static string Write(CliReply reply) => JsonSerializer.Serialize(reply, reply.GetType(), Options);

    /// <summary>The request in <paramref name="line"/>, or null when it is not one the command line could have sent.</summary>
    public static CliRequest? ReadRequest(string? line) =>
        Read<CliRequest>(line) is { IsValid: true } request ? request : null;

    public static T? Read<T>(string? line) where T : class
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The <c>ok</c> of a reply line; false when there is none.</summary>
    public static bool IsOk(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
