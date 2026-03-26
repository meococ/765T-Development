namespace BIM765T.Revit.Contracts.Bridge;

public static class ToolNames
{
    public const string SessionListOpenDocuments = "session.list_open_documents";
    public const string SessionListTools = "session.list_tools";
    public const string SessionGetCapabilities = "session.get_capabilities";
    public const string SessionGetRecentEvents = "session.get_recent_events";
    public const string SessionGetRecentOperations = "session.get_recent_operations";
    public const string SessionGetTaskContext = "session.get_task_context";
    public const string SessionGetRuntimeHealth = "session.get_runtime_health";
    public const string SessionGetQueueState = "session.get_queue_state";

    public const string WorkerMessage = "worker.message";
    public const string WorkerGetSession = "worker.get_session";
    public const string WorkerListSessions = "worker.list_sessions";
    public const string WorkerEndSession = "worker.end_session";
    public const string WorkerSetPersona = "worker.set_persona";
    public const string WorkerListPersonas = "worker.list_personas";
    public const string WorkerGetContext = "worker.get_context";

    public const string DocumentGetActive = "document.get_active";
    public const string DocumentGetMetadata = "document.get_metadata";
    public const string DocumentGetContextFingerprint = "document.get_context_fingerprint";
    public const string DocumentOpenBackgroundRead = "document.open_background_read";
    public const string DocumentCloseNonActive = "document.close_non_active";

    public const string ViewGetActiveContext = "view.get_active_context";
    public const string ViewGetCurrentLevelContext = "view.get_current_level_context";
    public const string ViewCreate3dSafe = "view.create_3d_safe";
    public const string ViewSetCropRegionSafe = "view.set_crop_region_safe";
    public const string ViewSetViewRangeSafe = "view.set_view_range_safe";
    public const string ViewListFilters = "view.list_filters";
    public const string ViewInspectFilter = "view.inspect_filter";
    public const string ViewRemoveFilterFromViewSafe = "view.remove_filter_from_view_safe";
    public const string ViewDeleteFilterSafe = "view.delete_filter_safe";
    public const string ViewCreateOrUpdateFilterSafe = "view.create_or_update_filter_safe";
    public const string ViewApplyFilterSafe = "view.apply_filter_safe";

    public const string SelectionGet = "selection.get";

    public const string AnnotationAddTextNoteSafe = "annotation.add_text_note_safe";
    public const string AnnotationUpdateTextNoteStyleSafe = "annotation.update_text_note_style_safe";
    public const string AnnotationUpdateTextNoteContentSafe = "annotation.update_text_note_content_safe";
    public const string AnnotationListTextNoteTypes = "annotation.list_text_note_types";
    public const string AnnotationGetTextTypeUsage = "annotation.get_text_type_usage";

    public const string ElementQuery = "element.query";
    public const string ElementInspect = "element.inspect";
    public const string ElementExplain = "element.explain";
    public const string ElementGraph = "element.graph";
    public const string ElementDeleteSafe = "element.delete_safe";
    public const string ElementMoveSafe = "element.move_safe";
    public const string ElementRotateSafe = "element.rotate_safe";
    public const string ElementPlaceFamilyInstanceSafe = "element.place_family_instance_safe";
    public const string FamilyBuildRoundProjectWrappersSafe = "family.build_round_project_wrappers_safe";
    public const string FamilySyncPenetrationAlphaNestedTypesSafe = "family.sync_penetration_alpha_nested_types_safe";

    public const string TypeListElementTypes = "type.list_element_types";

    public const string ParameterSetSafe = "parameter.set_safe";
    public const string ParameterTrace = "parameter.trace";

    public const string BatchSetParameters = "batch.set_parameters";
    public const string BatchMoveElements = "batch.move_elements";
    public const string BatchAuditAxes = "batch.audit_axes";
    public const string BatchCreateRoundShadowSafe = "batch.create_round_shadow_safe";
    public const string BatchCreateRoundPenetrationCutSafe = "batch.create_round_penetration_cut_safe";
    public const string CleanupRoundShadowByRunSafe = "cleanup.round_shadow_by_run_safe";

    public const string ReportPenetrationAlphaInventory = "report.penetration_alpha_inventory";
    public const string ReportPenetrationRoundShadowPlan = "report.penetration_round_shadow_plan";
    public const string ReportRoundExternalizationPlan = "report.round_externalization_plan";
    public const string ReportRoundShadowCleanupPlan = "report.round_shadow_cleanup_plan";
    public const string ReportRoundPenetrationCutPlan = "report.round_penetration_cut_plan";
    public const string ReportRoundPenetrationCutQc = "report.round_penetration_cut_qc";
    public const string ReviewRoundPenetrationPacketSafe = "review.round_penetration_packet_safe";
    public const string ScheduleCreatePenetrationAlphaInventorySafe = "schedule.create_penetration_alpha_inventory_safe";
    public const string ScheduleCreateRoundInventorySafe = "schedule.create_round_inventory_safe";
    public const string ScheduleCompare = "schedule.compare";

    public const string ReviewModelWarnings = "review.model_warnings";
    public const string ReviewParameterCompleteness = "review.parameter_completeness";
    public const string ReviewActiveViewSummary = "review.active_view_summary";
    public const string ReviewModelHealth = "review.model_health";
    public const string ReviewLinksStatus = "review.links_status";
    public const string ReviewWorksetHealth = "review.workset_health";
    public const string ReviewSheetSummary = "review.sheet_summary";
    public const string ReviewCaptureSnapshot = "review.capture_snapshot";
    public const string ReviewFixCandidates = "review.fix_candidates";
    public const string ReviewCadGenericModelOverlap = "review.cad_generic_model_overlap";
    public const string ReviewFamilyAxisAlignment = "review.family_axis_alignment";
    public const string ReviewFamilyAxisAlignmentGlobal = "review.family_axis_alignment_global";
    public const string ReviewRunRuleSet = "review.run_rule_set";

    public const string FileSaveDocument = "file.save_document";
    public const string FileSaveAsDocument = "file.save_as_document";

    public const string WorksharingSynchronizeWithCentral = "worksharing.synchronize_with_central";

    public const string DomainHullDryRun = "domain.hull_dry_run";
    public const string ViewUsage = "view.usage";
    public const string SheetDependencies = "sheet.dependencies";
    public const string WorkflowList = "workflow.list";
    public const string WorkflowPlan = "workflow.plan";
    public const string WorkflowApply = "workflow.apply";
    public const string WorkflowResume = "workflow.resume";
    public const string WorkflowGetRun = "workflow.get_run";
    public const string WorkflowFixLoopPlan = "workflow.fix_loop_plan";
    public const string WorkflowFixLoopApply = "workflow.fix_loop_apply";
    public const string WorkflowFixLoopVerify = "workflow.fix_loop_verify";
    public const string TaskPlan = "task.plan";
    public const string TaskPreview = "task.preview";
    public const string TaskApproveStep = "task.approve_step";
    public const string TaskExecuteStep = "task.execute_step";
    public const string TaskResume = "task.resume";
    public const string TaskVerify = "task.verify";
    public const string TaskGetRun = "task.get_run";
    public const string TaskListRuns = "task.list_runs";
    public const string TaskSummarize = "task.summarize";
    public const string TaskPromoteMemorySafe = "task.promote_memory_safe";
    public const string TaskGetMetrics = "task.get_metrics";
    public const string TaskGetResiduals = "task.get_residuals";
    public const string TaskIntakeExternal = "task.intake_external";
    public const string TaskEnqueueApproved = "task.enqueue_approved";
    public const string TaskListQueue = "task.list_queue";
    public const string TaskClaimQueueItem = "task.claim_queue_item";
    public const string TaskCompleteQueueItem = "task.complete_queue_item";
    public const string TaskRunQueueItem = "task.run_queue_item";
    public const string TaskBuildCallbackPreview = "task.build_callback_preview";
    public const string ContextGetHotState = "context.get_hot_state";
    public const string ContextGetDeltaSummary = "context.get_delta_summary";
    public const string ContextResolveBundle = "context.resolve_bundle";
    public const string ContextSearchAnchors = "context.search_anchors";
    public const string ArtifactSummarize = "artifact.summarize";
    public const string MemoryFindSimilarRuns = "memory.find_similar_runs";
    public const string ToolFindByCapability = "tool.find_by_capability";
    public const string ToolGetGuidance = "tool.get_guidance";
    public const string WorkspaceGetManifest = "workspace.get_manifest";
    public const string PackList = "pack.list";
    public const string StandardsResolve = "standards.resolve";
    public const string PlaybookMatch = "playbook.match";
    public const string PlaybookPreview = "playbook.preview";
    public const string ProjectDeepScan = "project.deep_scan";
    public const string ProjectGetDeepScan = "project.get_deep_scan";
    public const string FamilyXray = "family.xray";
    public const string SheetCaptureIntelligence = "sheet.capture_intelligence";

    public const string FamilyListLibraryRoots = "family.list_library_roots";
    public const string FamilyLoadSafe = "family.load_safe";
    public const string ScheduleCreateSafe = "schedule.create_safe";
    public const string SchedulePreviewCreate = "schedule.preview_create";
    public const string ExportIfcSafe = "export.ifc_safe";
    public const string ExportDwgSafe = "export.dwg_safe";
    public const string ExportListPresets = "export.list_presets";
    public const string SheetPrintPdfSafe = "sheet.print_pdf_safe";
    public const string StorageValidateOutputTarget = "storage.validate_output_target";
    public const string PolicyResolve = "policy.resolve";
    public const string SpecialistResolve = "specialist.resolve";
    public const string IntentCompile = "intent.compile";
    public const string IntentValidate = "intent.validate";
    public const string SystemCaptureGraph = "system.capture_graph";
    public const string SystemPlanConnectivityFix = "system.plan_connectivity_fix";
    public const string SystemPlanSlopeRemediation = "system.plan_slope_remediation";
    public const string IntegrationPreviewSync = "integration.preview_sync";
    public const string CommandSearch = "command.search";
    public const string CommandDescribe = "command.describe";
    public const string CommandExecuteSafe = "command.execute_safe";
    public const string CommandCoverageReport = "command.coverage_report";
    public const string FallbackArtifactPlan = "fallback.artifact_plan";
    public const string ScriptImportManifest = "script.import_manifest";
    public const string ScriptVerifySource = "script.verify_source";
    public const string ScriptInstallPack = "script.install_pack";
    public const string MemorySearchScoped = "memory.search_scoped";
    public const string MemoryPromoteVerifiedRun = "memory.promote_verified_run";
    public const string WorkflowQuickPlan = "workflow.quick_plan";

    // ── Phase 1A: Sheet & View Management ──
    public const string SheetListAll = "sheet.list_all";
    public const string SheetGetViewportLayout = "sheet.get_viewport_layout";
    public const string SheetCreateSafe = "sheet.create_safe";
    public const string SheetRenumberSafe = "sheet.renumber_safe";
    public const string SheetPlaceViewsSafe = "sheet.place_views_safe";
    public const string ViewDuplicateSafe = "view.duplicate_safe";
    public const string ViewCreateProjectViewSafe = "view.create_project_view_safe";
    public const string ViewSetTemplateSafe = "view.set_template_safe";
    public const string ViewListTemplates = "view.list_templates";
    public const string ViewportAlignSafe = "viewport.align_safe";

    // ── Phase 2: Smart View Template & Sheet Analysis ──
    public const string AuditTemplateHealth = "audit.template_health";
    public const string AuditSheetOrganization = "audit.sheet_organization";
    public const string AuditTemplateSheetMap = "audit.template_sheet_map";
    public const string ViewTemplateInspect = "view.template_inspect";
    public const string SheetGroupSummary = "sheet.group_summary";
    public const string AuditViewTemplateCompliance = "audit.view_template_compliance";

    // ── Phase 1B: Parameter & Data Management ──
    public const string ParameterListShared = "parameter.list_shared";
    public const string ParameterCopyBetweenSafe = "parameter.copy_between_safe";
    public const string ParameterAddSharedSafe = "parameter.add_shared_safe";
    public const string ParameterBatchFillSafe = "parameter.batch_fill_safe";
    public const string DataExport = "data.export";
    public const string DataExportSchedule = "data.export_schedule";
    public const string DataPreviewImport = "data.preview_import";
    public const string DataImportSafe = "data.import_safe";
    public const string DataExtractScheduleStructured = "data.extract_schedule_structured";
    public const string ReviewSmartQc = "review.smart_qc";

    // ── Phase 1C: QC & Model Audit ──
    public const string AuditNamingConvention = "audit.naming_convention";
    public const string AuditUnusedViews = "audit.unused_views";
    public const string AuditUnusedFamilies = "audit.unused_families";
    public const string AuditDuplicateElements = "audit.duplicate_elements";
    public const string AuditWarningsCleanupPlan = "audit.warnings_cleanup_plan";
    public const string AuditModelStandards = "audit.model_standards";
    public const string AuditPurgeUnusedSafe = "audit.purge_unused_safe";
    public const string AuditComplianceReport = "audit.compliance_report";

    // ── Phase A: Query Performance ──
    public const string QueryQuickFilter = "query.quick_filter";
    public const string QueryParameterFilter = "query.parameter_filter";
    public const string QueryLogicalCompound = "query.logical_compound";
    public const string QuerySpatialPrescreen = "query.spatial_prescreen";
    public const string CacheElementIndex = "cache.element_index";
    public const string QueryMultiCategory = "query.multi_category";
    public const string QueryElementCount = "query.element_count";
    public const string QueryBatchInspect = "query.batch_inspect";

    // ── Phase B: Spatial Intelligence ──
    public const string SpatialClashDetect = "spatial.clash_detect";
    public const string SpatialProximitySearch = "spatial.proximity_search";
    public const string SpatialRaycast = "spatial.raycast";
    public const string SpatialGeometryExtract = "spatial.geometry_extract";
    public const string SpatialSectionBoxFromElements = "spatial.section_box_from_elements";
    public const string SpatialZoneSummary = "spatial.zone_summary";
    public const string SpatialElementDistances = "spatial.element_distances";
    public const string SpatialLevelZoneAnalysis = "spatial.level_zone_analysis";
    public const string SpatialOpeningDetect = "spatial.opening_detect";
    public const string DirectShapeCreateSafe = "directshape.create_safe";

    // ── Family Authoring ──
    public const string FamilyAddParameterSafe = "family.add_parameter_safe";
    public const string FamilySetParameterFormulaSafe = "family.set_parameter_formula_safe";
    public const string FamilySetTypeCatalogSafe = "family.set_type_catalog_safe";
    public const string FamilyListGeometry = "family.list_geometry";
    public const string FamilyCreateExtrusionSafe = "family.create_extrusion_safe";
    public const string FamilyCreateSweepSafe = "family.create_sweep_safe";
    public const string FamilyCreateBlendSafe = "family.create_blend_safe";
    public const string FamilyCreateRevolutionSafe = "family.create_revolution_safe";
    public const string FamilyAddReferencePlaneSafe = "family.add_reference_plane_safe";
    public const string FamilySetSubcategorySafe = "family.set_subcategory_safe";
    public const string FamilyLoadNestedSafe = "family.load_nested_safe";
    public const string FamilySaveSafe = "family.save_safe";

    // ── Family Authoring Tier 1: Dimension, Alignment, Connector ──
    public const string FamilyAddDimensionSafe = "family.add_dimension_safe";
    public const string FamilyAddAlignmentSafe = "family.add_alignment_safe";
    public const string FamilyAddConnectorSafe = "family.add_connector_safe";

    // ── Family Authoring Tier 2: Visibility, Subcategory, Material ──
    public const string FamilySetVisibilitySafe = "family.set_visibility_safe";
    public const string FamilyCreateSubcategorySafe = "family.create_subcategory_safe";
    public const string FamilyBindMaterialSafe = "family.bind_material_safe";
    public const string FamilySetParameterVisibilitySafe = "family.set_parameter_visibility_safe";

    // ── Family Authoring Tier 3: Spline, SharedParam, Category ──
    public const string FamilyCreateSplineExtrusionSafe = "family.create_spline_extrusion_safe";
    public const string FamilyAddSharedParameterSafe = "family.add_shared_parameter_safe";
    public const string FamilySetCategorySafe = "family.set_category_safe";
    public const string FamilyCreateDocumentSafe = "family.create_document_safe";

    // ── Script Orchestration ──
    public const string ScriptList = "script.list";
    public const string ScriptValidate = "script.validate";
    public const string ScriptRunSafe = "script.run_safe";
    public const string ScriptGetRun = "script.get_run";
    public const string ScriptComposeSafe = "script.compose_safe";

    // ── Workset Operations ──
    public const string WorksetCreateSafe = "workset.create_safe";
    public const string WorksetBulkReassignElementsSafe = "workset.bulk_reassign_elements_safe";
    public const string WorksetOpenCloseSafe = "workset.open_close_safe";

    // ── Revision Operations ──
    public const string RevisionCreateSafe = "revision.create_safe";
    public const string RevisionList = "revision.list";

    // Project init / context engine
    public const string ProjectInitPreview = "project.init_preview";
    public const string ProjectInitApply = "project.init_apply";
    public const string ProjectGetManifest = "project.get_manifest";
    public const string ProjectGetContextBundle = "project.get_context_bundle";
}
