---
name: debate
description: Real Claude vs GPT multi-round debate. Use when you need a second opinion, want to debate architecture decisions, or evaluate competing approaches.
user-invocable: true
---

# /debate — Real Claude vs GPT Multi-Round Debate

## Usage
`/debate <topic or question>`

## Description
Structured debate: Claude proposes, GPT critiques, Claude revises, GPT re-evaluates — looping until convergence or max rounds. Real multi-model collaboration via codemoot CLI with session persistence.

## Instructions

### Phase 0: Setup
1. Parse topic from user's message
2. Start debate:
```bash
codemoot debate start "TOPIC_HERE"
```
3. Save the `debateId` from JSON output
4. Announce: "Entering debate mode: Claude vs GPT"

### Phase 1: Claude's Opening Proposal
Think deeply. Generate your genuine proposal. Be thorough and specific.

### Phase 1.5: Gather Codebase Context
If topic relates to code, use Grep/Glob/Read to find relevant files. Summarize for GPT.

### Phase 2: Send to GPT
```bash
codemoot debate turn DEBATE_ID "You are a senior technical reviewer debating with Claude about a codebase. You have full access to project files.

DEBATE TOPIC: <topic>
CODEBASE CONTEXT: <summary>
CLAUDE'S PROPOSAL: <proposal>

Respond with:
1. What you agree with
2. What you disagree with
3. Suggested improvements
4. STANCE: SUPPORT, OPPOSE, or UNCERTAIN" --round N
```

### Phase 3: Check Convergence
- STANCE: SUPPORT → go to Phase 5
- Max rounds reached → go to Phase 5
- Otherwise → Phase 4

### Phase 4: Claude's Revision
Read GPT's critique. Revise genuinely. Send back to GPT.

### Phase 5: Final Synthesis
```bash
codemoot debate complete DEBATE_ID
```
Present: final position, agreements, disagreements, stats.

### Rules
1. Be genuine — don't just agree to end the debate
2. Session resume is automatic via callWithResume()
3. State persisted to SQLite
4. Zero API cost (ChatGPT subscription)
5. 600s default timeout per turn
6. JSON `response` field capped to 2KB — check `responseTruncated` boolean
7. Progress heartbeat every 60s, throttled to 30s intervals
