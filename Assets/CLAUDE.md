# Project Instructions

## CodeMoot — Multi-Model Collaboration

This project uses [CodeMoot](https://github.com/katarmal-ram/codemoot) for Claude + GPT collaboration. CodeMoot bridges Claude Code and Codex CLI so they work as partners — one plans, the other reviews.

### How Sessions Work
- Every `codemoot` command uses a **unified session** with GPT via Codex CLI
- Sessions persist across commands — GPT remembers prior reviews, debates, and fixes
- Sessions are stored in `.cowork/db/cowork.db` (SQLite)
- When a session's token budget fills up, it auto-rolls to a new thread
- Run `codemoot session current` to see the active session

### Available Commands (use these, not raw codex)
- `codemoot review <file-or-dir>` — GPT reviews code with codebase access
- `codemoot review --prompt "question"` — GPT explores codebase to answer
- `codemoot review --diff HEAD~3..HEAD` — Review git changes
- `codemoot review --preset security-audit` — Specialized review presets
- `codemoot fix <file>` — Autofix loop: review → apply fixes → re-review
- `codemoot debate start "topic"` — Multi-round Claude vs GPT debate
- `codemoot cleanup` — Scan for unused deps, dead code, duplicates
- `codemoot plan generate "task"` — Generate plans via multi-model loop
- `codemoot plan review <plan-file>` — GPT review of execution plans
- `codemoot shipit --profile safe` — Composite workflow (lint+test+review)
- `codemoot cost` — Token usage dashboard
- `codemoot doctor` — Check prerequisites

### Slash Commands
- `/codex-review` — Quick GPT review (uses codemoot review internally)
- `/debate` — Start a Claude vs GPT debate
- `/plan-review` — GPT review of execution plans
- `/build` — Full build loop: debate → plan → implement → GPT review → fix
- `/cleanup` — Bidirectional AI slop scanner

### When to Use CodeMoot
- After implementing a feature → `codemoot review src/`
- Before committing → `codemoot review --diff HEAD --preset pre-commit`
- Architecture decisions → `/debate "REST vs GraphQL?"`
- Full feature build → `/build "add user authentication"`
- After shipping → `codemoot shipit --profile safe`

### Session Tips
- Sessions auto-resume — GPT retains context from prior commands
- `codemoot session list` shows all sessions with token usage
- `codemoot cost --scope session` shows current session spend
- Start fresh with `codemoot session start --name "new-feature"`
