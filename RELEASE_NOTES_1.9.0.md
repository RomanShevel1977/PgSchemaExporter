# Release Notes v1.9.0

## Developer Experience

This release focuses on visualizing the schema and understanding performance: an
**ER diagram** generator and a **performance profiling** (`--profile`) flag.

### ER diagrams

Generate a diagram from a live database or from an exported schema directory:

```bash
# Mermaid erDiagram (renders in GitHub/GitLab Markdown)
pgschema-export diagram \
  --connection "Host=localhost;Database=mydb;Username=postgres;Password=123" \
  --output schema.mmd

# Graphviz DOT from an exported directory
pgschema-export diagram --schema "./db-schema" --output schema.dot

# Render DOT to SVG
dot -Tsvg schema.dot -o schema.svg
```

- Supports `--format mermaid` (default) or `--format dot`.
- Format is inferred from the `--output` extension (`.mmd` / `.mermaid` → Mermaid;
  `.dot` / `.gv` → DOT).
- Primary-key, unique, and foreign-key constraints are used to mark key columns
  and compute relationship cardinality.
- Works from a live PostgreSQL connection (`--connection`) or from the exported
  directory (`--schema`).

### Performance profiling

Add `--profile` to any command to print a per-phase timing summary to stderr on
completion:

```bash
pgschema-export export --connection "<conn>" --output ./db-schema --profile
```

This wraps the normal progress reporter and records how long each step takes,
which helps pinpoint slow phases in large databases or CI pipelines.

### New CLI commands and options

```
diagram      Generate an ER diagram (Mermaid or Graphviz DOT)

Global: --profile   Print per-phase timing summary to stderr
```

### Internal additions

- `ConstraintDefinitionParser` — parses `PRIMARY KEY`, `UNIQUE`, and `FOREIGN KEY`
  definitions to extract column lists and referenced tables, tolerant of quoting
  and trailing `ON DELETE`/`DEFERRABLE` clauses.
- `ErModel` / `ErTable` / `ErColumn` / `ErRelationship` — diagram domain model.
- `ErModelBuilder` — builds the ER model from either a live `DatabaseModel` or
  the exported `tables/` and `constraints/` directory.
- `MermaidErRenderer` — emits Mermaid `erDiagram` syntax with PK/FK/UK markers
  and `||--o{` / `|o--o{` cardinality.
- `DotErRenderer` — emits Graphviz DOT with HTML-like table nodes.
- `SchemaDiagramGenerator` — orchestrator that loads the minimal table/constraint
  metadata and renders to the requested format.
- `TimingProgressReporter` — decorator that records per-step timing and produces a
  human-readable summary.

### Tests

- Added unit coverage for `ConstraintDefinitionParser`, `ErModelBuilder` (live DB
  and directory sources), `MermaidErRenderer`, `DotErRenderer`,
  `SchemaDiagramGenerator`, `TimingProgressReporter`, and CLI parsing for the new
  `diagram` command.

### Compatibility

- Requires .NET 8.0 and Npgsql 8.0.5.
- Backward compatible with 1.8.x. All new features are opt-in.
