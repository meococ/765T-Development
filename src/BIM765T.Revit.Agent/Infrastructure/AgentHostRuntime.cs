using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Infrastructure.Observability;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Agent.Workflow;
using BIM765T.Revit.Copilot.Core;

namespace BIM765T.Revit.Agent.Infrastructure;

internal interface IAgentHostServices
{
    AgentSettings Settings { get; }
    BridgePolicy Policy { get; }
    IAgentLogger Logger { get; }
    ToolInvocationQueue Queue { get; }
    ToolExternalEventHandler ExternalEventHandler { get; }
    ExternalEvent ExternalEvent { get; }
    KernelPipeHostedService KernelPipeServer { get; }
    ToolExecutor ToolExecutor { get; }
    PlatformServices Platform { get; }
    DocumentCacheService Cache { get; }
    OperationJournalService Journal { get; }
    EventIndexService EventIndex { get; }
    AutomationDialogGuard DialogGuard { get; }
    WorkflowRuntimeService WorkflowRuntime { get; }
    CopilotTaskService CopilotTasks { get; }
    ToolRegistry Registry { get; }
}

internal sealed class AgentHostRuntime : IAgentHostServices
{
    internal AgentHostRuntime(
        AgentSettings settings,
        BridgePolicy policy,
        IAgentLogger logger,
        ToolInvocationQueue queue,
        ToolExternalEventHandler externalEventHandler,
        ExternalEvent externalEvent,
        KernelPipeHostedService kernelPipeServer,
        ToolExecutor toolExecutor,
        PlatformServices platform,
        DocumentCacheService cache,
        OperationJournalService journal,
        EventIndexService eventIndex,
        AutomationDialogGuard dialogGuard,
        WorkflowRuntimeService workflowRuntime,
        CopilotTaskService copilotTasks,
        ToolRegistry registry)
    {
        Settings = settings;
        Policy = policy;
        Logger = logger;
        Queue = queue;
        ExternalEventHandler = externalEventHandler;
        ExternalEvent = externalEvent;
        KernelPipeServer = kernelPipeServer;
        ToolExecutor = toolExecutor;
        Platform = platform;
        Cache = cache;
        Journal = journal;
        EventIndex = eventIndex;
        DialogGuard = dialogGuard;
        WorkflowRuntime = workflowRuntime;
        CopilotTasks = copilotTasks;
        Registry = registry;
    }

    public AgentSettings Settings { get; }
    public BridgePolicy Policy { get; }
    public IAgentLogger Logger { get; }
    public ToolInvocationQueue Queue { get; }
    public ToolExternalEventHandler ExternalEventHandler { get; }
    public ExternalEvent ExternalEvent { get; }
    public KernelPipeHostedService KernelPipeServer { get; }
    public ToolExecutor ToolExecutor { get; }
    public PlatformServices Platform { get; }
    public DocumentCacheService Cache { get; }
    public OperationJournalService Journal { get; }
    public EventIndexService EventIndex { get; }
    public AutomationDialogGuard DialogGuard { get; }
    public WorkflowRuntimeService WorkflowRuntime { get; }
    public CopilotTaskService CopilotTasks { get; }
    public ToolRegistry Registry { get; }
}
