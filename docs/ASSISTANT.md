# Local assistant

Start the Coding server, then choose **Open in app** or **Assistant** in the sidebar. The assistant sends requests only to the configured loopback llama.cpp server. Chats and their full visible transcripts are saved locally under `%LOCALAPPDATA%\AIControl\chats`. **New chat** creates an independent session. Use the sidebar to switch, **Rename** to change its title and **Delete** to hide it. Deleted session files are retained for recovery; set their `deleted` flag to `false` while the app is closed to recover them. These JSON files contain conversation text, tool results, attached images and context checkpoints in plain text and stay outside the repository.

Choose the server's model on **Coding → Model for the next start**. A removed configured model appears as **Missing model**, and Start stays disabled until an installed model is explicitly selected. The selector never substitutes the first installed filename silently. A chat's desired model is independent; select the same model there before sending.

## Workspace and permissions

Choose a working folder. File tools use relative paths inside that folder. Absolute paths inside the same folder are normalized to relative paths and receive the same permission, link and credential checks. Paths outside the folder, ambiguous drive-relative paths, traversal, symbolic links, junctions, hard-linked files, device paths and alternate data streams are rejected. Credential files and common private/dependency directories are omitted. This is a bounded text workspace tool, not a general Windows security sandbox or a guarantee that every secret in an ordinary source file can be detected.

- **Read only** exposes listing, text reading and search.
- **Read & write** also allows file proposals. Every save requires review of the exact old and proposed content. A file changed after review cannot be silently overwritten.
- **Allow script proposals** permits requests to run existing PowerShell scripts. Each execution needs a separate approval showing the entire script. Scripts run as the Windows user and can access files or the network outside the workspace. This option is off by default. There is no automatic script execution or deletion tool.

Every session remembers its working folder, desired model and permissions. Choosing a different folder in a nonempty chat creates a new session to avoid mixing projects. Model and permission changes preserve the transcript, refresh mandatory rules and invalidate measured usage. Load the desired model on the Coding page before sending; the app rejects a mismatch instead of switching a running server automatically. Cancel interrupts inference and pending reviews; completed file changes remain saved. Tasks are limited to 16 model/tool rounds. Listing and search results are bounded; text reads and proposed files are limited to 128 KB. Only one assistant generation runs at a time. You can browse other chats, create a new one and queue a request there. Cancel applies to the selected chat; queued tasks also can be canceled. Tasks interrupted by app closure are marked interrupted on restart and are not replayed. Pending approvals never survive a restart.

Empty listing paths mean the workspace root; file operations still require a filename. Empty/excluded folders are reported explicitly. Activity entries identify the tool and supplied path, without logging proposed file content to a diagnostics file. During model reasoning, the interface shows measured reasoning-character counts and time for that response. Reaching the output budget marks the task **Incomplete** and retains its partial answer and working memory. If thinking used the entire budget, the activity explains that no final answer was returned. Unfinished tool proposals are discarded without execution. Already approved and completed file changes remain saved. These character counts are not token counts, and progress is not a guarantee that a model will complete a task.

## Instructions

`%LOCALAPPDATA%\AIControl\AGENTS.md` is the global rule file. The app creates a conservative default only when it does not exist. **Edit global AGENTS.md** opens it in the associated editor. It is loaded before every user request, regardless of the workspace. A missing global rule file blocks a request instead of continuing without it.

The model receives these contents directly and is instructed to use them without reading the file again. If it nevertheless calls `read_file` with the exact configured global rules path, the host reloads the mandatory rules and returns them without requiring a workspace change. This read-only exception grants no access to other files in AppData, directory listings, writes or script execution outside the workspace.

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

Optional Continue synchronization updates only `local-coder` entries on a loopback API in the existing `~/.continue/config.json`, retaining a backup. YAML configurations are not rewritten automatically; Continue retains its own completion limits separately from the built-in assistant.

Under **Compaction settings → Output budget**, **Automatic · remaining context** is the default, including for saved chats from older versions. Before each model/tool round, the app allows the active capacity minus measured input and a 256-token safety margin. Input, internal thinking and final output share that capacity. For example, 258,048 active tokens with 8,000 input tokens permit up to 249,792 generated tokens. **Custom token limit** accepts 1–1,048,576 tokens, with 16,384 as the new-chat custom fallback; it is capped only by actual free context. There is no 8,192-token or half-context cap. Existing custom values are retained when an older chat migrates to Automatic. A model can end an answer sooner or impose its own runtime limits.

The context panel shows working-context tokens, active capacity, a percentage and a progress bar. Capacity comes from the running server's `/props`, not GPU utilization or the model's advertised maximum. `/apply-template` and `/tokenize` count text after applying the model's actual template, including current rules and tool definitions. Counts refresh before each request and tool round; streamed server usage also includes generated output. For images, pre-request counts add an explicitly estimated 2,048-token allowance per attachment; the final server count includes actual image input when provided. Image estimates can differ from real use. An unavailable tokenizer leaves usage unknown and blocks inference instead of guessing.

**Automatic** compaction runs before the next request at 70% usage, or earlier if insufficient output space remains. **Manual** and **Off** disable automatic compaction; **Compact now** explicitly requests it. Older completed turns are summarized first, normally retaining the last two turns and their tool results. If those recent turns still fill the context, the app can summarize the entire completed conversation, including a single large answer, before adding the new message. Manual compaction also supports a short conversation when there are no older turns. The same local model summarizes with tools and thinking disabled, using a separate short 2,048-token budget. This summary budget does not restrict normal answers. Current global/project rules are reloaded, summaries remain untrusted memory, and earlier approval never authorizes a new file action. Summaries can omit details; consult the full transcript when precision matters.

A successful compaction reports before/after counts and saves a durable checkpoint before replacing working context. Empty, malformed, truncated or non-reducing summaries and canceled/failed compaction leave original working memory intact. The visible transcript, attachments and checkpoints are retained. **Context checkpoints → Restore** reinstates earlier working memory and retains a checkpoint of the current state; it never undoes file changes or replays tools. Compaction occurs between requests, not during a streamed answer. Manual/Off users must compact explicitly or start a new chat if input fills the context.

Response data and stored message text have a 32-million-character guard; session files are limited to 128 MB each. A storage failure is reported; old on-disk data is never silently truncated. Unsent composer drafts are isolated per session while the app is open, but are not persisted on restart. Automatic compaction is an in-app feature and runs when the next request is sent; it does not require a Codex scheduled automation.
