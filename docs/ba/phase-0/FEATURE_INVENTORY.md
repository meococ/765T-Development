# Feature Inventory Matrix — BIM765T Revit Agent

| Field | Value |
|-------|-------|
| **Purpose** | Baseline inventory for BA scope decisions; maps tool surface to current state and BA relevance. |
| **Inputs** | `ToolNames.cs`, `*ToolModule.cs`, `docs/assistant/USE_CASE_MATRIX.md`, current README/current-truth docs. |
| **Outputs** | Per-tool current-state classification and BA relevance mapping for MVP / pilot / later planning. |
| **Status** | Pass 1 baseline complete; Pass 2 validation of real usage still pending. |
| **Owner** | Product + Engineering |
| **Source refs** | `docs/ba/phase-0/SOURCE_OF_TRUTH_MAP.md`, `docs/assistant/BASELINE.md`, `docs/ARCHITECTURE.md`. |
| **Last updated** | 2026-03-24 |

## Classification Rules

- `shipped` = tool is registered and currently available in active runtime/read path; not a promise of production-hardening for every edge case.
- `partial` = tool exists or is exposed, but still needs UX hardening, policy tightening, evidence, or broader verification before MVP claim.
- `stub` = named in surface or design but not actually wired/registered for reliable use.
- `vision` is reserved for capabilities described in source docs but not represented in the live tool inventory.

## Summary Stats

### Total Tools: 245

### Count by Category

| Category | Count |
|----------|-------|
| Session & Context | 18 |
| Query & Element | 18 |
| Parameter & Data | 16 |
| Mutation & File | 12 |
| Family Authoring | 23 |
| Sheet & View | 23 |
| Annotation & Type | 5 |
| Review & QC | 25 |
| Workflow & Task | 28 |
| Intelligence & Copilot | 24 |
| Integration & Platform | 16 |
| Command & Standards | 8 |
| Hull & Penetration | 14 |
| Spatial Intelligence | 10 |
| Script Orchestration | 5 |
| **Total** | **245** |

### Count by Current State

| Current State | Count |
|---------------|-------|
| shipped | 86 |
| partial | 149 |
| stub | 10 |

### Count by BA Relevance

| BA Relevance | Count |
|--------------|-------|
| MVP | 66 |
| pilot | 120 |
| later | 59 |
| out | 0 |

## Main Table

### 1. Session & Context

Tools for session state, document context, and runtime health.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `session.list_open_documents` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Lists all open Revit docs |
| 2 | `session.list_tools` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Tool catalog for agent bootstrap |
| 3 | `session.get_capabilities` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Runtime capabilities summary |
| 4 | `session.get_recent_events` | SessionDocumentToolModule | ✅ Registered | shipped | pilot | P1 | Event tracking from EventIndex |
| 5 | `session.get_recent_operations` | SessionDocumentToolModule | ✅ Registered | shipped | pilot | P1 | Operation journal entries |
| 6 | `session.get_task_context` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Bundle: doc + view + selection + history + capabilities |
| 7 | `session.get_runtime_health` | CopilotTaskToolModule | ✅ Registered | shipped | pilot | P1 | Copilot runtime health, queue, task kinds |
| 8 | `session.get_queue_state` | CopilotTaskToolModule | ✅ Registered | shipped | pilot | P1 | Pending queue depth + active invocation |
| 9 | `document.get_active` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Active document summary |
| 10 | `document.get_metadata` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Document metadata by target |
| 11 | `document.get_context_fingerprint` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Context fingerprint for confirmable ops |
| 12 | `document.open_background_read` | SessionDocumentToolModule | ✅ Registered | shipped | pilot | P1 | Open non-active doc in read-first mode |
| 13 | `document.close_non_active` | SessionDocumentToolModule | ✅ Registered | shipped | later | P2 | Close non-active doc with confirm token |
| 14 | `context.get_hot_state` | CopilotTaskToolModule | ✅ Registered | shipped | MVP | P0 | Hot context: task, queue, doc state graph |
| 15 | `context.get_delta_summary` | CopilotTaskToolModule | ✅ Registered | shipped | pilot | P1 | Document delta + recovery suggestions |
| 16 | `context.resolve_bundle` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Bounded context bundle hot/warm/cold tiers |
| 17 | `context.search_anchors` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Search durable task/memory anchors |
| 18 | `artifact.summarize` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Summarize a local artifact/file |

### 2. Query & Element

Tools for querying, inspecting, and explaining Revit elements.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `element.query` | ElementAndReviewToolModule | ✅ Registered | shipped | MVP | P0 | Query by scope/category/class/id |
| 2 | `element.inspect` | ElementAndReviewToolModule | ✅ Registered | shipped | MVP | P0 | Inspect elements with parameters |
| 3 | `element.explain` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | Explain element: host, owner, dependents |
| 4 | `element.graph` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | Dependency graph for elements |
| 5 | `selection.get` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Current selection summary |
| 6 | `view.get_active_context` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Active view context |
| 7 | `view.get_current_level_context` | SessionDocumentToolModule | ✅ Registered | shipped | MVP | P0 | Current level context |
| 8 | `type.list_element_types` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | List element types with usage counts |
| 9 | `query.quick_filter` | QueryPerformanceToolModule | ✅ Registered | shipped | MVP | P0 | High-perf native quick filter (10-100x faster) |
| 10 | `query.parameter_filter` | QueryPerformanceToolModule | ✅ Registered | shipped | MVP | P0 | Server-side parameter value filter |
| 11 | `query.logical_compound` | QueryPerformanceToolModule | ✅ Registered | partial | pilot | P1 | Compound AND/OR filter trees |
| 12 | `query.spatial_prescreen` | QueryPerformanceToolModule | ✅ Registered | partial | pilot | P1 | Spatial region element lookup |
| 13 | `cache.element_index` | QueryPerformanceToolModule | ✅ Registered | partial | pilot | P1 | Element index cache management |
| 14 | `query.multi_category` | QueryPerformanceToolModule | ✅ Registered | partial | pilot | P1 | Multi-category batch query |
| 15 | `query.element_count` | QueryPerformanceToolModule | ✅ Registered | shipped | MVP | P0 | Fast element count without loading |
| 16 | `query.batch_inspect` | QueryPerformanceToolModule | ✅ Registered | partial | pilot | P1 | Batch inspect with column selection |
| 17 | `view.usage` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | How a view is used: filters, sheets, samples |
| 18 | `sheet.dependencies` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | Sheet dependency tree |

### 3. Parameter & Data

Tools for parameter management and data import/export.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `parameter.set_safe` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Set parameter values (dry-run + confirm) |
| 2 | `parameter.trace` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | Trace parameter values across elements |
| 3 | `parameter.list_shared` | ParameterToolModule | ✅ Registered | partial | pilot | P1 | List shared/project parameters |
| 4 | `parameter.copy_between_safe` | ParameterToolModule | ✅ Registered | partial | pilot | P1 | Copy params between elements |
| 5 | `parameter.add_shared_safe` | ParameterToolModule | ✅ Registered | partial | pilot | P1 | Add new shared parameter to categories |
| 6 | `parameter.batch_fill_safe` | ParameterToolModule | ✅ Registered | partial | pilot | P1 | Batch fill parameter on multiple elements |
| 7 | `batch.set_parameters` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Batch-safe alias for parameter set |
| 8 | `data.export` | DataLifecycleToolModule | ✅ Registered | partial | pilot | P1 | Export element params to JSON/CSV |
| 9 | `data.export_schedule` | DataLifecycleToolModule | ✅ Registered | partial | pilot | P1 | Export schedule table to JSON |
| 10 | `data.preview_import` | DataLifecycleToolModule | ✅ Registered | partial | pilot | P1 | Preview import file without changes |
| 11 | `data.import_safe` | DataLifecycleToolModule | ✅ Registered | partial | pilot | P1 | Import data and set params (high-risk) |
| 12 | `data.extract_schedule_structured` | DataLifecycleToolModule | ✅ Registered | shipped | pilot | P1 | Structured schedule extraction for AI |
| 13 | `schedule.create_safe` | DeliveryOpsToolModule | ✅ Registered | partial | pilot | P1 | Create generic model schedule |
| 14 | `schedule.preview_create` | DeliveryOpsToolModule | ✅ Registered | partial | pilot | P1 | Preview schedule definition |
| 15 | `schedule.compare` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered in any module |
| 16 | `batch.audit_axes` | ElementAndReviewToolModule | ✅ Registered | partial | pilot | P1 | Batch alias for family axis audit |

### 4. Mutation & File

Tools for element mutation, file operations, and worksharing.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `element.delete_safe` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Delete with dependency preview |
| 2 | `element.move_safe` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Move elements (dry-run + confirm) |
| 3 | `element.rotate_safe` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Rotate elements (dry-run + confirm) |
| 4 | `element.place_family_instance_safe` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Place family instance (high-risk) |
| 5 | `batch.move_elements` | MutationFileAndDomainToolModule | ✅ Registered | partial | pilot | P1 | Batch-safe alias for move |
| 6 | `file.save_document` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Save current document |
| 7 | `file.save_as_document` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Save document as new file |
| 8 | `worksharing.synchronize_with_central` | MutationFileAndDomainToolModule | ✅ Registered | shipped | MVP | P0 | Sync with central |
| 9 | `domain.hull_dry_run` | MutationFileAndDomainToolModule | ✅ Registered | partial | later | P2 | Sample domain module: Hull dry-run |
| 10 | `export.ifc_safe` | DeliveryOpsToolModule | ✅ Registered | shipped | MVP | P0 | Export IFC (preset + allowlisted root) |
| 11 | `export.dwg_safe` | DeliveryOpsToolModule | ✅ Registered | shipped | MVP | P0 | Export DWG (preset + allowlisted root) |
| 12 | `export.list_presets` | DeliveryOpsToolModule | ✅ Registered | partial | pilot | P1 | List delivery presets/output roots |

### 5. Family Authoring

Tools for family document creation, geometry, parameters, and constraints.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `family.list_geometry` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | List forms, ref planes, params, types |
| 2 | `family.add_parameter_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Add family parameter |
| 3 | `family.set_parameter_formula_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Set/update parameter formula |
| 4 | `family.set_type_catalog_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Type catalog builder |
| 5 | `family.create_extrusion_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Create extrusion (solid/void) |
| 6 | `family.create_sweep_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Create sweep from profile + path |
| 7 | `family.create_blend_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Create blend from bottom/top profiles |
| 8 | `family.create_revolution_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Create revolution from profile + axis |
| 9 | `family.add_reference_plane_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Add named reference plane |
| 10 | `family.set_subcategory_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Assign subcategory to geometry form |
| 11 | `family.load_nested_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Load nested .rfa into family doc |
| 12 | `family.save_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Save/Save-As family document |
| 13 | `family.add_dimension_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Dimension between ref planes + label |
| 14 | `family.add_alignment_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Alignment constraint (ref plane ↔ face) |
| 15 | `family.add_connector_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | pilot | P1 | Add MEP connector to geometry form |
| 16 | `family.set_visibility_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | later | P2 | Set visibility per detail level/direction |
| 17 | `family.create_subcategory_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | later | P2 | Create subcategory with line/color/material |
| 18 | `family.bind_material_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | later | P2 | Bind material parameter to geometry form |
| 19 | `family.set_parameter_visibility_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | later | P2 | Conditional visibility via Yes/No param |
| 20 | `family.create_spline_extrusion_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | later | P2 | Spline extrusion (Hermite/NURBS) |
| 21 | `family.add_shared_parameter_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | later | P2 | Shared parameter from .txt file |
| 22 | `family.set_category_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | later | P2 | Set/change family category |
| 23 | `family.create_document_safe` | FamilyAuthoringToolModule | ✅ Registered | partial | later | P2 | Create new family doc from .rft template |

### 6. Sheet & View

Tools for sheet management, view creation, templates, and viewport alignment.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `sheet.list_all` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | List all sheets with viewport details |
| 2 | `sheet.get_viewport_layout` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | Viewport positions/view names for a sheet |
| 3 | `sheet.create_safe` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | Create sheet with title block |
| 4 | `sheet.renumber_safe` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | Renumber/rename sheet |
| 5 | `sheet.place_views_safe` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | Place views on sheet at positions |
| 6 | `sheet.print_pdf_safe` | DeliveryOpsToolModule | ✅ Registered | shipped | MVP | P0 | Export sheets to PDF |
| 7 | `sheet.group_summary` | SheetViewToolModule | ✅ Registered | partial | pilot | P1 | Sheet group detail by prefix/pattern |
| 8 | `sheet.capture_intelligence` | IntelligenceToolModule | ✅ Registered | shipped | pilot | P1 | Capture sheet intelligence (title block, viewports, notes) |
| 9 | `view.create_3d_safe` | ViewAnnotationAndTypeToolModule | ✅ Registered | shipped | MVP | P0 | Create 3D view |
| 10 | `view.set_crop_region_safe` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered |
| 11 | `view.set_view_range_safe` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered |
| 12 | `view.list_filters` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | List all ParameterFilterElements |
| 13 | `view.inspect_filter` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | Full detail of a single filter |
| 14 | `view.remove_filter_from_view_safe` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | Remove filter from view |
| 15 | `view.delete_filter_safe` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | Delete ParameterFilterElement |
| 16 | `view.create_or_update_filter_safe` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | Create/update parameter filter |
| 17 | `view.apply_filter_safe` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | Apply filter to view with overrides |
| 18 | `view.duplicate_safe` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | Duplicate view (Duplicate/WithDetailing/AsDependent) |
| 19 | `view.create_project_view_safe` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | Create floor/ceiling/engineering plan |
| 20 | `view.set_template_safe` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | Apply/remove view template |
| 21 | `view.list_templates` | SheetViewToolModule | ✅ Registered | partial | pilot | P1 | List view templates with usage counts |
| 22 | `view.template_inspect` | SheetViewToolModule | ✅ Registered | partial | pilot | P1 | Deep inspect view template |
| 23 | `viewport.align_safe` | SheetViewToolModule | ✅ Registered | shipped | MVP | P0 | Align viewports on sheet |

### 7. Annotation & Type

Tools for text notes, annotation types, and element types.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `annotation.add_text_note_safe` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | Add TextNote to view |
| 2 | `annotation.update_text_note_style_safe` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | Duplicate/reuse type, resize, recolor |
| 3 | `annotation.update_text_note_content_safe` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | Update text note content |
| 4 | `annotation.list_text_note_types` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | List TextNoteType definitions |
| 5 | `annotation.get_text_type_usage` | ViewAnnotationAndTypeToolModule | ✅ Registered | partial | pilot | P1 | TextNoteType usage counts |

### 8. Review & QC

Tools for model review, audit, compliance, and quality control.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `review.model_warnings` | ElementAndReviewToolModule | ✅ Registered | shipped | MVP | P0 | Review current model warnings |
| 2 | `review.parameter_completeness` | ElementAndReviewToolModule | ✅ Registered | shipped | MVP | P0 | Review missing/empty parameters |
| 3 | `review.active_view_summary` | ElementAndReviewToolModule | ✅ Registered | shipped | MVP | P0 | Active view with category/class counts |
| 4 | `review.model_health` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Model health, warnings, links, activity |
| 5 | `review.links_status` | ElementAndReviewToolModule | ✅ Registered | shipped | MVP | P0 | Revit link load status |
| 6 | `review.workset_health` | ElementAndReviewToolModule | ✅ Registered | shipped | MVP | P0 | Workset hygiene and common mistakes |
| 7 | `review.sheet_summary` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Sheet review: title block, views, schedules |
| 8 | `review.capture_snapshot` | ElementAndReviewToolModule | ✅ Registered | shipped | MVP | P0 | Capture structured snapshot + optional image |
| 9 | `review.fix_candidates` | FixLoopToolModule | ✅ Registered | shipped | MVP | P0 | Supervised fix candidates for hygiene/cleanup |
| 10 | `review.family_axis_alignment` | ElementAndReviewToolModule | ✅ Registered | partial | pilot | P1 | Family axis alignment check (active view) |
| 11 | `review.family_axis_alignment_global` | ElementAndReviewToolModule | ✅ Registered | partial | pilot | P1 | Family axis alignment check (document-wide) |
| 12 | `review.run_rule_set` | AuditCenterToolModule | ✅ Registered | partial | pilot | P1 | Run review rule engine |
| 13 | `review.smart_qc` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Aggregated smart QC findings |
| 14 | `audit.template_health` | AuditCenterToolModule | ✅ Registered | partial | pilot | P1 | View template health analysis (A-F) |
| 15 | `audit.sheet_organization` | AuditCenterToolModule | ✅ Registered | partial | pilot | P1 | Sheet organization analysis (A-F) |
| 16 | `audit.template_sheet_map` | AuditCenterToolModule | ✅ Registered | partial | pilot | P1 | Template ↔ sheet mapping |
| 17 | `audit.view_template_compliance` | AuditCenterToolModule | ✅ Registered | partial | pilot | P1 | View template compliance check |
| 18 | `audit.naming_convention` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Naming convention audit |
| 19 | `audit.unused_views` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Find views not on any sheet |
| 20 | `audit.unused_families` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Find families with zero instances |
| 21 | `audit.duplicate_elements` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Detect overlapping/duplicate elements |
| 22 | `audit.warnings_cleanup_plan` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Categorize warnings + suggest cleanup |
| 23 | `audit.model_standards` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | BIM standards check |
| 24 | `audit.purge_unused_safe` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Purge unused views/families (high-risk) |
| 25 | `audit.compliance_report` | AuditCenterToolModule | ✅ Registered | shipped | MVP | P0 | Comprehensive compliance report |

### 9. Workflow & Task

Tools for workflow orchestration, fix loops, and durable task management.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `workflow.list` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | List built-in BIM workflows |
| 2 | `workflow.plan` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | Create workflow plan artifact |
| 3 | `workflow.apply` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | Apply planned workflow run |
| 4 | `workflow.resume` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | Resume/retry from checkpoint |
| 5 | `workflow.get_run` | WorkflowInspectorToolModule | ✅ Registered | partial | pilot | P1 | Get workflow run artifact |
| 6 | `workflow.fix_loop_plan` | FixLoopToolModule | ✅ Registered | shipped | MVP | P0 | Plan supervised fix-loop |
| 7 | `workflow.fix_loop_apply` | FixLoopToolModule | ✅ Registered | shipped | MVP | P0 | Apply fix-loop actions (high-risk) |
| 8 | `workflow.fix_loop_verify` | FixLoopToolModule | ✅ Registered | shipped | MVP | P0 | Verify fix-loop results |
| 9 | `workflow.quick_plan` | CommandAtlasToolModule | ✅ Registered | shipped | MVP | P0 | Quick command/workflow with auto-fill |
| 10 | `task.plan` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Plan durable copilot task run |
| 11 | `task.preview` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Hydrate task preview for approval |
| 12 | `task.approve_step` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Record operator approval for task step |
| 13 | `task.execute_step` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Execute next approved task step |
| 14 | `task.resume` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Resume paused durable task run |
| 15 | `task.verify` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Verify task run + persist residuals |
| 16 | `task.get_run` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Get task run with approval state |
| 17 | `task.list_runs` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | List durable task runs |
| 18 | `task.summarize` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Compact task summary for agent loops |
| 19 | `task.promote_memory_safe` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Promote verified run to memory |
| 20 | `task.get_metrics` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Aggregate task metrics |
| 21 | `task.get_residuals` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Get residual summary |
| 22 | `task.intake_external` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Normalize external connector task |
| 23 | `task.enqueue_approved` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Queue approved task for off-hours |
| 24 | `task.list_queue` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | List queued task items |
| 25 | `task.claim_queue_item` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Lease next ready queue item |
| 26 | `task.complete_queue_item` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Mark queue item complete/failed |
| 27 | `task.run_queue_item` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Execute leased queue item end-to-end |
| 28 | `task.build_callback_preview` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Build connector callback payload preview |

### 10. Intelligence & Copilot

Tools for AI reasoning, memory, tool guidance, workspace, playbooks, and governance.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `worker.message` | WorkerToolModule | ✅ Registered | shipped | MVP | P0 | NL message → Worker orchestration lane |
| 2 | `worker.get_session` | WorkerToolModule | ✅ Registered | shipped | pilot | P1 | Worker session state + conversation |
| 3 | `worker.list_sessions` | WorkerToolModule | ✅ Registered | shipped | pilot | P1 | List recent worker sessions |
| 4 | `worker.end_session` | WorkerToolModule | ✅ Registered | shipped | pilot | P1 | End worker session |
| 5 | `worker.set_persona` | WorkerToolModule | ✅ Registered | shipped | later | P2 | Set active worker persona |
| 6 | `worker.list_personas` | WorkerToolModule | ✅ Registered | shipped | later | P2 | List available worker personas |
| 7 | `worker.get_context` | WorkerToolModule | ✅ Registered | shipped | pilot | P1 | Get worker context |
| 8 | `memory.find_similar_runs` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Find similar task runs for reuse |
| 9 | `memory.search_scoped` | CommandAtlasToolModule | ✅ Registered | partial | later | P2 | Scoped memory search (atlas/playbook/run) |
| 10 | `memory.promote_verified_run` | CommandAtlasToolModule | ✅ Registered | partial | later | P2 | Promote verified run to lesson/evidence |
| 11 | `tool.find_by_capability` | CopilotTaskToolModule | ✅ Registered | shipped | MVP | P0 | Find tools by capability keywords |
| 12 | `tool.get_guidance` | CopilotTaskToolModule | ✅ Registered | shipped | MVP | P0 | Curated tool guidance with risk/cost |
| 13 | `workspace.get_manifest` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Workspace manifest + enabled packs |
| 14 | `pack.list` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | List pack manifests |
| 15 | `standards.resolve` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Resolve machine-readable team standards |
| 16 | `playbook.match` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Match playbook against task description |
| 17 | `playbook.preview` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Preview playbook with resolved tool chain |
| 18 | `policy.resolve` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Resolve capability policy packs |
| 19 | `specialist.resolve` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Resolve best specialist agents |
| 20 | `intent.compile` | CopilotTaskToolModule | ✅ Registered | partial | pilot | P1 | Compile NL task → capability plan |
| 21 | `intent.validate` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Validate compiled capability plan |
| 22 | `family.xray` | IntelligenceToolModule | ✅ Registered | shipped | pilot | P1 | Deep family inspection (types, nested, connectors) |
| 23 | `project.deep_scan` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered in bridge modules |
| 24 | `project.get_deep_scan` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered in bridge modules |

### 11. Integration & Platform

Tools for system analysis, external integration, delivery, and storage.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `system.capture_graph` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Scaffold system graph for connectivity/slope |
| 2 | `system.plan_connectivity_fix` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Policy-backed scaffold fix plan |
| 3 | `system.plan_slope_remediation` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Scaffold slope remediation plan |
| 4 | `integration.preview_sync` | CopilotTaskToolModule | ✅ Registered | partial | later | P2 | Preview integration deltas (Excel/PDF/CAD/BOQ/4D5D) |
| 5 | `family.list_library_roots` | DeliveryOpsToolModule | ✅ Registered | partial | pilot | P1 | List allowlisted family library roots |
| 6 | `family.load_safe` | DeliveryOpsToolModule | ✅ Registered | partial | pilot | P1 | Load family from allowlisted root |
| 7 | `storage.validate_output_target` | DeliveryOpsToolModule | ✅ Registered | partial | pilot | P1 | Validate output target path |
| 8 | `project.init_preview` | CopilotTaskToolModule | ✅ Registered | shipped | later | P2 | Preview BIM /init workspace discovery |
| 9 | `project.init_apply` | CopilotTaskToolModule | ✅ Registered | shipped | later | P2 | Apply BIM /init workspace + manifest |
| 10 | `project.get_manifest` | CopilotTaskToolModule | ✅ Registered | shipped | later | P2 | Get project-init manifest |
| 11 | `project.get_context_bundle` | CopilotTaskToolModule | ✅ Registered | shipped | later | P2 | Compact project context bundle |
| 12 | `workset.create_safe` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered |
| 13 | `workset.bulk_reassign_elements_safe` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered |
| 14 | `workset.open_close_safe` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered |
| 15 | `revision.create_safe` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered |
| 16 | `revision.list` | — | ❌ Stub | stub | later | P2 | In ToolNames.cs, not registered |

### 12. Command & Standards

Tools for command atlas, curated scripts, and fallback artifacts.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `command.search` | CommandAtlasToolModule | ✅ Registered | shipped | MVP | P0 | Search curated command atlas |
| 2 | `command.describe` | CommandAtlasToolModule | ✅ Registered | shipped | MVP | P0 | Describe atlas command entry |
| 3 | `command.execute_safe` | CommandAtlasToolModule | ✅ Registered | shipped | MVP | P0 | Execute quick atlas action safely |
| 4 | `command.coverage_report` | CommandAtlasToolModule | ✅ Registered | partial | pilot | P1 | Atlas coverage report |
| 5 | `fallback.artifact_plan` | CommandAtlasToolModule | ✅ Registered | partial | later | P2 | Fallback artifact proposal |
| 6 | `script.import_manifest` | CommandAtlasToolModule | ✅ Registered | partial | later | P2 | Import curated script manifest |
| 7 | `script.verify_source` | CommandAtlasToolModule | ✅ Registered | partial | later | P2 | Verify script provenance/approval |
| 8 | `script.install_pack` | CommandAtlasToolModule | ✅ Registered | partial | later | P2 | Install curated script pack |

### 13. Hull & Penetration

Tools for penetration workflow, round shadow management, and inventory.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `report.penetration_alpha_inventory` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Inventory Penetration Alpha instances |
| 2 | `report.penetration_round_shadow_plan` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Plan Round shadow for each Penetration Alpha |
| 3 | `report.round_externalization_plan` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Plan Round externalization (nested → project-level) |
| 4 | `report.round_shadow_cleanup_plan` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Report cleanup candidates for Round shadows |
| 5 | `report.round_penetration_cut_plan` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Plan round penetration openings |
| 6 | `report.round_penetration_cut_qc` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | QC round penetration opening workflow |
| 7 | `review.round_penetration_packet_safe` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Create 3D review views for round penetrations |
| 8 | `family.build_round_project_wrappers_safe` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Build/load clean Round project family |
| 9 | `family.sync_penetration_alpha_nested_types_safe` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Sync nested Penetration Alpha types |
| 10 | `schedule.create_penetration_alpha_inventory_safe` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Create Penetration Alpha inventory schedule |
| 11 | `schedule.create_round_inventory_safe` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Create Round inventory schedule |
| 12 | `batch.create_round_shadow_safe` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Create aligned Round shadow instances |
| 13 | `batch.create_round_penetration_cut_safe` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Build/place round penetration openings + void cuts |
| 14 | `cleanup.round_shadow_by_run_safe` | PenetrationWorkflowToolModule | ✅ Registered | partial | pilot | P1 | Delete traced Round shadow instances |

### 14. Spatial Intelligence

Tools for clash detection, proximity, raycast, geometry, and zone analysis.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `spatial.clash_detect` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | BBox-based clash detection (source vs target) |
| 2 | `spatial.proximity_search` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | Find elements within radius |
| 3 | `spatial.raycast` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | Ray intersection finder (supports links) |
| 4 | `spatial.geometry_extract` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | Volume, surface area, centroid extraction |
| 5 | `spatial.section_box_from_elements` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | Optimal section box computation |
| 6 | `spatial.zone_summary` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | Element distribution by zone |
| 7 | `spatial.element_distances` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | Pairwise element distances |
| 8 | `spatial.level_zone_analysis` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | Element distribution per level |
| 9 | `spatial.opening_detect` | SpatialIntelligenceToolModule | ✅ Registered | partial | pilot | P1 | Detect openings in host elements |
| 10 | `directshape.create_safe` | SpatialIntelligenceToolModule | ✅ Registered | partial | later | P2 | Create DirectShape for markers/indicators |

### 15. Script Orchestration

Tools for scripted automation above individual tool calls.

| # | Tool Name | Module | Code Status | Current State | BA Relevance | Priority | Notes |
|---|-----------|--------|-------------|---------------|--------------|----------|-------|
| 1 | `script.list` | ScriptOrchestrationToolModule | ✅ Registered | partial | later | P2 | List available scripts |
| 2 | `script.validate` | ScriptOrchestrationToolModule | ✅ Registered | partial | later | P2 | Validate script safety/syntax |
| 3 | `script.run_safe` | ScriptOrchestrationToolModule | ✅ Registered | partial | later | P2 | Execute validated script (dry-run + approval) |
| 4 | `script.get_run` | ScriptOrchestrationToolModule | ✅ Registered | partial | later | P2 | Get script execution result |
| 5 | `script.compose_safe` | ScriptOrchestrationToolModule | ✅ Registered | partial | later | P2 | Compose multi-step script |

*Use this inventory as the BA control surface for what is in MVP, what is merely available in code, and what must stay out of active claims until validated.*
