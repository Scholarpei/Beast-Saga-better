# Home Fullscreen Panel and External Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a browser-based layout previewer and synchronize its full-screen Home panel and split archive layout into the game plugin.

**Architecture:** Add a standalone .NET preview service that reads sandbox checkpoint metadata and serves a self-contained HTML preview. Keep shared size and text decisions in testable core APIs, then render the same structure with Unity IMGUI. The preview edits only in-memory state and never writes save data.

**Tech Stack:** .NET 8 minimal API, vanilla HTML/CSS/JavaScript, existing .NET 6 plugin and Unity IMGUI.

**Spec:** `docs/superpowers/specs/2026-11-11-home-panel-design.md`

## Global Constraints

- Do not alter save/load safety policy or add save-writing capability.
- The preview defaults to `sandbox/Game/SafeDialogueToolsData/checkpoints`.
- Preview note edits are browser-only simulations.
- Existing tests and Release builds must pass.

## Review Focus

- Missing or unreadable screenshot: show a stable placeholder rather than hiding the detail pane.
- Legacy/manual metadata with nullable map/level/round: display unknown values consistently.
- No manual checkpoints: keep the empty state and full-screen shell usable.
- Narrow screen heights: full-screen layout must not exceed screen bounds.
- Path inputs: preview service must reject roots outside the project workspace.

---

### Task 1: Fullscreen and Archive Layout Rules

**Files:**
- Modify: `Core/CommandPanelState.cs`
- Test: `Tests/Program.cs`

**Interfaces:**
- Produces: `CommandPanelLayout.Create(int screenWidth, int screenHeight)` full-screen bounds.
- Produces: `CommandPanelMetrics.Create(CommandPanelLayout layout)`, with `Scale`, `ArchiveListWidth`, `DetailPreviewHeight`, and row/major size values.

- [ ] Add failing assertions that full-screen bounds span the screen minus symmetric 16/24px margins.
- [ ] Add failing assertions that archive list width and preview height scale within minimum/maximum bounds.
- [ ] Implement layout and metrics; run all tests.

### Task 2: External Preview Service and Page

**Files:**
- Create: `Tools/PanelPreview/PanelPreview.csproj`
- Create: `Tools/PanelPreview/Program.cs`
- Create: `Tools/PanelPreview/index.html`
- Create: `tools/start-panel-preview.ps1`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `GET /api/state` returning status, tabs, checkpoints, selected checkpoint, screenshot URL, and preview-only notes.
- Produces: `GET /api/screenshots/{generation}` returning the selected generation's thumbnail only after checkpoint manifest validation.
- Produces: `POST /api/select` updating preview selection only.

- [ ] Add path-root safety checks and manifest validation.
- [ ] Serve the page with 1280×720, 1600×900, 1920×1080, and custom resolution controls.
- [ ] Recreate full-screen panel chrome, status tab, and split archive layout with the shared metrics logic.
- [ ] Run the service and verify API/page responses without writing checkpoint files.

### Task 3: In-Game Fullscreen Panel

**Files:**
- Modify: `Plugin/Plugin.cs`

**Interfaces:**
- Consumes: `CommandPanelMetrics.Create(CommandPanelLayout layout)`.
- Produces: full-screen status and split archive IMGUI rendering with large screenshot and detail fields.

- [ ] Replace fixed 72%-width panel with full-screen layout.
- [ ] Scale panel fonts and controls from screen height.
- [ ] Implement left archive list and right detail pane; preserve existing actions and note editing.
- [ ] Build plugin in Release.

### Task 4: Verification and Handoff

**Files:**
- Modify: `README.md`

- [ ] Run the full test project.
- [ ] Build plugin in Release.
- [ ] Start preview service, load actual sandbox state, exercise tabs/selection/note simulation, and capture a browser screenshot for review.
- [ ] Document how to start the preview and deploy manually.
