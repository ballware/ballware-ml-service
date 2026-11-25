# Request examples

Complete sequences against `https://localhost:6003` (development HTTPS endpoint,
`src/Ballware.Ml.Service/appsettings.Development.json:13-18`). Every path below exists in
[openapi/ml.json](../openapi/ml.json) or [openapi/service.json](../openapi/service.json).

Placeholders: `$USER_TOKEN` (user token with the `mlApi` scope and a `tenant` claim),
`$SERVICE_TOKEN` (client-credentials token with the `serviceApi` scope), `<tenantId>`, `<userId>`.

Generate a model uuid once and reuse it (the editing API treats `Id` as the model uuid):

```bash
MODEL_ID=$(uuidgen | tr 'A-Z' 'a-z')
```

## 1. Create and define a model

Start from the template the editing API offers (`GET /ml/mlmodel/new?identifier=primary` returns a
fresh document with a new `Id`; the persisted surrogate key is not part of the payload):

```bash
curl -s "https://localhost:6003/ml/mlmodel/new?identifier=primary" \
  -H "Authorization: Bearer $USER_TOKEN"
```

```json
{ "Id": "<generated-guid>", "Identifier": null, "Type": 0, "TrainSql": null, "TrainState": 0, "TrainResult": null, "Options": null }
```

Save the definition (`Options` is a **JSON string** — escape the inner quotes):

```bash
curl -s -X POST "https://localhost:6003/ml/mlmodel/save?identifier=primary" \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "Id": "'"$MODEL_ID"'",
    "Identifier": "daytime-personal-count",
    "Type": 1,
    "TrainSql": "select datepart(weekday, r.started) as Day, datepart(hour, r.started) as Hour, count(*) as Result from <tenant source> r group by 1, 2",
    "TrainState": 0,
    "TrainResult": null,
    "Options": "{\"trainingTime\":120,\"trainingFileName\":\"model_current.zip\",\"trainingLogFileName\":\"model_current.txt\",\"featureFields\":[{\"type\":1,\"name\":\"Day\"},{\"type\":1,\"name\":\"Hour\"}],\"predictionField\":{\"type\":1,\"name\":\"Result\"}}"
  }'
```

Verify it (`200`; `404` if the tenant or the `meta.mlmodel` entity metadata is missing, `401` if
`meta.mlmodel.edit`/`view` was denied):

```bash
curl -s "https://localhost:6003/ml/mlmodel/byid?identifier=primary&id=$MODEL_ID" \
  -H "Authorization: Bearer $USER_TOKEN"
```

## 2. Train and poll the state

Trigger training (`202 Accepted`, empty body — the snapshot's `201` is a declaration mismatch):

```bash
curl -s -i -X POST "https://localhost:6003/ml/mlmodel/train?id=$MODEL_ID" \
  -H "Authorization: Bearer $USER_TOKEN"
```

Poll `TrainState` until it leaves `5` (`Queued`) / `6` (`InProgress`):

```bash
until s=$(curl -s "https://localhost:6003/ml/mlmodel/byid?identifier=primary&id=$MODEL_ID" \
            -H "Authorization: Bearer $USER_TOKEN" | python3 -c 'import json,sys; print(json.load(sys.stdin)["TrainState"])'); \
      [ "$s" != "5" ] && [ "$s" != "6" ]; do sleep 2; done; echo "TrainState=$s"
```

`10` means the artifact is available; `99` means failure — read `TrainResult` for the message:

```bash
curl -s "https://localhost:6003/ml/mlmodel/byid?identifier=primary&id=$MODEL_ID" \
  -H "Authorization: Bearer $USER_TOKEN" | python3 -c 'import json,sys; m=json.load(sys.stdin); print(m["TrainState"]); print(m["TrainResult"])'
```

## 3. Predict

Preferred variant — POST service API with typed JSON values:

```bash
curl -s -X POST \
  "https://localhost:6003/ml/mlmodel/consumebyidentifierbehalfofuser/<tenantId>/<userId>/daytime-personal-count" \
  -H "Authorization: Bearer $SERVICE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "Day": 3, "Hour": 7 }'
```

```json
{ "Day": 3, "Hour": 7, "Score": 12.4 }
```

Same model by uuid:

```bash
curl -s -X POST \
  "https://localhost:6003/ml/mlmodel/consumebyidbehalfofuser/<tenantId>/<userId>/$MODEL_ID" \
  -H "Authorization: Bearer $SERVICE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "Day": 3, "Hour": 7 }'
```

The GET user-API variant exists but does not parse typed feature values correctly
([model-lifecycle.md](../concepts/model-lifecycle.md#known-limitation-get-prediction-input-binding)):

```bash
curl -s "https://localhost:6003/ml/mlmodel/consumebyid/$MODEL_ID?Day=3&Hour=7" \
  -H "Authorization: Bearer $USER_TOKEN"
```

## 4. List, export and re-import

```bash
curl -s "https://localhost:6003/ml/mlmodel/selectlist" -H "Authorization: Bearer $USER_TOKEN"
curl -s "https://localhost:6003/ml/mlmodel/all?identifier=primary" -H "Authorization: Bearer $USER_TOKEN"

# inline export (right: meta.mlmodel.exportjson)
curl -s "https://localhost:6003/ml/mlmodel/export?identifier=exportjson" \
  -H "Authorization: Bearer $USER_TOKEN" > models.json

# anonymous download link, valid for one day
curl -s -X POST "https://localhost:6003/ml/mlmodel/exporturl?identifier=exportjson" \
  -H "Authorization: Bearer $USER_TOKEN" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d 'id='"$MODEL_ID"
# → { "TenantId": "<tenantId>", "Id": "<exportId>" }
curl -s "https://localhost:6003/ml/mlmodel/download/<tenantId>/<exportId>" -o models.json

# bulk import (background job; 201 Created)
curl -s -X POST "https://localhost:6003/ml/mlmodel/import?identifier=primary" \
  -H "Authorization: Bearer $USER_TOKEN" \
  -F "files=@models.json"
```

## 5. Remove

```bash
curl -s -X DELETE "https://localhost:6003/ml/mlmodel/remove/$MODEL_ID" \
  -H "Authorization: Bearer $USER_TOKEN"
```

Model artifacts in the Storage Service are not removed with the row
([generic-crud.md](generic-crud.md#remove)).

## 6. Reporting datasource (service token)

```bash
curl -s "https://localhost:6003/ml/tenant/reportmldatasourcesfortenant/<tenantId>" \
  -H "Authorization: Bearer $SERVICE_TOKEN"
```

The response contains the ML storage connection string — handle it as a secret
([service-api.md](service-api.md#security-note)).
