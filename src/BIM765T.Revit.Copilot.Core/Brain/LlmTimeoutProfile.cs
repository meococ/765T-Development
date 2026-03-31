namespace BIM765T.Revit.Copilot.Core.Brain;

/// <summary>
/// Centralized timeout configuration for all LLM-related operations.
/// Passed through constructors so callers control the timeout contract.
///
/// Invariant: PlannerTimeoutSeconds &lt; ConversationalTimeoutSeconds
///            &lt; ResponseTimeoutSeconds &lt; HttpTimeoutSeconds
///
/// The HTTP-level timeout must always exceed all business-level CancelAfter values
/// to avoid the "timed out after Xs" message lying about the actual timeout layer.
/// </summary>
public sealed class LlmTimeoutProfile
{
    /// <summary>
    /// Transport-level timeout for the underlying <see cref="System.Net.Http.HttpClient"/>.
    /// Must be the highest value — the HTTP socket stays open long enough for any business timeout to fire first.
    /// Default: 25s (exceeds the 20s response enhancement timeout).
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 25;

    /// <summary>
    /// CancelAfter for standard response enhancement (the main LLM narration path).
    /// This is the "full" LLM call budget for tool-assisted responses.
    /// Default: 20s.
    /// </summary>
    public int ResponseTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// CancelAfter for conversational fast-path (greeting, identity, context_query).
    /// Tighter budget because conversational responses are short and latency-sensitive.
    /// Default: 8s.
    /// </summary>
    public int ConversationalTimeoutSeconds { get; set; } = 8;

    /// <summary>
    /// CancelAfter for planner intent classification — must be the fastest LLM call.
    /// If the planner is slow, the system falls back to rule-based intent classification.
    /// Default: 4s.
    /// </summary>
    public int PlannerTimeoutSeconds { get; set; } = 4;

    /// <summary>
    /// CancelAfter for Ollama embedding requests.
    /// Local embeddings are typically fast but can spike if the model is being loaded.
    /// Default: 15s.
    /// </summary>
    public int EmbeddingTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Max tokens for planner/intent classification completions.
    /// Planner output is structured (JSON-like), so 640 tokens is generous.
    /// Default: 640.
    /// </summary>
    public int PlannerMaxTokens { get; set; } = 640;

    /// <summary>
    /// Max tokens for narration/response enhancement completions.
    /// Full response text can be longer than planner output.
    /// Default: 1024.
    /// </summary>
    public int NarrationMaxTokens { get; set; } = 1024;

    /// <summary>
    /// Returns a profile with all default values.
    /// </summary>
    public static LlmTimeoutProfile Default { get; } = new LlmTimeoutProfile();
}
