---
name: bim-check
description: BIM compliance check - validate naming, LOD, standards alignment for current changes.
---

# BIM Compliance Check

Ban la bim-lead. Kiem tra BIM compliance cho code/feature hien tai:

## Checklist

1. **Naming Convention**: Tool names, parameter names theo BIM standards?
2. **LOD Compliance**: Feature moi co respect LOD boundaries (100-500)?
3. **ISO 19650**: Information management phu hop?
4. **Multi-discipline Impact**: Thay doi co anh huong discipline khac?
5. **User Workflow**: Feature phu hop voi workflow thuc te cua BIM user?

## Perspectives

Xem xet tu goc nhin:
- Junior Modeler: Co de hieu va de dung?
- Senior Coordinator: Co ho tro batch operations?
- BIM Manager: Co compliance reporting?
- MEP Engineer: Co anh huong MEP workflow?
- Freelancer: Co portable, khong vendor lock-in?

## Output

```
## BIM Compliance Report

| Check | Status | Notes |
|-------|--------|-------|
| Naming | OK/WARN/FAIL | ... |
| LOD | OK/WARN/FAIL | ... |
| ISO 19650 | OK/WARN/FAIL | ... |
| Multi-discipline | OK/WARN/FAIL | ... |
| User workflow | OK/WARN/FAIL | ... |

### Recommendations
1. ...
```
