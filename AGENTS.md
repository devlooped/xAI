# xAI SDK implementation notes

- `GrokClient` is primarily backed by generated gRPC protocol clients, but voice features use xAI's documented REST/WebSocket endpoints because there are no generated voice protocol types in `src\xAI.Protocol`.
- Voice REST calls use `GrokClient.HttpHandler` (backed by `httpHandlers` cache) — a plain `SocketsHttpHandler`+Polly pipeline separate from the gRPC channel. `ChannelHandler` returns `ChannelBase` only; there is no `.Handler` property on it.
- `AsITextToSpeechClient` returns an `ITextToSpeechClient` implementation that uses `POST /v1/tts` for unary audio and `wss://.../v1/tts` for streaming audio.
- `AsISpeechToTextClient` returns an `ISpeechToTextClient` implementation that uses `POST /v1/stt` for file transcription and `wss://.../v1/stt` for raw-audio streaming transcription.
- TTS defaults follow xAI docs: voice `eve`, language `en` when omitted by `TextToSpeechOptions`, and MP3 output when no codec is specified.
- STT streaming defaults follow xAI docs: encoding `pcm` and sample rate `16000` when omitted; WebSocket input must be raw encoded audio, not MP3/WAV container bytes.
- Chat streaming `GetChatCompletionChunk.Usage` values are cumulative within a sampling segment and may reset across tool-driven segments; emit deltas (or restart deltas after a reset) so `ToChatResponse()` totals match non-streaming usage.
- `ChatOptions` mappings include `Seed`, `StopSequences`, `AllowMultipleToolCalls` → `parallel_tool_calls`, `Reasoning.Effort` → `reasoning_effort`, and `ConversationId` → `previous_response_id`. `GrokChatOptions.StoreMessages` enables stored responses and surfaces `ChatResponse.ConversationId`.
- `UsageDetails` maps `ReasoningTokenCount` and `CachedInputTokenCount` from xAI usage, plus prompt text/image/source/cost details in `AdditionalCounts`.
- Web/X search tool calls map to MEAI `WebSearchToolCallContent` / `WebSearchToolResultContent` (queries from tool arguments when present; citation URLs become `UriContent` outputs).

## Comprehensive upstream maintenance

`.github/workflows/dotnet-file.yml` now coordinates a weekly SDK assessment, not a
generic file-bump PR. The `upstream-update` cloud agent considers xAI protocols,
official HTTP/WebSocket documentation, and stable MEAI packages/public APIs together.
It runs even without a proto diff; protocol-only updates are valid when no MEAI
mapping is warranted. Preserve public compatibility and net8.0/net10.0 library
targets. Relevant unresolved or breaking changes must remain visibly blocked.

### Setup and rollout

Merge the automation and `copilot-setup-steps.yml` to `main` before enabling it.
Create a dedicated repository secret `UPSTREAM_UPDATE_TOKEN` for a user with paid
Copilot access and repository write access. A fine-grained PAT needs metadata read
and actions, contents, issues, and pull requests read/write; a classic PAT needs
`repo`. A workflow `GITHUB_TOKEN` or App installation token cannot assign Copilot.
Rotate the token under the same user identity; cycle state is authenticated against
that identity. Never provide this assignment token to the cloud agent.

Allow the required official hosts in the cloud firewall: `github.com`,
`api.github.com`, `raw.githubusercontent.com`, `docs.x.ai`, `api.nuget.org`, and
NuGet's required package-download hosts. Keep the firewall enabled. Cloud setup uses
the existing .NET 8/10 environment and restore. Ordinary validation requires no xAI
credentials; optional live tests remain secret-gated.

Manually run **Upstream SDK update** with its default `dry-run: true` first. This
collects evidence without creating issues or consuming a cloud-agent session. Inspect
the artifact, then explicitly run with `dry-run: false` for an end-to-end smoke cycle.
Check issue assignment, substantive integration/coverage decisions, exact-head
completion, continuation on the same PR, and no-op cleanup. Set repository variable
`UPSTREAM_UPDATE_ENABLED=true` only after that succeeds; scheduled dispatch is disabled
until then. Humans approve agent workflow execution as required, mark drafts ready,
review, and merge. Existing generic sync/MEAI PRs must be reconciled deliberately
after the replacement is operational, never closed automatically as part of rollout.

### Ownership and operation

- `.netconfig` owns the source subscriptions. Local `skip` entries protect the sync,
  Dependabot, build, and release-note policy. Do not reinitialize it from the shared
  seed; new shared subscriptions are deliberate. `dnx --yes dotnet-file -- sync`
  remains the mechanical downloader, including upstream additions/removals.
- Evidence collection pins GitHub revisions in a disposable config projection,
  restores canonical source URLs, normalizes protos, and records file hashes,
  documentation snapshots, stable MEAI versions, and prior assessment links.
  Public revisions are pinned without the workflow token. `xai-org` directory
  listings go through an unauthenticated `curl` shim because that organization
  rejects an Actions or Copilot credential via its IP allow list, while `gh`
  itself refuses anonymous calls. Other `dotnet-file` sources keep the workflow
  token. Private commit lookups fall back to that token only after an anonymous
  401 or 404. Missing sources fail
  collection explicitly. Artifacts are supporting evidence;
  essential inventory and durable state also live in the cycle issue.
  Newly introduced missing proto imports are captured as integration diagnostics,
  not mistaken for a source outage or a successful build; the agent must resolve
  them and pass the actual SDK validation.
- The updater owns routine `Microsoft.Extensions.AI*` upgrades to stable versions.
  Dependabot's ignore rule is scoped to version update types so security remediation
  and unrelated dependencies remain enabled. Inspect resolved/transitive MEAI APIs,
  not just the direct package version or release-note prose.
- One cycle issue and one agent-owned `copilot/*` draft PR are active at a time.
  Keep `Cycle: #<issue>` in the PR body. Weekly evidence refreshes the same PR through
  a trusted `@copilot` request; in-flight evidence is queued instead of starting a
  concurrent session. Preserve human edits and surface sync overlaps for explicit
  merging, never reset/force-push.
- The custom agent defines the structured result contract. Reports are visible JSON
  comments on the cycle issue, covering every candidate, actual changed files,
  remote head SHA, validation, compatibility, and release-note text. No-op audits
  leave no final PR or state-only source commit. A protocol/shared-file update is
  substantive even without adapter work.
- Finalization runs only trusted `main` scripts and GitHub metadata, never PR-head
  code, with serialized API payloads. Reports cannot finalize a different head,
  forged actor, missing candidate, unresolved gap, or failing validation.
- Feature/fix summaries receive `enhancement`/`bug`, not exclusion labels such as
  `dependencies` or `docs`. This preserves inclusion in both release-note paths.
  Pure protocol/package/shared maintenance without new behavior remains dependency
  categorized. Managed PR summaries preserve text outside their delimiters.

For an interrupted session, inspect its logs before using the weekly workflow's
`resume` input. A linked PR is continued without replacement. If assignment failed,
retry the same pending issue; if an assigned session ended before creating a PR,
explicitly reassign that issue after inspection. **Finalize upstream SDK update**
can manually reconcile a cycle's latest report or flag a missing report. Failed
collection/validation never means "no changes"; closed-unmerged updates are not an
applied baseline. Queued findings after merge/close are reassessed against fresh
`main` evidence on the next weekly/manual collection.

Assignment, continuation, and completion are idempotent against the durable issue
state. The next dispatch reconciles a stored completion or closed PR if GitHub's
shared concurrency queue dropped its event. A closed cycle is recorded as merged
or closed-unmerged before a new assessment uses fresh `main` evidence.
Finalization also verifies required sync paths
and exact committed proto bytes against the captured evidence, not just the agent's
description. Event handling and recovery use the same reporter checks: recognized
cloud bot identities or repository users with write, maintain, or admin permission.
No-op closure is checkpointed before closing the draft and can be retried after an
API interruption; retries recheck that the draft is still empty. Replayed PR-closed
events preserve the audited no-op state. A human exclusion label such as `wontfix` blocks feature/fix
categorization instead of being silently removed.

Run `.github/scripts/upstream-update.tests.ps1` for offline orchestration fixtures.
Run `dotnet build`, `dnx --yes retest`, and whitespace/style format validation for
SDK changes. The library builds both targets; the current test project targets
net10.0. Disclose skipped live tests instead of claiming live API validation.
