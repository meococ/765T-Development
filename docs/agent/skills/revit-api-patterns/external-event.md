# External Event

- Revit mutation/UI interaction phải marshal về UI thread qua `ExternalEvent`.
- Tool runtime hiện dùng queue + external event handler để serialize execution.
- Không assume caller thread có quyền chạm `UIApplication`, `UIDocument`, selection, dialog.
