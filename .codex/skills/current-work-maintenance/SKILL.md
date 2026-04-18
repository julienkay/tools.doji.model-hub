---
name: current-work-maintenance
description: Maintain `.codex/current-work.md` as the short session-resume doc for this repository. Use when updating active tasks, changing focus, recording blockers, preparing a handoff, or trimming completed work from the current work list after a task changes project state.
---

# Current Work Maintenance

Use this skill whenever work changes the active project state enough that the next agent session should pick up from updated notes.

Primary file:

- `.codex/current-work.md`

## Purpose

Keep `.codex/current-work.md` as a fast session-resume file, not a history log and not an architecture document.

The file should let the next agent answer three questions in under a minute:

1. What is the current focus?
2. What still needs to be done next?
3. What docs or files must be read before editing?

## Required Rules

- No completed work.
- No long history.
- No architecture explanations unless needed to unblock the next task.
- Delete done items instead of archiving them there.
- Keep it short enough to read in under a minute.
- If the user confirms that relevant tests passed for a task, treat that task as successfully finished work unless there is still explicit follow-up remaining.

## What Belongs Here

- the current focus
- still-open tasks that are next in line
- blockers or decisions that are actively constraining current work
- exact docs or code areas the next agent should read first
- concrete exit criteria for the active slice

## What Does Not Belong Here

- lists of finished phases or already-implemented features
- broad backlog items that are not active now
- detailed architecture rationale that belongs in `.codex/architecture/`
- long narrative status updates
- duplicate content already captured in stable maintainer docs

## Update Workflow

1. Read the current file.
2. Remove completed items instead of marking them as done.
3. If the user reports that written tests passed, treat the covered task as complete and remove it from `Next Tasks` unless another explicit action is still pending.
4. Rewrite focus if the active slice has changed.
5. Keep only the next actionable tasks.
6. Move durable implementation detail to `.codex/architecture/` if it matters later.
7. Move non-active ideas, longer-scoped epics, or lower-priority work to `.codex/backlog.md` if they should be kept.
8. Confirm the file is still short and scannable.

## Default Shape

Use this structure unless there is a strong reason not to:

```md
# Current Work

## Focus
Short sentence.

## Next Tasks
- [ ] ...
- [ ] ...

## Blockers And Decisions Needed
- ...

## Read Before Editing
- .codex/architecture/runtime-locator.md
- com.doji.hf-hub/...

## Exit Criteria
- ...
```

## Editing Standard

- Prefer replacing vague bullets with concrete next actions.
- Keep section names stable so agents can scan quickly.
- If a section has nothing useful to say, keep it brief instead of padding it.
- When a task is fully done, remove it from `Next Tasks` and update `Focus` or `Exit Criteria` to match the remaining active slice.
- When the user verifies that tests passed for a task, that validation is enough to remove the corresponding TODO from `Next Tasks` unless the task still has non-test follow-up work.
- Move lower-priority or longer-running items to `.codex/backlog.md` instead of leaving them in `current-work.md`.
- If all active work changed, rewrite the file to fit the new slice instead of incrementally preserving stale context.

## Coordination Rule

When a task meaningfully changes project focus, blockers, or next steps, update `.codex/current-work.md` before finishing unless the user explicitly says not to.
