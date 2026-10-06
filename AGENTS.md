# AI Control

This repository owns the Windows control application and its service launchers.
The ComfyUI and llama.cpp installations, weights, generated outputs and user
projects live outside this repository. Never move or delete them as part of
application maintenance.

- Keep the English UI concise. Use C#/.NET 10 and WPF without additional runtime
  dependencies unless required by an explicit feature request.
- Read server health and workload before stopping. Unknown workload must not be
  treated as idle. Stop only the exact configured executable and installation.
- Keep local settings and logs in LocalApplicationData, outside Git.
- Preserve the Windows icon at all relevant sizes and verify UI changes in the
  running application.
- Build with `Build.ps1 -Verify`. Tests must cover process identity, workload
  protection, malformed responses, and configuration persistence when changed.
- Do not start inference jobs on user code or change model weights just to test
  the control application. Use a small synthetic request for server smoke tests.
- Closing the app leaves servers running. A scheduled stop belongs to the open
  control window and must be clearly explained when closing.
