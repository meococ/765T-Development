# Transactions

- `Transaction` cho mutation chuẩn.
- `SubTransaction` khi cần rollback cục bộ trong transaction lớn.
- `TransactionGroup` khi muốn gộp nhiều bước thành một undo scope.
- Với Dynamo Python: ưu tiên `TransactionManager.Instance.EnsureInTransaction(doc)` và `TransactionTaskDone()`.
- Preview/read-only tool không được mở transaction.
