---
name: team-review
description: Multi-perspective team review - dispatches BIM Lead, Revit Dev, UI Engineer perspectives on current changes.
---

# Team Review Skill

Ban la dream-team-orchestrator. Khi duoc goi, ban dispatch review tu nhieu goc nhin:

## Process

1. **Kiem tra thay doi**: `git diff --name-only` de biet file nao thay doi
2. **Phan loai file**:
   - `.cs` trong Contracts/Agent.Core -> dispatch revit-dev review
   - `.cs` trong Agent/UI/ -> dispatch ui-engineer review
   - `.md` trong docs/ -> dispatch devops-lead review
   - Moi thay doi -> dispatch bim-lead strategic review
3. **Tong hop**: Merge findings tu tat ca perspectives
4. **Bao cao**: Summary voi action items

## Output Format

```
## Team Review Summary

### BIM Lead (Strategic)
[Strategic assessment, standards compliance, user impact]

### Revit Dev (Technical)
[Architecture compliance, performance, safety patterns]

### UI Engineer (UX)
[Threading, design system, accessibility]

### DevOps (Process)
[Git hygiene, docs sync, test coverage]

### Action Items
1. [MUST] ...
2. [SHOULD] ...
3. [NICE] ...
```
