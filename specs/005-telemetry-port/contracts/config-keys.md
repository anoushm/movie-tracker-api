# Contract: Configuration Keys

**Feature**: 005-telemetry-port
**Spec**: [spec.md](../spec.md) FR-006, FR-009, FR-010, FR-012

This slice reads six config keys at startup and one block from
`appsettings.json`. None of the six keys appear with a committed value in
this repository; the three pricing keys mirror the Function App's
"deliberately no defaults" stance (research Decision 5). The connection
string is already surfaced on the Container App revision via Bicep
(research Decision 7); no change required.

## Keys consumed in `Program.cs` at startup

### `APPLICATIONINSIGHTS_CONNECTION_STRING`

| Property | Value |
|---|---|
| **Type** | string (nullable) |
| **Config path** | `builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]` (underscore spelling matches the Container App env var) |
| **Source** | Container App env var set in `infrastructure/container-app.bicep:52-54` via `secretRef: 'appinsights-connection-string'`, which is itself backed by the `appInsightsConnectionString` module parameter resolved from the Application Insights component in `main.bicep`. Locally: `dotnet user-secrets` under the same key or an env var in the dev shell. |
| **Default** | *(none)* — a missing value is a valid state. Spec FR-014 forbids application-level startup validation. |
| **Consumer** | Three exporter registrations: `AddAzureMonitorTraceExporter(options => options.ConnectionString = ...)`, `AddAzureMonitorMetricExporter(options => options.ConnectionString = ...)`, `AddAzureMonitorLogExporter(exporter => { exporter.ConnectionString = ...; ... })`. |
| **Function App parity** | The Function App uses the hyphen form `APPLICATIONINSIGHTS-CONNECTION-STRING` because Azure Functions' App Settings historically prefer hyphens; the API's env var is the underscore form (ASP.NET Core / OTel SDK standard). Research Decision 7 and spec FR-010. |

### `Telemetry:Enable-Sensitive-Data`

| Property | Value |
|---|---|
| **Type** | `bool?` (nullable boolean) |
| **Config path** | `builder.Configuration.GetValue<bool?>("Telemetry:Enable-Sensitive-Data")` |
| **Env-var form** | `Telemetry__Enable-Sensitive-Data` |
| **Default** | `true` — read once at startup with `?? true`. Carried through two sinks (chat client `.UseOpenTelemetry` and `InstrumentedAIFunction` constructor). |
| **Consumer** | (a) `IChatClient` factory: `.UseOpenTelemetry(loggerFactory, serviceName, options => options.EnableSensitiveData = enableSensitiveTelemetry)`. (b) `AIAgent` factory: `new InstrumentedAIFunction(function, toolLogger, enableSensitiveTelemetry)`. |
| **Function App parity** | Function App reads at `Program.cs:125-126` and flows to the same two sinks. Spec FR-006. |

### `AzureOpenAI:Price-Input-Per-1M`, `AzureOpenAI:Price-Output-Per-1M`, `AzureOpenAI:Price-Cached-Input-Per-1M`

| Property | Value |
|---|---|
| **Type** | double (parsed via `double.TryParse`) |
| **Config paths** | `builder.Configuration["AzureOpenAI:Price-Input-Per-1M"]`, `builder.Configuration["AzureOpenAI:Price-Output-Per-1M"]`, `builder.Configuration["AzureOpenAI:Price-Cached-Input-Per-1M"]` |
| **Env-var forms** | `AzureOpenAI__Price-Input-Per-1M`, `AzureOpenAI__Price-Output-Per-1M`, `AzureOpenAI__Price-Cached-Input-Per-1M` |
| **Default** | *(none)* — unset is the committed state (research Decision 5). The slice MUST NOT seed any value in `appsettings.json`. |
| **Consumer** | Startup code constructs `new ModelPricing(inputRate, outputRate, cachedRate?)` ONLY when `Input` and `Output` both parse; otherwise registers `null`. The resulting `ModelPricing?` flows to `RunTurnAsync` via `services.AddSingleton<ModelPricing?>(_ => pricing)`. |
| **Behaviour when unset** | `ModelPricing?` is null; `gen_ai.usage.cost_usd` emits nothing (spec FR-009 edge case). |
| **Function App parity** | Function App reads the same three keys with lowercase 'i' at `Function.cs:213-218`; the port uses the API's existing capitalisation convention (`AzureOpenAI:*` with uppercase `I`, consistent with slices 001-004). Values and semantics are unchanged. Spec FR-009. |

### `AzureOpenAI:DeploymentName`

| Property | Value |
|---|---|
| **Type** | string |
| **Config path** | `builder.Configuration["AzureOpenAI:DeploymentName"]` (already read by slice 003 at `Program.cs:27`) |
| **Default** | *(none; validated by slice 003's startup check at `Program.cs:27`)* |
| **Consumer, new in this slice** | `RunTurnAsync` passes this as the `model` dimension on `gen_ai.usage.cost_usd` via `Telemetry.RecordCost(deployedModel, usage, pricing)`. |
| **Note** | This slice reuses slice 003's startup read; no second lookup is introduced. Flow: register as `services.AddSingleton<string>(deployedModel)` or pass through `IConfiguration` resolution in the endpoint handler. (Implementation detail; the data-model's `ModelPricing?` singleton carries pricing, deployment name flows separately via `IConfiguration` in the handler to avoid building a bespoke "pricing + model name" record.) |

## Configuration block in `appsettings.json`

### `Logging:OpenTelemetry`

Added under the existing `Logging` block (which currently carries
`Logging:LogLevel` only):

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  },
  "OpenTelemetry": {
    "IncludeFormattedMessage": true,
    "IncludeScopes": true,
    "ParseStateValues": true
  }
}
```

| Property | Value |
|---|---|
| **Keys** | `Logging:OpenTelemetry:IncludeFormattedMessage`, `Logging:OpenTelemetry:IncludeScopes`, `Logging:OpenTelemetry:ParseStateValues` |
| **Type** | all three are boolean |
| **Default (committed)** | all three `true` |
| **Consumer** | The OTel logger provider binds `Logging:OpenTelemetry:*` to `OpenTelemetryLoggerOptions` automatically when `builder.Logging.AddOpenTelemetry(...)` is called (research Decision 8). |
| **Function App parity** | Function App's `appsettings.json:6-10` has the identical block. Spec FR-012. |

## Non-contracts

- **No `OTEL_*` env var** is read. The OTel SDK's auto-configuration precedence is left at the defaults (`ResourceBuilder.CreateDefault()` picks up any `OTEL_RESOURCE_ATTRIBUTES` set by the host automatically; the port adds no explicit reader).
- **No `APPINSIGHTS_PROFILER_ENABLED`, `APPINSIGHTS_SNAPSHOTFEATURE_VERSION`, or any snapshot/profiler env var** is read. These features are explicitly out of scope.
- **No `ApplicationInsights:*` config block** (the classic 2.x shape) is read. The port uses OpenTelemetry end-to-end.
- **No new `appsettings.Development.json` override**. Slice 003's dev settings stand unchanged.
