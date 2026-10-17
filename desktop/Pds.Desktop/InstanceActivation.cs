using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Pds.Desktop;

// A second launch asks the existing process to show its window, without touching drafts.
internal sealed class InstanceActivation(string name) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Task? listener;
    public static string NameFor(string dataPath) => "PdsOpen-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + Path.GetFullPath(dataPath).ToUpperInvariant())))[..24];

    public void Listen(Func<Task> activate, Action<Exception> failed)
    {
        listener = Task.Run(async () =>
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    request.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        var message = new byte[1];
                        if (await server.ReadAsync(message, request.Token).ConfigureAwait(false) == 1 && message[0] == 1)
                        {
                            await activate().ConfigureAwait(false);
                            await server.WriteAsync(new byte[] { 1 }, request.Token).ConfigureAwait(false);
                        }
                    }
                    catch (IOException) { /* A closing client must not stop future restore requests. */ }
                    catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { failed(ex); }
        });
    }

    public async Task<bool> RequestAsync(TimeSpan timeout)
    {
        using var request = new CancellationTokenSource(timeout);
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(request.Token).ConfigureAwait(false);
            await client.WriteAsync(new byte[] { 1 }, request.Token).ConfigureAwait(false);
            var reply = new byte[1];
            return await client.ReadAsync(reply, request.Token).ConfigureAwait(false) == 1 && reply[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        // Do not block the UI thread: an accepted activation may be waiting for that dispatcher.
        if (listener is null) lifetime.Dispose();
        else _ = listener.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
}
