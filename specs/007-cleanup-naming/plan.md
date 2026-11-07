# Implementation Plan: Cleanup and Naming

**Branch**: `007-cleanup-naming` (nominal; owner controls git) | **Date**: 2026-11-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-cleanup-naming/spec.md`

## Summary

A pure refactor of `MovieTracker.Api/`: rename each tool class (dropping the misleading "Agent" suffix), move the one real agent out of `Program.cs` into `Agents/MovieAgent.cs`, flatten the folder layout to six application-source subfolders, delete legacy `WeatherForecast`/`Ask`/ROP-Result leftovers, split `Program.cs` into six registration extension methods, and update only the "Project Structure" section of `CLAUDE.md`. Technical approach: mechanical renames and file moves, with strict preservation of tool method names, telemetry names, storage shapes, prompt strings, and HTTP contracts for retained endpoints. No new tests, no new packages, no new abstractions.

## Technical Context

**Language/Version**: `net10.0` target framework (as configured in `MovieTracker.Api.csproj`), ASP.NET Core Web API.

**Primary Dependencies**: Microsoft.Agents.AI, Microsoft.Extensions.AI, Azure.AI.OpenAI, Microsoft.Azure.Cosmos, TMDbLib, Azure.Monitor.OpenTelemetry.Exporter — all already referenced, none added or removed by this slice.

**Storage**: Azure Cosmos DB (serverless, NoSQL). Document shape and Cosmos partition key are frozen for this slice.

**Testing**: No test project exists in the repository today. This slice does not create one. Verification is `dotnet build` + manual smoke tests via `quickstart.md`.

**Target Platform**: Linux container on Azure Container Apps (unchanged).

**Project Type**: Single web-service project (`MovieTracker.Api`) plus `infrastructure/` Bicep. Only `MovieTracker.Api/` and repository-root `CLAUDE.md` are touched by this slice.

**Performance Goals**: N/A (no behavior change).

**Constraints**: Every retained endpoint response, every Cosmos document field, every telemetry name, every LLM-observable string, and every tool method name registered via `AIFunctionFactory.Create` MUST remain byte-identical.

**Scale/Scope**: Approximately 30 source files move or are deleted; one file (`Program.cs`) is heavily reorganised into six new extension-method classes plus one new `HealthEndpoints.cs`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Managed Identity Over Secrets**: No change to Azure auth paths. `CosmosClient` still constructs with `DefaultAzureCredential`. **PASS**.
- **II. Infrastructure Is Bicep, Not Clicks**: This slice touches zero Bicep. **PASS**.
- **III. Incremental Migration With Declared Boundaries**: Spec declares explicit in-scope and out-of-scope lists; this plan conforms to that boundary. **PASS**.
- **IV. Dependencies Prove Themselves**: `/health/cosmos`, `/health/tmdb`, `/health/openai` are preserved verbatim (same handlers, same shape). **PASS**.
- **V. No Secret Material In Source**: No change to how config or secrets are sourced. **PASS**.
- **Development Workflow — Codex cross-review at each stage**: This plan is being written to be reviewed by Codex per the constitution. **PASS**.

**Initial gate verdict**: PASS. No violations, no Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/007-cleanup-naming/
├── prompt.md         # Authoritative feature prompt (pre-existing)
├── spec.md           # /speckit-specify output (round-3 approved)
├── plan.md           # This file
├── research.md       # Phase 0 output (this stage)
├── data-model.md     # Phase 1 output (this stage) — "no model changes" declaration
├── quickstart.md     # Phase 1 output (this stage) — smoke-test runbook
├── checklists/
│   └── requirements.md
└── tasks.md          # Written by /speckit-tasks (next stage)
```

No `contracts/` directory is produced: the slice changes zero HTTP contracts on retained endpoints; a contract artefact would restate the current OpenAPI spec and invite accidental drift.

### Source Code (repository root)

Pre-slice state (what exists today — verified against the working tree):

```text
MovieTracker.Api/
├── Program.cs
├── WeatherForecast.cs                             # delete
├── MovieTracker.Api.http                          # edit: remove weatherforecast request only
├── MovieTracker.Api.csproj                        # unchanged
├── appsettings.json                               # unchanged
├── appsettings.Development.json                   # unchanged
├── Dockerfile                                     # unchanged
├── Properties/                                    # unchanged
├── Agents/
│   ├── OpenMovieDbAgent.cs                        # rename -> Tools/OmdbTools.cs
│   ├── TrailerAgent.cs                            # rename -> Tools/TrailerTools.cs
│   ├── WikipediaSearchAgent.cs                    # rename -> Tools/WikipediaTools.cs
│   ├── MovieRatingResult.cs                       # move -> Models/
│   ├── RatingComparisonResult.cs                  # move -> Models/
│   ├── WikidataHead.cs                            # move -> Models/
│   ├── WikidataInfo.cs                            # move -> Models/
│   ├── WikidataResponse.cs                        # move -> Models/
│   ├── WikidataResults.cs                         # move -> Models/
│   ├── WikidataValue.cs                           # move -> Models/
│   ├── WikipediaContent.cs                        # move -> Models/
│   ├── WikipediaResult.cs                         # move -> Models/
│   ├── WikipediaSummary.cs                        # move -> Models/
│   └── WikipediaThumbnail.cs                      # move -> Models/
├── Prompts/
│   ├── ChatPlanner.cs                             # move -> Tools/
│   ├── DateTimeKernelFunctions.cs                 # rename -> Tools/DateTimeTools.cs
│   └── TheMovieDBKernelFunctions.cs               # rename -> Tools/TmdbTools.cs; extract
│                                                  #   top-level records `MovieSearchResult` and
│                                                  #   `GenresItem` into separate `Models/*.cs` files
│                                                  #   (FR-003: one type per file)
├── Core/
│   ├── AppError.cs                                # delete
│   ├── Result.cs                                  # delete
│   ├── ResultExtensions.cs                        # delete
│   └── CosmosSystemTextJsonSerializer.cs          # move -> ChatSessions/
├── Controllers/
│   ├── AskController.cs                           # delete
│   └── WeatherForecastController.cs               # delete
├── Models/
│   ├── AskRequestDot.cs                           # delete
│   └── AskResponseDot.cs                          # delete
├── Chat/
│   ├── Ask.cs                                     # move -> Models/
│   ├── AssistantChatMessage.cs                    # move -> Models/
│   ├── ChatMessage.cs                             # move -> Models/
│   ├── ChatMessageResponse.cs                     # move -> Models/
│   ├── ChatTurnEndpoints.cs                       # stays in Chat/; add MapChatEndpoints
│   ├── ChatTurnResponse.cs                        # move -> Models/
│   ├── MovieListItem.cs                           # move -> Models/
│   ├── MovieListResponse.cs                       # move -> Models/
│   ├── MovieTrailerInfo.cs                        # move -> Models/
│   ├── MovieViewModel.cs                          # move -> Models/
│   └── UserChatMessage.cs                         # move -> Models/
├── ChatSessions/
│   ├── ChatSessionRepository.cs                   # stays
│   ├── ChatStartSystemPrompt.cs                   # stays
│   └── MoviceTrackerChatSession.cs                # rename -> MovieTrackerChatSession.cs
│   # NEW file added here post-slice: ChatSessionRegistration.cs (AddChatSessions)
└── Telemetry/
    ├── InstrumentedAIFunction.cs                  # stays
    ├── ModelPricing.cs                            # stays
    └── Telemetry.cs                               # stays
    # NEW file added here post-slice: TelemetryRegistration.cs (AddTelemetry)
```

Post-slice state (what will exist):

```text
MovieTracker.Api/
├── Program.cs                                     # composition root (short)
├── HealthEndpoints.cs                             # NEW: MapHealthEndpoints extension
├── MovieTracker.Api.http                          # weatherforecast request removed
├── MovieTracker.Api.csproj                        # unchanged
├── appsettings.json                               # unchanged
├── appsettings.Development.json                   # unchanged
├── Dockerfile                                     # unchanged
├── Properties/                                    # unchanged
├── Agents/
│   └── MovieAgent.cs                              # NEW: AddMovieAgent extension
├── Tools/
│   ├── ChatPlanner.cs
│   ├── DateTimeTools.cs
│   ├── OmdbTools.cs
│   ├── TmdbTools.cs
│   ├── TrailerTools.cs
│   ├── WikipediaTools.cs
│   └── MovieTools.cs                              # NEW: AddMovieTools extension
├── Chat/
│   ├── ChatTurnEndpoints.cs                       # keeps Ask/AskV2; gains MapChatEndpoints
│   └── (no other files)
├── Models/
│   ├── Ask.cs
│   ├── AssistantChatMessage.cs
│   ├── ChatMessage.cs
│   ├── ChatMessageResponse.cs
│   ├── ChatTurnResponse.cs
│   ├── GenresItem.cs                              # extracted from TheMovieDBKernelFunctions.cs
│   ├── MovieListItem.cs
│   ├── MovieListResponse.cs
│   ├── MovieRatingResult.cs
│   ├── MovieSearchResult.cs                       # extracted from TheMovieDBKernelFunctions.cs
│   ├── MovieTrailerInfo.cs
│   ├── MovieViewModel.cs
│   ├── RatingComparisonResult.cs
│   ├── UserChatMessage.cs
│   ├── WikidataHead.cs
│   ├── WikidataInfo.cs
│   ├── WikidataResponse.cs
│   ├── WikidataResults.cs
│   ├── WikidataValue.cs
│   ├── WikipediaContent.cs
│   ├── WikipediaResult.cs
│   ├── WikipediaSummary.cs
│   └── WikipediaThumbnail.cs
├── ChatSessions/
│   ├── ChatSessionRegistration.cs                 # NEW: static class with AddChatSessions extension
│   ├── ChatSessionRepository.cs
│   ├── ChatStartSystemPrompt.cs
│   ├── CosmosSystemTextJsonSerializer.cs
│   └── MovieTrackerChatSession.cs                 # typo-fixed class name, same JSON shape
└── Telemetry/
    ├── InstrumentedAIFunction.cs
    ├── ModelPricing.cs
    ├── Telemetry.cs
    └── TelemetryRegistration.cs                   # NEW: static class with AddTelemetry extension
```

**Structure Decision**: Six application-source subfolders (`Agents/`, `Tools/`, `Chat/`, `Models/`, `ChatSessions/`, `Telemetry/`), one level deep. `Properties/`, `bin/`, `obj/` are framework/build-owned and not application source — the layout rule does not apply to them. One registration extension per folder of what it registers, placed next to the types it registers. The two new root files (`HealthEndpoints.cs`, revised `Program.cs`) are composition-root concerns that don't belong to any one subfolder.

### Registration extensions (one per Program.cs responsibility)

| Extension method | Static class (file) | Folder | What it registers |
|---|---|---|---|
| `AddTelemetry(this WebApplicationBuilder builder)` | `TelemetryRegistration` (file: `Telemetry/TelemetryRegistration.cs`) | `Telemetry/` | Reads `builder.Configuration` for the Application Insights connection string and the sensitive-telemetry flag; calls `builder.Logging.AddOpenTelemetry(...)` (logging exporter), `builder.Services.AddOpenTelemetry().WithTracing(...).WithMetrics(...)` (tracing + metrics), and registers the `Tracer` and `ModelPricing` singletons. The extension takes the whole `WebApplicationBuilder` because configuring OpenTelemetry logging requires `builder.Logging`, which is not reachable from `IServiceCollection` alone. |
| `AddMovieTools(this IServiceCollection services, string tmdbApiKey)` | `MovieTools` (file: `Tools/MovieTools.cs`) | `Tools/` | Named `HttpClient("HttpClient")` (with the existing standard resilience handler), `TMDbClient`, `TmdbTools`, `OmdbTools`, `TrailerTools`, `ChatPlanner`, and the typed `HttpClient<WikipediaTools>` with its existing `UserAgent` headers and 15-second `Timeout`. |
| `AddMovieAgent(this IServiceCollection services, Uri openAiEndpoint, string openAiApiKey, string openAiDeploymentName, bool enableSensitiveTelemetry)` | `MovieAgent` (file: `Agents/MovieAgent.cs`) | `Agents/` | `IChatClient` and the `AIAgent`. The agent is built with tool order TMDb → DateTime → ChatPlanner; each `AIFunction` is wrapped in `InstrumentedAIFunction`; `ChatClientAgentOptions.Name = "MovieTracker.Api"` and the logger category `MovieTracker.Api.ToolCalls` stay byte-identical. This static class is the sole surviving `*Agent` name in production code (FR-001). |
| `AddChatSessions(this IServiceCollection services, string cosmosEndpoint, string cosmosDatabase, string cosmosContainer)` | `ChatSessionRegistration` (file: `ChatSessions/ChatSessionRegistration.cs`) | `ChatSessions/` | `CosmosClient` constructed with `DefaultAzureCredential` and `new CosmosSystemTextJsonSerializer()`, plus `ChatSessionRepository`. |
| `MapHealthEndpoints(this WebApplication app)` | `HealthEndpoints` (file: `HealthEndpoints.cs`) | repo root | `/health`, `/health/ready`, `/health/cosmos`, `/health/tmdb`, `/health/openai`, `/version` — each handler lifted verbatim from `Program.cs`. |
| `MapChatEndpoints(this WebApplication app)` | `ChatTurnEndpoints` (file: `Chat/ChatTurnEndpoints.cs`, same file that already contains `Ask`/`AskV2`) | `Chat/` | `/api/chat/start`, `/api/chat/{chatId}/ask`, `/api/chat/{chatId}/ask/v2` — handlers unchanged; `start` moves in from `Program.cs`, `Ask` and `AskV2` already live here. |

No options class is introduced. `AzureOpenAIConfig` is not a type in this codebase and this plan does not add one — each extension takes the scalar values it needs directly.

**Ordering constraint**: In the composition root, `AddMovieTools` MUST be called before `AddMovieAgent`, because the latter consumes the named `HttpClient("HttpClient")` that the former registers via `IHttpClientFactory`.

### Program.cs post-slice shape (indicative, ~40 lines)

```text
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

IConfiguration configuration = builder.Configuration;

Uri openAiEndpoint = new Uri(configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:Endpoint"));
string openAiApiKey = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:ApiKey");
string openAiDeploymentName = configuration["AzureOpenAI:DeploymentName"] ?? throw new InvalidOperationException("Missing configuration value: AzureOpenAI:DeploymentName");
string tmdbApiKey = configuration["TheMovieDb:Api-Key"] ?? throw new InvalidOperationException("Missing configuration value: TheMovieDb:Api-Key");
string cosmosEndpoint = configuration["Cosmos:Endpoint"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Endpoint");
string cosmosDatabase = configuration["Cosmos:Database"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Database");
string cosmosContainer = configuration["Cosmos:Container"] ?? throw new InvalidOperationException("Missing configuration value: Cosmos:Container");
bool enableSensitiveTelemetry = configuration.GetValue<bool?>("Telemetry:Enable-Sensitive-Data") ?? true;

builder.AddTelemetry();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMovieTools(tmdbApiKey);
builder.Services.AddMovieAgent(openAiEndpoint, openAiApiKey, openAiDeploymentName, enableSensitiveTelemetry);
builder.Services.AddChatSessions(cosmosEndpoint, cosmosDatabase, cosmosContainer);
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Container"))
{
    app.UseHttpsRedirection();
}

app.MapHealthEndpoints();
app.MapChatEndpoints();

app.Run();
```

Every fail-fast `??` error message is preserved verbatim from the pre-slice `Program.cs`.

### What stays identical (frozen)

- Every registered tool method name (every `AIFunctionFactory.Create(...)` target).
- `ChatClientAgentOptions.Name = "MovieTracker.Api"`.
- Logger category string `MovieTracker.Api.ToolCalls`.
- Telemetry source name, meter name, span names (all via `MovieTracker.Api.Telemetry.Telemetry`).
- Every `[Description]` attribute value on tool methods.
- `ChatStartSystemPrompt.Text` string.
- `MovieTrackerChatSession.id`, `ChatMessage.role`, every JSON type discriminator, every record property name.
- Every retained endpoint path, HTTP verb, status code, and response body shape.
- `appsettings.json`, `appsettings.Development.json`, `MovieTracker.Api.csproj`, every file under `infrastructure/`, every file under `.github/workflows/`, `.gitignore`.

## Complexity Tracking

No violations. The slice adds zero new abstractions; it removes several (ROP `Result`, `AppError`, `AskController`, `WeatherForecastController`).
