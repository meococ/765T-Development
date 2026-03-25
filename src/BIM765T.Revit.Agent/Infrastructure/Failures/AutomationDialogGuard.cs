using System;
using System.Threading;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BIM765T.Revit.Agent.Infrastructure.Logging;

namespace BIM765T.Revit.Agent.Infrastructure.Failures;

internal sealed class AutomationDialogGuard
{
    private readonly IAgentLogger _logger;
    private readonly object _nameLock = new object();
    private int _activeDepth;
    private string _currentToolName = string.Empty;
    private string _currentRequestId = string.Empty;

    internal AutomationDialogGuard(IAgentLogger logger)
    {
        _logger = logger;
    }

    internal void Attach(UIControlledApplication app)
    {
        app.DialogBoxShowing += OnDialogBoxShowing;
    }

    internal void Detach(UIControlledApplication app)
    {
        app.DialogBoxShowing -= OnDialogBoxShowing;
    }

    internal IDisposable Enter(string toolName, string requestId)
    {
        lock (_nameLock)
        {
            _currentToolName = toolName ?? string.Empty;
            _currentRequestId = requestId ?? string.Empty;
        }

        Interlocked.Increment(ref _activeDepth);
        return new Scope(this);
    }

    private void Exit()
    {
        var remaining = Interlocked.Decrement(ref _activeDepth);
        if (remaining <= 0)
        {
            _activeDepth = 0;
            lock (_nameLock)
            {
                _currentToolName = string.Empty;
                _currentRequestId = string.Empty;
            }
        }
    }

    private void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs args)
    {
        if (Volatile.Read(ref _activeDepth) <= 0)
        {
            return;
        }

        string currentToolName;
        string currentRequestId;
        lock (_nameLock)
        {
            currentToolName = _currentToolName;
            currentRequestId = _currentRequestId;
        }

        var dialogId = args.DialogId ?? string.Empty;
        _logger.Warn($"Auto-dismissing dialog during tool `{currentToolName}` ({currentRequestId}). DialogId={dialogId}.");

        try
        {
            if (args is TaskDialogShowingEventArgs taskDialogArgs)
            {
                taskDialogArgs.OverrideResult(2); // Cancel/Close when available.
                return;
            }

            args.OverrideResult(2);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to auto-dismiss Revit dialog: " + dialogId, ex);
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly AutomationDialogGuard _owner;
        private int _disposed;

        internal Scope(AutomationDialogGuard owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Exit();
            }
        }
    }
}
