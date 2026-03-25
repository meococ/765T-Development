# ADR-0005 - Target state canonical IPC topology

## Status
Accepted for target-state planning

## Decision
Target state sẽ chuẩn hóa thành:

- **1 canonical public control plane** ở WorkerHost
- **1 canonical private ingress** vào Revit kernel qua named pipe
- MCP/CLI/web là adapter, không phải runtime truth riêng
- legacy lanes phải sunset

## Context
- runtime hiện có nhiều đường gọi cùng lúc
- product/UX không nên phụ thuộc vào việc user hiểu nhiều IPC layers
- technical debt hiện tại nằm ở chồng lớp và vai trò bị overlap

## Consequences
- docs, health, logging, readiness phải quy về topology này
- protocol façades vẫn có thể tồn tại, nhưng ownership phải rõ là adapter
- deprecation plan cho legacy lane trở thành bắt buộc

## Notes
ADR này là **target state**, không phủ nhận runtime transitional đang tồn tại hôm nay.

