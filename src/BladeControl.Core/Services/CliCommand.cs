using System.Globalization;
using BladeControl.Core.Diagnostics;
using BladeControl.Core.Models;

namespace BladeControl.Core.Services;

/// <summary>
/// The command line: <c>--status</c>, <c>--set-mode &lt;mode&gt;</c> and
/// <c>--set-charge-limit &lt;60-100&gt;</c>. The process started with one never
/// opens the laptop's controller; it passes the request to the running app
/// (see <see cref="CliPipeClient"/>), prints the one JSON line it answers and exits.
/// </summary>
internal static class CliCommand
{
    public const string StatusSwitch = "--status";
    public const string SetModeSwitch = "--set-mode";
    public const string SetChargeLimitSwitch = "--set-charge-limit";

    public const int Done = 0;
    public const int Refused = 1;
    public const int BadArguments = 2;
    public const int AppUnreachable = 3;

    /// <summary>The modes <c>--set-mode</c> takes, by the name typed (any case).</summary>
    public static readonly IReadOnlyDictionary<string, PerformanceMode> Modes =
        new Dictionary<string, PerformanceMode>(StringComparer.OrdinalIgnoreCase)
        {
            ["silent"] = PerformanceMode.Silent,
            ["balanced"] = PerformanceMode.Balanced,
            ["performance"] = PerformanceMode.Performance,
            ["custom"] = PerformanceMode.Custom
        };

    /// <summary>
    /// Runs the command if <paramref name="args"/> is one, writes the reply to
    /// <paramref name="output"/> and returns the exit code. Returns null when the
    /// arguments are not a command-line request, so normal startup carries on.
    /// A malformed one returns <see cref="BadArguments"/> and never starts the app.
    /// </summary>
    public static int? TryRun(string[] args, CliPipeClient client, TextWriter output)
    {
        if (args is not [StatusSwitch or SetModeSwitch or SetChargeLimitSwitch, ..])
            return null;

        if (Parse(args, out var error) is not { } request)
        {
            AppLog.Info($"Refused a malformed command (exit code {BadArguments}): {string.Join(' ', args)}");
            return Answer(output, new CliMessage(false, error), BadArguments);
        }

        var reply = client.Send(CliJson.Write(request));

        if (reply is null)
            return Answer(output, new CliMessage(false, "Blade Control could not be reached."), AppUnreachable);

        output.WriteLine(reply);
        output.Flush();
        return CliJson.IsOk(reply) ? Done : Refused;
    }

    /// <summary>The request in <paramref name="args"/>, or null with the reason when it is malformed.</summary>
    internal static CliRequest? Parse(string[] args, out string error)
    {
        error = string.Empty;

        switch (args)
        {
            case [StatusSwitch]:
                return CliRequest.Status();

            case [SetModeSwitch, var name] when Modes.TryGetValue(name, out var mode):
                return CliRequest.SetMode(mode);

            case [SetModeSwitch, ..]:
                error = $"Use {SetModeSwitch} silent, balanced, performance or custom.";
                return null;

            case [SetChargeLimitSwitch, var text]
                when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) && BatteryLimitRange.IsValid(percent):
                return CliRequest.SetChargeLimit(percent);

            case [SetChargeLimitSwitch, ..]:
                error = $"Use {SetChargeLimitSwitch} with a whole number from {BatteryLimitRange.Minimum} to {BatteryLimitRange.Maximum} (100 for no limit).";
                return null;

            default:
                error = $"{StatusSwitch} takes nothing after it.";
                return null;
        }
    }

    private static int Answer(TextWriter output, CliReply reply, int exitCode)
    {
        output.WriteLine(CliJson.Write(reply));
        output.Flush();
        return exitCode;
    }
}
