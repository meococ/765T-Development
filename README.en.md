# 765T Revit Agent

**[Tieng Viet -> README.md](README.md)**

This is the product repo for `765T Revit Agent`.
It ships runtime source, runtime manifests, operational scripts, and user-facing documentation only.
Roadmaps, build notes, internal architecture direction, agent charters, and research packs are intentionally kept out of this repository.

## Product Topology

```text
IDE / MCP client
    -> BIM765T.Revit.McpHost
    -> BIM765T.Revit.WorkerHost
    -> BIM765T.Revit.Agent
    -> Revit API
```

`BIM765T.Revit.Agent` is the only component allowed to call the Revit API.

## What Lives Here

- `src/`: runtime and add-in source
- `tests/`: unit tests and repo validation
- `tools/`: install, startup, verification, and smoke helpers
- `catalog/`, `packs/`, `workspaces/default/`: machine-readable runtime assets
- `docs/`: user-facing install, integration, reference, troubleshooting, and release docs

## Runtime Requirements

- Windows 10/11
- Autodesk Revit 2024 or 2026
- Installed Revit add-in
- Running WorkerHost
- Exactly one `Revit.exe` for live mutation/export work

## Product Usage Flow

1. Install the add-in from the build or package your team ships.
2. Start `BIM765T.Revit.WorkerHost`.
3. Open the target model with `tools/restart_revit_and_trust_addin.ps1`.
4. Verify the stack with `tools/check_bridge_health.ps1 -AsJson`.
5. Point your IDE MCP config at `BIM765T.Revit.McpHost.exe`.

The canonical product path is:

```text
IDE/MCP -> McpHost -> WorkerHost -> kernel
```

`BIM765T.Revit.Bridge` and other transitional lanes remain useful for diagnostics and scripts, but they are not the primary IDE path.

## Product Docs

- [docs/README.md](docs/README.md)
- [docs/integration/quickstart-claude-code.md](docs/integration/quickstart-claude-code.md)
- [docs/integration/quickstart-ai-testing.md](docs/integration/quickstart-ai-testing.md)
- [docs/reference/mcphost.md](docs/reference/mcphost.md)
- [docs/reference/snapshot-strategy.md](docs/reference/snapshot-strategy.md)
- [docs/troubleshooting/revit-agent-debug.md](docs/troubleshooting/revit-agent-debug.md)
- [docs/release/mvp-manual-smoke.md](docs/release/mvp-manual-smoke.md)
- [tools/README.md](tools/README.md)

## Boundary Note

If you are looking for:

- roadmap material
- build or contributor workflow
- internal architecture redlines
- agent operating model
- research packs

those belong in the `Control Tower`, not in this product repo.

## License

MIT. See [LICENSE](LICENSE).
