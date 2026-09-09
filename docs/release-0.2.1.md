# BetterGI Remote 0.2.1

This release completes the locally verified production onboarding and mobile control flow.

## Changes

- Extends the first pairing confirmation timeout to two minutes.
- Adds clear pending, success, and error feedback for configuration, task, sync, and storage actions.
- Adds WeChat-specific entry-saving guidance and a reusable fixed production entry.
- Adds PWA update handling and no-cache shell headers so embedded browsers do not remain on stale releases.
- Fixes mobile task dragging so SortableJS helper nodes can never become duplicate task records.
- Adds deterministic task-order tests and cache-header tests.
- Updates Vitest to 5.0.0; the dependency audit reports zero known vulnerabilities.

## Verified scenario

- BetterGI 0.64.0 on Windows 10/11 x64.
- One-time pairing in a mobile WeChat browser.
- Configuration save with revision and backup generation.
- A real `领取邮件` run completed successfully and closed BetterGI and Genshin Impact.
- The local report was generated from BetterGI logs with no unhandled errors.
