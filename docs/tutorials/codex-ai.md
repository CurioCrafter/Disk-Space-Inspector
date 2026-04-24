# Codex AI Advisor

Disk Space Inspector can ask Codex to rank cleanup candidates, but Codex never receives permission to delete files.

1. Install Codex CLI if needed: `npm i -g @openai/codex`.
2. In Disk Space Inspector, open `Cleanup`.
3. Use `Login with Codex`.
4. Use `Check Codex status`.
5. Use `Ask Codex AI`.

The app delegates auth to the official Codex CLI flow and never reads `~/.codex/auth.json`. Codex may rank and explain existing findings, but it cannot invent paths or make blocked/system findings executable.
