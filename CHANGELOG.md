# Changelog

## 0.3.1 — October 6, 2026

- Normalize tool paths inside the workspace while preserving boundary and approval checks; explain missing filenames and empty folders.
- Show the tool and supplied path in activity results, measured reasoning progress and clear reasoning-budget failures.
- Replace blank assistant cards with a pending state and clear it when an answer or error arrives.

## 0.3 — October 6, 2026

- Added a local assistant with streamed responses, cancellation, workspace reading/search and separately reviewed file writes.
- Added mandatory global and scoped AGENTS.md instructions, read-only defaults and optional reviewed PowerShell script proposals.
- Added image attachments for vision-enabled servers and microphone dictation through a local Whisper CLI.
- Added custom numeric context settings, GGUF context-limit checks and optional local Continue JSON synchronization.
- Preserved external service interfaces, process identity checks and GPU workload protection.

## 0.2 — October 6, 2026

- Added a shared WPF/WebView2 control app for ComfyUI and llama.cpp.
- Added embedded service interfaces, GGUF model selection, project selection, and VS Code launch.
- Added workload protection, scheduled stopping, process identity checks, and saved settings.
- Retained the original WPF controller under `legacy/ComfyUIControl`.
- Translated the interface, dialogs, launcher messages, legacy controller, and documentation into English.
- Aligned the README structure and badges with PC / AI Dashboard and added Windows build artifacts.
