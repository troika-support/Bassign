# AI in Bassign (step 13)

AI **suggests**; people decide. Every AI feature has a safe fallback, so Bassign works the same with AI off.

| Feature | Where | Fallback |
|---|---|---|
| Auto-assign suggestion | Assign dialog (single task) | People listed lightest-workload first |
| Daily summary | Dashboard (managers) → digest job in step 14 | "Summary unavailable" — the numbers still show |

## Guardrails (spec §3.F)
- **Validated output** — the model picks a short key (c1, c2…) for a person we offered; anything else, or confidence < `Ai:MinConfidence` (0.6), is ignored. Real user ids never leave the server.
- **Untrusted text** — task text sits inside `<task>` / `<candidates>` delimiters, delimiter characters are stripped from user text, and the system prompt says to treat it as data. Worst case an injected task can only pick another real candidate, which a manager still has to confirm.
- **Bounded** — input capped at `Ai:MaxInputChars`, `max_tokens` on every call, a hard `Ai:TimeoutSeconds` timeout.
- **Metered** — every call writes a row to the `AiUsage` table (tokens in/out, duration, outcome).
- **"Skills"** are inferred from each person's last 5 completed task titles — no extra data entry.

## Choosing a provider (config only)
```jsonc
"Ai": { "Provider": "claude", "Model": "claude-haiku-5-5" }                        // default
"Ai": { "Provider": "openai", "Model": "gpt-6-luna" }                              // OpenAI
"Ai": { "Provider": "openai", "Model": "deepseek-flash", "BaseUrl": "https://api.deepseek.com/v1" }
"Ai": { "Provider": "openai", "Model": "llama3.2", "BaseUrl": "http://localhost:11434/v1" }   // local Ollama, no key
"Ai": { "Provider": "none" }                                                       // AI off
```
Optional embeddings (OpenAI-compatible only): `"EmbeddingModel": "text-embedding-3-small"` adds a similarity score per person to the prompt. Set `"UseJsonSchema": false` for a model that rejects structured output.

**The key is a secret.** Development (both hosts share one store):
```
dotnet user-secrets set "Ai:ApiKey" "sk-ant-..." --project src/Bassign.Api
```
Production (IIS): environment variable `Ai__ApiKey`. Tell clients that task titles and staff names are sent to the provider (cross-border transfer under the Cyber and Data Protection Act) — or use local Ollama.

## Offline evaluation
`tests/Bassign.UnitTests/Ai/AssignmentEval.cs` holds golden cases (skills, workload tie-break, "nobody fits", prompt injection). The harness is tested on every build; to score a **real** model (costs well under a cent):
```
$env:Ai__ApiKey="sk-ant-..."; dotnet test tests/Bassign.UnitTests --filter "FullyQualifiedName~AssignmentEval" -- xUnit.Explicit=only
```
(or run *Live_model_meets_the_threshold* from Test Explorer). It must stay ≥ 80% after any prompt or model change.
