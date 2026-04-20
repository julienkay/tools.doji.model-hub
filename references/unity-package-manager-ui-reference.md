# Unity Package Manager UI Reference

Maintainer note for finding Unity Package Manager editor UI source when shaping HF Hub editor UX.

Purpose:

- point future sessions at official upstream source first
- keep local checkout location consistent with other reference repos
- highlight most useful files for Package Manager-like list/details interaction work

## Preferred Upstream Source

Primary reference repo:

- `https://github.com/Unity-Technologies/UnityCsReference`

Primary local checkout path:

- `references/UnityCsReference`

Reason:

- official Unity editor C# reference source
- contains `PackageManagerUI` module used for Package Manager window patterns

Important upstream note:

- Unity states this repo is reference-only code
- current public repo note says updates are paused beyond Unity `6000.2.0b4`
- still useful for layout, naming, and interaction patterns even when editor version differs

## Download Or Refresh

Clone once:

```powershell
git clone https://github.com/Unity-Technologies/UnityCsReference.git references/UnityCsReference
```

Refresh only when task depends on newer upstream changes:

```powershell
git -C references/UnityCsReference pull --ff-only
```

Use same rule as `huggingface_hub` reference:

- reuse local checkout when present
- do not vendor this code into package
- read only files relevant to current UI decision

## First Files To Inspect

Start here for Package Manager list/details behavior:

- `references/UnityCsReference/Modules/PackageManagerUI/Editor/UI/PackageItem.cs`
- `references/UnityCsReference/Modules/PackageManagerUI/Editor/UI/PackageListView.cs`
- `references/UnityCsReference/Modules/PackageManagerUI/Editor/UI/PackageDetails.cs`
- `references/UnityCsReference/Modules/PackageManagerUI/Editor/UI/PackageToolbar.cs`

Useful directory:

- `references/UnityCsReference/Modules/PackageManagerUI/Editor/UI`

What these answer:

- row selection and highlight behavior
- right-side status icon placement
- split list/details composition
- toolbar layout and status affordances

## HF Hub Guidance

Use Package Manager as interaction reference, not copy target.

Important difference:

- Unity Package Manager UI code is UI Toolkit based
- current HF Hub Model Hub window is IMGUI

So copy:

- hierarchy
- interaction model
- density
- visual emphasis

Do not copy:

- internal package services
- UI Toolkit-specific implementation details
- reference-source code into package

## Useful Links

- UnityCsReference repo: <https://github.com/Unity-Technologies/UnityCsReference>
- Package Manager UI folder: <https://github.com/Unity-Technologies/UnityCsReference/tree/master/Modules/PackageManagerUI/Editor/UI>
- Example item renderer: <https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/PackageManagerUI/Editor/UI/PackageItem.cs>
- Example list view: <https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/PackageManagerUI/Editor/UI/PackageListView.cs>
