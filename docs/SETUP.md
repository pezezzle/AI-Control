# Setup and development

## Prerequisites

- Windows 10/11 x64.
- .NET SDK 10 and Node.js/npm for development.
- Microsoft Edge WebView2 Runtime for the application.
- Google Chrome for Playwright interface tests (`channel: chrome`).
- Existing compatible ComfyUI and llama.cpp installations, with model files supplied separately.

The local publish output needs the .NET Desktop Runtime 10 (x64). The GitHub Actions artifact includes that runtime. Node.js is not required to run the published app.

## Expected installations

Default paths are relative to `%USERPROFILE%` and can be changed in **Settings**:

```text
Coding/
  AI-Control/                  Controller source repository
  ComfyUI/
    python_embeded/python.exe
    ComfyUI/main.py
  LocalAI-Worker/
    llama.cpp/build/bin/llama-server.exe
    models/*.gguf
```

The ComfyUI launcher expects the Windows standalone layout and starts the configured `python_embeded/python.exe`. The llama.cpp launcher expects a build supporting `--slots`, `--metrics`, `--jinja`, `--fit-target`, and automatic GPU fitting. Required llama.cpp runtime DLLs must accompany the executable.

Default ports are 8188 for ComfyUI and 8080 for coding. The model default is `qwen2.5-coder-14b-instruct.gguf`; choose a file actually present in the model folder. The app does not install Python, download models, or upgrade either server.

## Build, verify, and launch

From the repository root in PowerShell:

```powershell
npm ci
.\Build.ps1 -Verify -Publish
.\Install-Shortcut.ps1
.\Start-App.cmd
```

`Build.ps1` compiles `web/*.ts` into generated JavaScript under `src/AIControl.Host/Web`, builds the host, and optionally runs checks and publishes to `artifacts/app`. Keep that entire folder together. Package versions are recorded in `package-lock.json` and the NuGet lock file.

The shortcut installer creates **AI Control** on the current user's desktop and updates **ComfyUI Control** if that older shortcut exists. Both launch `artifacts/app/AIControl.exe`.

## Configuration and logs

Settings are saved atomically to `%LOCALAPPDATA%\AIControl\settings.json`. Service logs and process metadata are in the `comfy` and `coding` subfolders. The WebView2 profile is also kept under the same local data directory. **Log** opens the relevant service or controller log.

Both servers must be stopped to change installation paths, ports, context size, or GPU settings. Changing the coding model only requires the coding server to be stopped. Closing the app leaves services running and cancels scheduled stops after confirmation.

## Repository layout

| Folder | Purpose |
|---|---|
| `src/AIControl.Core` | Configuration, HTTP status queries, workload protection, and launcher interface |
| `src/AIControl.Host` | WPF window, WebView2, restricted command bridge, and service scripts |
| `web/app.ts` | TypeScript interface logic |
| `web/assistant.ts` | Chat, file reviews, image attachments and microphone recording |
| `src/AIControl.Host/Web` | HTML, CSS, icon, and generated JavaScript |
| `tests` | C# checks, process identity checks, and browser tests |
| `legacy/ComfyUIControl` | Retained original WPF controller |
| `docs` | Setup, architecture, and verification details |

Model weights, server runtimes, projects, logs, credentials, and build outputs remain outside Git. No editor extension is installed automatically; use the API details in the README to connect a compatible coding assistant.

For workspace permissions, global instructions, image projectors, local Whisper paths and custom context configuration, see [Assistant setup](ASSISTANT.md). Speech and vision components are optional external installations; they are not included in application build artifacts.
