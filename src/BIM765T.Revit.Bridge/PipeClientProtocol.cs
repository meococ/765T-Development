using System;
using System.IO;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Bridge;

internal static class PipeClientProtocol
{
    internal static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(BridgeConstants.DefaultRequestTimeoutSeconds);

    internal static async Task<string?> ReadResponseLineAsync(StreamReader reader, TimeSpan timeout)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        var readTask = reader.ReadLineAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != readTask)
        {
            throw new TimeoutException($"Timed out waiting for pipe response after {timeout.TotalSeconds:F0}s.");
        }

        return await readTask.ConfigureAwait(false);
    }
}
