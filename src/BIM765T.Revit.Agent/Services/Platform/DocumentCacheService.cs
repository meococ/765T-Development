using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class DocumentCacheService
{
    private readonly ConcurrentDictionary<string, Lazy<object>> _entries = new ConcurrentDictionary<string, Lazy<object>>(StringComparer.OrdinalIgnoreCase);
    private readonly Func<Document, string> _documentKeyResolver;
    private UIControlledApplication? _app;

    internal DocumentCacheService(Func<Document, string> documentKeyResolver)
    {
        _documentKeyResolver = documentKeyResolver;
    }

    internal void Attach(UIControlledApplication app)
    {
        if (_app != null)
        {
            return;
        }

        _app = app;
        app.ControlledApplication.DocumentChanged += OnDocumentChanged;
        app.ControlledApplication.DocumentSaved += OnDocumentSaved;
    }

    internal void Detach()
    {
        if (_app == null)
        {
            return;
        }

        _app.ControlledApplication.DocumentChanged -= OnDocumentChanged;
        _app.ControlledApplication.DocumentSaved -= OnDocumentSaved;
        _app = null;
        _entries.Clear();
    }

    internal T GetOrAdd<T>(Document doc, string scopeKey, Func<T> factory)
    {
        var cacheKey = BuildEntryKey(doc, scopeKey);
        var entry = _entries.GetOrAdd(cacheKey, _ => new Lazy<object>(() => factory()!, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
        return (T)entry.Value;
    }

    internal void Invalidate(Document doc)
    {
        var prefix = BuildDocumentPrefix(doc);
        var keysToRemove = new List<string>();
        foreach (var key in _entries.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _entries.TryRemove(key, out _);
        }
    }

    private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
    {
        Invalidate(e.GetDocument());
    }

    private void OnDocumentSaved(object sender, DocumentSavedEventArgs e)
    {
        Invalidate(e.Document);
    }

    private string BuildEntryKey(Document doc, string scopeKey)
    {
        return BuildDocumentPrefix(doc) + "|" + (scopeKey ?? string.Empty);
    }

    private string BuildDocumentPrefix(Document doc)
    {
        return _documentKeyResolver(doc);
    }
}
