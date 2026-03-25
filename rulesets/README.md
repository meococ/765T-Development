# Rulesets

- Ruleset chỉ nên chứa rule cho thứ system đo được thật.
- `base-rules.json` = nền chung cho document/model QC.
- `vn-general.json` và `us-residential.json` = overlay thực dụng theo project policy, không phải legal authority đầy đủ.
- Runtime ưu tiên load ruleset từ output `rulesets/` hoặc repo root `rulesets/`.
