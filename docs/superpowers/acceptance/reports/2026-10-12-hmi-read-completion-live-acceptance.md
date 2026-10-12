# `hmi_read` completion live acceptance (Task 11) — 2026-10-12

Result: **PASS with recorded limits.** Cursor paging, the cursor failure categories, the new `validate`
categories, the `{hmi}` cross-reference selector, the runtime-settings allow-list and the Classic-only failure all
behaved as specified on the installed candidate. The 42-row `hmi_read` regression set from 2026-10-08 passed again;
the only differences are the intended changes of this track. Four checks could not be run (see "Not executed").

Two deviations from spec §7.3 were observed and accepted:

- **Case 2.** A worker restart between pages returns `binding_conflict`, not `snapshot_unavailable`. Worker loss
  invalidates the session binding before the cursor is examined (multiuser PR3 behaviour). After `bind_project`, the
  old cursor fails `cursor_binding_mismatch` and a call without a cursor succeeds. The failure is closed in every
  step; `snapshot_unavailable` is not reachable through a worker restart.
- **Case 7.** `screenItems` validated every item on every screen read (all 56 P1 screens and 201 P2 screens with 5,851 items, `notValidatable:[]`), so real
  screen items implement `IValidator`. No item produced a finding, so the broken-binding fixture was not observed
  as a finding (see "Not executed").

## Scope and authorization

- **Authorization.** The maintainer opened two disposable project copies in TIA Portal, installed the candidate as
  the global tool and authorized opening and switching projects in the disposable and session folders. Project,
  device, screen and tag names below are neutral labels or fixture names the maintainer added.
- **Calls.** Reads (`hmi_read`, `read_cross_references`, `browse_project_tree`, `get_project_status`,
  `bind_project`) plus lifecycle calls to switch projects: `close_project` twice and `open_project` three times.
  No content write or compile was made.
- **Client.** Most calls went through a scripted stdio MCP client that started its own `tia-mcp` process
  (`--access-mode read-only` for reads, `full` for lifecycle switches). Timings are measured by that client
  around each `tools/call`, so they include MCP transport but not model latency.

## Environment

| Item | Value |
| --- | --- |
| Tool build | `tiamcpserver 4.0.0-local.17.g7482756`, which is `7482756` on `feature/hmi-read-completion` |
| TIA Portal | V21 Update 2 Hotfix 1 |
| Project P1 | Disposable copy holding one Unified Comfort panel (fw 21.0.1.0) and the §7.2 fixtures |
| Project P2 | The disposable copy used on 2026-10-08: Unified PC, four Unified Comfort panels (A, B, D fw 21.0.1.0; C fw 21.0.0.0) and one Classic panel |
| Local session | One `.als21` session without an HMI (opened); the session that has an HMI is protected (see "Not executed") |
| Languages | en-US and pl-PL in both projects |

**Memory pressure.** The first part of the run had about 0.9 GB of 16 GB free with five Portal processes. Every
Openness call then took at least 30 s (`list_hmi_devices` 33 s, `browse_project_tree` depth 2 92 s), and Claude
Code stopped one run for low memory. Those timings are discarded. After the maintainer closed one Portal
(4.3 GB free), `bind_project` dropped from 11–20 s to about 1.3 s. Every timing below is from that second part.

## Results — spec §7.3 cases

| # | Case | Observed | Result |
| --- | --- | --- | --- |
| 1 | Faceplate paging on the Unified PC through the cursor, `limit:100` | 1,286 instances. First page 21.0 s; 11 continuation pages 0.53–0.83 s each. An offset-only page 2 without a cursor took 20.8 s. The page at offset 1,100 was `omitted` (`resultExceededItemCharLimit`: 100 faceplate rows with bindings exceed 60,000 characters), and the script stopped there, so the last page was not read. | PASS |
| 2 | Worker restart between pages | Continuation after killing the worker: `binding_conflict` (binding invalidated). After `bind_project`: old cursor `cursor_binding_mismatch`; a call without a cursor succeeded. | PASS (deviation, see above) |
| 3 | Rebind between pages | Rebinding the same project with `forceRebind` keeps the binding (`transition:"unchanged"`), and the cursor continues. After closing P1 and opening P2, the P1 cursor failed `cursor_binding_mismatch` in 9 ms. | PASS |
| 4 | Changed filter with a cursor | `tableName` changed: `cursor_filter_mismatch`. `cursor` with `offset`: `validation_error` "'cursor' is not valid with 'offset'; the cursor carries the position." Same filter: continued (95 ms vs 1,756 ms first page). | PASS |
| 5 | `list_screens` timing with composition `Count` | Unified PC (180 screens): 20.8, 19.9 and 20.2 s. 2026-10-08: 23.5 s including client overhead. P1 (56 screens): 4.2 s. Decision: keep (R12). | PASS |
| 6 | `read_cross_references` with `{hmi}` | P1 UDT tag used by an analog alarm and on screens: `isComplete:true`. Member rows list the analog alarm (`UsedBy … Property.Trigger tag`), screens and faceplates (`UsedBy … Property.Process value` / `Interface.*`), a data log, PLC blocks and the bound DB. A plain tag lists its screens, cycle and connection. 1.3–1.5 s. A dotted name fails `target_not_found` "… UDT member paths are not supported in this version; name only the top-level tag." | PASS |
| 7 | `validate` new categories | `memberTags` on the UDT tag: 10 scanned, 10 clean. `loggingTags` on a logged tag: 1 scanned, 1 clean. `screenItems`: P1 56 screens (largest 231 items), P2 Comfort A 21 screens / 384 items, P2 Unified PC 180 screens / 5,467 items; all clean, `notValidatable:[]`, `isComplete:true`. | PASS (no finding observed) |
| 8 | Comfort `get_runtime_settings` | Comfort A, Comfort C and the P1 panel: `isComplete:true`; only "X is not available on this device." messages. Unified PC: `isComplete:true` with the allow-listed note "Runtime setting 'TagOptimizationActive' could not be read: … not supported in the current device version." | PASS |
| 9 | §7.2 fixtures | See the next table. | PASS with one gap |
| 10 | 42-row regression set | 45 checks run against P2, 44 passed on first evaluation; the one failure was the checker's exact count for a project change (see below). | PASS |

### Fixtures (§7.2)

| Fixture | Observed | Result |
| --- | --- | --- |
| Analog alarm | `list_alarms alarmKind:"analog"` returned 4; `get_alarm` returned `condition:"UpperLimit"`, `conditionValue {type:"System.Double", value:0}` and the trigger tag | PASS |
| Tag comment in en-US and pl-PL | Both cultures returned; `language:"pl-PL"` narrowed to pl-PL only | PASS |
| Non-integral start value (12.75) | `initialValue {type:"System.String", value:"12.75"}`. Openness returns the internal tag's initial value as a string, so the worker reports the CLR type it received. The range variants (`hmiStartValue` …) are `System.Double`. Variant `Double` encoding of a non-integral value is still not observed on a `Double`. | PASS (observation) |
| IO field bound to a deleted tag | Not observed as a finding on any screen read. | Not observed |
| UDT tag with members; logging tag | Members returned (10 at depth 1 in P1, 8 in P2); `memberTags` and `loggingTags` validated | PASS |
| OPC UA connection (optional) | Not present; every connection is `SIMATIC S7 1200/1500` | Not executed |

### Extra live checks (from the plan review)

| Check | Observed | Result |
| --- | --- | --- |
| Real screen items implement `IValidator` | Every item on 257 screens validated, `notValidatable:[]` | PASS |
| `Find` case semantics and dotted names | `get_tag "DEBUGMODE"` resolved the tag `debugMode`. A dotted UDT member path: `get_tag` → `target_not_found` "Tag '…' was not found. Use list_tags to see the tags."; cross-reference → `target_not_found` with the UDT guidance. | PASS |
| `object.Equals` on one `ProjectBase` across calls in a local `.als21` session | Not executed | — |
| Deleted object throws `EngineeringObjectDisposedException` on `Name` | Not executed | — |
| `HmiTag.GetService<CrossReferenceService>()` non-null | Case 6 returned references for both tags | PASS |

## Regression set (2026-10-08 rows)

Rows 1–20, E1–E13, R1–R5 and K1–K3 were repeated on P2 with the same inputs where they still apply.

| Row | Difference from 2026-10-08 | Cause |
| --- | --- | --- |
| 9, E5b | `list_alarm_classes` rejects `language` with `validation_error`; E5b was run on `list_alarms` instead and returned the same `target_not_found` | `1cb187b` (2026-10-08, before this track) removed `language` from `list_alarm_classes` |
| 14 / K3 | The layout screen now has 219 faceplate instances (208 before); the filter still narrows and every row belongs to the screen | P2 was edited after 2026-10-08 (`lastModified` 2026-10-09) |
| 16 | Comfort A and C now read `GMPEnabled` and `GeneralESIGCommentsStrategy` (`false`, `BothComments`) and are `isComplete:true`; on 2026-10-08 both threw "not supported" | Not established. The allow-list handles both outcomes. |
| 20, E7 | `validate all` scanned 3,614 objects with `notValidatable:[]` (3,764 with 150 system tags before); the Unified PC `total` is 2,881 (2,913 before) | Intended: system tags are dropped from `validate` (Task 2) |
| E3 | Message now ends "…; only WinCC Unified HMIs are supported." | Intended (Task 3) |
| R3 | The valid-fields list now includes `cursor` | Intended (Task 6) |

Every other row matched its 2026-10-08 expectation, including K1 (no password driver property), E6 (whole-value
omission at 110,313 characters) and E12 (`binding_conflict` on a mismatched `projectPath`, other item succeeded).

## Defects and observations

| ID | Severity | Observation |
| --- | --- | --- |
| O1 | Low | Every `hmi_read` item resolves its HMI again. On P2 one `validate screenItems` item costs about 6 s, so 180 screens took 20.6 min. A batch-level software cache would cut this; it is a follow-up, not a regression. |
| O2 | Low | `get_tag` with a dotted name does not carry the "UDT member paths are not supported" hint that the cross-reference selector gives. |
| O3 | Low, pre-existing | `open_project` dry run while another project is open in the same Portal reports no guard and `willCloseSource:false`; the actual call then fails `worker_operation_failed` "Another project is already open." |
| O4 | Info | `close_project` saves the source (`savesSource:true`). P2 reported `isModified:true` immediately after `open_project`, so closing it saved the disposable copy. P1 was unmodified when closed. |
| O5 | Info | After the maintainer closed a second Portal, the existing session's `bind_project list_portals` returned `binding_conflict` "The worker did not return a project path for the completed project operation." and an invalidated binding; the remaining Portal was still listed. |

## Not executed

- **`object.Equals` on a local session.** The only `.als21` session that holds an HMI is protected: opening it
  failed "The project is protected. You are not authorized to open the project in write mode." The session that
  opened has no HMI, so no paged HMI read was possible. Needs a session copy with an HMI and access rights.
- **Deleted held object.** Needs a human to delete an HMI object between two pages; there are no HMI writes.
  Covered offline by the worker snapshot store tests.
- **Broken-binding finding.** No `screenItems` finding appeared on any screen read. Either Unified `Validate()` does
  not report an IO field whose tag was deleted, or the fixture is on a screen not read (P2 Comfort B, C and D
  were skipped on the maintainer's advice that their screens match Comfort A). The shape of a finding is still
  covered only offline.
- **OPC UA connection.** No such connection exists in either project.

## Final state

P1 was closed (saved, unmodified) and P2 was opened, then closed (saved). The SimpleProject local session is now
open in the single remaining Portal; `close_project` does not support local sessions. No content write or compile
was made. TIA Portal did not crash; the scripted worker restarts in case 2 were deliberate.
