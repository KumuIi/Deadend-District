---
name: plan-review
description: Send execution plans to GPT for structured review via Codex CLI. Use when you have a written plan and want independent validation before implementation.
user-invocable: true
---

# /plan-review — GPT Review of Execution Plans

## Usage
`/plan-review <plan-file-or-description>`

## Description
Sends a Claude-authored execution plan to GPT via `codemoot plan review` for structured critique. GPT returns ISSUE (HIGH/MEDIUM/LOW) and SUGGEST lines with file-level references.

## Instructions

### Step 1: Prepare the plan
- If the user provides a file path, use it directly
- If reviewing the current conversation's plan, write to a temp file first

### Step 2: Run plan review
```bash
codemoot plan review <plan-file> [--phase N] [--build BUILD_ID] [--timeout ms] [--output file]
```

### Step 3: Parse and present output
JSON output includes: `verdict`, `score`, `issues[]` (severity + message), `suggestions[]`.

Present as:
```
## GPT Plan Review
**Score**: X/10 | **Verdict**: APPROVED/NEEDS_REVISION
### Issues
- [HIGH] description
### Suggestions
- suggestion text
```

### Step 4: If NEEDS_REVISION
Fix HIGH issues, revise the plan, re-run. Session resume gives GPT context of prior review.

### Important Notes
- **Session resume**: GPT remembers prior reviews via thread resume
- **Codebase access**: GPT reads actual project files to verify plan references
- **Output capped**: JSON `review` field is capped to 2KB. Use `--output` for full text
- **Zero API cost**: Uses ChatGPT subscription via Codex CLI
