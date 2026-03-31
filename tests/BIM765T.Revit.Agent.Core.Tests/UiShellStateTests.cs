using BIM765T.Revit.Agent.UI;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class UiShellStateTests
{
    [Fact]
    public void ResolveWorkspaceId_Treats_Default_As_Empty_And_Prefers_Real_Workspace()
    {
        var resolved = UiShellState.ResolveWorkspaceId("default", "workspace-alpha", string.Empty);
        Assert.Equal("workspace-alpha", resolved);
    }

    [Fact]
    public void NormalizeWorkspaceId_Returns_Empty_For_Default()
    {
        Assert.Equal(string.Empty, UiShellState.NormalizeWorkspaceId("default"));
        Assert.Equal(string.Empty, UiShellState.NormalizeWorkspaceId(" DEFAULT "));
    }

    [Fact]
    public void RememberSession_Sets_ShellMode_To_Transcript()
    {
        UiShellState.ClearSession();
        UiShellState.SetShellMode(UiShellState.ShellModes.Onboarding);

        UiShellState.RememberSession("session-alpha");

        Assert.Equal(UiShellState.ShellModes.Transcript, UiShellState.CurrentShellMode);
    }

    [Fact]
    public void ClearSession_Falls_Back_To_Dashboard_When_Workspace_Exists()
    {
        UiShellState.UpdateFromWorker(new WorkerResponse
        {
            WorkspaceId = "workspace-alpha",
            OnboardingStatus = new OnboardingStatusDto
            {
                WorkspaceId = "workspace-alpha",
                InitStatus = ProjectOnboardingStatuses.Initialized,
                DeepScanStatus = ProjectDeepScanStatuses.NotStarted
            }
        });
        UiShellState.RememberSession("session-alpha");

        UiShellState.ClearSession();

        Assert.Equal(UiShellState.ShellModes.Dashboard, UiShellState.CurrentShellMode);
    }

    [Fact]
    public void SetShellMode_Overrides_Current_Mode_Explicitly()
    {
        UiShellState.SetShellMode(UiShellState.ShellModes.Dashboard);
        Assert.Equal(UiShellState.ShellModes.Dashboard, UiShellState.CurrentShellMode);
    }

    [Fact]
    public void SetShellMode_Can_Move_To_Waiting()
    {
        UiShellState.SetShellMode(UiShellState.ShellModes.Waiting);
        Assert.Equal(UiShellState.ShellModes.Waiting, UiShellState.CurrentShellMode);
    }

    [Fact]
    public void UpdateAmbient_DoesNot_Reset_InitStatus_When_Real_Workspace_Already_Exists()
    {
        UiShellState.UpdateFromWorker(new WorkerResponse
        {
            WorkspaceId = "workspace-alpha",
            OnboardingStatus = new OnboardingStatusDto
            {
                WorkspaceId = "workspace-alpha",
                InitStatus = ProjectOnboardingStatuses.Initialized,
                DeepScanStatus = ProjectDeepScanStatuses.NotStarted
            }
        });

        UiShellState.UpdateAmbient("default", new ProjectContextBundleResponse
        {
            WorkspaceId = string.Empty,
            Exists = false,
            DeepScanStatus = ProjectDeepScanStatuses.NotStarted
        });

        Assert.Equal("workspace-alpha", UiShellState.CurrentWorkspaceId);
        Assert.Equal(ProjectOnboardingStatuses.Initialized, UiShellState.CurrentInitStatus);
    }
}
