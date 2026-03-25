# ADR-0009 - Target state 765T Hub là productized workspace surface

## Status
Accepted for target-state planning

## Decision
`project.init_*`, context bundle, deep scan, workspace reports sẽ được productize thành **Hub / Project Brief surface**, không chỉ là infra endpoints.

## Context
- engine cho workspace/context đã có
- nhưng user chưa thấy rõ giá trị dưới dạng một surface sản phẩm
- cần bridge từ infra truth sang product shell: brief, health, quick actions, next steps

## Consequences
- onboarding state phải map trực tiếp sang UI state
- workspace health, context readiness, deep scan readiness phải nhìn thấy được
- Hub trở thành nơi bắt đầu cho project-grounded AI, không chỉ là hidden state

