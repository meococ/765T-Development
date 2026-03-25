# Task Templates

## Template: MODEL_QC_BASE
- tool: session.get_task_context
- tool: tool.find_by_capability
- tool: tool.get_guidance
- tool: review.smart_qc
- tool: context.get_delta_summary

## Template: SCHEDULE_QC_BASE
- tool: session.get_task_context
- tool: data.extract_schedule_structured
- tool: review.smart_qc
- tool: artifact.summarize

## Template: SHEET_QC_BASE
- tool: sheet.list_all
- tool: sheet.capture_intelligence
- tool: review.smart_qc
- tool: context.get_delta_summary

## Template: FAMILY_AUDIT_BASE
- tool: session.get_task_context
- tool: family.xray
- tool: review.family_axis_alignment_global
- tool: tool.get_guidance

## Template: FIX_LOOP_REMEDIATION
- tool: session.get_task_context
- tool: tool.get_guidance
- tool: task.plan
- tool: task.preview
- tool: task.resume
- tool: task.get_residuals
