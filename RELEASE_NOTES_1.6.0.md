# Release Notes v1.6.0

## Advanced Diff Features

This release makes `diff` far more powerful and configurable: customizable live-database comparisons, parallel live-db export, noise-reducing ignore rules, per-type change statistics, and context-aware line-by-line diffs.

### Customizable live-database diff

When comparing live databases you can now control which schemas are exported for the comparison, instead of always defaulting to `public`:

```bash
pgschema-export diff \
  --left-db "Host=old;Database=app" \
  --right-db "Host=new;Database=app" \
  --schemas public,billing \
  --exclude-schemas audit
```

- `--schemas` — comma-separated schemas to export for the comparison (default: `public`).
- `--exclude-schemas` — comma-separated schemas to exclude (default: `pg_catalog,information_schema`).

### Parallel live-database diff

`--parallel` now applies to live-database comparisons, running each side's metadata queries concurrently for a significant speedup on large databases:

```bash
pgschema-export diff --left-db "<conn>" --right-db "<conn>" --parallel
```

### Ignore patterns

Reduce diff noise by ignoring cosmetic-only changes:

```bash
pgschema-export diff --left ./old --right ./new --ignore-comments --ignore-whitespace
```

- `--ignore-comments` — ignores SQL comments (whole-line and trailing `--` comments; `--` inside single-quoted string literals is preserved).
- `--ignore-whitespace` — ignores whitespace-only differences (leading/trailing/collapsed whitespace and blank lines).

Both options affect whether a file is reported as **changed** and what the context-aware diff shows.

### Diff summary statistics

Every diff now reports change counts grouped by object type (the top-level export folder such as `tables`, `views`, `functions`):

```
## Changes by type
- functions: +2 -0 ~1
- tables:    +0 -1 ~3
- views:     +1 -0 ~0
```

Statistics are included in all three report formats (text, JSON, HTML). The JSON report exposes them under a new `statistics` array with `objectType`, `added`, `removed`, `changed`, and `total`.

### Context-aware diff

Pass `--context` to see the actual line-by-line changes within each changed file, rendered git-style with `+`/`-`/context markers:

```bash
pgschema-export diff --left ./old --right ./new --context
```

- Text report gains a `## Details` section with `### <path>` blocks.
- HTML report renders each changed file as its own section with colored add/remove lines.
- JSON report exposes a new `fileDiffs` array (`path` + `lines[]` of `{ kind, text }`).

Line diffs are produced by a new LCS-based `LineDiffer` in the core library.

### Tests

- Added coverage for `--ignore-comments`, `--ignore-whitespace`, per-type statistics, context diffs, report rendering, and the `LineDiffer` algorithm.

### Compatibility

- Requires .NET 8.0 and Npgsql 8.0.5.
- Backward compatible with 1.5.x. All new diff options are opt-in; existing commands, configs, and report consumers work unchanged (the new `statistics`/`fileDiffs` JSON fields are additive).
