using System.Threading;
using System.Collections.Concurrent;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class ToolInvocationQueue
{
    private readonly ConcurrentQueue<PendingToolInvocation> _highPriority = new ConcurrentQueue<PendingToolInvocation>();
    private readonly ConcurrentQueue<PendingToolInvocation> _normalPriority = new ConcurrentQueue<PendingToolInvocation>();
    private readonly ConcurrentQueue<PendingToolInvocation> _lowPriority = new ConcurrentQueue<PendingToolInvocation>();
    private int _pendingCount;
    private int _highCount;
    private int _normalCount;
    private int _lowCount;

    internal int PendingCount => Volatile.Read(ref _pendingCount);

    internal int PendingHighPriorityCount => Volatile.Read(ref _highCount);

    internal int PendingNormalPriorityCount => Volatile.Read(ref _normalCount);

    internal int PendingLowPriorityCount => Volatile.Read(ref _lowCount);

    internal void Enqueue(PendingToolInvocation invocation)
    {
        if (invocation == null)
        {
            return;
        }

        switch (ToolQueuePriorityResolver.NormalizePriority(invocation.Priority))
        {
            case ToolQueuePriorities.High:
                _highPriority.Enqueue(invocation);
                Interlocked.Increment(ref _highCount);
                break;
            case ToolQueuePriorities.Low:
                _lowPriority.Enqueue(invocation);
                Interlocked.Increment(ref _lowCount);
                break;
            default:
                _normalPriority.Enqueue(invocation);
                Interlocked.Increment(ref _normalCount);
                break;
        }

        Interlocked.Increment(ref _pendingCount);
    }

    internal bool TryDequeue(out PendingToolInvocation? invocation)
    {
        if (TryDequeue(_highPriority, ref _highCount, out invocation)
            || TryDequeue(_normalPriority, ref _normalCount, out invocation)
            || TryDequeue(_lowPriority, ref _lowCount, out invocation))
        {
            Interlocked.Decrement(ref _pendingCount);
            return true;
        }

        invocation = null;
        return false;
    }

    private static bool TryDequeue(ConcurrentQueue<PendingToolInvocation> queue, ref int counter, out PendingToolInvocation? invocation)
    {
        if (!queue.TryDequeue(out invocation))
        {
            return false;
        }

        Interlocked.Decrement(ref counter);
        return true;
    }
}
