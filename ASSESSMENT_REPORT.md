# Глубокая оценка PgSchemaExporter

## Общая картина

- **Размер:** `src/PgSchemaExporter.Core` + `src/PgSchemaExporter.Cli` — ~140 файлов, ~9600 строк C#.
- **Качество сборки:** `dotnet build` обоих проектов чистый, warning’ов нет. Все 325 тестов проходят.
- **Архитектура:** CLI и Core разделены, используются абстракции `ILogger`, `IProgressReporter`, `CancellationToken`. Но внутри Core много статических хелперов, ручного связывания зависимостей и «богатых» статических классов.

---

## 1. Архитектура и проектирование

### 1.1. Ручное связывание зависимостей и нарушение SRP
- **`Program.cs`** (940 строк) — огромный `Main`, который делает всё: парсинг аргументов, роутинг команд, форматирование вывода, создание сервисов, управление `Environment.ExitCode`.
- Конструкторы в `SchemaExporter` и `DumpSplitter` получают часть зависимостей, но внутри создают `DependencyManifestWriter`, `DeploymentPlanBuilder` — **смешанное управление зависимостями**.
- `MigrationGenerator`, `MigrationPlanner`, `SqlStatementSplitter` создают внутри себя `SqlStatementCache`, `MigrationGenerator` — нет DI, тестировать по классу неудобно.

### 1.2. Переизбыток статических классов
Большая часть бизнес-логики реализована статикой:
- `SqlTokenizer`, `SqlIdentifier`, `MigrationTimeout`, `HazardAnalyzer`, `OnlineDdlRewriter`, `TableMigrationBuilder`, `SqlDropBuilder`, `SchemaFingerprint`, `LineDiffer`, `ConstraintDefinitionParser`, `TableDefinitionParser`.

**Минус:** нельзя подменить поведение в тестах, сложно мокировать, логика размазана по статическим методам. Часть из них чистые функции (это ок), но многие содержат состояние/кеш (`SqlStatementCache`, `PostgresMetadataProvider.PolicyDefFunctionExistsCache`).

### 1.3. Нет solution-файла
- `dotnet build PgSchemaExporter.sln` падает с `MSB1009` — решения нет. Это мешает стандартному `dotnet test` из корня и некоторым инструментам.

### 1.4. Несовпадение версий
- `PgSchemaExporter.Core.csproj` → `Version 1.8.0`.
- `Program.cs` → `const string VersionString = "1.9.0"`.
- В корне лежат артефакты `pgschema-export-v1.9.0-*.zip`.

---

## 2. Качество кода и поддерживаемость

### 2.1. `Program.cs` — god object
- 60 `Console.Write/WriteLine`, собственная логика `--help`, `--version`, фильтрации глобальных флагов, свич по командам.
- **Рекомендация:** выделить `CommandHandler`/`ICommand` на команду (`export`, `diff`, `migrate`, `split-dump`, `init`, `apply`, `plan`).

### 2.2. Дублирование в `CliParser`
- Каждый метод-парсер (`ParsePlanOptions`, `ParseApplyOptions`, `ParseDriftOptions`…) содержит одинаковый локальный `NextValue()`.
- `ApplyArgs` и `ExportOptions`/`MigrationOptions` имеют `public` сеттеры и пустые строки по умолчанию вместо `required`/`init` — nullable enable, но объект может быть в невалидном состоянии.

### 2.3. `ExportOptions` мутирует себя при валидации
```csharp
public void EnsureValidForExport()
{
    ...
    Schemas = Schemas.Where(...).Distinct(...).ToArray();  // side effect
}
```
- `Validate()` возвращает `IReadOnlyList<string>`, `EnsureValid()` кидает `ArgumentException` с `string.Join(" ", errors)`. Вызов `LoadAsync` вызывает `EnsureValidForExport()`, который не только валидирует, но и **нормализует** массив — неочевидное поведение.

### 2.4. `FileWriteResult` — 22 однотипных `List<string>`
- Все списки файлов копируются в `GetDeployOrder()` через `Concat(...).Distinct(...).ToList()` — аллокации и дублирование структуры. Можно заменить на единый `IReadOnlyList<SchemaFile>` с метаданными.

### 2.5. Дублирование SQL-парсинга
- `SqlTokenizer.SplitStatements`, `FindMatchingParen`, `SplitTopLevel` дублируют логику кавычек/комментариев/долларов. Изменение в одном месте легко забыть в другом.
- `TableDefinitionParser` и `ConstraintDefinitionParser` снова содержат собственный `IndexOfWord`/`MatchesWordAt`/`ReadQuotedIdentifierEnd` — это следствие отката к быстрым сканерам, но увеличивает дублирование.

### 2.6. Мёртвый код и неиспользуемые переменные
- `OnlineDdlRewriter.TryFindIndexPrefix`: `var hasUnique = TryMatchKeyword(...); _ = hasUnique;` — переменная не используется.

---

## 3. Производительность

### 3.1. `SqlTokenizer.IndexOfWord` — повторяющиеся `Split` на каждый вызов
```csharp
var words = word.Split([' '], StringSplitOptions.RemoveEmptyEntries);
```
- Вызывается в циклах `HazardAnalyzer`, `PgDumpObjectClassifier`, `OnlineDdlRewriter` и др. Каждый вызов аллоцирует массив и строки.

### 3.2. `SqlTokenizer.ReadIdentifier` без fast path
- После отката используется `StringBuilder()` на каждый вызов. Для `PgDumpObjectClassifier` и диаграмм это горячий путь. Бенчмарк `MigrationGenerator` уже показал, что `ReadIdentifier` в `TableDefinitionParser`/`ConstraintDefinitionParser` был дорогим.
- **Рекомендация:** вернуть лёгкий fast path для простых незакавыченных идентификаторов, но не перегружать код.

### 3.3. `MigrationGenerator` — избыточные нормализации и сплиты
- `BuildStatementSetDiff` делает `fromStatements.Select(NormalizeStatement).ToHashSet(...)`, а потом в цикле снова `NormalizeStatement(statement)` — двойная работа.
- `BuildAdded`/`BuildRemoved` вызывают `SplitStatements(content)` по два раза.
- `Normalize` в `MigrationGenerator` делает три строковых преобразования подряд.

### 3.4. `SchemaDiffer` — `Parallel.ForEach` + `lock` на `List`
```csharp
Parallel.ForEach(common, parallelOptions, relativePath =>
{
    ...
    lock (unchanged) unchanged.Add(relativePath);
    lock (changed) changed.Add(relativePath);
});
```
- `lock` на обычных списках сериализует потоки и снижает эффект от параллелизма. `ConcurrentBag` / `Partitioner` или локальные списки с merge в конце — лучше.
- `ConsoleProgressReporter.Step` вызывается из многих потоков: `_completed++` не атомарен, вывод в `Console.Error` будет перемешан.

### 3.5. `SchemaFileWriter` — LINQ-цепочки и `Parallel.For` + последовательная запись
- `ApplyFormat` делает `sql.Replace(" IF NOT EXISTS ", " ")` — хрупко: не ловит `IF NOT EXISTS` с `\n` или несколькими пробелами.
- `StableHash` берёт **первые 8 байт SHA256** (64 бита) для имени файла функции. Вероятность коллизии низкая, но для критичных артефактов лучше больше байт или проверку дублей.

### 3.6. `DumpSplitter` читает весь дамп в память
```csharp
var sql = await File.ReadAllTextAsync(options.InputFile, cancellationToken);
```
- Для больших дампов это OOM-риск. Нужен streaming-сплиттер или хотя бы `Memory<char>`/массив с `FileShare.Read`.

### 3.7. `PostgresMetadataProvider` — `Parallel=true` увеличивает аллокации
- Бенчмарк показывает `+25 %` allocated при ускорении на `~38 %`. Множество `Task`, `List<T>`, `NpgsqlConnection` создают давление на GC.
- `MaxParallelQueries` захардкожен на 8, нет настройки.

### 3.8. `SqlStatementCache` — неограниченный кеш без потокобезопасности
- `_splitCache` и `_normalizeCache` — обычные `Dictionary<string, ...>`. Если `MigrationGenerator` когда-либо станет многопоточным, будет race. Даже в однопотоке кеш не вытесняется — большие миграции будут держать всё в памяти.

---

## 4. Безопасность и надёжность

### 4.1. SQL-инъекции через строковую интерполяцию
- `MigrationScript.AppendSessionSettings` вставляет `LockTimeout`/`StatementTimeout` в строку `SET lock_timeout = '{options.LockTimeout}'`.
- `MigrationTimeout.IsValid` допускает только цифры и единицы (`5s`, `1min`), но сам факт интерполяции — потенциальный вектор, если валидацию обойдут или изменят.
- `MigrationApplier.ApplySessionSettingsAsync` тоже интерполирует в `NpgsqlCommand`.
- `TableMigrationBuilder` строит `ALTER TABLE {table} ...` через интерполяцию; `table` берётся из `TableDefinitionParser.QualifiedName` без `SqlIdentifier.Quote`, `column.Definition` — сырой кусок SQL.

**Рекомендация:** для `SET` использовать параметры `NpgsqlCommand`; для `ALTER TABLE` экранировать все идентификаторы (`SqlIdentifier.Quote`).

### 4.2. `MigrationHistory.AppendAsync` — неатомарное чтение-изменение-запись
- Между `ReadAsync` и `WriteAllTextAsync` может вклиниться другой процесс/CLI-экземпляр — `history.json` может повредиться.

### 4.3. `PostgresMetadataProvider.PolicyDefFunctionExistsCache` — статический кеш
- `ConcurrentDictionary` живёт в `AppDomain` и не сбрасывается. Если на сервере функция появится/исчезнет внутри жизни процесса, кеш вернёт устаревшее значение.

### 4.4. `ExportConfigLoader`/`ExportConfigWriter` используют рефлексию
- `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` — в отличие от `MigrationHistory`/`SchemaDiffReportWriter`, конфиг не использует `PgSchemaExporterJsonContext`. Это ломает AOT/тримминг и не консистентно.

### 4.5. `ConsoleProgressReporter` не потокобезопасен
- При `--parallel` в `diff`/`export` несколько потоков могут одновременно вызывать `Step`, портя `_completed` и вывод.

### 4.6. `MigrationApplier` — нет таймаута команды
- `NpgsqlCommand` создаётся без `CommandTimeout`. Долгоиграющая `ALTER TABLE` может повиснуть бесконечно.

### 4.7. `MigrationApplier` — concurrent-операции не откатываются
- `CREATE INDEX CONCURRENTLY` выполняются вне транзакции. Если один из них упадёт, предыдущие уже применены — миграция останется частично применённой. Это архитектурный trade-off, но стоит документировать и, возможно, писать journal.

---

## 5. Тестирование

### 5.1. Преобладание интеграционных тестов
- 67 тестовых файлов, многие из них `*IntegrationTests.cs` и используют Testcontainers/PostgreSQL/Docker. Это хорошо для end-to-end, но:
  - Долгий прогон.
  - Требуется Docker.
  - Юнит-тестов на чистую логику (`SqlTokenizer`, `DeploymentPlanBuilder`, `MigrationGenerator`) относительно мало.

### 5.2. Нет автоматических perf-regression тестов
- Бенчмарки есть, но запускаются вручную. CI не проверяет `benchmark-comparison.md` автоматически.

---

## 6. Документация и DX

### 6.1. `README.md` и `RELEASE_NOTES`
- Версии в csproj / `Program.cs` / артефактах не согласованы. Риск, что пользователь видит `1.9.0`, а сборка идентифицирует себя как `1.8.0`.

### 6.2. Нет solution-файла
- Усложняет onboarding и `dotnet test` без указания проекта.

---

## Приоритетные рекомендации

| Приоритет | Действие | Где |
|---|---|---|
| **P0** | Внедрить параметризацию/экранирование для `SET lock_timeout`/`statement_timeout` и `ALTER TABLE` | `MigrationApplier`, `MigrationScript`, `TableMigrationBuilder` |
| **P0** | Сделать `MigrationHistory.AppendAsync` атомарной (или с file lock) | `MigrationHistory` |
| **P1** | Разбить `Program.cs` на команды/хендлеры | `Program.cs`, `CliParser` |
| **P1** | Убрать мутации из `ExportOptions.EnsureValidForExport`; нормализация отдельно от валидации | `ExportOptions`, `ExportConfigLoader` |
| **P1** | Перевести `ExportConfigLoader`/`ExportConfigWriter` на `PgSchemaExporterJsonContext` | `Configuration/*` |
| **P1** | Добавить `.sln` и унифицировать версии | корень, `*.csproj`, `Program.cs` |
| **P2** | Оптимизировать `IndexOfWord` (избежать `Split` на каждый вызов) и вернуть лёгкий fast path в `ReadIdentifier` | `SqlTokenizer` |
| **P2** | Переделать `SchemaDiffer` на `ConcurrentBag`/локальные списки | `SchemaDiffer` |
| **P2** | Добавить `CommandTimeout` и journal для `MigrationApplier` | `MigrationApplier` |
| **P3** | Сделать `SchemaExporter`/`DumpSplitter` консистентными по DI | `SchemaExporter`, `DumpSplitter` |
| **P3** | Streaming-чтение для `DumpSplitter` | `DumpSplitter` |

---

## Итог

Приложение хорошо структурировано, современно (C# 12, nullable, source-gen JSON), быстро и покрыто тестами. Основные риски — **безопасность SQL-генерации**, **нестабильная работа с файлами/историей**, **разрастание CLI и статических хелперов**, **несколько узких мест производительности** (кеши, парсинг, параллелизм). Самые важные: защитить SQL-интерполяцию, сделать историю атомарной и разбить `Program.cs`.
