namespace BIM765T.Revit.Contracts.Common;

/// <summary>
/// Centralized constants shared across Agent, Bridge, McpHost.
/// Trước đây các giá trị này hardcode rải rác ở nhiều nơi.
/// </summary>
public static class BridgeConstants
{
    // ── Named Pipe ──
    public const string DefaultPipeName = "BIM765T.Revit.Agent";
    public const string DefaultWorkerHostPipeName = "BIM765T.Revit.WorkerHost";
    public const string DefaultKernelPipeName = "BIM765T.Revit.Agent.Kernel";

    // ── Timeouts ──
    public const int DefaultRequestTimeoutSeconds = 120;
    public const int DefaultApprovalTokenTtlMinutes = 10;
    public const int DefaultPipeConnectTimeoutMs = 5000;

    // ── Capacity Limits ──
    public const int DefaultMaxRecentOperations = 100;
    public const int DefaultMaxRecentEvents = 200;
    public const int DefaultMaxRetainedWorkflowRuns = 200;
    public const int MaxMcpPayloadBytes = 10 * 1024 * 1024; // 10 MB

    // ── Buffer Sizes ──
    public const int PipeBufferSize = 4096;

    // ── Agent Info ──
    public const string AgentName = "BIM765T.Revit.Agent";
    public const string McpHostName = "BIM765T.Revit.McpHost";
    public const string McpHostVersion = "1.0.0";
    public const string McpDefaultProtocolVersion = "2025-06-18";

    // ── AppData ──
    /// <summary>
    /// Tên thư mục trong %APPDATA% dùng cho settings, logs, journal, snapshots.
    /// </summary>
    public const string AppDataFolderName = "BIM765T.Revit.Agent";
}
