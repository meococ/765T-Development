# ADR-0008 - Target state ưu tiên Flow shell trước Hub và Audit dashboard

## Status
Accepted for target-state planning

## Decision
Thứ tự ship UI/product shell:

1. Flow chat shell
2. Project Brief / Hub
3. Audit dashboard

## Context
- value gần nhất của 765T vẫn là task execution + grounded review + approval
- nếu làm dashboard rộng trước khi flow shell ổn thì UX sẽ đẹp nhưng không đáng tin
- transcript/session/approval là core loop phải ổn trước

## Consequences
- dark/light, streaming, transcript persistence, approval UX là ưu tiên cao
- dashboard manager không được phá hoặc làm chậm loop chính
- web dashboard nên dùng chung state/runtime truth, không tạo runtime riêng

