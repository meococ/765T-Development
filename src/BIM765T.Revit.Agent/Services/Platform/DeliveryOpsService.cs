using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class DeliveryOpsService
{
    private readonly PlatformServices _platform;
    private const string DefaultCatalogName = "delivery_ops";

    internal DeliveryOpsService(PlatformServices platform)
    {
        _platform = platform;
    }

    internal FamilyLibraryRootsResponse ListFamilyLibraryRoots()
    {
        var catalog = ResolveCatalog();
        return new FamilyLibraryRootsResponse
        {
            Roots = catalog.FamilyRoots.Select(ToNamedRootDto).ToList()
        };
    }

    internal DeliveryPresetCatalogResponse ListPresets(Document doc)
    {
        var catalog = ResolveCatalog();
        var response = new DeliveryPresetCatalogResponse
        {
            FamilyRoots = catalog.FamilyRoots.Select(ToNamedRootDto).ToList(),
            OutputRoots = catalog.OutputRoots.Select(ToNamedRootDto).ToList()
        };

        response.Presets.AddRange(catalog.IfcPresets.Select(x => new DeliveryPresetDescriptor
        {
            PresetName = x.Name,
            Kind = "ifc",
            Description = x.Description ?? string.Empty,
            Source = catalog.Source,
            NativeSetupName = string.Empty
        }));
        response.Presets.AddRange(catalog.PdfPresets.Select(x => new DeliveryPresetDescriptor
        {
            PresetName = x.Name,
            Kind = "pdf",
            Description = x.Description ?? string.Empty,
            Source = catalog.Source,
            NativeSetupName = string.Empty
        }));
        response.Presets.AddRange(catalog.DwgPresets.Select(x => new DeliveryPresetDescriptor
        {
            PresetName = x.Name,
            Kind = "dwg",
            Description = x.Description ?? string.Empty,
            Source = catalog.Source,
            NativeSetupName = x.NativeSetupName ?? string.Empty
        }));

        foreach (var setup in BaseExportOptions.GetPredefinedSetupNames(doc).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (response.Presets.Any(x => string.Equals(x.Kind, "dwg", StringComparison.OrdinalIgnoreCase) && string.Equals(x.NativeSetupName, setup, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            response.Presets.Add(new DeliveryPresetDescriptor
            {
                PresetName = "native:" + setup,
                Kind = "dwg",
                Description = "Native DWG export setup from Revit.",
                Source = "revit_native",
                NativeSetupName = setup
            });
        }

        return response;
    }

    internal OutputTargetValidationResponse ValidateOutputTarget(OutputTargetValidationRequest request)
    {
        var catalog = ResolveCatalog();
        ConfiguredRoot root;
        if (!TryResolveOutputRoot(catalog, request.OutputRootName, out root))
        {
            return new OutputTargetValidationResponse
            {
                OperationKind = request.OperationKind ?? string.Empty,
                OutputRootName = request.OutputRootName ?? string.Empty,
                Allowed = false,
                Reason = "Output root is not allowlisted."
            };
        }

        string resolvedPath;
        string reason;
        var allowed = TryResolveChildPath(root.Path, request.RelativePath, out resolvedPath, out reason);
        return new OutputTargetValidationResponse
        {
            OperationKind = request.OperationKind ?? string.Empty,
            OutputRootName = root.Name,
            ResolvedRootPath = NormalizeAbsolutePath(root.Path),
            RelativePath = request.RelativePath ?? string.Empty,
            ResolvedPath = resolvedPath,
            Allowed = allowed,
            Reason = reason
        };
    }

    internal ScheduleCreatePreviewResponse PreviewScheduleCreate(Document doc, ScheduleCreateRequest request)
    {
        request.Fields = request.Fields ?? new List<ScheduleFieldSpec>();
        request.Filters = request.Filters ?? new List<ScheduleFilterSpec>();
        request.Sorts = request.Sorts ?? new List<ScheduleSortSpec>();
        var preview = new ScheduleCreatePreviewResponse
        {
            DocumentKey = _platform.GetDocumentKey(doc),
            ScheduleName = request.ScheduleName ?? string.Empty,
            CategoryName = request.CategoryName ?? string.Empty
        };

        var category = ResolveCategory(doc, request.CategoryName ?? string.Empty);
        if (category == null)
        {
            preview.Warnings.Add($"Category '{request.CategoryName}' not found.");
            return preview;
        }

        preview.ResolvedCategoryId = checked((int)category.Id.Value);
        var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .FirstOrDefault(x => string.Equals(x.Name, request.ScheduleName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            preview.ExistingScheduleId = checked((int)existing.Id.Value);
            preview.Warnings.Add($"Schedule '{request.ScheduleName}' already exists.");
        }

        var schedulable = GetSchedulableFieldMap(doc, category.Id);
        foreach (var field in request.Fields.Take(Math.Max(1, request.MaxFieldCount)))
        {
            if (schedulable.ContainsKey(field.ParameterName))
            {
                preview.FieldNames.Add(field.ParameterName);
            }
            else
            {
                preview.Warnings.Add($"Field '{field.ParameterName}' is not schedulable for category '{category.Name}'.");
            }
        }

        preview.FilterCount = request.Filters.Count;
        preview.SortCount = request.Sorts.Count;
        if (category.CategoryType != CategoryType.Model)
        {
            preview.Warnings.Add("Current delivery lane only supports model schedules.");
        }

        return preview;
    }

    internal ExecutionResult PreviewFamilyLoad(Document doc, FamilyLoadRequest request, ToolRequestEnvelope envelope)
    {
        request.TypeNames = request.TypeNames ?? new List<string>();
        var catalog = ResolveCatalog();
        ConfiguredRoot root;
        if (!TryResolveFamilyRoot(catalog, request.LibraryRootName, out root))
        {
            throw new InvalidOperationException(StatusCodes.AllowlistBlocked);
        }

        string resolvedPath;
        string reason;
        if (!TryResolveChildPath(root.Path, request.RelativeFamilyPath, out resolvedPath, out reason))
        {
            throw new InvalidOperationException(StatusCodes.AllowlistBlocked + ": " + reason);
        }

        var diagnostics = new List<DiagnosticRecord>();
        if (!File.Exists(resolvedPath))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_FILE_NOT_FOUND", DiagnosticSeverity.Error, $"Family file not found: {resolvedPath}"));
        }

        var availableTypes = ReadFamilyTypeNames(doc.Application, resolvedPath);
        if (!request.LoadAllSymbols && request.TypeNames.Count > 0)
        {
            foreach (var typeName in request.TypeNames)
            {
                if (!availableTypes.Contains(typeName, StringComparer.OrdinalIgnoreCase))
                {
                    diagnostics.Add(DiagnosticRecord.Create("FAMILY_TYPE_NOT_FOUND", DiagnosticSeverity.Warning, $"Type '{typeName}' was not found in family file."));
                }
            }
        }

        var token = _platform.Approval.IssueToken(envelope.ToolName, _platform.Approval.BuildFingerprint(envelope), _platform.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = ToolNames.FamilyLoadSafe,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                $"LibraryRoot={root.Name}",
                $"FamilyPath={resolvedPath}",
                $"AvailableTypeCount={availableTypes.Count}",
                $"RequestedTypeCount={request.TypeNames.Count}",
                $"OverwriteExisting={request.OverwriteExisting}",
                $"LoadAllSymbols={request.LoadAllSymbols}"
            }
        };
    }

    internal ExecutionResult ExecuteFamilyLoad(Document doc, FamilyLoadRequest request)
    {
        request.TypeNames = request.TypeNames ?? new List<string>();
        var catalog = ResolveCatalog();
        ConfiguredRoot root;
        if (!TryResolveFamilyRoot(catalog, request.LibraryRootName, out root))
        {
            throw new InvalidOperationException(StatusCodes.AllowlistBlocked);
        }

        string resolvedPath;
        string reason;
        if (!TryResolveChildPath(root.Path, request.RelativeFamilyPath, out resolvedPath, out reason))
        {
            throw new InvalidOperationException(StatusCodes.AllowlistBlocked + ": " + reason);
        }

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Family file not found.", resolvedPath);
        }

        var changedIds = new HashSet<int>();
        var options = new FamilyLoadOptionsAdapter(request.OverwriteExisting);
        if (request.LoadAllSymbols || request.TypeNames.Count == 0)
        {
            Family family;
            if (!doc.LoadFamily(resolvedPath, options, out family) || family == null)
            {
                throw new InvalidOperationException("LoadFamily returned false.");
            }

            changedIds.Add(checked((int)family.Id.Value));
            foreach (var symbolId in family.GetFamilySymbolIds())
            {
                changedIds.Add(checked((int)symbolId.Value));
            }
        }
        else
        {
            foreach (var typeName in request.TypeNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                FamilySymbol symbol;
                if (!doc.LoadFamilySymbol(resolvedPath, typeName, options, out symbol) || symbol == null)
                {
                    throw new InvalidOperationException($"LoadFamilySymbol failed for type '{typeName}'.");
                }

                changedIds.Add(checked((int)symbol.Id.Value));
                if (symbol.Family != null)
                {
                    changedIds.Add(checked((int)symbol.Family.Id.Value));
                }
            }
        }

        return new ExecutionResult
        {
            OperationName = ToolNames.FamilyLoadSafe,
            DryRun = false,
            ChangedIds = changedIds.ToList(),
            DiffSummary = new DiffSummary { CreatedIds = changedIds.ToList() },
            Artifacts = new List<string>
            {
                $"LibraryRoot={root.Name}",
                $"FamilyPath={resolvedPath}",
                $"LoadedCount={changedIds.Count}"
            }
        };
    }

    internal ExecutionResult PreviewCreateSchedule(Document doc, ScheduleCreateRequest request, ToolRequestEnvelope envelope)
    {
        var preview = PreviewScheduleCreate(doc, request);
        var diagnostics = preview.Warnings.Select(x => DiagnosticRecord.Create("SCHEDULE_PREVIEW", DiagnosticSeverity.Warning, x)).ToList();
        var token = _platform.Approval.IssueToken(envelope.ToolName, _platform.Approval.BuildFingerprint(envelope), _platform.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = ToolNames.ScheduleCreateSafe,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                $"ScheduleName={preview.ScheduleName}",
                $"Category={preview.CategoryName}",
                $"FieldCount={preview.FieldNames.Count}",
                $"FilterCount={preview.FilterCount}",
                $"SortCount={preview.SortCount}"
            }
        };
    }

    internal ExecutionResult ExecuteCreateSchedule(Document doc, ScheduleCreateRequest request)
    {
        request.Fields = request.Fields ?? new List<ScheduleFieldSpec>();
        request.Filters = request.Filters ?? new List<ScheduleFilterSpec>();
        request.Sorts = request.Sorts ?? new List<ScheduleSortSpec>();
        var category = ResolveCategory(doc, request.CategoryName) ?? throw new InvalidOperationException($"Category '{request.CategoryName}' not found.");
        if (category.CategoryType != CategoryType.Model)
        {
            throw new InvalidOperationException("Current delivery lane only supports model schedules.");
        }

        var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
            .FirstOrDefault(x => string.Equals(x.Name, request.ScheduleName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            throw new InvalidOperationException($"Schedule '{request.ScheduleName}' already exists.");
        }

        using (var group = new TransactionGroup(doc, "765TAgent::schedule.create_safe"))
        {
            group.Start();
            ViewSchedule schedule;
            var diagnostics = new List<DiagnosticRecord>();
            using (var tx = new Transaction(doc, "Create schedule"))
            {
                tx.Start();
                BIM765T.Revit.Agent.Infrastructure.Failures.AgentFailureHandling.Configure(tx, diagnostics);
                schedule = ViewSchedule.CreateSchedule(doc, category.Id);
                schedule.Name = request.ScheduleName;
                var definition = schedule.Definition;
                definition.IsItemized = request.IsItemized;
                definition.IncludeLinkedFiles = request.IncludeLinkedFiles;
                definition.ShowGrandTotal = request.ShowGrandTotal;
                if (request.ShowGrandTotal)
                {
                    definition.ShowGrandTotalCount = true;
                    definition.ShowGrandTotalTitle = true;
                }

                var schedulable = GetSchedulableFieldMap(definition, doc);
                var fieldMap = new Dictionary<string, ScheduleField>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in request.Fields.Take(Math.Max(1, request.MaxFieldCount)))
                {
                    ScheduleField addedField;
                    if (!TryEnsureScheduleField(definition, schedulable, field.ParameterName, out addedField))
                    {
                        throw new InvalidOperationException($"Field '{field.ParameterName}' is not schedulable for '{category.Name}'.");
                    }

                    fieldMap[field.ParameterName] = addedField;
                    if (!string.IsNullOrWhiteSpace(field.ColumnHeading))
                    {
                        addedField.ColumnHeading = field.ColumnHeading;
                    }

                    addedField.IsHidden = field.Hidden;
                }

                foreach (var filter in request.Filters)
                {
                    ScheduleField filterField;
                    if (!fieldMap.TryGetValue(filter.ParameterName, out filterField))
                    {
                        if (!TryEnsureScheduleField(definition, schedulable, filter.ParameterName, out filterField))
                        {
                            throw new InvalidOperationException($"Filter field '{filter.ParameterName}' is not schedulable.");
                        }

                        filterField.IsHidden = true;
                        fieldMap[filter.ParameterName] = filterField;
                    }

                    definition.AddFilter(BuildScheduleFilter(filterField, filter));
                }

                foreach (var sort in request.Sorts)
                {
                    ScheduleField sortField;
                    if (!fieldMap.TryGetValue(sort.ParameterName, out sortField))
                    {
                        if (!TryEnsureScheduleField(definition, schedulable, sort.ParameterName, out sortField))
                        {
                            throw new InvalidOperationException($"Sort field '{sort.ParameterName}' is not schedulable.");
                        }

                        sortField.IsHidden = true;
                        fieldMap[sort.ParameterName] = sortField;
                    }

                    definition.AddSortGroupField(new ScheduleSortGroupField(sortField.FieldId, sort.Ascending ? ScheduleSortOrder.Ascending : ScheduleSortOrder.Descending));
                }

                tx.Commit();
            }

            group.Assimilate();
            var createdId = checked((int)schedule.Id.Value);
            return new ExecutionResult
            {
                OperationName = ToolNames.ScheduleCreateSafe,
                DryRun = false,
                ChangedIds = new List<int> { createdId },
                DiffSummary = new DiffSummary { CreatedIds = new List<int> { createdId } },
                Artifacts = new List<string>
                {
                    $"ScheduleId={createdId}",
                    $"ScheduleName={request.ScheduleName}",
                    $"Category={category.Name}"
                }
            };
        }
    }

    internal ExecutionResult PreviewIfcExport(UIApplication uiapp, Document doc, IfcExportRequest request, ToolRequestEnvelope envelope)
    {
        var preset = ResolveIfcPreset(request.PresetName);
        var target = EnsureAllowedOutput(request.OutputRootName, request.RelativeOutputPath);
        var fileName = EnsureExtension(string.IsNullOrWhiteSpace(request.FileName) ? doc.Title : request.FileName, ".ifc");
        var fullPath = Path.Combine(target.ResolvedPath, fileName);
        var diagnostics = new List<DiagnosticRecord>();
        if (File.Exists(fullPath) && !request.OverwriteExisting)
        {
            diagnostics.Add(DiagnosticRecord.Create("OUTPUT_EXISTS", DiagnosticSeverity.Error, $"Output file already exists: {fullPath}"));
        }

        if (ResolveOptionalView(uiapp, doc, request.ViewId, request.ViewName) == null && (request.ViewId.HasValue || !string.IsNullOrWhiteSpace(request.ViewName)))
        {
            diagnostics.Add(DiagnosticRecord.Create("IFC_VIEW_NOT_FOUND", DiagnosticSeverity.Warning, "Requested IFC filter view was not found; export will run without FilterViewId."));
        }

        var token = _platform.Approval.IssueToken(envelope.ToolName, _platform.Approval.BuildFingerprint(envelope), _platform.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = ToolNames.ExportIfcSafe,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = diagnostics,
            Artifacts = new List<string> { $"Preset={preset.Name}", $"Output={fullPath}" }
        };
    }

    internal ExecutionResult ExecuteIfcExport(UIApplication uiapp, Document doc, IfcExportRequest request)
    {
        var preset = ResolveIfcPreset(request.PresetName);
        var target = EnsureAllowedOutput(request.OutputRootName, request.RelativeOutputPath);
        Directory.CreateDirectory(target.ResolvedPath);
        var fileName = EnsureExtension(string.IsNullOrWhiteSpace(request.FileName) ? doc.Title : request.FileName, ".ifc");
        var fullPath = Path.Combine(target.ResolvedPath, fileName);
        if (File.Exists(fullPath) && !request.OverwriteExisting)
        {
            throw new InvalidOperationException("IFC output already exists and OverwriteExisting=false.");
        }

        var options = new IFCExportOptions
        {
            ExportBaseQuantities = preset.ExportBaseQuantities,
            WallAndColumnSplitting = preset.WallAndColumnSplitting,
            SpaceBoundaryLevel = preset.SpaceBoundaryLevel
        };
        IFCVersion parsedVersion;
        if (Enum.TryParse(preset.FileVersion, true, out parsedVersion))
        {
            options.FileVersion = parsedVersion;
        }

        var filterView = ResolveOptionalView(uiapp, doc, request.ViewId, request.ViewName);
        if (filterView != null)
        {
            options.FilterViewId = filterView.Id;
        }

        var diagnostics = new List<DiagnosticRecord>();
        using (var transaction = new Transaction(doc, "765TAgent::export.ifc_safe"))
        {
            transaction.Start();
            BIM765T.Revit.Agent.Infrastructure.Failures.AgentFailureHandling.Configure(transaction, diagnostics);

            var exported = doc.Export(target.ResolvedPath, Path.GetFileNameWithoutExtension(fileName), options);
            transaction.RollBack();

            if (!exported)
            {
                throw new InvalidOperationException("Document.Export returned false for IFC export.");
            }
        }

        return new ExecutionResult
        {
            OperationName = ToolNames.ExportIfcSafe,
            DryRun = false,
            Diagnostics = diagnostics,
            Artifacts = new List<string> { $"Preset={preset.Name}", $"Output={fullPath}", "TransactionMode=rollback_wrapper" }
        };
    }

    internal ExecutionResult PreviewDwgExport(UIApplication uiapp, Document doc, DwgExportRequest request, ToolRequestEnvelope envelope)
    {
        var preset = ResolveDwgPreset(doc, request.PresetName);
        var target = EnsureAllowedOutput(request.OutputRootName, request.RelativeOutputPath);
        var exportIds = ResolveDwgExportIds(uiapp, doc, request);
        var token = _platform.Approval.IssueToken(envelope.ToolName, _platform.Approval.BuildFingerprint(envelope), _platform.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = ToolNames.ExportDwgSafe,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = exportIds.Count == 0
                ? new List<DiagnosticRecord> { DiagnosticRecord.Create("DWG_SCOPE_EMPTY", DiagnosticSeverity.Error, "No exportable views or sheets were resolved for DWG export.") }
                : new List<DiagnosticRecord>(),
            Artifacts = new List<string> { $"Preset={preset.Name}", $"ExportCount={exportIds.Count}", $"OutputRoot={target.ResolvedPath}" }
        };
    }

    internal ExecutionResult ExecuteDwgExport(UIApplication uiapp, Document doc, DwgExportRequest request)
    {
        var preset = ResolveDwgPreset(doc, request.PresetName);
        var target = EnsureAllowedOutput(request.OutputRootName, request.RelativeOutputPath);
        Directory.CreateDirectory(target.ResolvedPath);
        var exportIds = ResolveDwgExportIds(uiapp, doc, request);
        if (exportIds.Count == 0)
        {
            throw new InvalidOperationException("No exportable views or sheets were resolved for DWG export.");
        }

        var fileName = string.IsNullOrWhiteSpace(request.FileName) ? doc.Title : request.FileName;
        if (!doc.Export(target.ResolvedPath, Path.GetFileNameWithoutExtension(fileName), exportIds.Select(x => new ElementId((long)x)).ToList(), preset.Options))
        {
            throw new InvalidOperationException("Document.Export returned false for DWG export.");
        }

        return new ExecutionResult
        {
            OperationName = ToolNames.ExportDwgSafe,
            DryRun = false,
            Artifacts = new List<string> { $"Preset={preset.Name}", $"ExportCount={exportIds.Count}", $"OutputRoot={target.ResolvedPath}" }
        };
    }

    internal ExecutionResult PreviewPdfPrint(Document doc, PdfPrintRequest request, ToolRequestEnvelope envelope)
    {
        var preset = ResolvePdfPreset(request.PresetName);
        var target = EnsureAllowedOutput(request.OutputRootName, request.RelativeOutputPath);
        var sheetIds = ResolveSheetIds(doc, request.SheetIds, request.SheetNumbers);
        var fileName = EnsureExtension(string.IsNullOrWhiteSpace(request.FileName) ? doc.Title : request.FileName, ".pdf");
        var token = _platform.Approval.IssueToken(envelope.ToolName, _platform.Approval.BuildFingerprint(envelope), _platform.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = ToolNames.SheetPrintPdfSafe,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = sheetIds.Count == 0
                ? new List<DiagnosticRecord> { DiagnosticRecord.Create("PDF_SCOPE_EMPTY", DiagnosticSeverity.Error, "No sheets were resolved for PDF export.") }
                : new List<DiagnosticRecord>(),
            Artifacts = new List<string> { $"Preset={preset.Name}", $"SheetCount={sheetIds.Count}", $"OutputFile={Path.Combine(target.ResolvedPath, fileName)}" }
        };
    }

    internal ExecutionResult ExecutePdfPrint(Document doc, PdfPrintRequest request)
    {
        var preset = ResolvePdfPreset(request.PresetName);
        var target = EnsureAllowedOutput(request.OutputRootName, request.RelativeOutputPath);
        Directory.CreateDirectory(target.ResolvedPath);
        var sheetIds = ResolveSheetIds(doc, request.SheetIds, request.SheetNumbers).Select(x => new ElementId((long)x)).ToList();
        if (sheetIds.Count == 0)
        {
            throw new InvalidOperationException("No sheets were resolved for PDF export.");
        }

        var fileName = EnsureExtension(string.IsNullOrWhiteSpace(request.FileName) ? doc.Title : request.FileName, ".pdf");
        using (var options = BuildPdfOptions(preset, fileName, request.Combine))
        {
            if (!doc.Export(target.ResolvedPath, sheetIds, options))
            {
                throw new InvalidOperationException("Document.Export returned false for PDF export.");
            }
        }

        return new ExecutionResult
        {
            OperationName = ToolNames.SheetPrintPdfSafe,
            DryRun = false,
            Artifacts = new List<string> { $"Preset={preset.Name}", $"SheetCount={sheetIds.Count}", $"OutputRoot={target.ResolvedPath}" }
        };
    }

    private DeliveryOpsCatalog ResolveCatalog()
    {
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIM765T.Revit.Agent", "presets", DefaultCatalogName + ".json");
        if (File.Exists(appDataPath))
        {
            var catalog = JsonUtil.DeserializeRequired<DeliveryOpsCatalog>(File.ReadAllText(appDataPath));
            catalog.Source = appDataPath;
            return NormalizeCatalog(catalog);
        }

        var repoRoot = TryFindRepoRoot(AppDomain.CurrentDomain.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            var repoPath = Path.Combine(repoRoot, "docs", "agent", "presets", DefaultCatalogName + ".json");
            if (File.Exists(repoPath))
            {
                var catalog = JsonUtil.DeserializeRequired<DeliveryOpsCatalog>(File.ReadAllText(repoPath));
                catalog.Source = repoPath;
                return NormalizeCatalog(catalog);
            }
        }

        var fallback = BuildCodeDefaultCatalog();
        fallback.Source = "code_default";
        return NormalizeCatalog(fallback);
    }

    private static DeliveryOpsCatalog NormalizeCatalog(DeliveryOpsCatalog catalog)
    {
        catalog = catalog ?? new DeliveryOpsCatalog();
        catalog.FamilyRoots = catalog.FamilyRoots ?? new List<ConfiguredRoot>();
        catalog.OutputRoots = catalog.OutputRoots ?? new List<ConfiguredRoot>();
        catalog.IfcPresets = catalog.IfcPresets ?? new List<ConfiguredIfcPreset>();
        catalog.DwgPresets = catalog.DwgPresets ?? new List<ConfiguredDwgPreset>();
        catalog.PdfPresets = catalog.PdfPresets ?? new List<ConfiguredPdfPreset>();
        return catalog;
    }

    private static DeliveryOpsCatalog BuildCodeDefaultCatalog()
    {
        return new DeliveryOpsCatalog
        {
            FamilyRoots = new List<ConfiguredRoot>
            {
                new ConfiguredRoot { Name = "documents_library", Path = @"%USERPROFILE%\Documents\BIM765T\FamilyLibrary", Kind = "family_library" }
            },
            OutputRoots = new List<ConfiguredRoot>
            {
                new ConfiguredRoot { Name = "documents_exports", Path = @"%USERPROFILE%\Documents\BIM765T\Exports", Kind = "output" }
            },
            IfcPresets = new List<ConfiguredIfcPreset>
            {
                new ConfiguredIfcPreset { Name = "coordination_ifc", Description = "Coordination-focused IFC export.", FileVersion = "IFC4", WallAndColumnSplitting = true, SpaceBoundaryLevel = 1 }
            },
            DwgPresets = new List<ConfiguredDwgPreset>
            {
                new ConfiguredDwgPreset { Name = "default_dwg", Description = "Use the Revit default DWG export setup.", NativeSetupName = string.Empty }
            },
            PdfPresets = new List<ConfiguredPdfPreset>
            {
                new ConfiguredPdfPreset { Name = "sheet_issue_pdf", Description = "Combined PDF issue set.", Combine = true, ColorDepth = "Color", PaperPlacement = "Center" }
            }
        };
    }

    private static string TryFindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BIM765T.Revit.Agent.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return string.Empty;
    }

    private static NamedRootDto ToNamedRootDto(ConfiguredRoot root)
    {
        var normalized = NormalizeAbsolutePath(root.Path);
        return new NamedRootDto
        {
            Name = root.Name,
            Path = normalized,
            Kind = root.Kind ?? string.Empty,
            Exists = Directory.Exists(normalized)
        };
    }

    private static bool TryResolveFamilyRoot(DeliveryOpsCatalog catalog, string rootName, out ConfiguredRoot root)
    {
        root = catalog.FamilyRoots.FirstOrDefault(x => string.Equals(x.Name, rootName, StringComparison.OrdinalIgnoreCase));
        return root != null;
    }

    private static bool TryResolveOutputRoot(DeliveryOpsCatalog catalog, string rootName, out ConfiguredRoot root)
    {
        root = catalog.OutputRoots.FirstOrDefault(x => string.Equals(x.Name, rootName, StringComparison.OrdinalIgnoreCase));
        return root != null;
    }

    private static string NormalizeAbsolutePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path ?? string.Empty);
        return Path.GetFullPath(expanded);
    }

    private static bool TryResolveChildPath(string rootPath, string relativePath, out string resolvedPath, out string reason)
    {
        var normalizedRoot = NormalizeAbsolutePath(rootPath);
        var combined = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath ?? string.Empty));
        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.OrdinalIgnoreCase)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(combined, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            resolvedPath = string.Empty;
            reason = "Resolved path escapes allowlisted root.";
            return false;
        }

        resolvedPath = combined;
        reason = string.Empty;
        return true;
    }

    private OutputTargetValidationResponse EnsureAllowedOutput(string rootName, string relativePath)
    {
        var validation = ValidateOutputTarget(new OutputTargetValidationRequest
        {
            OutputRootName = rootName,
            RelativePath = relativePath,
            OperationKind = "export"
        });

        if (!validation.Allowed)
        {
            throw new InvalidOperationException(StatusCodes.OutputTargetBlocked + ": " + validation.Reason);
        }

        return validation;
    }

    private ConfiguredIfcPreset ResolveIfcPreset(string presetName)
    {
        var catalog = ResolveCatalog();
        var preset = catalog.IfcPresets.FirstOrDefault(x => string.Equals(x.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            throw new InvalidOperationException(StatusCodes.PresetNotFound + ": " + presetName);
        }

        return preset;
    }

    private ResolvedDwgPreset ResolveDwgPreset(Document doc, string presetName)
    {
        var catalog = ResolveCatalog();
        var preset = catalog.DwgPresets.FirstOrDefault(x => string.Equals(x.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (preset != null)
        {
            if (string.IsNullOrWhiteSpace(preset.NativeSetupName))
            {
                return new ResolvedDwgPreset { Name = preset.Name, Options = new DWGExportOptions() };
            }

            return new ResolvedDwgPreset { Name = preset.Name, Options = DWGExportOptions.GetPredefinedOptions(doc, preset.NativeSetupName) };
        }

        if (!string.IsNullOrWhiteSpace(presetName) && presetName.StartsWith("native:", StringComparison.OrdinalIgnoreCase))
        {
            var nativeName = presetName.Substring("native:".Length);
            return new ResolvedDwgPreset { Name = presetName, Options = DWGExportOptions.GetPredefinedOptions(doc, nativeName) };
        }

        throw new InvalidOperationException(StatusCodes.PresetNotFound + ": " + presetName);
    }

    private ConfiguredPdfPreset ResolvePdfPreset(string presetName)
    {
        var catalog = ResolveCatalog();
        var preset = catalog.PdfPresets.FirstOrDefault(x => string.Equals(x.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            throw new InvalidOperationException(StatusCodes.PresetNotFound + ": " + presetName);
        }

        return preset;
    }

    private static Category? ResolveCategory(Document doc, string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return null;
        }

        return doc.Settings.Categories.Cast<Category>().FirstOrDefault(x => string.Equals(x.Name, categoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, SchedulableField> GetSchedulableFieldMap(Document doc, ElementId categoryId)
    {
        using (var group = new TransactionGroup(doc, "765TAgent::schedule.preview_lookup"))
        {
            group.Start();
            using (var tx = new Transaction(doc, "Create temporary schedule"))
            {
                tx.Start();
                var schedule = ViewSchedule.CreateSchedule(doc, categoryId);
                var map = GetSchedulableFieldMap(schedule.Definition, doc);
                tx.RollBack();
                group.RollBack();
                return map;
            }
        }
    }

    private static Dictionary<string, SchedulableField> GetSchedulableFieldMap(ScheduleDefinition definition, Document doc)
    {
        return definition.GetSchedulableFields()
            .GroupBy(x => x.GetName(doc), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryEnsureScheduleField(ScheduleDefinition definition, IDictionary<string, SchedulableField> schedulable, string parameterName, out ScheduleField field)
    {
        field = null!;
        SchedulableField schedulableField;
        if (!schedulable.TryGetValue(parameterName, out schedulableField))
        {
            return false;
        }

        field = definition.AddField(schedulableField);
        return true;
    }

    private static ScheduleFilter BuildScheduleFilter(ScheduleField field, ScheduleFilterSpec spec)
    {
        ScheduleFilterType filterType;
        if (!Enum.TryParse(spec.Operator ?? "Equal", true, out filterType))
        {
            filterType = ScheduleFilterType.Equal;
        }

        if (filterType == ScheduleFilterType.HasValue || filterType == ScheduleFilterType.HasNoValue)
        {
            return new ScheduleFilter(field.FieldId, filterType);
        }

        int intValue;
        if (int.TryParse(spec.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
        {
            return new ScheduleFilter(field.FieldId, filterType, intValue);
        }

        double doubleValue;
        if (double.TryParse(spec.Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out doubleValue))
        {
            return new ScheduleFilter(field.FieldId, filterType, doubleValue);
        }

        return new ScheduleFilter(field.FieldId, filterType, spec.Value ?? string.Empty);
    }

    private static List<string> ReadFamilyTypeNames(Autodesk.Revit.ApplicationServices.Application application, string familyPath)
    {
        var results = new List<string>();
        if (application == null || string.IsNullOrWhiteSpace(familyPath) || !File.Exists(familyPath))
        {
            return results;
        }

        Document? familyDoc = null;
        try
        {
            familyDoc = application.OpenDocumentFile(familyPath);
            if (familyDoc != null && familyDoc.IsFamilyDocument && familyDoc.FamilyManager != null)
            {
                foreach (FamilyType type in familyDoc.FamilyManager.Types)
                {
                    if (!string.IsNullOrWhiteSpace(type.Name))
                    {
                        results.Add(type.Name);
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (familyDoc != null)
            {
                familyDoc.Close(false);
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static View? ResolveOptionalView(UIApplication uiapp, Document doc, int? viewId, string viewName)
    {
        if (!viewId.HasValue && string.IsNullOrWhiteSpace(viewName))
        {
            return null;
        }

        if (viewId.HasValue && viewId.Value > 0)
        {
            return doc.GetElement(new ElementId((long)viewId.Value)) as View;
        }

        return new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(x => string.Equals(x.Name, viewName, StringComparison.OrdinalIgnoreCase));
    }

    private static List<int> ResolveDwgExportIds(UIApplication uiapp, Document doc, DwgExportRequest request)
    {
        var ids = new HashSet<int>((request.ViewIds ?? new List<int>()).Where(x => x > 0));
        foreach (var id in (request.SheetIds ?? new List<int>()).Where(x => x > 0))
        {
            ids.Add(id);
        }

        if (ids.Count == 0 && request.UseActiveViewWhenEmpty && uiapp.ActiveUIDocument != null && uiapp.ActiveUIDocument.Document.Equals(doc) && doc.ActiveView != null)
        {
            ids.Add(checked((int)doc.ActiveView.Id.Value));
        }

        return ids.ToList();
    }

    private static List<int> ResolveSheetIds(Document doc, IList<int> sheetIds, IList<string> sheetNumbers)
    {
        var resolved = new HashSet<int>((sheetIds ?? new List<int>()).Where(x => x > 0));
        foreach (var number in sheetNumbers ?? new List<string>())
        {
            var sheet = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .FirstOrDefault(x => string.Equals(x.SheetNumber, number, StringComparison.OrdinalIgnoreCase));
            if (sheet != null)
            {
                resolved.Add(checked((int)sheet.Id.Value));
            }
        }

        return resolved.ToList();
    }

    private static string EnsureExtension(string fileName, string extension)
    {
        return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? fileName : Path.GetFileNameWithoutExtension(fileName) + extension;
    }

    private static PDFExportOptions BuildPdfOptions(ConfiguredPdfPreset preset, string fileName, bool combineOverride)
    {
        var options = new PDFExportOptions
        {
            Combine = combineOverride,
            FileName = fileName,
            AlwaysUseRaster = false
        };

        ColorDepthType colorDepth;
        if (Enum.TryParse(preset.ColorDepth ?? "Color", true, out colorDepth))
        {
            options.ColorDepth = colorDepth;
        }

        PaperPlacementType placement;
        if (Enum.TryParse(preset.PaperPlacement ?? "Center", true, out placement))
        {
            options.PaperPlacement = placement;
        }

        return options;
    }
}

internal sealed class ResolvedDwgPreset
{
    internal string Name { get; set; } = string.Empty;
    internal DWGExportOptions Options { get; set; } = new DWGExportOptions();
}

internal sealed class FamilyLoadOptionsAdapter : IFamilyLoadOptions
{
    private readonly bool _overwriteExisting;

    internal FamilyLoadOptionsAdapter(bool overwriteExisting)
    {
        _overwriteExisting = overwriteExisting;
    }

    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
    {
        overwriteParameterValues = _overwriteExisting;
        return true;
    }

    public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
    {
        source = FamilySource.Family;
        overwriteParameterValues = _overwriteExisting;
        return true;
    }
}

[DataContract]
internal sealed class DeliveryOpsCatalog
{
    [DataMember(Order = 1)]
    public List<ConfiguredRoot> FamilyRoots { get; set; } = new List<ConfiguredRoot>();

    [DataMember(Order = 2)]
    public List<ConfiguredRoot> OutputRoots { get; set; } = new List<ConfiguredRoot>();

    [DataMember(Order = 3)]
    public List<ConfiguredIfcPreset> IfcPresets { get; set; } = new List<ConfiguredIfcPreset>();

    [DataMember(Order = 4)]
    public List<ConfiguredDwgPreset> DwgPresets { get; set; } = new List<ConfiguredDwgPreset>();

    [DataMember(Order = 5)]
    public List<ConfiguredPdfPreset> PdfPresets { get; set; } = new List<ConfiguredPdfPreset>();

    [IgnoreDataMember]
    public string Source { get; set; } = "code_default";
}

[DataContract]
internal sealed class ConfiguredRoot
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Path { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Kind { get; set; } = string.Empty;
}

[DataContract]
internal sealed class ConfiguredIfcPreset
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string FileVersion { get; set; } = "Default";

    [DataMember(Order = 4)]
    public bool ExportBaseQuantities { get; set; }

    [DataMember(Order = 5)]
    public bool WallAndColumnSplitting { get; set; } = true;

    [DataMember(Order = 6)]
    public int SpaceBoundaryLevel { get; set; } = 1;
}

[DataContract]
internal sealed class ConfiguredDwgPreset
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string NativeSetupName { get; set; } = string.Empty;
}

[DataContract]
internal sealed class ConfiguredPdfPreset
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool Combine { get; set; } = true;

    [DataMember(Order = 4)]
    public string ColorDepth { get; set; } = "Color";

    [DataMember(Order = 5)]
    public string PaperPlacement { get; set; } = "Center";
}
