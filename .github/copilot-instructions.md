# movie-tracker-api — Copilot Instructions

## What this is
A .NET 10 movie-assistant API. Exposes `/ask` plus health endpoints, integrates with
Azure OpenAI, and queries TheMovieDB. Deployed to Azure Container Apps via Bicep
(Container Apps environment, ACR, Key Vault, RBAC, monitoring, autoscaling).

## Coding standards
Follow the global standards in `~/.copilot/copilot-instructions.md`: no `var`, no inline
comments, braces on every control-flow statement, fire-and-forget cross-service calls,
shared logic at the lowest common layer.

## Project-specific rules
- **Secrets never live in source.** No keys, connection strings, or tokens in
  `appsettings*.json`. Local dev uses user-secrets; deployed config reads from Key Vault.
- **Key Vault is the source of truth for secrets** in deployed environments. The app
  must read secrets from Key Vault — it is already provisioned and RBAC'd, so use it.
- **Config binding:** C# uses hierarchical keys (`AzureOpenAI:Endpoint`). When setting
  these as container env vars in Bicep, use the double-underscore form
  (`AzureOpenAI__Endpoint`) so .NET binds them correctly. Do not mix flat
  (`AZURE_OPENAI_ENDPOINT`) and hierarchical names.
- **Validate required config at startup.** Do not use null-forgiving (`!`) on required
  config values. Fail fast with a clear message naming the missing key.
- **Parse defensively.** No unchecked `int.Parse` / `DateTime.Parse` on user or agent
  input. Use `TryParse` and handle the failure path; malformed input must not crash a request.
- **CORS:** no wildcard (`*`) origins in deployed config. Restrict to known origins.
- **Infra must be wired end to end.** No empty/placeholder values for things like
  `containerRegistryServer`; the Container App must actually pull the intended ACR image.
- **No hardcoded principal IDs** in parameter files. Parameterize identities per environment.

## Build / test
- Build: `dotnet build` from the project directory.
- Confirm the build is clean before proposing changes are review-complete.
