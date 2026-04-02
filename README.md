# 765T Revit Agent

**[English -> README.en.md](README.en.md)**

Repo nay la product repo cho `765T Revit Agent`.
No chi ship source code runtime, manifests, scripts van hanh, va tai lieu su dung san pham.
Roadmap, build notes, agent charters, architecture debate, va tai lieu dinh huong noi bo khong duoc ship trong repo nay.

## Product Topology

```text
IDE / MCP client
    -> BIM765T.Revit.McpHost
    -> BIM765T.Revit.WorkerHost
    -> BIM765T.Revit.Agent
    -> Revit API
```

`BIM765T.Revit.Agent` la boundary duy nhat duoc goi Revit API.

## Trong Repo Nay Co Gi

- `src/`: runtime va add-in
- `tests/`: unit tests va repo validation
- `tools/`: scripts install, start, verify, smoke
- `catalog/`, `packs/`, `workspaces/default/`: machine-readable runtime assets
- `docs/`: tai lieu user-facing cho install, integration, reference, troubleshooting, release checks

## Yeu Cau Van Hanh

- Windows 10/11
- Autodesk Revit 2024 hoac 2026
- Revit add-in da duoc install
- WorkerHost dang chay
- Chi nen de mot `Revit.exe` khi lam mutation/export live

## Cach Su Dung Product Path

1. Install add-in tu build/package ma team cua anh dang ship.
2. Start `BIM765T.Revit.WorkerHost`.
3. Mo model bang `tools/restart_revit_and_trust_addin.ps1`.
4. Verify stack bang `tools/check_bridge_health.ps1 -AsJson`.
5. Cau hinh IDE MCP tro vao `BIM765T.Revit.McpHost.exe`.

Muc tieu product path la:

```text
IDE/MCP -> McpHost -> WorkerHost -> kernel
```

`BIM765T.Revit.Bridge` va cac lane transitional chi la helper/diagnostics, khong phai canonical IDE path.

## Tai Lieu Product

- [docs/README.md](docs/README.md)
- [docs/integration/quickstart-claude-code.md](docs/integration/quickstart-claude-code.md)
- [docs/integration/quickstart-ai-testing.md](docs/integration/quickstart-ai-testing.md)
- [docs/reference/mcphost.md](docs/reference/mcphost.md)
- [docs/reference/snapshot-strategy.md](docs/reference/snapshot-strategy.md)
- [docs/troubleshooting/revit-agent-debug.md](docs/troubleshooting/revit-agent-debug.md)
- [docs/release/mvp-manual-smoke.md](docs/release/mvp-manual-smoke.md)
- [tools/README.md](tools/README.md)

## Boundary Note

Neu anh dang tim:

- roadmap
- build/developer workflow
- internal architecture redlines
- agent operating model
- research packs

thi nhung tai lieu do thuoc `Control Tower`, khong thuoc product repo nay.

## License

MIT. Xem [LICENSE](LICENSE).
