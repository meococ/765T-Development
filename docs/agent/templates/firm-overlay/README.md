# Firm overlay template

Clone thư mục này thành pack riêng cho công ty, sau đó đặt pack vào:
- packs/standards/<firm-name>/
- packs/playbooks/<firm-name>/
- packs/skills/<firm-name>/

Mục đích:
- `/init` V1 chỉ consume pack đã tồn tại
- không tự tạo firm pack bằng command
- team có thể copy bộ template này để bắt đầu nhanh và giữ cấu trúc nhất quán

## Gợi ý đặt tên
- standards: `contoso.standards.firm`
- playbooks: `contoso.playbooks.firm`
- skills: `contoso.skills.firm`

## Tối thiểu cần có
1. `pack.json`
2. `assets/README.md`
3. standards/playbook JSON machine-readable mà BIM765T có thể resolve

## Lưu ý
- Không commit file dự án `.rvt/.rfa/.pdf` vào pack
- Pack chỉ nên chứa doctrine, standards, playbooks, templates machine-readable
- Nếu rule thử nghiệm chưa ổn định, giữ ở docs/lessons trước khi promote vào pack
