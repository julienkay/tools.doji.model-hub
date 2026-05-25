---
name: user-facing-docfx-docs
description: Write or revise user-facing DocFX documentation for the in-development `com.doji.hf-hub` Unity package. Use when expanding the manual, documenting package manifests, shared cache behavior, HuggingFace download workflows, Sentis-related outputs, editor status surfaces, or improving DocFX navigation while keeping maintainer-only build notes out of consumer docs.
---

# User-Facing DocFX Docs

Use this skill when editing `docs/` for package consumers.

## Audience split

Keep user-facing docs focused on using the package:

- what HF Hub does for package authors and Unity projects
- when to use package manifests, shared cache, and generated assets
- what editor-visible surfaces or menu entries do
- what files, folders, downloads, or generated outputs the user should expect

Keep maintainer-only material out of consumer docs.

## Source of truth

Do not guess from UI text alone. Verify against implementation before documenting behavior.

Read only the files needed for the current page, usually:

- package-level constants and manifest contracts in `com.doji.hf-hub/Runtime/`
- runtime-safe manifest and model dependency types in `com.doji.hf-hub/Runtime/`
- editor package discovery, menu items, cache services, and download logic in `com.doji.hf-hub/Editor/`
- package tests in `com.doji.hf-hub/Tests/` when behavior or edge cases need confirmation
- existing user docs in `docs/` so new pages match the current manual structure

Document actual behavior, including:

- manifest fields, validation rules, and uniqueness constraints
- package discovery conditions and failure modes
- shared cache paths, lock behavior, and metadata side effects
- generated project assets, output paths, and when regeneration is required
- editor-only vs runtime-safe behavior boundaries
- current limitations, missing UI, or in-development workflow gaps when they materially affect user expectations

## Documentation standards

Write from orientation to depth: start with the reader's context, the workflow or problem being solved, and the expected outcome, then move into steps, concrete outputs, reference details, edge cases, and constraints.

Prefer task-oriented docs first, then reference depth:

1. Landing page with workflow map
2. Task pages for package author and Unity project workflows
3. Reference pages for manifest schema, cache layout, and editor status surfaces
4. Supporting pages for generated output, Sentis conversion expectations, and troubleshooting

Before rewriting a page or section, decide these three things privately:

1. Where does this page sit in the docs flow?
2. What job should this section do for the future reader?
3. What is the main point the reader must leave with?

Reflect those answers through refactoring, not explanation:

- change headings so they carry the right question or topic
- move sections so the reader gets orientation before detail
- cut repetition that does not help a decision or action
- replace vague framing with concrete effects, outputs, paths, or UI behavior

Write to the future reader of the docs, not to the current user prompt:

- avoid meta lines such as "this page explains" or "the goal of this page is" unless the orientation is genuinely useful
- do not narrate your documentation strategy inside the docs
- show the important consequence directly instead of explaining that you are about to explain it
- if a sentence sounds like reassurance to the requester rather than guidance to the product user, rewrite or delete it
- prefer refactoring-style fixes to the prose: rename headings, reorder sections, tighten labels, or replace meta sentences with direct content

The resulting prose should sound like product documentation:

- direct, specific, and user-facing
- centered on UI behavior, generated output, and reader decisions
- written as statements of fact, consequences, paths, options, and workflows
- structured so headings do orientation work and paragraphs do explanatory work
- free of conversational reassurance, prompt-response tone, or commentary about the writing itself

Prefer showing over telling:

- if the point is that a field changes generated output, show the output it changes
- if the point is that a toggle reveals nested options, document the conditional behavior directly
- if the point is that a page is for reference, make the headings and layout reference-friendly instead of saying so
- if the point is that a workflow is the main entry point, prove it through ordering, screenshots, and link placement

Default to visuals-first documentation:

- if a screenshot explains a UI section faster than prose, place the image before the explanation
- when documenting a specific editor dialog, menu result, or inspector surface, include a screenshot of that section
- if a page introduces a major workflow surface, include an overview screenshot near the top
- create placeholder PNGs in `docs/images/` when the final screenshots are not available yet, and reference them from the markdown immediately

Screenshot best practices:

- prefer cropped, purpose-specific screenshots over one huge full-screen capture when the page is about a specific section
- keep the relevant control visible in the first screenful; do not make the reader hunt for the thing being discussed
- use one overview screenshot for orientation, then smaller section screenshots for detailed reference pages
- when a screenshot is meant to explain a menu command, status dialog, or conditional UI state, capture it in the state being described
- keep filenames descriptive and stable, usually `<page-topic>-<section>.png`
- when adding a placeholder, use the final intended filename so the real screenshot can replace it without markdown changes
- keep alt text functional and specific, describing the UI surface shown rather than repeating the heading verbatim

For HF Hub workflow documentation:

- distinguish package author scope, project scope, shared machine cache scope, and generated asset scope clearly
- explain which data lives in package manifests versus shared cache metadata versus generated project assets
- document the user-visible trigger for each workflow, such as package install, menu action, confirmation prompt, or future import step
- call out when a workflow is editor-only today but intentionally structured for future runtime reuse

For manifest and cache reference pages:

- document every supported manifest field and what it controls
- explain validation rules and user-visible failures with concrete examples
- show expected paths, filenames, and cache metadata where those details drive behavior
- describe Sentis-related outputs and regeneration expectations only to the extent the implementation actually supports them

## Project-specific conventions

- Use `docs/manual/` for user-facing guidance pages.
- Keep `docs/index.md` as a concise entry page that points into the manual and API reference.
- Update `docs/manual/toc.yml` when adding or reordering manual pages.
- Keep the tone practical and explicit. Avoid filler marketing language.
- Prefer concrete file names, menu paths, cache paths, generated output paths, and manifest filenames.
- Put screenshot placeholders under `docs/images/`, preferably grouped by topic such as `docs/images/manual/`.
- Treat `hf-hub-manifest.json` as the primary package author entry point unless the current implementation adds another stronger user-facing workflow.
- Prefer `com.doji.hf-hub/` as the package-owned source of truth and `projects/HF Hub/Assets` only when documenting companion-project examples or sample scenes.
- Reflect the active architecture direction: shared Hub logic should be documented as runtime-safe where implemented, with editor-only integration called out separately.

## Local preview workflow

After changing user-facing docs in this repository, default to rebuilding and serving the DocFX site unless the user explicitly says not to.

Run these commands from the repository root:

- `docfx metadata docs/docfx.json`
- `docfx build docs/docfx.json`
- `docfx serve docs/_site`

Notes:

- If `docfx` is not installed, install it as a global .NET tool with `dotnet tool install -g docfx`.
- On macOS with Homebrew `.NET`, if `docfx` cannot locate the SDK, set `DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec` and prepend that directory to `PATH` before running `docfx`.
- Run `metadata` before `build` when API docs may have changed, especially after changing public types, namespaces, XML documentation, or assembly-visible API shape.
- `build` alone does not regenerate this repository's API metadata under `docs/api/`.
- Run the build after documentation edits so navigation and broken-link issues surface immediately. Treat a successful `build` as the authoritative local verification step.
- `serve` does not rebuild; it only serves the current `docs/_site` output.
- `docfx serve` prints the local preview URL after startup. Stop the server with `Ctrl+C`.
- If a serve session is already running for the current workspace, prefer reusing it after a fresh build instead of starting duplicates.
- Do not assume HTTP checks from the current Codex session can reach a DocFX server started by another session. Session-local `curl` failures do not prove the preview is stale or broken.
- Use HTTP checks such as `curl -I --max-time 2 http://127.0.0.1:8080` only as positive confirmation when they succeed from the current session.
- If a preview server likely already exists but this session cannot reach it, report that the build succeeded and that preview reachability appears session-local rather than treating it as a DocFX failure.
- Report the local preview URL or any build errors back to the user. When reusing an existing preview, make it clear whether the URL was confirmed from the current session or inferred from an already-running server.
- If namespaces or generated API file names changed, stale files can linger in `docs/api/` and `docs/_site/api/`. In that case, clean generated API outputs first while preserving hand-authored files such as `docs/api/index.md` and `docs/api/.gitignore`.
- If the repository also ships a PDF DocFX config such as `docs/docfx-pdf.json`, use `docfx build` against that file when the task explicitly involves PDF docs, and report the configured output location rather than assuming a fixed PDF path across repositories.

## Verification checklist

Before finishing:

- confirm every newly documented field or behavior against code
- confirm every new manual page is linked from `docs/manual/toc.yml`
- confirm `docs/index.md` still matches the manual structure
- confirm user-facing pages do not contain maintainer-only DocFX build instructions
- confirm image references resolve to actual files under `docs/images/`
- run `docfx metadata docs/docfx.json` when API docs may have changed
- run `docfx build docs/docfx.json` unless the user explicitly skips local verification
- start, refresh, or reuse `docfx serve docs/_site` unless the user explicitly skips local preview
- do not treat a failed `curl` from the current session as evidence that an already-running preview server is unusable
- if stale served output is involved, remember that `docfx serve docs/_site` serves existing generated files and does not rebuild them
