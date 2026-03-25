using System;
using System.Collections.Generic;
using System.Threading.Channels;

namespace BIM765T.Revit.WorkerHost.Eventing;

internal interface IMissionEventBus
{
    MissionEventSubscription Subscribe(string missionId);

    void Publish(MissionEventRecord record);
}

internal sealed class MissionEventSubscription : IAsyncDisposable
{
    private readonly Action _dispose;

    public MissionEventSubscription(ChannelReader<MissionEventRecord> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<MissionEventRecord> Reader { get; }

    public ValueTask DisposeAsync()
    {
        _dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class InMemoryMissionEventBus : IMissionEventBus
{
    private readonly object _gate = new object();
    private readonly Dictionary<string, List<Channel<MissionEventRecord>>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    public MissionEventSubscription Subscribe(string missionId)
    {
        var normalizedMissionId = string.IsNullOrWhiteSpace(missionId) ? string.Empty : missionId.Trim();
        var channel = Channel.CreateUnbounded<MissionEventRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(normalizedMissionId, out var channels))
            {
                channels = new List<Channel<MissionEventRecord>>();
                _subscriptions[normalizedMissionId] = channels;
            }

            channels.Add(channel);
        }

        return new MissionEventSubscription(channel.Reader, () =>
        {
            lock (_gate)
            {
                if (_subscriptions.TryGetValue(normalizedMissionId, out var channels))
                {
                    channels.Remove(channel);
                    if (channels.Count == 0)
                    {
                        _subscriptions.Remove(normalizedMissionId);
                    }
                }
            }

            channel.Writer.TryComplete();
        });
    }

    public void Publish(MissionEventRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.StreamId))
        {
            return;
        }

        Channel<MissionEventRecord>[] targets;
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(record.StreamId, out var channels) || channels.Count == 0)
            {
                return;
            }

            targets = channels.ToArray();
        }

        foreach (var target in targets)
        {
            target.Writer.TryWrite(record);
            if (record.Terminal)
            {
                target.Writer.TryComplete();
            }
        }
    }
}
