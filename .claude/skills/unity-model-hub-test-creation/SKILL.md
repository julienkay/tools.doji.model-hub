---
name: unity-model-hub-test-creation
description: Add or revise Unity tests for `tools.doji.model-hub`. Use when creating regression coverage for package runtime models, editor package discovery, manifest parsing, menu/editor workflows, asmdef-visible test seams, or package test-suite structure under `tools.doji.model-hub/Tests/`.
---

# Unity AI Model Hub Test Creation

Use this skill when adding or updating tests for `tools.doji.model-hub`.

Primary scope:

- `tools.doji.model-hub/Tests/`
- `tools.doji.model-hub/Editor/`
- `tools.doji.model-hub/Runtime/`
- temp directories or in-memory data used by editor tests

## Choose test target first

- Put editor discovery and manifest-scan coverage in `tools.doji.model-hub/Tests/Editor/`.
- Add runtime tests only when package gets runtime test assembly and runtime behavior needs direct coverage.
- Prefer extending existing fixture for same output contract before creating new fixture.
- Create new fixture only when assertions cover distinct contract and would make existing fixture noisy.

## Default testing strategy

1. Prefer black-box or near-black-box tests against stable package behavior.
2. Use temp directories under `Path.GetTempPath()` for filesystem-backed scenarios.
3. Assert user-visible contract: discovered models, parsed manifest values, generated paths, issue messages, menu-visible status data.
4. Keep cleanup in `TearDown`.
5. If Unity API makes black-box testing impractical, add smallest internal seam needed for deterministic tests.

## What to cover first

- manifest parsing rules: required fields, version checks, duplicate ids, invalid JSON, whitespace handling
- package discovery behavior: package ordering, display/version fallback, manifest hashing, skip behavior, non-fatal issue collection
- editor workflow outputs that users or maintainers rely on, not helper implementation details
- regression cases around package paths, missing files, malformed manifests, and mixed valid/invalid package sets

## What to avoid by default

- tests that depend on live HuggingFace network access
- tests that need opening extra Unity projects or manual editor interaction
- assertions on incidental implementation details when public or assembly-visible contract captures behavior
- broad full-file snapshots when targeted assertions prove contract
- adding test seams larger than needed to isolate Unity package manager or filesystem dependencies

## Project-specific conventions

- Keep package-owned tests inside `tools.doji.model-hub/Tests/`, not companion project `projects/AI Model Hub/Assets`.
- Editor tests need editor-only asmdef with explicit references and `optionalUnityReferences: [ "TestAssemblies" ]`.
- If tests need `internal` members, expose them deliberately with `InternalsVisibleTo` instead of widening runtime/editor API.
- Preserve Unity `.meta` files for new test folders, asmdefs, and scripts.
- Follow project C# style: no raw public fields, `_camelCase` private fields, XML summaries on public and non-obvious internal types.
- Give each test method a short, simple XML summary that answers "what is tested here?" in plain language.
- If method name uses project terms like "normalized", briefly explain them in summary instead of assuming reader already knows term.
- When test coverage changes durable behavior contracts or adds new regression areas, update `internal-docs/testing.md` so maintainers can find the relevant fixture later.

## Implementation pattern

```csharp
internal sealed class PackageDiscoveryTests
{
    private string _rootDirectory;

    [SetUp]
    public void SetUp()
    {
        _rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "Doji.ModelHub.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, true);
        }
    }
}
```

## Assertion pattern

1. Build only data needed for scenario.
2. Write minimal manifest JSON directly in test when that keeps intent obvious.
3. Assert both positive contract and failure/skip behavior where relevant.
4. For hashes or paths, compute expected value in test instead of hardcoding magic strings.
5. When issue text matters to users or maintainers, assert exact message.

## Test seam rules

- Start with existing public API.
- If Unity package manager or editor singleton state blocks deterministic tests, add narrow internal overload or snapshot type.
- Keep seam close to boundary being isolated.
- Do not refactor unrelated architecture only to make tests easier.

## Execution rule

- Do not run tests by default.
- Add or update tests, verify files and asmdefs, then stop unless user explicitly asks to run Unity tests.
- If user asks for validation without running tests, report that coverage was added but not executed.

## Finish checklist

- confirm fixture lives in correct package test folder
- confirm asmdef references only needed assemblies
- confirm new folders/files have `.meta`
- confirm each test method has clear, simple documentation
- confirm `internal-docs/testing.md` still maps new or changed coverage when test scope changed
- confirm assertions target behavior users or maintainers depend on
- confirm any internal seam is minimal and justified by testability
- tests do not need to be run unless user explicitly requested it
