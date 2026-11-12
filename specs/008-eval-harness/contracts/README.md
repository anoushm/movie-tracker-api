# 008-eval-harness — Contracts

The harness has two input-file contracts and one output-file contract. All three are plain JSON Schema (2020-12).

**Documentation-only.** The harness does **not** reference a JSON Schema validation package and does **not** validate the files against these schemas at run time. The loader enforces only unique ids per file (spec FR-004a). `data-model.md` is authoritative when the schemas and the data model diverge.

| File | Purpose | Schema |
|---|---|---|
| `MovieTracker.Evals/eval-movie-cases.json` | The two plot-anchor cases, committed | [`eval-movie-cases.schema.json`](./eval-movie-cases.schema.json) |
| `MovieTracker.Evals/eval-movie-templates.json` | The eight generated-case templates, committed | [`eval-movie-templates.schema.json`](./eval-movie-templates.schema.json) |
| `MovieTracker.Evals/results/last-run.json` | Last run's results, committed | [`last-run.schema.json`](./last-run.schema.json) |

## HTTP contracts the harness depends on (not defined here)

The harness is also bound to two HTTP shapes defined in `MovieTracker.Api` — unchanged by this slice. ASP.NET Core minimal APIs serialize with `JsonSerializerDefaults.Web` (camelCase).

- `GET /api/chat/start` → `{ "chatId": "<string>" }`
  Source: `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` (`ChatStart` route).
- `POST /api/chat/{chatId}/ask/v2` with body `{ "input": "<string>" }`
  → `{ "funnyFact": string | null, "turn": { "role": "assistant" | "user", "text": string, "movieList": [...] } }`
  Source: `MovieTracker.Api/Chat/ChatTurnEndpoints.cs` and `MovieTracker.Api/Models/{ChatTurnResponse, AssistantChatMessage, MovieViewModel}.cs`.
  The harness reads `turn.text`, `funnyFact`, and `turn.movieList` (spec FR-003; rich-eval-prompt §1). Each `turn.movieList` entry is projected to `{ id, title, releaseDate, genreIds, genreNames }` for the Context payload the checks consume.

## Third-party contracts the harness calls directly

Not defined in this repo; documented here for reviewer orientation. Keyed by `TheMovieDb:Api-Key` and `OpenMovieDb:Api-Key` read through the hosted API's `IConfiguration` (`research.md` Decision 3).

| Request | Fields used |
|---|---|
| `GET https://api.themoviedb.org/3/movie/popular?api_key=<key>&region=<US\|IN\|SE>&page=<1..5>` | pick one random entry per region; keep `id` and `title` |
| `GET https://api.themoviedb.org/3/movie/{id}?api_key=<key>&append_to_response=external_ids` | `overview`, `release_date`, `genres[].{id,name}`, `external_ids.imdb_id` |
| `GET https://api.themoviedb.org/3/movie/{id}/credits?api_key=<key>` | first `crew[*]` with `job == "Director"` → `name`; `cast[*]` with `order == 0` → `id` and `name` |
| `GET https://api.themoviedb.org/3/person/{person_id}/movie_credits?api_key=<key>` | `cast[*].{id, title}` (actor's acting-filmography id set + title set) |
| `GET https://api.themoviedb.org/3/movie/{id}/videos?api_key=<key>` | `results[*]` filtered to `site == "YouTube"` AND `type == "Trailer"` → `key` |
| `GET https://www.omdbapi.com/?i=<imdbId>&apikey=<key>` | `imdbRating` |
