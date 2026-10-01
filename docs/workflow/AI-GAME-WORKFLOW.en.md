# AI Game Production Workflow

> **Version 2.0 · 2026-09-28** · Vietnamese version: [AI-GAME-WORKFLOW.vi.md](AI-GAME-WORKFLOW.vi.md) (same content; keep both in sync).
> A project-independent rule set for making a game with AI assistance. It is written for two readers:
> **the AI agent**, which must follow it step by step, and **the human team**, which makes the decisions.
> If the project uses an engine framework with its own rules, those rules also apply and win on technical details.

## Contents

1. [How to use this document](#1-how-to-use-this-document)
2. [Is the 7-step flow enough? (review)](#2-is-the-7-step-flow-enough-review)
3. [Roles and decision rights](#3-roles-and-decision-rights)
4. [The working protocol: AI proposes, asks, human decides](#4-the-working-protocol-ai-proposes-asks-human-decides)
5. [Global rules](#5-global-rules)
6. [Pipeline overview](#6-pipeline-overview)
7. [The steps in detail (Step 0 → Step 8)](#7-the-steps-in-detail)
8. [Animation tool choice (Spine, DragonBones, engine-native)](#8-animation-tool-choice)
9. [Lessons learned](#9-lessons-learned)
10. [Templates](#10-templates)
11. [Change log](#11-change-log)

---

## 1. How to use this document

**For the human team**

1. Copy this file (both languages) into `docs/workflow/` of the game project.
2. Add one line to the project's agent instructions (`AGENTS.md`, `CLAUDE.md` or equivalent):
   *"Follow `docs/workflow/AI-GAME-WORKFLOW.en.md`. Read `docs/workflow/STATE.md` and `docs/workflow/DECISIONS.md` before doing any work."*
3. Create `STATE.md` and `DECISIONS.md` from the templates in [§10](#10-templates).
4. Decide who approves each gate (§3) and write their names in `STATE.md`.

**For the AI agent — at the start of every session**

1. Read `STATE.md` (current step, open questions, next actions) and `DECISIONS.md`.
2. Say in one short message: current step, what is done, what is waiting for a human decision.
3. Never start a later step before the current step's gate is approved, except for the parallel work allowed in §6.
4. Follow the protocol in §4 for every piece of work.
5. At the end of the session, update `STATE.md`.

---

## 2. Is the 7-step flow enough? (review)

The original 7 steps are:

1. Game design document.
2. Mock-up.
3. Art in design files with QA.
4. Spine animation.
5. Game logic.
6. Integration.
7. AI testing against the mock-up.

They cover the **core production line** well. They were checked against what a real casual puzzle project actually needed: rules engine, 60 levels, economy, daily rewards, shop, FTUE, audio, localization, Android build and performance. Several things had no home in the 7 steps.

| Needed in practice | Covered by the 7 steps? | Resolution in this document |
|---|---|---|
| Concept, reference analysis, scope, platform, tech choice | No (was implicit) | **Step 0 — Kickoff** added |
| Level content (generator, solver, difficulty curve) | Partly (step 1 mentions levels) | **Level track** inside Steps 1 and 5 |
| Economy and balance numbers | Partly | **Economy sheet** in Step 1, config data in Step 5 |
| Audio (SFX, music) | No | **Audio track** in Steps 1, 4 and 6 |
| Text and localization | No | **Text track** in Steps 1 and 6 |
| Project setup and architecture | No | Part of Step 0 (decision) and Step 5 |
| Performance budget and device testing | No | Budgets in Step 0, checks in Steps 7 and 8 |
| Human playtest (fun, difficulty) | No (AI testing only) | **Step 8** |
| Build, store, SDKs (ads/IAP/analytics), release | No | **Step 8** |
| Going back when a later step finds a design problem | No | **Change request rule** in §4.4 |

**Decision in this version:** the 7 core steps are kept, with the same numbers and meaning. **Step 0** (kickoff) and **Step 8** (device, playtest, release) are added around them. Audio, text, levels and economy run as **tracks** through the existing steps. They are not separate steps, so the flow stays easy to follow.

---

## 3. Roles and decision rights

| Decision | Human decides | AI does |
|---|---|---|
| Game concept, scope, MVP feature list | ✅ | proposes options, estimates effort, lists risks |
| Rules, level curve, economy targets | ✅ | drafts, simulates, points out ambiguities |
| Visual direction, mock-up approval | ✅ | generates alternatives, checks consistency |
| Art approval, acceptable defects | ✅ | produces/repairs, runs automated QA |
| Tool, engine, framework, paid licences, SDKs | ✅ | compares, tests on a small sample, recommends |
| Architecture details inside an approved framework | informed | decides and records (asks if it changes structure) |
| Test pass criteria, accepted differences, release | ✅ | tests, reports evidence, recommends go/no-go |
| Anything destructive, public, or costing money | ✅ always | never does it without explicit approval |

Every step has one named **approver**. When the approver is not available, the AI may continue only with work that does not depend on the pending decision, and it must say so.

---

## 4. The working protocol: AI proposes, asks, human decides

### 4.1 The loop for every step

```
 ┌─ 1 START    read STATE/DECISIONS, list inputs, confirm the step goal
 │  2 PROPOSE  a short plan + options with one recommendation (and why)
 │  3 ASK      the step's decision questions (§7), grouped, with defaults
 │  4 CONFIRM  wait for answers; record them in DECISIONS.md
 │  5 PRODUCE  create the outputs
 │  6 VERIFY   run the step's checks; collect evidence
 │  7 REVIEW   present outputs + evidence + open issues to the approver
 └─ 8 CLOSE    approver accepts → update STATE (gate passed) → next step
               approver rejects → back to 2 or 5 with the feedback recorded
```

### 4.2 How the AI asks questions

- Ask **only what changes the outcome**. Do not ask what can be answered from the approved documents or a sensible default.
- Ask at most **5 questions per round**, grouped by topic. Put the most important one first.
- Each question has **2–4 concrete options**. Mark one **(recommended)** and explain it in one line. Allow a free answer.
- Say what the AI will assume if there is no answer, **except for "must-decide" items** (marked 🔒 in §7). For those the AI waits and never assumes.
- Use the team's language and avoid jargon. Add a small example or sketch when it helps choose.
- After the answers, repeat the final decision in one line and record it in `DECISIONS.md`.

Example:

> **Q1 🔒 Login.** How do players identify themselves?
> a) Guest only, save on device **(recommended for MVP: no backend needed)** · b) Guest + platform login (Google/Apple) with cloud save · c) Account with email.
> *Without an answer I will not design account features.*

### 4.3 How the AI reports

Every report at the end of a step (or session) has the same shape:

1. **Done:** outputs with links.
2. **Evidence:** what was checked, how, and the result. Say which checks were *not* run.
3. **Open issues:** blocking vs non-blocking.
4. **Needs your decision:** questions in the §4.2 format.
5. **Next:** what happens after approval.

### 4.4 Change requests (going back)

When a later step finds a problem in an approved earlier output (a rule that cannot be implemented, a mock-up that does not fit the device, a level that is too hard):

1. The AI stops that part of the work and writes a **change request**: what, why, which approved item it affects, the options and a recommendation.
2. The approver of the *earlier* step decides.
3. The approved document is updated with a new version and a change-log line. Downstream items that depended on it are listed and rechecked.

The AI never silently changes an approved design, mock-up or art to make its current step easier.

---

## 5. Global rules

**Decisions and sources**
- **G1. One approved source per step.** Only approved, versioned outputs flow to the next step (`lobby-mockup-v03`). Everything else is a draft.
- **G2. Decisions are written down.** A decision that is not in `DECISIONS.md` has not been made.
- **G3. The human approves design, visuals, art, tools with cost, and release.** The AI never approves its own work.

**Truth and evidence**
- **G4. Evidence before claims.** "Done", "works" and "looks right" require evidence seen in the current session: test output, a screenshot that was actually looked at, a log that was read. Old reports prove nothing about today.
- **G5. Name the kind of evidence.** Editor, build, real device and human playtest are different. A bot's win rate is not a player's win rate.
- **G6. Report what was not done.** Skipped checks, untested platforms and assumptions are listed explicitly.

**Assets and legal**
- **G7. Record provenance.** Every AI-generated asset (image, audio, text, level) keeps its prompt, tool/model, date and output path. Mark AI drawings as design drawings, not game screenshots.
- **G8. Reference games inform, they are not copied.** Pacing, structure and value ranges may be studied. Art, audio, text and level data are not shipped. Record what was studied.
- **G9. Check licences** for tools, runtimes, fonts, audio and SDKs before they enter the project.
- **G10. No secrets** (tokens, keys, account data, licence holders) in documents, logs or commits.

**Craft**
- **G11. Edit through the tool.** Scenes, prefabs, layered design files and animation projects are changed through their editor or its automation API, not by rewriting the raw file.
- **G12. Generated files are regenerated, never hand-edited.**
- **G13. Keep sources editable.** Keep the PSD, the animation project and the level generator, not only the exports.
- **G14. Budget expensive loops.** Agree on limits for image generations, play sessions and builds per step. A tool timeout does not mean the operation stopped: check state before retrying.
- **G15. Protect user data.** Back up and restore save files around testing. Never test on someone's real progress without a backup.

**Game code (apply unless the framework says otherwise)**
- **G16.** Game rules live in a pure, engine-independent model with injected randomness and time. The seed is logged.
- **G17.** Views only display data and forward input. Decisions belong to controllers.
- **G18.** Every durable change (move, reward, purchase) is one atomic commit: change, then save, and restore everything if the save fails.
- **G19.** Rewards are saved before they are shown, and never granted twice. Closing or cancelling a dialog never grants anything.
- **G20.** Balance numbers and level content are data (config/level files with names, defaults, ranges and a content revision), not constants in code.
- **G21.** All visible text goes through localization keys. Colours, sizes and fonts come from one design-token file.

---

## 6. Pipeline overview

```
Step 0 Kickoff ──► Step 1 GDD ──► Step 2 Mock-up ──► Step 3 Art ──► Step 4 Animation ──► Step 6 Integrate ──► Step 7 AI test ──► Step 8 Device/Playtest/Release
                        │                                                                   ▲
                        └──────────────► Step 5 Logic (starts after Step 1 gate) ────────────┘
Tracks running through the steps:  Levels (1,5,7,8) · Economy (1,5,8) · Audio (1,4,6,7) · Text/Localization (1,2,6,7)
```

**Allowed parallel work.** After the Step 1 gate, Step 5 (rules engine, tests, level tools) can start with grey-box visuals. Steps 3 and 4 can run in parallel on approved mock-ups. Everything meets in Step 6.

**Artifacts (suggested layout):**

```
docs/workflow/   STATE.md, DECISIONS.md, this document
docs/design/     GDD.md, features/*.md, economy-sheet, level-design.md, glossary.md, screen-inventory.md
docs/mockups/    <screen>-vNN.png, prompts, approval record
art/source/      layered masters (PSD)       art/export/   exports + export-report
art/anim/        animation projects, build scripts, export settings
audio/           sources, exports, audio-list
<engine>/        code, content (levels, config, localization), scenes
docs/qa/         test plan, test cases, captures, judge reports, bug list, release checklist
```

---

## 7. The steps in detail

Each step lists: **Purpose · Inputs · AI proposes · AI must ask (🔒 = must-decide, never assumed) · Outputs · Rules · Gate (done when)**.

### Step 0 — Kickoff: concept, scope and setup *(added)*

- **Purpose:** agree on what is being made and with what, before any design work.
- **Inputs:** the idea or brief, reference games, team constraints.
- **AI proposes:**
  - a one-page concept (genre, core loop, target player, session length);
  - an analysis of the reference games (loop, features, pacing, monetization);
  - an MVP feature list versus later features;
  - options for platform, engine and framework;
  - a risk list and a rough effort estimate.
- **AI must ask:**
  - 🔒 Target platforms, orientation and reference resolution?
  - 🔒 Engine and framework, including version?
  - 🔒 MVP scope: number of levels, which features are in and which are out?
  - 🔒 Monetization: none, ads, IAP or both? Real or simulated in MVP?
  - Languages at launch? Art style direction (2–3 options)?
  - Who approves each step? Deadline and budget (AI/image generation, tools, licences)?
  - Performance targets (minimum device, FPS, build size)?
- **Outputs:** `docs/design/concept.md`, the reference analysis, the MVP scope, a filled project card in `STATE.md`, the first entries in `DECISIONS.md`.
- **Gate:** concept, scope, platform, tech and approvers decided.

### Step 1 — Game design document (GDD)

- **Purpose:** a complete and testable description of the game. Everything later is built and checked against it.
- **Inputs:** the Step 0 decisions.
- **AI proposes:** a full GDD draft with these parts:
  1. **Core rules:** board/space, pieces, actions, resolution order, tie-breaks, win/lose conditions, scoring, stars, randomness.
  2. **Level design (Level track):** number of levels and chapters, difficulty knobs, curve targets, relief levels, which mechanic is introduced when, and how levels are made (hand-made or generator + solver).
  3. **Features:** meta progression and unlocks, boosters, shop, daily rewards and quests, profile, leaderboard, collections, settings and pause.
  4. **Login, account and save:** identity type, first run, reinstall, device change, offline behaviour, save versioning and migration.
  5. **FTUE:** each step, what advances it, whether skip is allowed, how it combines with unlock pop-ups.
  6. **Economy sheet (Economy track):** every currency and item, sources and sinks, all numbers with name, default and valid range, and the expected income per level.
  7. **UI flow and screen inventory:** every screen and pop-up, how to get there, its states (normal/empty/locked/owned/error).
  8. **Audio list (Audio track):** SFX events, music, their priority.
  9. **Text (Text track):** copy tone, key naming, languages, maximum text lengths.
  10. **Monetization and ads** (if any), **analytics events** and **edge cases** (offline, clock change, app killed during reward or purchase, save failure).
- **AI must ask:**
  - 🔒 Every rule the AI found ambiguous (the AI lists them with options).
  - 🔒 Login/account type and cloud save: yes or no?
  - 🔒 Win/lose conditions and any limits (moves, time, lives)?
  - 🔒 Economy targets: income per level, prices, what can be bought with real money?
  - FTUE: how many steps, can it be skipped, does tapping count or only dragging?
  - Number of levels and chapters, and the difficulty curve shape (with a chart)?
  - Which features are MVP versus later?
- **Outputs:** `GDD.md`, one spec per feature (`features/<name>.md`), `economy-sheet`, `level-design.md`, `glossary.md` (canonical IDs such as `flavor_0` with display names), `screen-inventory.md`.
- **Rules:**
  - Every rule must be precise enough to write a test.
  - Every feature spec has: purpose, player flow, states, saved data, edge cases, text keys, a **Visual Contract** (what a person must be able to see) and **Acceptance** (measurable checks).
  - Refer to game objects by their canonical ID, never only by display name.
- **Gate:** approver signs off; open questions are empty or explicitly deferred with an owner.

### Step 2 — Mock-up (fake game UI)

- **Purpose:** approved pictures of every screen, so art, code and testing aim at the same target.
- **Inputs:** GDD, screen inventory, style direction.
- **AI proposes:** 2–3 style directions for the key screens (home, gameplay, one pop-up); then, after a direction is chosen, every screen and state from the inventory.
- **AI must ask:**
  - 🔒 Which style direction? Approval per screen.
  - Which device aspect ratios must be shown (phone 9:16, tall 9:20, tablet)?
  - Is a banner or ad area reserved? How strict is the safe area?
  - Which screens come first?
- **Outputs:** `docs/mockups/<screen>-vNN.png`, the prompts, the **design tokens** draft (palette, type scale, spacing, corner radii), and the approval record.
- **Rules:**
  - Mock-ups are made at the real reference resolution and use real text lengths and the largest realistic numbers.
  - Show locked, empty and error states.
  - One visual language only: the design tokens become the single token file in code.
- **Gate:** every screen in the inventory has an approved version. Only approved versions are used in Step 7.

### Step 3 — Art: layered design files, export standard, asset QA

- **Purpose:** production-ready art, correctly sized, with defects found before integration.
- **Inputs:** approved mock-ups and tokens, engine requirements (pixel-per-unit, atlas size, texture budget).
- **AI proposes:** the file and layer structure, the naming convention, an export table (asset, size, pivot, 9-slice borders, atlas), and a list of assets to redraw or clean.
- **AI must ask:**
  - 🔒 Source tool and who owns the master files?
  - 🔒 Export rules: sizes/scale, atlas size limits, texture budget, compression?
  - Layer naming convention: accept the proposal or change it?
  - Which defects may the AI repair on its own, and which must go back to an artist?
  - Which minor defects are acceptable for MVP?
- **Outputs:** `art/source/*.psd` with named layers and groups; `art/export/*`; an **export report** listing each asset with its checks and issues.
- **Rules:**
  - One exportable element per group. Text stays live text in the source but is never baked into exported images.
  - Automated checks on every export:
    - size matches the spec;
    - power-of-two or even sizes where the atlas needs it;
    - no alpha halo or dark fringe;
    - no content clipped at the canvas edge;
    - symmetric elements pass a mirror check;
    - no seams or cracks in tiled or 9-sliced pieces;
    - no stray semi-transparent pixels;
    - file size within budget.
  - Every AI-generated or AI-repaired image records its prompt and is checked by a human eye.
- **Gate:** no blocking issue left in the export report; art approver accepts.

### Step 4 — Animation and effects (Spine or equivalent)

- **Purpose:** animations and effects in an editable source, exported for the game's runtime.
- **Inputs:** approved art, the animation list from the GDD, the tool decision (§8).
- **AI proposes:**
  - the animation list with name, duration, loop or one-shot, trigger, and the "reduced motion" end pose;
  - the tool route (§8);
  - which effects can be built by script (simple FX) and which need an animator (rigged characters).
- **AI must ask:**
  - 🔒 Animation tool and runtime version? Is there a licence for it?
  - 🔒 Final list of animations and their priority.
  - Performance limits (bones, draw calls, texture size)?
  - Style references for timing and feel?
- **Outputs:** the animation project (source), build scripts if any, export settings, a README with exact export steps, and runtime exports (data + atlas).
- **Rules:**
  - The editor version and the runtime version must match (same major.minor).
  - Animation names are an API: list them in the spec and do not rename them silently.
  - Gameplay never waits on or depends on animation events.
  - Every animation has a final pose for the "animation off" setting.
  - Never regenerate over a source that an artist has edited by hand.
- **Audio track:** produce or source SFX and music here, with licence records. Normalize loudness.
- **Gate:** exports load in the runtime; every named animation plays correctly in a capture; approver accepts.

### Step 5 — Game logic

- **Purpose:** rules, meta, economy and saving implemented, deterministic and tested.
- **Inputs:** GDD, economy sheet, level design, framework rules.
- **AI proposes:** the architecture (modules, data flow, save model); the test plan; the level-generation approach (generator + solver + difficulty probe); the list of GDD ambiguities found while coding.
- **AI must ask:**
  - 🔒 Every rule question that the GDD does not answer (never guess game rules).
  - 🔒 Save format and migration policy, including what happens to old saves when content changes.
  - Level content: may the AI generate levels from the curve? Who reviews them?
  - Required test depth: rules only, or also economy and save failure cases?
- **Outputs:** code, headless/unit tests, level files, config data, a short architecture note.
- **Rules:**
  - Follow G16–G21 and the framework rules.
  - Write tests before or together with the code: rules, GDD edge cases, save/restore round trips, a replay of every level's verified solution, economy transactions with injected save failures.
  - Levels are not published unless a solver proves they can be won and the difficulty report is reviewed.
- **Gate:** tests pass (shown with output); code reviewed; level report reviewed by the designer.

### Step 6 — Integrate art, animation, audio and text into the game

- **Purpose:** the real game, looking like the approved mock-ups.
- **Inputs:** approved art, animations, audio, logic, screen inventory.
- **AI proposes:** the integration order (core gameplay screen first), which mock-up version each screen implements, and a list of expected deviations with reasons.
- **AI must ask:**
  - 🔒 Any deviation from an approved mock-up (layout does not fit, missing asset).
  - Priority order of screens?
  - Placeholders: allowed temporarily, or not at all?
- **Outputs:** implemented screens, content wired to real data, localization files, and an updated screen inventory (status per screen).
- **Rules:**
  - Views display data only (G17). Colours and sizes come from tokens, text from keys (G21).
  - Scenes and prefabs are edited through the editor tools (G11).
  - New screens and dialogs are created with the framework's scaffolding, if it has one.
  - No placeholder stays unless the approver allowed it.
- **Gate:** every screen in the inventory exists, uses real data and has no placeholder; editor/integration tests pass.

### Step 7 — AI testing and comparison with the approved mock-ups

- **Purpose:** prove that behaviour matches the GDD and appearance matches the approved mock-ups.
- **Inputs:** the build or editor session, test cases, approved mock-ups, Visual Contracts.
- **AI proposes:** the test plan (test cases per feature from the specs), device aspect list, and bug severity levels.
- **AI must ask:**
  - 🔒 Pass criteria: which severities block?
  - 🔒 Which differences from the mock-up are accepted?
  - Which screen sizes and aspect ratios must pass?
- **Method: from floor to ceiling.**
  1. **Functional floor, deterministic:** automated tests, and scripted input through the real input system with state checks (score, wallet, status). A pixel check confirms that each surface actually renders.
  2. **Visual ceiling:** capture every screen and state at every required aspect. A vision model judges each capture against the Visual Contract, **with the approved mock-up attached as the reference**. Anything the judge "cannot tell" becomes a numeric check.
  3. **Regression:** re-run after every fix. Compare with the previous captures.
- **Outputs:** test report, captures, comparison results, bug list (steps, expected, actual, capture, severity).
- **Rules:**
  - Test with a backup of the save (G15).
  - List deliberate differences so they are not reported again.
  - A capture that nobody looked at is not evidence.
- **Gate:** no open blocking bug; remaining differences accepted by the approver in writing.

### Step 8 — Device test, human playtest and release *(added)*

- **Purpose:** confirm the game works on real devices and is fun and fair for real players, then release.
- **AI proposes:**
  - the device matrix (low-end, mid-range, tablet, notch);
  - the performance checks (FPS, memory, load time, build size);
  - a playtest plan (players, tasks, questions, metrics such as level win rate and quit points);
  - the release checklist (store data, privacy, SDK configuration, versioning).
- **AI must ask:**
  - 🔒 Which devices and how many playtesters?
  - 🔒 Which SDKs go live (ads, IAP, analytics), in which stores, under which accounts?
  - 🔒 Release approval (go / no-go).
  - Which difficulty changes are allowed from the playtest data?
- **Outputs:** device report, playtest report with tuning proposals, release checklist, release notes.
- **Rules:**
  - Balance changes from playtests go back through Step 1 (economy/level docs) and Step 5 (data), via a change request.
  - No real payment, ad or store submission without explicit approval.
- **Gate:** performance targets met on the target devices; playtest issues triaged; approver releases.

---

## 8. Animation tool choice

The AI presents these options in Step 4 and the human decides.

| Option | Good for | Watch out |
|---|---|---|
| **Spine** (editor + official runtime) | 2D characters and FX; meshes, IK, skins; strong engine runtimes | Paid licence; the Spine runtime licence requires a valid Spine editor licence for products using it. Editor and runtime major.minor must match. |
| **Spine data generated by script** (AI writes the skeleton JSON + atlas, then imports it into the Spine editor) | Simple FX: glow, rays, confetti, coins, scale/rotate/colour | Fine for effects, weak for rigged characters. Still needs the Spine licence to ship the runtime. Keep the editor project as the source so artists can adjust it. |
| **DragonBones → Spine** | Teams whose animators prefer DragonBones | DragonBones exports an older Spine 3.x format. A current Spine editor may refuse it directly. The working route is: import with a Spine 3.8 editor → open/export with the target Spine version → pack atlas. Check meshes, IK, easing, events and frame rate after conversion. The export may need an online service. Pilot one real asset first. |
| **DragonBones runtime directly** | Legacy projects already using it | Runtime is old and not actively maintained; check engine-version support; avoid two animation runtimes in one game. |
| **Engine-native 2D animation** (e.g. Unity 2D Animation + PSD Importer) | Rigging straight from layered PSDs, no extra licence | Different workflow and tooling. Choose it per project, not per asset. |
| **Tweened UI motion in code** | Buttons, pop-ups, counters, simple highlights | Keep timings in tokens/config; respect "reduced motion". |

**Decision questions for the human:**

- Do we have (or will we buy) a Spine licence for the team?
- Which tool will the animators use?
- Is there any rigged character, or only effects?
- Which runtime version does the engine project pin?

---

## 9. Lessons learned

These come from building a full casual puzzle game with AI. They are written as rules.

### Design

1. **Write the tie-breaks and the evaluation order.** When the reference game's exact rules are unknown, choose a rule, document it and test it. Never claim to match the reference.
2. **Canonical IDs first.** Internal names, display names and art names drifted apart (a flavor called "Strawberry" in code was shown as "Watermelon"). Use IDs in specs and tests.
3. **Content growth breaks copy.** The game grew from 10 to 60 levels but some text still said "ten puzzles". After each content change, search for stale text and docs.
4. **Validate generated content against its own data.** A generated level description mentioned a mechanic the level did not contain.
5. **Decide which inputs count.** The tutorial advanced only on drag, not on tap. Put that choice in the spec.
6. **Bots are diagnostics.** A simple bot showed the late levels were very hard, but only human playtests give win rates.
7. **Version your content.** Saved games from an older level revision must be restarted safely, not loaded into a changed board.

### Code and architecture

8. **A pure rules engine with headless tests** makes rule changes cheap: more than a million assertions ran in seconds, including a replay of every level solution.
9. **Atomic commits with rollback** (and wallet compensation) prevented lost or duplicated rewards when a save failed.
10. **Save the reward, then show it.** The daily wheel draws and saves before it spins. An unacknowledged reward is shown again, never granted again.
11. **De-duplicate purchases** by transaction ID. Keep the purchase gateway behind an interface, so a simulated store can be replaced by real IAP.
12. **One owner for pop-ups.** A single coordinator queues dialogs. Cancelled or stale dialogs never run their actions.
13. **When the framework forbids something you need**, write the options and get a decision. Do not hide a workaround from the validators.
14. **Asset bundling can duplicate shared assets.** Keep renderer and global assets in the root/boot scope and test for duplicates.
15. **Record any toolchain exception** (e.g. an engine version older than the framework baseline) in a decision note, and add a check that fails on unexpected versions.

### Performance

16. **Editor quality settings are not device settings.** Check the platform default.
17. **Measure the expensive features first:** MSAA, real-time shadows, HDR/post, and full-resolution offscreen textures. The fix was a low mobile quality tier with capped render-texture size.
18. **Custom shaders can fail on only one platform.** Prefer simple, portable shaders and verify on the device.

### Process and tools

19. **Tool timeouts do not cancel work.** Builds and tests kept running after the command timed out. Check state before retrying and guard one-shot actions such as builds.
20. **Look at every capture.** An image that was saved but not opened proved nothing.
21. **Open every evidence file you cite.** Two reports were serialized as empty `{}` objects.
22. **Back up the real save file.** Testing changed a real player profile. Find the actual save location, then back up and restore it.
23. **Read the real test result.** A test run that "timed out" or returned an empty result has not passed.
24. **Keep a checkpoint.** Scope grew several times in one project. A `STATE.md` with the full list and a verified/not-verified mark per item kept work recoverable across sessions.
25. **Split evidence by kind.** Editor passes, a successful build and a real-device check were reported separately. That avoided claiming more than was proven.

---

## 10. Templates

### 10.1 `docs/workflow/STATE.md`

```markdown
# Project state
Game: … | Platforms: … | Engine/framework: … | Reference resolution: …
Approvers: Design … · Visual … · Art … · Tech … · Release …

| Step | Status (not started / in progress / waiting decision / approved) | Approved output (version) | Approver, date |
|---|---|---|---|
| 0 Kickoff | | | |
| 1 GDD | | | |
| 2 Mock-up | | | |
| 3 Art | | | |
| 4 Animation | | | |
| 5 Logic | | | |
| 6 Integration | | | |
| 7 AI test | | | |
| 8 Device/Playtest/Release | | | |

## Open questions (waiting for a human)
- [ ] Q… (step, asked on …)

## Next actions (ordered)
1. …

## Last session
Date · what changed · evidence · what failed · what was not checked
```

### 10.2 `docs/workflow/DECISIONS.md` entry

```markdown
### D-012 · 2026-10-01 · Step 1 · Login type
Question: How do players identify themselves?
Options: a) guest/device save · b) guest + platform login · c) email account
Decision: a) — decided by <name>
Reason: MVP without backend; revisit at Step 8.
Affects: GDD §4, save model, settings screen.
```

### 10.3 Step kickoff message (AI → human)

```markdown
**Step 2 — Mock-up: starting.**
Inputs: GDD v1.2 (approved), screen inventory (24 screens).
Plan: 3 style directions for Home / Gameplay / Win → you choose → full set.
Questions:
1. 🔒 Aspect ratios to cover: a) 9:16 only · b) 9:16 + 9:20 (recommended) · c) + tablet
2. Reserve a 160 px banner area at the bottom? a) yes (recommended if ads are planned) · b) no
If no answer on 2, I will reserve the banner area.
```

### 10.4 Change request

```markdown
### CR-004 · raised in Step 6 · affects Mock-up "gameplay-v03" (approved)
Problem: the booster bar overlaps the tray on 9:20 screens.
Options: a) move boosters to the right edge (recommended) · b) shrink the tray 10% · c) keep, accept overlap
Needs decision from: Visual approver
Downstream to recheck: gameplay captures, tutorial highlight positions.
```

---

## 11. Change log

| Date | Version | Change |
|---|---|---|
| 2026-09-28 | 1.0 | First version, from the CakeSort project review |
| 2026-09-28 | 2.0 | Made project/machine independent. Added Step 0 and Step 8, the tracks (levels, economy, audio, text), roles, the propose-ask-decide protocol, per-step decision questions, change requests, templates and an animation tool guide. Lessons generalized. |
