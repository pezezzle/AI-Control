# Architecture

AI Control has three separately maintained components: a platform-independent .NET core, a Windows WPF/WebView2 host, and a TypeScript/HTML/CSS frontend. The repository retains the working original WPF controller as an import baseline and a legacy source folder.

The shell loads only bundled assets from `https://ai-control.local`. Messages are accepted only from that exact HTTPS host and default port. The host recognizes a fixed set of commands; the frontend cannot submit shell commands. Install paths and model filenames are validated. The second WebView2 displays one configured loopback service and has web messaging disabled. ComfyUI and llama.cpp content cannot call the privileged shell bridge.

HTTP polling uses `/system_stats` and `/queue` for ComfyUI. llama.cpp uses `/health`, `/v1/models`, `/slots` and Prometheus `/metrics`. Both active and deferred jobs protect a normal stop. Unavailable or malformed workload data is unknown, never idle. Stop reads fresh status, then the PowerShell boundary verifies the configured executable and port owner before terminating that process. Stored startup PIDs are accompanied by their creation timestamp to protect against PID reuse.

Services start hidden, bind to loopback, and log outside Git. The app does not upgrade runtimes, install models or alter existing installation launch scripts. The llama.cpp launcher exposes metrics and slots, sets alias `local-coder`, uses a single request slot and automatic GPU fitting with a 1.5 GiB reserve. Larger models may use system RAM with `GpuLayers=auto`.

Only one service is started at a time through the host. It never automatically terminates the other service. Existing independently launched services are detected; unavailable identity or workload prevents silent termination. Scheduled stops belong to the open app and are canceled when the window is closed after confirmation. Servers remain running when the app closes normally.

Personal configuration is saved atomically under `%LOCALAPPDATA%/AIControl/settings.json`. Models are enumerated in the configured llama.cpp installation's `models` folder. Changing a model is permitted only while the coding server is offline. Installation and port settings require both servers to be stopped.

The editor action only opens the selected project in VS Code. Actual coding agent configuration and editor extensions are outside the controller's scope. The included local chat is the llama.cpp service UI.
