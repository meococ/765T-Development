using System;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.WorkerHost.Configuration;
using Microsoft.Extensions.Hosting;

namespace BIM765T.Revit.WorkerHost.Eventing;

internal sealed class SqliteWalCheckpointService : BackgroundService
{
    private readonly SqliteMissionEventStore _store;
    private readonly WorkerHostSettings _settings;

    public SqliteWalCheckpointService(SqliteMissionEventStore store, WorkerHostSettings settings)
    {
        _store = store;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.WalCheckpointIntervalSeconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.WalCheckpointIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _store.CheckpointAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // WAL checkpoint is operational hygiene only; event store remains durable without it.
            }
        }
    }
}
