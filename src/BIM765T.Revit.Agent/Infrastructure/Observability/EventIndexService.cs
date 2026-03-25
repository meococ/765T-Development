using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI.Events;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Observability;

internal sealed class EventIndexService
{
    private readonly ConcurrentQueue<EventRecord> _recent = new ConcurrentQueue<EventRecord>();
    private readonly ConcurrentDictionary<string, long> _documentEpochs = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxItems;
    private readonly Func<Document, string> _documentKeyResolver;
    private readonly Func<View, string> _viewKeyResolver;
    private readonly IAgentLogger _logger;
    private readonly ISystemClock _clock;
    private UIControlledApplication? _app;
    private long _epochCounter;

    internal EventIndexService(IAgentLogger logger, int maxItems, Func<Document, string> documentKeyResolver, Func<View, string> viewKeyResolver, ISystemClock clock)
    {
        _logger = logger;
        _maxItems = Math.Max(20, maxItems);
        _documentKeyResolver = documentKeyResolver;
        _viewKeyResolver = viewKeyResolver;
        _clock = clock;
    }

    internal void Attach(UIControlledApplication app)
    {
        if (_app != null)
        {
            return;
        }

        _app = app;
        app.ViewActivated += OnViewActivated;
        app.ControlledApplication.DocumentChanged += OnDocumentChanged;
        app.ControlledApplication.DocumentSaving += OnDocumentSaving;
        app.ControlledApplication.DocumentSaved += OnDocumentSaved;
    }

    internal void Detach()
    {
        if (_app == null)
        {
            return;
        }

        _app.ViewActivated -= OnViewActivated;
        _app.ControlledApplication.DocumentChanged -= OnDocumentChanged;
        _app.ControlledApplication.DocumentSaving -= OnDocumentSaving;
        _app.ControlledApplication.DocumentSaved -= OnDocumentSaved;
        _app = null;
    }

    internal List<EventRecord> GetRecent()
    {
        return new List<EventRecord>(_recent.ToArray());
    }

    internal long GetDocumentEpoch(string documentKey)
    {
        if (string.IsNullOrWhiteSpace(documentKey))
        {
            return 0;
        }

        return _documentEpochs.TryGetValue(documentKey, out var epoch) ? epoch : 0;
    }

    private void OnViewActivated(object sender, ViewActivatedEventArgs e)
    {
        TryAdd(new EventRecord
        {
            EventKind = "ViewActivated",
            DocumentKey = _documentKeyResolver(e.Document),
            ViewKey = _viewKeyResolver(e.CurrentActiveView),
            TimestampUtc = _clock.UtcNow,
            Message = e.CurrentActiveView?.Name ?? "<none>"
        });
    }

    private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
    {
        var ids = e.GetAddedElementIds().Concat(e.GetModifiedElementIds()).Select(x => checked((int)x.Value)).Take(200).ToList();
        ids.AddRange(e.GetDeletedElementIds().Select(x => checked((int)x.Value)).Take(200));
        TryAdd(new EventRecord
        {
            EventKind = "DocumentChanged",
            DocumentKey = _documentKeyResolver(e.GetDocument()),
            TimestampUtc = _clock.UtcNow,
            ElementIds = ids,
            Message = $"Added:{e.GetAddedElementIds().Count} Modified:{e.GetModifiedElementIds().Count} Deleted:{e.GetDeletedElementIds().Count}"
        });
    }

    private void OnDocumentSaving(object sender, DocumentSavingEventArgs e)
    {
        TryAdd(new EventRecord
        {
            EventKind = "DocumentSaving",
            DocumentKey = _documentKeyResolver(e.Document),
            TimestampUtc = _clock.UtcNow,
            Message = e.Document.PathName ?? string.Empty
        });
    }

    private void OnDocumentSaved(object sender, DocumentSavedEventArgs e)
    {
        TryAdd(new EventRecord
        {
            EventKind = "DocumentSaved",
            DocumentKey = _documentKeyResolver(e.Document),
            TimestampUtc = _clock.UtcNow,
            Message = e.Status.ToString()
        });
    }

    private void TryAdd(EventRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.DocumentKey))
        {
            _documentEpochs[record.DocumentKey] = System.Threading.Interlocked.Increment(ref _epochCounter);
        }

        _recent.Enqueue(record);
        var overflow = _recent.Count - _maxItems;
        while (overflow-- > 0 && _recent.TryDequeue(out _))
        {
        }
    }
}
