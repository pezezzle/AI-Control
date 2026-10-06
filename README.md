# AI Control · Windows

[![Windows build](https://github.com/pezezzle/AI-Control/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/pezezzle/AI-Control/actions/workflows/build.yml)
[![Last commit](https://img.shields.io/github/last-commit/pezezzle/AI-Control/main)](https://github.com/pezezzle/AI-Control/commits/main)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20WebView2-0078D4)](docs/ARCHITECTURE.md)
[![TypeScript 5.9](https://img.shields.io/badge/TypeScript-5.9-3178C6?logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![Windows x64](https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D4)](docs/SETUP.md)

A local Windows control app for ComfyUI and a llama.cpp coding server. Start and stop services, chat with local models, inspect a selected workspace, review file changes, attach images and dictate messages. Development version **0.3.1**. The host uses C# / .NET 10 and WPF; the interface uses HTML, CSS, and TypeScript in WebView2. The app interface, documentation, and application messages are in English.

**Project status:** The Windows build and automated server, assistant and interface checks pass. Real local tests cover Huihui Qwen3.8 file tools, reviewed file creation, image recognition and Whisper transcription. See [Build and test status](docs/VERIFIED.md) for the tested environment and limitations.

## Features

- Service status, active and queued jobs, start/stop controls, logs, and installation folders.
- ComfyUI embedded in the app and a built-in local assistant with streamed responses and cancelable tasks.
- Workspace file listing, reading, search and reviewed writes; read-only access by default, with optional separately approved script execution.
- Mandatory global `AGENTS.md`, project and directory rules, and a visible list of loaded instructions.
- PNG, JPEG and WebP attachments with compatible vision models; local microphone dictation through Whisper.
- Existing GGUF model selection, custom numeric context size, model-limit validation, optional Continue context sync and automatic GPU allocation.
- Project folder selection, VS Code launch, and connection details for compatible coding assistants.
- Stop after all jobs finish, explicit immediate cancellation, and cancellation of a scheduled stop.
- Saved local settings, a Windows app icon, desktop shortcuts, and single-instance behavior.

The controller starts one service at a time because both share the GPU. Switching never automatically stops the other service. Existing server installations, model weights, workflows, and generated files stay in place. The older AUTOMATIC1111 installation is not managed by this version.

## Getting started

AI Control manages existing ComfyUI and llama.cpp installations; it does not download models or install those servers. See [Setup](docs/SETUP.md) for the expected folder structure and prerequisites.

1. Install the .NET SDK 10, Node.js/npm, Microsoft Edge WebView2 Runtime, and Google Chrome for interface tests.
2. Build from PowerShell in the repository folder:

   ```powershell
   .\Build.ps1 -Verify -Publish
   ```

3. Run `Start-App.cmd` or `artifacts/app/AIControl.exe`. Optionally run `Install-Shortcut.ps1` to create the **AI Control** desktop shortcut.
4. Open **Settings** to check installation folders, ports, context size, and GPU allocation. Both services must be stopped before changing server settings.
5. Start a service, then choose **Open in app** or **Open in browser**.

Closing the window leaves servers running. Keep the app open for a scheduled stop; closing it cancels the schedule after confirmation. The earlier **ComfyUI Control** shortcut, if present, is updated by the shortcut installer.

## Download

Successful [Windows build runs](https://github.com/pezezzle/AI-Control/actions/workflows/build.yml) provide an `ai-control-win-x64` artifact. GitHub requires sign-in to download Actions artifacts. Extract the entire folder and run `AIControl.exe`; keep the accompanying Web assets and scripts together. The Actions package includes the .NET runtime and requires Microsoft Edge WebView2 Runtime.

There is currently no published GitHub Release or installer. The local `Build.ps1 -Publish` output requires the .NET Desktop Runtime 10 (x64). Build artifacts stay outside Git history.

## Coding assistants

The coding server provides an OpenAI-compatible API at `http://127.0.0.1:8080/v1` by default, with model ID `local-coder`. In Cline or a compatible editor assistant, select **OpenAI Compatible**, enter that address and model ID, and use a placeholder such as `local` if the client requires a nonempty API key.

The app starts the server, offers its own workspace assistant and can open VS Code. It does not install an editor extension automatically. **Open in app** on the Coding server opens the built-in Assistant. **Open in browser** opens the original llama.cpp Web UI, which cannot access the app's workspace tools. See [Assistant, rules, images and dictation](docs/ASSISTANT.md).

## Services and GPU allocation

Use **Stop** to end an idle service. When jobs are active or their status is unknown, choose **Stop after all jobs** or explicitly **Stop now and cancel jobs**. **Cancel stop** removes a scheduled stop. Unknown workload is never treated as idle.

The default coding model is `qwen2.5-coder-14b-instruct.gguf`, with a 32,768-token context and automatic GPU fitting. Model files are discovered in the configured server's `models` folder. Change the model while the coding server is stopped. Automatic fitting can offload part of a larger model to system RAM; performance depends on the model and hardware.

## Privacy and security

The controller starts services on loopback and does not require cloud API credentials. Settings, logs, process metadata, and the WebView2 profile stay under `%LOCALAPPDATA%\AIControl`. Model weights, runtimes, user projects, credentials, and generated outputs are excluded from the repository.

Bundled UI messages use a restricted command bridge. Embedded server pages cannot access that bridge. The stop script checks the configured executable and process identity before termination. Read [Security](SECURITY.md) before sharing diagnostics or exposing a server beyond the local PC.

## Further documentation

- [Setup, expected installations, and development](docs/SETUP.md)
- [Architecture and local data flow](docs/ARCHITECTURE.md)
- [Build and test status](docs/VERIFIED.md)
- [Changelog](CHANGELOG.md)
- [Security](SECURITY.md)
- [Original WPF controller](ORIGINAL-COMFYUI-CONTROL.md)

## License

No general open-source license has been granted. Public repository access alone is not a license grant. Model weights, third-party server installations, private runtime data, and user projects are not part of this repository.
