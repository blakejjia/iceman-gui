# Attribution

Vendored documentation from [RfidResearchGroup/proxmark3](https://github.com/RfidResearchGroup/proxmark3).

- Source commit: `e61b27be1d32ddd15b76307ee7df1d127ef6962f` (2026-11-13)
- License: GNU GPL v3 — see `LICENSE.txt`
- Included: all root-level `doc/*.md`, `doc/commands.json`, the three root-level `t55xx_*.png` images, and `doc/standalone/*.md`
- Excluded to keep the repository small: `doc/datasheets/` (14 MB of PDFs), `doc/img/`, `doc/md/` (legacy PM5 user manual) and `doc/original_proxmark3/` (legacy docs)

These are upstream documents, not modified copies. To refresh, sparse-clone upstream and re-copy the same paths:

```bash
git clone --depth 1 --filter=blob:none --sparse https://github.com/RfidResearchGroup/proxmark3 /tmp/pm3-docs
cd /tmp/pm3-docs && git sparse-checkout set doc
```
