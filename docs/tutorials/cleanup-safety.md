# Cleanup Safety

Cleanup recommendations are grouped into guardrail lanes:

- `Safe`: low-risk cache or temporary data, still reviewed before action.
- `Review`: user or app context matters.
- `Use system cleanup`: use Windows or app-provided cleanup routes.
- `Blocked`: direct cleanup is not allowed.

The Cleanup page has a review queue. Only Safe and Review findings can be staged. The queue shows exact paths, evidence, size, file count, risk lane, and recommended action. You can export the queue as a local checklist.

Blocked and system-managed locations such as `WinSxS`, pagefile/swapfile, protected Windows folders, installer stores, driver stores, active browser databases, and unknown credential stores remain advisory only.

Use at your own risk. Back up important files before taking cleanup action outside the app.
