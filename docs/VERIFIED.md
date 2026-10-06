# Build and test status

Version 0.3.1 was tested on Windows with an RTX 4090 on October 6, 2026. The interface, documentation, host dialogs and launcher messages are in English.

## Automated checks

`Build.ps1 -Verify` builds without warnings or errors and runs 89 C# checks, six process identity checks, one lifecycle check with a real short-lived test process, and 14 browser tests: **110 checks in total**.

Coverage includes active and deferred jobs, unknown or malformed status responses, foreign processes, fresh workload checks before stopping, persisted permissions and custom context settings, model filenames, the command bridge, and the interface. Assistant checks cover mandatory global/project/directory instructions, path traversal and hard-link protection, denied and stale writes, tool permissions, malformed streams, image/audio validation and Continue configuration preservation. Browser tests cover streamed plain text, exact approval dialogs, image previews, transcript insertion, and microphone recording/resampling with simulated audio hardware. Browser tests use a simulated host bridge; they do not replace real service tests.

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
