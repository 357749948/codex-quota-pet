# Security policy

This project is maintained on a best-effort basis. Security fixes target the latest release; no response-time guarantee is offered.

For a vulnerability, use GitHub's **Security → Report a vulnerability** when available. If private reporting is unavailable, open an issue requesting a private contact channel without publishing exploit details or personal data. Ordinary compatibility and build problems belong in the bug report template.

Never attach Codex login files, tokens, full environment dumps, local recognition templates, or screenshots containing private desktop content. Local diagnostics can include quota values, timestamps, process/window identifiers, and screen coordinates; review and redact them first.

The app runs as the current Windows user. It starts an independent Codex CLI app-server using that CLI's existing login, and it reads the pet's screen region in memory. See [data handling](docs/privacy.md) for local files and network behavior.
