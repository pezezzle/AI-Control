# Build and test status

Version 0.4.3 was built and tested on Windows with an RTX 4090 on October 7, 2026. The interface, documentation, host dialogs and launcher messages are in English.

## Automated checks

`Build.ps1 -Verify` builds without warnings or errors and runs 125 C# checks, six process identity checks, one lifecycle check with a real short-lived test process, and 24 browser tests: **156 checks in total**.

Coverage includes active and deferred jobs, unknown or malformed status responses, foreign processes, fresh workload checks before stopping, persisted permissions and custom context settings, model filenames, the command bridge, and the interface. Assistant checks cover mandatory global/project/directory instructions, path traversal and hard-link protection, denied and stale writes, tool permissions, malformed streams, image/audio validation and Continue configuration preservation. Browser tests cover streamed plain text, exact approval dialogs, image previews, transcript insertion, and microphone recording/resampling with simulated audio hardware. Browser tests use a simulated host bridge; they do not replace real service tests.

Version 0.4.3 verifies a removed configured model remains visibly selected as missing instead of silently displaying the first installed model. Start is disabled until an installed model is explicitly selected and acknowledged by the host. The regression test verifies the model selection bridge command and the resulting enabled start state.

The published 0.4.3 app started Huihui Qwen3.8 through its native Start button with 32,768 context tokens. `/props` confirmed the actual model filename and capacity; the selected empty chat displayed 790 / 32,768 tokens after refresh. A separate synthetic request with thinking disabled and a 16-token cap returned `MODEL_OK`; no user code was submitted. It is a functional startup check, not a throughput benchmark. The existing shortcut retains its stable installation target. Evidence is outside Git under `GPTPC/ai-control-model-start-20261007`.

Version 0.4.2 covers an exact read-only exception for the configured global AGENTS.md: successful reads without changing workspace, freshly reloaded contents, blocked neighboring external files, and blocked external writes. Tests use synthetic rules and mocked model tool calls; no user prompt is replayed. The package was prepared separately under `artifacts/app-next` while the user's task ran. After the user canceled that task and requested the new version, the idle app was closed and a one-time helper backed up and replaced its package with SHA-256 verification. The native window now displays 0.4.2. The taskbar shortcut targets the same stable `artifacts/app/AIControl.exe` path. The coding server retained its existing process and remained running. Installation status, screenshots and local performance observations are outside Git under `GPTPC/ai-control-rules-fix-20261007`.

## Version 0.4 sessions and context

Synthetic session tests verify independent persistence, interrupted-task handling, preserved corrupt files, deletion recovery, complete tool-round validation, failed compaction rollback, retained recent turns and checkpoint restore without replay. Automatic mode triggers at 70%; Manual and Off never compact implicitly, and response reserves block overflow before inference. Browser checks cover drafts, queued cross-session requests, scoped streams/reviews/dictation, compaction controls and minimum-window layout.

Version 0.4.1 adds regression checks for automatic output at 258,048 context tokens, custom budgets above the former 8,192 and half-context limits, legacy chat migration, storage beyond one million characters, long streamed answers, retained length-limited responses, and discarded incomplete tool proposals. A single large completed answer can be compacted before the next request while a checkpoint retains its exact original. Truncated summaries are rejected without changing working memory. Browser tests check automatic defaults, a 131,072-token custom setting, and partial-answer recovery.

A read-only check against the user's already running Huihui server measured **688 input tokens / 258,048 active capacity**, leaving **257,104 automatic output tokens** after the safety margin. The same context accepted a **131,072-token custom budget**. This used a synthetic empty folder and rules, invoked only health/template/tokenizer APIs, and started no inference. It verifies measured budgeting, not the quality or speed of a 250,000-token generated answer. Evidence is outside the repository under `GPTPC/ai-control-output-budget-20261007`.

The published 0.4.1 Windows app was visually checked with the existing empty chat: refresh measured **758 / 258,048** input tokens, and Automatic displayed **257,034** available output tokens. Selecting Custom exposed the numeric limit field; Automatic was restored afterward. The coding server remained running and idle, and existing conversations were preserved.

The actual Huihui Qwen3.8 server (llama.cpp b11429, 16,384 active context tokens) counted a synthetic prompt using its template/tokenizer, supplied streamed usage, and answered a small synthetic request. Tool-free local summarization reduced a synthetic conversation from **5,289 to 853 tokens**. The checkpoint and original transcript survived a store reload. Evidence and fixtures are saved locally outside the repository under `GPTPC/ai-control-sessions-20261007`. The published Windows app also displayed separate saved chats, queued a second request while the first was running, and continued it after the first hit its output budget. The second answered `SESSION_TWO_OK`. Restoring the synthetic checkpoint retained the transcript, created a forward checkpoint and refreshed the count with current global rules. These are feature checks, not model-quality benchmarks.

## Version 0.3 local integration tests

The 0.3.1 recovery test used Huihui in a fresh synthetic folder: an empty listing path was handled as the workspace root, an absolute file path inside that folder was reviewed as a relative path, and the exact approved file was created. Regression checks retain outside-workspace and credential blocking, read-only protection and mandatory approval. Reasoning progress and budget-exhaustion recovery were checked in both the core stream handler and the interface.

- Huihui Qwen 3.8 27B read a synthetic workspace file through the assistant tools and saved the exact proposed output after approval. No user repository code was used for inference tests.
- Both Huihui Qwen 3.8 27B and Qwen 3.6 35B-A3B loaded their matching vision components and correctly identified the red background and the word `VISION` in a synthetic PNG. Both servers reported vision support.
- The actual C# speech pipeline ran local whisper.cpp with the multilingual small model on the CPU and transcribed a canonical PCM version of its official sample recording. Browser recording and resource cleanup passed a separate simulated-hardware test. Live microphone hardware and its first-use permission prompt still require a user test.
- The published Windows app displayed the global and repository rule paths, sent a small synthetic chat request, and rendered the expected `APP_OK` answer. Its context, vision, Continue synchronization and speech settings were visually inspected.
- Models, vision components and the Whisper runtime remain outside the repository. Downloads were checked against their published SHA-256 digests. Huihui was restored as the selected running model with a 16,384-token context after the second model's vision test.

Evidence, synthetic fixtures and responses are stored locally outside the repository. Context values larger than the active 16,384-token configuration were validated through metadata/configuration tests, not benchmarked on the GPU. Larger contexts may require RAM offloading or fail to allocate.

## Earlier controller integration tests

- Detected ComfyUI during a user render. Opened the stop dialog and canceled it; the render was not interrupted.
- Stopped ComfyUI through the app after the render finished and started it again.
- Opened ComfyUI and the llama.cpp chat interface inside the app window.
- Started Qwen 2.5 Coder 14B with a 32,768-token context on the RTX 4090 through the app. llama.cpp reported 49 of 49 model layers on the GPU. A short synthetic Python request was answered correctly.
- Stopped the coding server through the app while idle.
- Also tested 7B model startup, a request, and shutdown in CPU mode on a separate test port using the C# launcher and Windows PowerShell 5.1. The test server was stopped.
- Selected a project through the Windows folder dialog, persisted it, and opened it in VS Code.
- Confirmed that launching the app again activates the existing instance.
- Visually checked settings, desktop shortcuts, and the Windows icon.

These earlier checks used the Qwen 2.5 controller configuration. They do not imply that every model, context size or third-party editor extension was tested by version 0.3.

Evidence and test responses are kept locally outside the repository.

## Build packages

The Windows Actions workflow runs the same automated checks, then publishes a self-contained x64 package as an Actions artifact. Local `Build.ps1 -Publish` output is framework-dependent. Both require WebView2 and preserve the complete output folder. Neither includes model weights or server installations.
