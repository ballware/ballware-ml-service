# Best practice: model definitions

Patterns only. Everything below is derived from the implementation
(`src/Ballware.Ml.Engine.AutoMl/`, `src/Ballware.Ml.Metadata/`) and from generic reference usage;
**no tenant-specific metadata, queries or artifacts are reproduced here** — replace every `<…>`
placeholder.

## Definition template

`MlModel` payload for `POST /ml/mlmodel/save?identifier=primary` (see
[../api/generic-crud.md](../api/generic-crud.md#save)):

```json
{
  "Id": "<model-uuid>",
  "Identifier": "<model-identifier>",
  "Type": 1,
  "TrainSql": "<plain select returning exactly the feature columns plus the label column>",
  "TrainState": 0,
  "TrainResult": null,
  "Options": "<JSON string, see below>"
}
```

`Options` skeleton (camelCase keys, embedded as a JSON **string**):

```json
{
  "trainingTime": 120,
  "trainingFileName": "<model-identifier>_model.zip",
  "trainingLogFileName": "<model-identifier>_train.log",
  "featureFields": [
    { "type": 1, "name": "<featureColumnA>" },
    { "type": 1, "name": "<featureColumnB>" },
    { "type": 0, "name": "<featureFlag>" }
  ],
  "predictionField": { "type": 3, "name": "<labelColumn>" }
}
```

`trainingFileName` defaults to `model.zip` when omitted (`AutoMlExecutor.cs:104-106, 200`). Giving each
model its own artifact name is the observed reference convention — it makes attachments identifiable in
the Storage Service and keeps the artifact discoverable through `Options` alone. `trainingLogFileName`
has **no** default: if it is absent, no log is uploaded (`AutoMlExecutor.cs:140`). Keep the log name
only where the AutoML search log is actually inspected.

## Feature design

1. **Regression only.** `Type` must be `1` (`MlModelTypes.cs:3-6`); any other value produces an empty
   column set and schema (`AutoMlModelBuilder.cs:19-54, 64-78`).
2. **Column names are the contract.** Training rows are mapped by name (`AutoMlExecutor.cs:65-75`) and
   prediction input keys are matched against `featureFields[].name`
   (`AutoMlModelBuilder.cs:161-171`). `TrainSql` aliases, `Options` names and prediction request keys
   must be identical, including case for the JSON body.
3. **Pick the numeric type deliberately** (`FieldTypes.cs:3-12`,
   [model-lifecycle.md](../concepts/model-lifecycle.md#column-classification-and-typing)):
   - `1 Int` and `2 Decimal` become **single-precision** `Single` columns in the training data view
     (`AutoMlModelBuilder.cs:89-94`) — fine for counts, hours, small magnitudes.
   - `3 Double` stays double precision (`AutoMlModelBuilder.cs:94`) — use for large or high-precision
     measures.
   - `0 Bool` and `4 String` are treated as **categorical** by the featurizer
     (`AutoMlModelBuilder.cs:29-31, 41-43`); keep categorical values small and stable.
   - `5 Date`, `6 Datetime`, `7 Time` are **ignored** by the AutoML pipeline
     (`AutoMlModelBuilder.cs:44-48`). Do not declare raw timestamps as features — derive integers
     (weekday, hour, month, `isWeekend`) in `TrainSql` instead.
4. **The label must be numeric.** `predictionField` is the AutoML label column
   (`AutoMlModelBuilder.cs:52`); use `1`, `2` or `3`. A `4 String` label produces an unusable
   regression experiment.
5. **Feature count.** Every declared feature appears in the data view schema
   (`AutoMlModelBuilder.cs:60-81`); `TrainSql` must return all of them on every row. Start with the few
   features that carry the signal — more features widen the AutoML search without necessarily improving
   R².

## `trainingTime`

`trainingTime` is the AutoML experiment budget in seconds (`AutoMlExecutor.cs:90`):

| Budget | Use |
| --- | --- |
| omitted / `0` | falls back to `60` s (`AutoMlExecutor.cs:90`) |
| `15`–`60` | smoke tests, small feature sets, CI/dev iterations |
| `60`–`300` | typical production regressions |
| `> 300` | only for wider search spaces; the budget applies per training trigger and delays `UpToDate` |

The budget does not change data volume: `TrainingRecordCount`, `DurationInMilliseconds`, `PeakCpu` and
`PeakMemoryInMegaByte` are recorded in `TrainResult` (`AutoMlExecutor.cs:111-124`) — use them to decide
whether more budget or fewer features is the right adjustment.

## `TrainSql` patterns

`TrainSql` is executed verbatim by the Generic Service against the tenant's data source
([integration-dependencies.md](../concepts/integration-dependencies.md#generic-service)). Conventions
that keep definitions portable:

- **Aggregate to the training grain.** Return one row per training observation, e.g.
  `select <expr> as <featureColumnA>, <expr> as <featureColumnB>, count(*) as <labelColumn> from … group by 1, 2`.
- **Alias every column** to the exact feature/label name. Unnamed or duplicate columns break the row →
  object mapping.
- **Do not select key, tenant or audit columns** — they are not declared as features and simply widen
  each row.
- **Derive time features as integers** (`datepart`-style expressions aliased to `Day`, `Hour`, …)
  instead of declaring `5`/`6`/`7` fields.
- **Encode flags as booleans or short categorical strings** (`isWeekend`, `shift`) rather than free
  text.
- **Keep the query provider-agnostic where a definition must run on both** `postgres` and `mssql`
  deployments (`Startup.cs:271-279`), and remember that it runs with the tenant's permissions.
- The training result set must be non-empty — an empty set trains on zero rows and reports
  `TrainingRecordCount: 0` (or an error, `AutoMlExecutor.cs:70, 111-137`).

## Lifecycle hygiene

- **Set `TrainState` deliberately.** The service never resets it on save; use `1` (`Outdated`) in the
  payload when a definition changed so consumers can detect staleness
  ([model-lifecycle.md](../concepts/model-lifecycle.md#trainstate-machine)).
- **Train after every definition change** — the artifact is only rewritten by a training run. Until
  then predictions still use the artifact of the previous definition, and predictions load the artifact
  by the **current** `Options.trainingFileName` (`AutoMlExecutor.cs:199-200`), so renaming the artifact
  without retraining breaks prediction with a `500`.
- **Renaming `Identifier` breaks callers, not artifacts** — attachments are owned by the model uuid, but
  the prediction cache key is `tenant + identifier` (`InMemoryTenantAwareModelCache.cs:14-16`) and
  `consumebyidentifier…` addresses the model by name. Purge or wait out `Cache.CacheExpirationHours`
  (default 1 h, `CacheOptions.cs:5`) after renames.
- **A failed retrain keeps the previous engine in cache** — only a successful run purges it
  (`AutoMlExecutor.cs:109`). Check `TrainState`/`TrainResult` before trusting predictions.
- **Compare models by `Metric` (R²)** in `TrainResult`; treat `Error` runs as blocking and read the
  recorded `Exception` message first.
- **Address predictions by `Identifier` in user-facing flows** and by uuid only internally — names are
  portable across environments, uuids are not.
