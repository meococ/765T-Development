# Catalogs

Th? m?c n?y gi? machine-readable catalogs ???c export t? `packs/`, `workspaces/`, v? playbook manifests.

- `pack-catalog.json`
- `workspace-catalog.json`
- `playbook-catalog.json`
- `standards-catalog.json`
- `tool-catalog.json` (stub ho?c live export t?y runtime)

C?c file n?y l? generated artifacts; canonical truth v?n l? source manifests trong repo.

## Regenerate

```powershell
.\tools\dev\export-pack-catalog.ps1
```

## Path policy

- `pack-catalog.json`
- `workspace-catalog.json`
- `playbook-catalog.json`
- `standards-catalog.json`

???c export v?i **repo-relative paths** ?? tr?nh hardcode local machine path khi repo ??i v? tr?.

N?u manifests thay ??i ho?c repo ???c move sang m?y/path kh?c, h?y regenerate l?i catalogs thay v? s?a tay.
