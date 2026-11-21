using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>
/// The running app's side of the command line: a named pipe only this user can
/// open, one request and one reply per connection. Only the command line's own
/// requests are passed to <c>handle</c>; anything else is answered with a refusal.
/// </summary>
internal sealed class CliPipeServer(string pipeName, Func<CliRequest, Task<CliReply>> handle) : IDisposable
{
    // Per session, like the app's single-instance lock: each signed-in user has their own app.
    public static string PipeNameForThisSession => $"BladeControl.Cli.{Process.GetCurrentProcess().SessionId}";

    // A client that connects but never sends is let go.
    private static readonly TimeSpan RequestWait = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stop = new();

    public void Start() => _ = Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        var failing = false;

        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;

            try
            {
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                pipe?.Dispose();

                // A connection dropped before it was taken: listen again at once. Failing
                // again and again (the pipe cannot be made), it is logged once and retried each second.
                if (failing)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
                else
                {
                    AppLog.Error("The command line's pipe could not take a connection.", exception);
                }

                failing = true;
                continue;
            }

            failing = false;

            // Each connection on its own, so a slow request does not hold up the next.
            _ = AnswerAsync(pipe);
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            try
            {
                string? line;

                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                {
                    wait.CancelAfter(RequestWait);
                    using var reader = new StreamReader(pipe, encoding, leaveOpen: true);
                    line = await reader.ReadLineAsync(wait.Token);
                }

                // Connected and closed without a word: nobody to answer.
                if (line is null)
                    return;

                var reply = await ReplyToAsync(line);

                await using var writer = new StreamWriter(pipe, encoding, leaveOpen: true);
                await writer.WriteLineAsync(CliJson.Write(reply).AsMemory(), _stop.Token);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The command line gave up or went away; nobody is left to answer.
            }
        }
    }

    private async Task<CliReply> ReplyToAsync(string? line)
    {
        if (CliJson.ReadRequest(line) is not { } request)
        {
            AppLog.Error("The command line's pipe received something that is not a request.");
            return new CliMessage(false, "Not a Blade Control request.");
        }

        try
        {
            return await handle(request);
        }
        catch (Exception exception)
        {
            AppLog.Error($"The command line's {request.Action} request failed.", exception);
            return new CliMessage(false, "Blade Control could not do it; see its log.");
        }
    }

    // Not disposed: a request still being answered keeps using its token.
    public void Dispose() => _stop.Cancel();
}
