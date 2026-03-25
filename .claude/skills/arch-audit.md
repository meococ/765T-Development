---
name: arch-audit
description: Architecture audit - verify layer boundaries, contract safety, performance patterns.
---

# Architecture Audit

Ban la revit-dev. Kiem tra architecture compliance toan dien:

## Process

1. **Layer Boundaries**: Grep tat ca projects cho RevitAPI references
   - `grep -r "using Autodesk.Revit" src/BIM765T.Revit.Contracts/`
   - `grep -r "using Autodesk.Revit" src/BIM765T.Revit.WorkerHost/`
   - `grep -r "using Autodesk.Revit" src/BIM765T.Revit.McpHost/`
   - `grep -r "using Autodesk.Revit" src/BIM765T.Revit.Copilot.Core/`

2. **Contract Safety**: Check DataMember ordering
   - Tim tat ca `[DataMember(Order=` patterns
   - Verify sequential, append-only

3. **Performance Scan**: Check FilteredElementCollector usage
   - Grep cho `.WherePassesFilterRule` TRUOC `.OfCategory` (anti-pattern)
   - Check `.Result` usage (deadlock risk)

4. **Thread Safety**: Check ExternalEvent usage
   - Mutation code PHAI co ExternalEvent dispatch
   - ViewModel KHONG goi Revit API truc tiep

5. **Build Verification**: `dotnet build -c Release`

## Output

```
## Architecture Audit Report

### Layer Boundaries
[OK/VIOLATION per project]

### Contract Safety
[DataMember ordering status]

### Performance
[Collector patterns, deadlock risks]

### Thread Safety
[ExternalEvent usage, ViewModel isolation]

### Build Status
[Pass/Fail with details]
```
