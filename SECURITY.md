# Security

AI Control is intended for local use on a trusted Windows PC. New services launched by the app bind to `127.0.0.1`. It does not add cloud credentials or expose a remote administration interface.

## Command and process boundaries

The bundled Web UI can request only predefined commands through the host bridge. Messages are accepted from the exact local HTTPS origin. The separate WebView2 used for service pages has web messaging disabled, preventing those pages from calling the privileged bridge.

The stop script checks the configured executable and process identity. Stored process metadata includes the PID, creation time, and port. Unknown workload does not permit an automatic stop; immediate stopping requires an explicit user choice.

The configured installation folders must be trusted: starting a service executes files from those folders. Independently launched services may have different network settings. Server plugins, generated content, and model files are governed by their own software and sources.

## Local data

Settings, logs, process metadata, and the embedded browser profile remain under `%LOCALAPPDATA%\AIControl`. Service pages can save their own chat and workflow data in that browser profile or their installation folders. Logs and screenshots may reveal paths, prompts, or project details; review them before sharing.

Do not commit tokens, `.env` files, certificates, local settings, models, or generated outputs. The repository's ignore rules help prevent common accidental inclusions but cannot identify every secret.

## Reporting

For a non-sensitive issue, open a GitHub issue with a minimal reproduction. Do not publish credentials, private prompts, or unredacted diagnostics in issues. If a credential was exposed, revoke it through its provider before sharing any report.
