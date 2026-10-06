# Original ComfyUI Control

The original small WPF controller is retained under `legacy/ComfyUIControl`. AI Control supersedes it for normal use; the legacy version's interface and messages have also been translated into English.

- **Start** launches an existing ComfyUI installation in the background.
- **Open in browser** opens `http://127.0.0.1:8188` in the default browser.
- **Stop** ends ComfyUI. For active or queued jobs, choose to wait for all jobs or stop immediately.
- **Cancel stop** removes a scheduled stop.

Closing the window leaves ComfyUI running. Keep the control window open for a scheduled stop; closing it cancels the schedule after confirmation. Status refreshes every two seconds. **Open log** opens diagnostics.

The legacy app expects `Start-ComfyUI.ps1` and `Stop-ComfyUI.ps1` in the installation root. Those installation-specific scripts are not included in this repository. The legacy process derives its installation root from two parent folders above the executable, so deployment must preserve that layout. Use the current AI Control host for configurable installation paths and the maintained service launchers.

Build and test from `legacy/ComfyUIControl`:

```powershell
dotnet publish ComfyUIControl.csproj -c Release -o publish
dotnet run --project Tests/ComfyUIControl.Tests.csproj -c Release
```

The legacy output is `publish/ComfyUIControl.exe` and requires the .NET Desktop Runtime 10 (x64).
