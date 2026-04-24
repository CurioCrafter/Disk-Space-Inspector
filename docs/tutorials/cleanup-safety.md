# Cleanup Safety

Cleanup recommendations are grouped into guardrail lanes:

- `Safe`: low-risk cache or staged shell cleanup, still reviewed before action.
- `Review`: user or app context matters.
- `Use system cleanup`: use Windows or app-provided cleanup routes.
- `Blocked`: direct cleanup is not allowed.

Blocked and system-managed locations such as `WinSxS`, pagefile/swapfile, protected Windows folders, installer stores, and active app state remain advisory only.
