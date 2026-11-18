using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PromptNotebook.Services;

internal sealed class SingleInstance : IDisposable
{
    private const int MaxPayload = 256 * 1024;
    private const PipeOptions LocalPipe = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stopping = new();
    private bool _ownsMutex;

    public SingleInstance()
    {
        using var process = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var scope = $"NotebookEditor-{process.SessionId}-{identity.User!.Value}-{(elevated ? "admin" : "user")}";
        _mutex = new Mutex(false, $@"Local\{scope}-Instance");
        _pipeName = $"{scope}-OpenFiles-v1";
    }

    public bool TryAcquire()
    {
        if (_ownsMutex)
            return true;
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }
        return _ownsMutex;
    }

    public async Task<bool> TryForwardAsync(string[] paths, CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(TimeSpan.FromSeconds(4));
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, LocalPipe);
        try
        {
            await client.ConnectAsync(600, attempt.Token).ConfigureAwait(false);
            if (GetNamedPipeServerProcessId(client.SafePipeHandle, out var processId))
                AllowSetForegroundWindow(processId);

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(paths);
            if (payload.Length > MaxPayload)
                return false;
            byte[] length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
            await client.WriteAsync(length, attempt.Token).ConfigureAwait(false);
            await client.WriteAsync(payload, attempt.Token).ConfigureAwait(false);
            await client.ReadExactlyAsync(length.AsMemory(0, 1), attempt.Token).ConfigureAwait(false);
            return length[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    public Task ListenAsync(Func<string[], CancellationToken, Task<bool>> onLaunch) =>
        ListenAsync(onLaunch, _stopping.Token);

    private async Task ListenAsync(Func<string[], CancellationToken, Task<bool>> onLaunch, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut,
                    1, PipeTransmissionMode.Byte, LocalPipe);
                await server.WaitForConnectionAsync(stopping).ConfigureAwait(false);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                request.CancelAfter(TimeSpan.FromSeconds(4));
                byte[] header = new byte[sizeof(int)];
                await server.ReadExactlyAsync(header, request.Token).ConfigureAwait(false);
                var size = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (size is <= 0 or > MaxPayload)
                    continue;

                byte[] payload = new byte[size];
                await server.ReadExactlyAsync(payload, request.Token).ConfigureAwait(false);
                var paths = JsonSerializer.Deserialize<string[]>(payload);
                if (paths is null || paths.Any(path => path is null))
                    continue;

                var accepted = await onLaunch(paths, request.Token).ConfigureAwait(false);
                header[0] = accepted ? (byte)1 : (byte)0;
                await server.WriteAsync(header.AsMemory(0, 1), request.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Closing the app or dropping a client that stopped before sending a complete request.
            }
            catch (JsonException)
            {
                // An invalid request is discarded without affecting the next client.
            }
            catch (IOException) when (!stopping.IsCancellationRequested)
            {
                await Task.Delay(40).ConfigureAwait(false);
            }
            catch (IOException) when (stopping.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
        if (_ownsMutex)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
