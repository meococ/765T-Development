using System;
using System.Collections.Generic;
using System.Globalization;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.UI;
using BIM765T.Revit.Agent.UI.Components;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace BIM765T.Revit.Agent.UI.Tabs;

internal sealed class QuickToolTab : UserControl
{
    private readonly AgentSettings _settings;
    private readonly Action<string, string, string, IEnumerable<KeyValuePair<string, string>>> _onInspect;
    private readonly StackPanel _toastArea;
    private readonly CommandPalettePanel _palette;
    private readonly List<string> _recentPrompts = new List<string>();

    internal QuickToolTab(
        AgentSettings settings,
        Action<string, string, string, IEnumerable<KeyValuePair<string, string>>> onInspect)
    {
        _settings = settings;
        _onInspect = onInspect;
        Background = AppTheme.PageBackground;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _toastArea = new StackPanel { Margin = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceLG, AppTheme.SpaceLG, 0) };
        Grid.SetRow(_toastArea, 0);
        root.Children.Add(_toastArea);

        _palette = new CommandPalettePanel();
        _palette.SetPinnedPrompts(GetPinnedPrompts());
        _palette.SetRecommendedPrompts(GetRecommendedPrompts());
        _palette.SetRecentPrompts(_recentPrompts);
        _palette.SetSummary("Slash command va quick command de xu ly task don gian. Ctrl+K -> go lenh -> Enter.");
        _palette.SearchSubmitted += ExecuteSearch;
        _palette.PromptInvoked += RunQuickPrompt;
        _palette.EntryInvoked += ExecuteAtlasEntry;
        _palette.EntryInspected += InspectAtlasEntry;

        Grid.SetRow(_palette, 1);
        root.Children.Add(new Border
        {
            Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceLG, AppTheme.SpaceLG, AppTheme.SpaceLG),
            Child = _palette
        });

        Content = root;
        Loaded += (_, _) =>
        {
            _palette.FocusSearchBox();
            LoadCoverageOverview();
        };
    }

    internal void FocusSearch()
    {
        _palette.FocusSearchBox();
    }

    private IEnumerable<string> GetPinnedPrompts()
    {
        return new[]
        {
            "create 3d view",
            "duplicate active view",
            "create sheet",
            "renumber current sheet",
            "apply view template",
            "smart qc",
            "preview purge unused"
        };
    }

    private IEnumerable<string> GetRecommendedPrompts()
    {
        return _settings.AllowWriteTools
            ? new[]
            {
                "/view floor plan",
                "/sheet batch",
                "/template apply",
                "/qc model",
                "/purge preview"
            }
            : new[]
            {
                "/describe create sheet",
                "/inspect atlas",
                "/qc model",
                "/evidence lookup"
            };
    }

    private void LoadCoverageOverview()
    {
        var workspaceId = ResolveWorkspaceId();
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            _palette.SetSummary("Workspace chua san sang. Mo Chat de init/resume truoc khi dung command palette.");
            return;
        }

        var payload = new CoverageReportRequest
        {
            WorkspaceId = workspaceId,
            CoverageTier = CommandCoverageTiers.Baseline
        };

        InternalToolClient.Instance.CallWithCallback(
            ToolNames.CommandCoverageReport,
            JsonUtil.Serialize(payload),
            false,
            response =>
            {
                if (!response.Succeeded)
                {
                    return;
                }

                var report = JsonUtil.DeserializeRequired<CoverageReportResponse>(response.PayloadJson);
                _palette.SetSummary(
                    $"Atlas baseline: {report.MappedCommands}/{report.TotalCommands} mapped - {report.ExecutableCommands} executable - {report.VerifiedCommands} verified.");
            });
    }

    private void ExecuteSearch(string query)
    {
        if (!TryResolveWorkspaceId(out var workspaceId))
        {
            return;
        }

        var payload = new CommandAtlasSearchRequest
        {
            WorkspaceId = workspaceId,
            Query = query,
            DocumentContext = "project",
            MaxResults = 8
        };

        InternalToolClient.Instance.CallWithCallback(
            ToolNames.CommandSearch,
            JsonUtil.Serialize(payload),
            false,
            response =>
            {
                if (!response.Succeeded)
                {
                    ToastNotification.Show(_toastArea, "Command search loi: " + response.StatusCode, ToastType.Error);
                    return;
                }

                RememberPrompt(query);
                var result = JsonUtil.DeserializeRequired<CommandAtlasSearchResponse>(response.PayloadJson);
                _palette.SetSummary($"Tim thay {result.Matches.Count} lenh cho '{query}'. Enter de chay, Inspect de mo drawer.");
                _palette.SetResults(result.Matches);
            });
    }

    private void RunQuickPrompt(string prompt)
    {
        if (!TryResolveWorkspaceId(out var workspaceId))
        {
            return;
        }

        var payload = new QuickActionRequest
        {
            WorkspaceId = workspaceId,
            Query = prompt,
            DocumentContext = "project"
        };

        InternalToolClient.Instance.CallWithCallback(
            ToolNames.WorkflowQuickPlan,
            JsonUtil.Serialize(payload),
            false,
            response =>
            {
                if (!response.Succeeded)
                {
                    ToastNotification.Show(_toastArea, "Quick plan loi: " + response.StatusCode, ToastType.Error);
                    return;
                }

                RememberPrompt(prompt);
                var quick = JsonUtil.DeserializeRequired<QuickActionResponse>(response.PayloadJson);
                _onInspect(
                    quick.MatchedEntry.DisplayName,
                    quick.ExecutionDisposition,
                    quick.Summary,
                    new[]
                    {
                        new KeyValuePair<string, string>("Tool", quick.PlannedToolName),
                        new KeyValuePair<string, string>("Requires context", quick.RequiresClarification.ToString()),
                        new KeyValuePair<string, string>("Confidence", ((int)(quick.Confidence * 100)).ToString(CultureInfo.InvariantCulture) + "%")
                    });

                if (!quick.RequiresClarification && quick.MatchedEntry.CanAutoExecute && !quick.MatchedEntry.NeedsApproval)
                {
                    ExecuteAtlasEntry(quick.MatchedEntry);
                }
                else
                {
                    ToastNotification.Show(_toastArea, quick.Summary, quick.MatchedEntry.NeedsApproval ? ToastType.Warning : ToastType.Info);
                }
            });
    }

    private void ExecuteAtlasEntry(CommandAtlasEntry entry)
    {
        if (!TryResolveWorkspaceId(out var workspaceId))
        {
            return;
        }

        var payload = new CommandExecuteRequest
        {
            WorkspaceId = workspaceId,
            CommandId = entry.CommandId,
            Query = entry.DisplayName,
            AllowAutoExecute = entry.CanAutoExecute && !entry.NeedsApproval
        };

        InternalToolClient.Instance.CallWithCallback(
            ToolNames.CommandExecuteSafe,
            JsonUtil.Serialize(payload),
            false,
            response =>
            {
                if (!response.Succeeded && !response.ConfirmationRequired)
                {
                    ToastNotification.Show(_toastArea, "Quick execute loi: " + response.StatusCode, ToastType.Error);
                    return;
                }

                RememberPrompt(entry.DisplayName);
                var result = JsonUtil.DeserializeRequired<CommandExecuteResponse>(response.PayloadJson);
                var subtitle = response.ConfirmationRequired ? "Preview / approval" : result.StatusCode;
                _onInspect(
                    entry.DisplayName,
                    subtitle,
                    result.Summary,
                    new[]
                    {
                        new KeyValuePair<string, string>("Tool lane", result.ToolName),
                        new KeyValuePair<string, string>("Command family", entry.CommandFamily),
                        new KeyValuePair<string, string>("Safety", entry.SafetyClass),
                        new KeyValuePair<string, string>("Verify", entry.VerificationMode)
                    });

                ToastNotification.Show(_toastArea, result.Summary, response.ConfirmationRequired ? ToastType.Warning : ToastType.Success);
            });
    }

    private void InspectAtlasEntry(CommandAtlasEntry entry)
    {
        if (!TryResolveWorkspaceId(out var workspaceId))
        {
            return;
        }

        var payload = new CommandDescribeRequest
        {
            WorkspaceId = workspaceId,
            CommandId = entry.CommandId,
            Query = entry.DisplayName
        };

        InternalToolClient.Instance.CallWithCallback(
            ToolNames.CommandDescribe,
            JsonUtil.Serialize(payload),
            false,
            response =>
            {
                if (!response.Succeeded)
                {
                    ToastNotification.Show(_toastArea, "Inspect atlas loi: " + response.StatusCode, ToastType.Error);
                    return;
                }

                var detail = JsonUtil.DeserializeRequired<CommandAtlasEntry>(response.PayloadJson);
                _onInspect(
                    detail.DisplayName,
                    detail.ExecutionMode + " - " + detail.CoverageStatus,
                    detail.Description,
                    new[]
                    {
                        new KeyValuePair<string, string>("Family", detail.CommandFamily),
                        new KeyValuePair<string, string>("Source", detail.SourceKind),
                        new KeyValuePair<string, string>("Verify", detail.VerificationMode),
                        new KeyValuePair<string, string>("Safety", detail.SafetyClass),
                        new KeyValuePair<string, string>("Auto execute", detail.CanAutoExecute.ToString())
                    });
            });
    }

    private void RememberPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        _recentPrompts.RemoveAll(x => string.Equals(x, prompt, StringComparison.OrdinalIgnoreCase));
        _recentPrompts.Insert(0, prompt);
        while (_recentPrompts.Count > 6)
        {
            _recentPrompts.RemoveAt(_recentPrompts.Count - 1);
        }

        _palette.SetRecentPrompts(_recentPrompts);
    }

    private bool TryResolveWorkspaceId(out string workspaceId)
    {
        workspaceId = ResolveWorkspaceId();
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            return true;
        }

        ToastNotification.Show(_toastArea, "Workspace chua khoi tao. Sang tab Chat de init/resume truoc.", ToastType.Warning);
        return false;
    }

    private static string ResolveWorkspaceId()
    {
        return UiShellState.CurrentWorkspaceId;
    }
}
