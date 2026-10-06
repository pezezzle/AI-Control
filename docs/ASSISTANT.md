# Local assistant

Start the Coding server, then choose **Open in app** or **Assistant** in the sidebar. The assistant sends requests only to the configured loopback llama.cpp server. Conversations stay in memory and are cleared when the app closes or you choose **New chat**.

## Workspace and permissions

Choose a working folder. File tools use relative paths inside that folder. Absolute paths inside the same folder are normalized to relative paths and receive the same permission, link and credential checks. Paths outside the folder, ambiguous drive-relative paths, traversal, symbolic links, junctions, hard-linked files, device paths and alternate data streams are rejected. Credential files and common private/dependency directories are omitted. This is a bounded text workspace tool, not a general Windows security sandbox or a guarantee that every secret in an ordinary source file can be detected.

- **Read only** exposes listing, text reading and search.
- **Read & write** also allows file proposals. Every save requires review of the exact old and proposed content. A file changed after review cannot be silently overwritten.
- **Allow script proposals** permits requests to run existing PowerShell scripts. Each execution needs a separate approval showing the entire script. Scripts run as the Windows user and can access files or the network outside the workspace. This option is off by default. There is no automatic script execution or deletion tool.

Changing the folder, model or permissions requires a fresh conversation. Cancel interrupts inference and pending reviews; completed file changes remain saved. Tasks are limited to 16 model/tool rounds. Listing and search results are bounded; text reads and proposed files are limited to 128 KB. Long conversations can reach the model's context limit; use **New chat** instead of relying on silent truncation.

Empty listing paths mean the workspace root; file operations still require a filename. Empty/excluded folders are reported explicitly. Activity entries identify the tool and supplied path, without logging proposed file content to a diagnostics file. During model reasoning, the interface shows measured reasoning-character counts and time for that response. A response-budget error explains when reasoning consumed the budget without producing a final answer. These counts are not token counts, and progress is not a guarantee that a model will complete a task.

## Instructions

`%LOCALAPPDATA%\AIControl\AGENTS.md` is the global rule file. The app creates a conservative default only when it does not exist. **Edit global AGENTS.md** opens it in the associated editor. It is loaded before every user request, regardless of the workspace. A missing global rule file blocks a request instead of continuing without it.

The workspace-root `AGENTS.md` is appended next. Before tools access a deeper path, `AGENTS.md` files along its directory hierarchy are added. New directory instructions are returned to the model before a write or script proposal proceeds. Rule changes are re-read on subsequent requests/operations. The selected workspace is the rule-discovery boundary; parent directories outside it are not read.

**Active instructions** lists the loaded paths. Global rules remain the baseline; narrower rules add project details. Rules guide the model, while host code enforces tool permissions and path checks independently.

## Images

Attach or paste up to four PNG, JPEG or WebP images, each up to 5 MB. The running model must support vision and be started with a matching `mmproj` component. Store that component at:

```text
<coding installation>/models/vision/<model filename without .gguf>.mmproj.gguf
```

Enable images in **Settings**, then restart the coding server. Model listing excludes the vision directory. The app checks the running server's reported modalities before submitting images. Visual recognition is not a guarantee of accurate OCR or interpretation. It analyzes images; image generation remains in ComfyUI.

## Dictation

Configure a local whisper.cpp CLI and a multilingual model in **Settings**. **Dictate** requests microphone permission, records up to two minutes, then **Stop recording** transcribes locally on the CPU. The transcript is inserted into the message field for review and is never submitted automatically. **Cancel transcription** stops the transcription process. Temporary WAV and transcript files are deleted after success, cancellation or failure.

No speech API, cloud account or token is needed. Microphone availability depends on Windows permissions and hardware. Model accuracy varies with language, background noise and recording quality.

## Context

Enter a custom context size between 2,048 and 1,048,576 tokens, or use a suggested value. The host checks the selected GGUF model's declared limit before startup. The runtime still decides whether memory allocation succeeds. Larger contexts can require RAM offloading and reduce speed. Settings apply after restarting the server.

Optional Continue synchronization updates only `local-coder` entries on a loopback API in the existing `~/.continue/config.json`, retaining a backup. YAML configurations are not rewritten automatically. Output allowance is capped at half the context or 8,192 tokens, whichever is smaller.
