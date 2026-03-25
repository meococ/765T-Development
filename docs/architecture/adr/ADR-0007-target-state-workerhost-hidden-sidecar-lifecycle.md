# ADR-0007 - Target state WorkerHost lifecycle là hidden sidecar, auto-start và auto-recover

## Status
Accepted for target-state planning

## Decision
WorkerHost vẫn là sidecar process, nhưng UX phải như integrated:

- auto-start hidden
- auto-reconnect
- health-aware retry
- failure messaging rõ nghĩa

## Context
- bắt user tự mở WorkerHost là UX không chấp nhận được
- nhét toàn bộ WorkerHost vào Revit process làm tăng risk lag/crash/coupling
- sidecar vẫn là boundary đúng cho public control plane

## Consequences
- readiness và pane bootstrap phải coi sidecar lifecycle là first-class
- UI không được surface lỗi generic network khi sidecar vừa khởi động
- restart/recover path phải là productized behavior, không phải thao tác dev

