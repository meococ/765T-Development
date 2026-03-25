using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class PipeServerHostedService : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly AgentSettings _settings;
    private readonly IAgentLogger _logger;
    private readonly List<Task> _connectionTasks = new List<Task>();
    private readonly object _connectionTasksLock = new object();
    private readonly PipeRequestProcessor _processor;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    internal PipeServerHostedService(
        AgentSettings settings,
        IAgentLogger logger,
        PipeRequestProcessor processor)
    {
        _settings = settings;
        _logger = logger;
        _processor = processor;
    }

    internal void Start()
    {
        if (_cts != null || !_settings.EnablePipeServer)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => AcceptLoop(_cts.Token));
        _logger.Info("Pipe server started: " + _settings.PipeName);
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(
                    _settings.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4_096,
                    4_096,
                    CreatePipeSecurity());

                await WaitForConnectionAsync(server).ConfigureAwait(false);
                TrackConnectionTask(HandleConnectionAsync(server, token));
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error("Pipe server loop failure.", ex);
                await Task.Delay(750, token).ConfigureAwait(false);
            }
        }
    }

    private static Task WaitForConnectionAsync(NamedPipeServerStream server)
    {
        return Task.Factory.FromAsync(server.BeginWaitForConnection, server.EndWaitForConnection, null);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using (server)
        using (var reader = new StreamReader(server, Utf8NoBom, false, 4_096, true))
        using (var writer = new StreamWriter(server, Utf8NoBom, 4_096, true) { AutoFlush = true })
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var clientIdentity = TryGetClientIdentity(server, out var lookupFailed);
            var response = await _processor.ProcessAsync(line, clientIdentity, lookupFailed, token).ConfigureAwait(false);
            if (!token.IsCancellationRequested)
            {
                await writer.WriteLineAsync(JsonUtil.Serialize(response)).ConfigureAwait(false);
            }
        }
    }

    private void TrackConnectionTask(Task task)
    {
        lock (_connectionTasksLock)
        {
            _connectionTasks.Add(task);
        }

        task.ContinueWith(
            _ =>
            {
                lock (_connectionTasksLock)
                {
                    _connectionTasks.Remove(task);
                }
            },
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _loopTask?.Wait(1_500);
            Task[] pending;
            lock (_connectionTasksLock)
            {
                pending = _connectionTasks.ToArray();
            }

            Task.WaitAll(pending, 1_500);
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                _logger.Warn("Pipe server shutdown observed task exception: " + inner.Message);
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.Warn("Pipe server shutdown encountered disposed resource: " + ex.Message);
        }
        catch (OperationCanceledException ex)
        {
            _logger.Warn("Pipe server shutdown cancelled pending work: " + ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Error("Pipe server shutdown failed.", ex);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var sid = WindowsIdentity.GetCurrent().User;
        if (sid != null)
        {
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        return security;
    }

    private static string TryGetClientIdentity(NamedPipeServerStream server, out bool lookupFailed)
    {
        lookupFailed = false;
        try
        {
            return server.GetImpersonationUserName() ?? string.Empty;
        }
        catch
        {
            lookupFailed = true;
            return string.Empty;
        }
    }
}
