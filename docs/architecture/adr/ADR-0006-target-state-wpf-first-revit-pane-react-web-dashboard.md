# ADR-0006 - Target state UI host strategy: WPF-first cho Revit 2024, React-first cho web dashboard

## Status
Accepted for target-state planning

## Decision

- Revit 2024 pane tiếp tục dùng **WPF-first shell**
- React dashboard phát triển **ngoài pane trước**
- WebView2 không là mặc định cho dockable pane Revit 2024

## Context
- repo research hiện tại đã ghi rõ rủi ro WebView2/CEF trên Revit 2024/2025
- web repo đã tồn tại, nên có thể đẩy nhanh dashboard theo hướng web-first
- mục tiêu gần là UX hiện đại hơn nhưng không đánh đổi stability

## Consequences
- modernize UI trước hết bằng design system + Flow shell trong WPF
- dashboard manager/project brief nên đi theo web lane trước
- WebView2 chỉ dùng khi host strategy đủ an toàn hoặc version cho phép

