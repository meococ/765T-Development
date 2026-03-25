using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ToolInvocationQueueTests
{
    [Fact]
    public void ToolInvocationQueue_Dequeue_Prefers_High_Then_Normal_Then_Low()
    {
        var queue = new ToolInvocationQueue();
        queue.Enqueue(new PendingToolInvocation(new ToolRequestEnvelope { ToolName = "file.save_document", RequestedPriority = ToolQueuePriorities.Low }));
        queue.Enqueue(new PendingToolInvocation(new ToolRequestEnvelope { ToolName = "worker.get_context", RequestedPriority = ToolQueuePriorities.High }));
        queue.Enqueue(new PendingToolInvocation(new ToolRequestEnvelope { ToolName = "review.smart_qc", RequestedPriority = ToolQueuePriorities.Normal }));

        Assert.True(queue.TryDequeue(out var first));
        Assert.True(queue.TryDequeue(out var second));
        Assert.True(queue.TryDequeue(out var third));

        Assert.Equal("worker.get_context", first!.Request.ToolName);
        Assert.Equal("review.smart_qc", second!.Request.ToolName);
        Assert.Equal("file.save_document", third!.Request.ToolName);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void ToolInvocationQueue_Tracks_Pending_Counts_Per_Priority()
    {
        var queue = new ToolInvocationQueue();
        queue.Enqueue(new PendingToolInvocation(new ToolRequestEnvelope { ToolName = "context.get_delta_summary", RequestedPriority = ToolQueuePriorities.High }));
        queue.Enqueue(new PendingToolInvocation(new ToolRequestEnvelope { ToolName = "workflow.apply", RequestedPriority = ToolQueuePriorities.Low }));
        queue.Enqueue(new PendingToolInvocation(new ToolRequestEnvelope { ToolName = "review.smart_qc", RequestedPriority = ToolQueuePriorities.Normal }));

        Assert.Equal(3, queue.PendingCount);
        Assert.Equal(1, queue.PendingHighPriorityCount);
        Assert.Equal(1, queue.PendingNormalPriorityCount);
        Assert.Equal(1, queue.PendingLowPriorityCount);
    }

    [Fact]
    public void PendingToolInvocation_Cancel_Fails_After_Execution_Has_Started()
    {
        var pending = new PendingToolInvocation(new ToolRequestEnvelope { ToolName = "worker.get_context" });

        Assert.True(pending.TryBeginExecution());
        Assert.False(pending.TryCancelBeforeExecution());
        Assert.False(pending.Completion.Task.IsCompleted);
    }

    [Fact]
    public async System.Threading.Tasks.Task PendingToolInvocation_Cancel_Completes_When_Still_Pending()
    {
        var pending = new PendingToolInvocation(new ToolRequestEnvelope { ToolName = "worker.get_context" });

        Assert.True(pending.TryCancelBeforeExecution());
        Assert.True(pending.Completion.Task.IsCompleted);
        var response = await pending.Completion.Task;
        Assert.Equal(StatusCodes.Timeout, response.StatusCode);
    }
}
