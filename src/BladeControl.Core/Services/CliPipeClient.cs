using System.IO.Pipes;
using System.Text;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>
/// The command line's side of the pipe: one request, one reply. When no app is
/// listening it starts one (in the tray) and tries again.
/// </summary>
/// <param name="startApp">Starts Blade Control in the tray.</param>
internal sealed class CliPipeClient(string pipeName, Action startApp, TimeSpan firstTry, TimeSpan afterStart, TimeSpan replyWait)
{
    public CliPipeClient(string pipeName, Action startApp)
        : this(pipeName, startApp, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30))
    {
    }

    /// <summary>The app's reply line, or null when it could not be reached or did not answer in time.</summary>
    public string? Send(string requestLine) => Task.Run(() => SendAsync(requestLine)).GetAwaiter().GetResult();

    private async Task<string?> SendAsync(string requestLine)
    {
        await using var pipe = await ConnectAsync();

        if (pipe is null)
            return null;

        using var timeout = new CancellationTokenSource(replyWait);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        try
        {
            await using (var writer = new StreamWriter(pipe, encoding, leaveOpen: true))
                await writer.WriteLineAsync(requestLine.AsMemory(), timeout.Token);

            using var reader = new StreamReader(pipe, encoding, leaveOpen: true);
            return await reader.ReadLineAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<NamedPipeClientStream?> ConnectAsync()
    {
        if (await TryConnectAsync(firstTry) is { } running)
            return running;

        try
        {
            startApp();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Error("The command line could not start Blade Control.", exception);
            return null;
        }

        return await TryConnectAsync(afterStart);
    }

    private async Task<NamedPipeClientStream?> TryConnectAsync(TimeSpan timeout)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds);
            return pipe;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync();
            return null;
        }
    }
}
