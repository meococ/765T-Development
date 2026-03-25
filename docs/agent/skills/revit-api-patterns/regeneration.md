# Regeneration

- Chỉ gọi regenerate khi thật sự cần đọc state sau mutation tạo/phá dependency.
- Nhiều API mutation tự regenerate; đừng gọi dư trong loop vì sẽ chậm mạnh.
