# Build and test status

Version 0.2 was tested on Windows with an RTX 4090 on October 6, 2026. The interface, documentation, host dialogs, launcher messages, and retained legacy controller were subsequently translated into English.

## Automated checks

`Build.ps1 -Verify` builds without warnings or errors and runs 41 C# checks, six process identity checks, one lifecycle check with a real short-lived test process, and six browser tests: **54 checks in total**.

Coverage includes active and deferred jobs, unknown or malformed status responses, foreign processes, fresh workload checks before stopping, persisted settings, model filenames, the command bridge, and the interface. Browser tests use a simulated host bridge; they do not replace real service tests. English labels are checked by the interface tests.

## Real services and Windows interface

- Detected ComfyUI during a user render. Opened the stop dialog and canceled it; the render was not interrupted.
- Stopped ComfyUI through the app after the render finished and started it again.
- Opened ComfyUI and the llama.cpp chat interface inside the app window.
- Started Qwen 2.5 Coder 14B with a 32,768-token context on the RTX 4090 through the app. llama.cpp reported 49 of 49 model layers on the GPU. A short synthetic Python request was answered correctly.
- Stopped the coding server through the app while idle.
- Also tested 7B model startup, a request, and shutdown in CPU mode on a separate test port using the C# launcher and Windows PowerShell 5.1. The test server was stopped.
- Selected a project through the Windows folder dialog, persisted it, and opened it in VS Code.
- Confirmed that launching the app again activates the existing instance.
- Visually checked settings, desktop shortcuts, and the Windows icon.

The 32B model is available for selection when present but was not loaded for this verification. An editor extension such as Cline was not installed or configured. Existing server installations and model weights were not changed.

Evidence and test responses are kept locally outside the repository.

## Build packages

The Windows Actions workflow runs the same automated checks, then publishes a self-contained x64 package as an Actions artifact. Local `Build.ps1 -Publish` output is framework-dependent. Both require WebView2 and preserve the complete output folder. Neither includes model weights or server installations.
