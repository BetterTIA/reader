# `hmi_read` completion — design

*2026-10-11. Closes the [`hmi_read` follow-ups](../../IMPROVEMENT_LOG.md) left by
[the `hmi_read` design](2026-10-07-hmi-read-design.md). `hmi_write` is a later track; Appendix A
records what this track hands to it.*

## 1. Intent

Finish `hmi_read` before any HMI write work starts: remove the review debt, make repeated
whole-project scans cheap, add the WinCC Unified `HmiTag` owner kind to `read_cross_references`, and
widen `validate` to the objects it currently skips.

**In scope:**

- Final-review cleanups M3, M4, M8, M9 and a Classic-only locator defect (§3).
- An opt-in snapshot cursor for every paged operation (§4).
- A provisional `{hmi:{hmiName, tagName}}` selector in `read_cross_references` (§5).
- `validate` categories `memberTags`, `loggingTags`, `screenItems` (§6).
- Hand-seeded fixtures that close the live-evidence gaps (§7).

**Out of scope (decided):**

- `GmpRelevant` and `MandatoryCommenting` stay permanently unread, in the same class as
  `ConfirmationType` (crashed TIA Portal in the spike). No probe.
- HMI nodes in `browse_project_tree`; a unified HMI selector for alarms or screens.
- A host-side row cache, a snapshot-age field, a cursor for unpaged `get_screen_navigation`.
- Redaction review for non-S7 drivers unless the optional OPC UA fixture exists.
- The Portal-exit-after-detach watch item stays open.

**Success:** every change passes offline gates (full suite, coverage gate, stub build), and one
live V21 acceptance run on a disposable project copy passes the §7 cases plus the existing 42-case
`hmi_read` regression set. Tool counts stay 7 / 16 / 16; no new tool.

**Packaging:** one spec, one plan, branch `feature/hmi-read-completion`, one PR. Documentation is
the last task, after live acceptance.

## 2. Contract changes at a glance

| Surface | Change | Compatibility |
|---|---|---|
| `hmi_read` item | optional `cursor` (string) on paged operations; `cursor` with `offset` is a `validation_error` | additive |
| `HmiPage` | `NextCursor` (string, conditional: omitted when null) | additive |
| `hmi_read` `validate` | categories `memberTags`, `loggingTags`, `screenItems`; `name` required for them; not in `all` | additive |
| `hmi_read` `validate` `tags` | `@` system tags no longer scanned or counted | behaviour change, documented |
| `hmi_read` locator | Classic-only project with `hmiName` omitted → `target_kind_unsupported` (was `target_not_found`) | defect fix |
| `get_runtime_settings` | spike-documented unsupported properties add a note, not incompleteness | behaviour change, documented |
| `read_cross_references` selector | `Hmi` variant `{hmiName, tagName}` beside `Path`/`Member` | additive, provisional |

`HmiContractVersion.Current` moves `1.0` → `1.1`. `NextCursor` and the selector's `Hmi` member are
conditional members and join `ConditionalMemberRegisterTests` with their descriptions.

## 3. Cleanups and small fixes

All are size S and verified offline.

1. **One helper set (M3).** `ReadText` and `ReadVariant` move onto `HmiReadLog`. Delete
   `HmiTagReader.Guard` (duplicate of `HmiReadLog.Guard`), `HmiTagReader.Variant`,
   `HmiTagReader.ReadComment` and the unused public `ReadRow(tag, language, ICollection<string>)`
   overload. `HmiAlarmReader` and `HmiScreenReader` call the `HmiReadLog` versions.
2. **One completeness idiom (M4).** A skipped text item calls `Fail` through `ReadText`. Every
   reader that holds a `HmiReadLog` reports `log.IsComplete`; the `Messages.Count == 0` sites in
   `HmiTagReader` go. The member row-cap message stays a `Note` (complete), as today.
   `HmiSoftwareInfoReader.ListDevices` collects walk messages without a log; it moves onto
   `HmiReadLog` only if the result is identical, otherwise it is left alone.
3. **`validate` `tags` without system tags (M8).** `HmiValidationReader.Objects("tags")` and
   `Objects("all")` drop `NamedSystemTags`. System tags have no `Validate()`, so they only padded
   the first pages and inflated `scanned` and `notValidatable`.
4. **Comfort runtime settings complete (M9).** A fixed allow-list of spike-documented unsupported
   properties — Comfort `GMPEnabled`, `GeneralESIGCommentsStrategy`; PC `TagOptimizationActive` —
   turns `EngineeringTargetInvocationException` / `EngineeringNotSupportedException` into
   `HmiReadLog.Note`. Matching is by property name and exception type, never message text. Any
   other property, or any other exception type, still calls `Fail`. The test asserting
   `IsComplete == false` for Comfort flips.
5. **Classic-only locator.** `HmiSoftwareLocator.FindUnique` with `hmiName` omitted: when no
   Unified software exists but at least one Classic HMI does, fail `target_kind_unsupported`, not
   `target_not_found`. The message stops naming `hmi_read` so later callers can reuse it.
6. **Cost guidance.** The tool description states that whole-project scans take seconds on large
   stations, recommends `screenName` / `tableName` filters, and points to the cursor for
   continuation.
7. **Quick performance wins.**
   - Resolve the HMI software once per item: `HmiReadDispatch.Software()` stops re-walking devices
     when the reader already resolved it.
   - `list_screens` `itemCount` uses the composition `Count` property instead of LINQ `Count()`.
     Kept only if live timing shows a saving; reverted otherwise.
   - Where a composition `Find(name)` exists and the operation filters by one exact name, use it
     instead of enumerating (`get_tag`, `list_screen_items` screen lookup, `screenName` filters).
     `Find(nonexistent)` returning `null` was observed live and maps to `target_not_found`.

## 4. Opt-in snapshot cursor

### 4.1 Scope and contract

Covers the seven paged operations: `list_tags`, `list_system_tags`, `list_alarms`,
`list_logging_tags`, `list_screen_items`, `list_faceplate_instances`, `validate`.

- `HmiPage(Offset, Limit, Total, NextOffset)` gains `NextCursor`. It is set whenever `NextOffset`
  is set, and null on the last page.
- An item may send `cursor` instead of `offset`. Both together is a `validation_error`. `limit`
  may differ between pages (same 1–2000 range).
- A call without `cursor` behaves exactly as today — a fresh scan with offset paging — and
  additionally returns `NextCursor` when more rows exist. Offset paging stays fully supported.

### 4.2 Worker snapshot store

Openness objects cannot leave the worker process, so the snapshot lives there.

- During a scan that has more rows than the page, the worker keeps the name-ordered list of the
  scanned Openness objects (for `validate`, the objects it would validate) under a worker-generated
  opaque `snapshotId`, returned in the worker page payload.
- A continuation reads property rows, or calls `Validate()`, only for its page, directly from the
  held objects. No device walk, no composition enumeration, no name reads for other rows.
- Limits: at most 4 snapshots, LRU eviction, 10-minute sliding TTL — the same numbers as
  `ProjectTreeSnapshotStore`, using `TimeProvider` so tests drive a manual clock.
- A snapshot records the identity of the HMI software it was taken from; a continuation whose
  resolved software differs fails `cursor_snapshot_mismatch`.
- The worker clears the store when the binding changes or the worker detaches. A worker restart
  loses it by construction.

### 4.3 Host cursor

- The host seals `{snapshotId, operation, queryHash, offset, hostBinding}` with the existing
  `AuthenticatedCursorProtector` (process-scoped key), in a new `HmiPageCursorCodec` that mirrors
  `ProjectTreeCursorCodec`.
- `queryHash` covers the operation name and every filter that shapes the row set: `hmiName`,
  `tableName`, `language`, `alarmKind`, `dataLogName`, `tagName`, `screenName`, `category`, `name`.
  It excludes `limit`.
- Host validation before any worker call, with existing categories:

  | Condition | Category |
  |---|---|
  | malformed, tampered, wrong key or process | `invalid_cursor` |
  | different operation or filters | `cursor_filter_mismatch` |
  | binding changed since issue | `cursor_binding_mismatch` |

  A sealed cursor over a frozen identity list cannot point past its end, so `cursor_out_of_range`
  is not used.

- Worker-side: an unknown `snapshotId` (expired, evicted, worker restarted) fails
  `snapshot_unavailable`, with a message telling the caller to restart without a cursor.
- Cursor errors are item failures, like any other `hmi_read` item failure; the batch still runs.

### 4.4 Staleness rule

The identity list is frozen at scan time; every row is read live.

- An object deleted since the scan fails its row: a message, `isComplete:false` for that item.
  Other rows on the page are unaffected.
- Objects added since the scan are not included. The tool description says so; a fresh call
  without `cursor` sees them.
- `hmi_write` (later track) drops the snapshots of the HMI software it mutated (Appendix A).

## 5. `read_cross_references`: provisional HMI tag selector

### 5.1 Contract

- `CrossReferenceSelectorInfo` gains a conditional `Hmi` member:
  `{"hmi": {"hmiName": "...", "tagName": "..."}}`.
- A selector carries either a non-empty `path` (with optional `member`) or `hmi`, never both and
  never neither: `validation_error`. Unknown keys inside `hmi` are rejected, consistent with the
  existing exact raw-key validation.
- `hmiName` is **required** (unlike `hmi_read`, where it is optional): a cross-reference selector
  must name one object exactly. It matches software or device name, case-insensitively, through
  `HmiSoftwareLocator.FindUnique`; ambiguity is `target_ambiguous`, a Classic HMI is
  `target_kind_unsupported`.
- `tagName` is one top-level `HmiTag` name. UDT member paths are not supported in this version.
- Filters, `maxResults`, budgets and the result shape are unchanged. The source identity in
  results echoes the `hmi` selector.
- Documentation labels the variant provisional: it may be replaced by a unified HMI selector once
  alarms or screens need cross-references.

### 5.2 Resolution

- Worker: `HmiSoftwareLocator.FindUnique(hmiName)` → `FindUniqueTag(software, tagName)` → the
  existing `Leaf()` lookup of `GetService<CrossReferenceService>()` and the existing mapping.
  A null service is `target_kind_unsupported`, as today.
- `FindUniqueTag` moves out of `HmiTagReader` into a shared `internal` lookup used by both
  `hmi_read` and the resolver.
- `ProjectCapabilityCatalog` needs no change: `read_cross_references` already covers local
  sessions.

## 6. `validate`: name-scoped categories

| Category | `name` means | Objects validated | Finding identity |
|---|---|---|---|
| `memberTags` | top-level tag | every member, recursively, depth cap 8 (reuses the `ReadMembers` walk) | member path, e.g. `Motor_1.Speed` |
| `loggingTags` | top-level tag | the tag's logging tags | `Temp_1/Log_1` |
| `screenItems` | screen | every item on that one screen | `Main/Button_3` |

- `name` is required for the three categories: a missing `name` is a `validation_error` at
  request validation.
- None of them is part of `all`; `all` keeps its current object set and cost.
- They page like the other categories and support the cursor.
- Each finding also carries the object's CLR type name. Objects that are not `IValidator` are
  counted in `notValidatable`, as today.

## 7. Testing and live acceptance

### 7.1 Offline (TDD per task)

- **Readers:** extend the existing Siemens doubles for the merged helpers, M8, M9, the Classic-only
  locator, the three categories and the HMI cross-reference resolver.
- **Worker snapshot store:** TTL expiry, LRU eviction at 4, `snapshot_unavailable`, software
  mismatch, a deleted held object failing only its row; manual clock.
- **Host cursor codec:** seal/unseal round trip, each §4.3 category before any worker call,
  `cursor` + `offset` rejection, `queryHash` excluding `limit`.
- **IPC:** a FakeWorker paged scenario driving first page → continuation → last page, and a
  restart → `snapshot_unavailable`.
- **Contract registers:** `ConditionalMemberRegisterTests` for `NextCursor` and `Hmi`;
  `ToolOutputContractConformanceTests` probes stay green; cross-reference selector validation
  tests for the new variant.
- **CI stub:** extend `reference-stubs/Siemens.Engineering.WinCCUnified` only for members the new
  code calls that the stub lacks — likely `HmiBase.GetService<T>` and composition `Count`/`Find` —
  regenerate, and pass `scripts/verify-reference-stubs.ps1`.
- Gate: full suite green, coverage gate held, stub build green.

### 7.2 Fixtures (prepared by hand in a disposable project copy)

| Fixture | Proves |
|---|---|
| one analog alarm | analog `list_alarms` / `get_alarm` |
| a tag comment in en-US and pl-PL | `Comment` mapping and language selection |
| a non-integral start value (e.g. `12.75`) | variant Double encoding |
| an IO field bound to a tag that is then deleted | a real finding through `screenItems` |
| a UDT tag with members, and a logging tag on a tag | `memberTags`, `loggingTags` |
| an OPC UA connection (optional, if feasible) | non-S7 driver properties and redaction |

### 7.3 Live cases

1. Faceplate paging on the PC station through the cursor: first-page time vs continuation time;
   continuations are expected to take a few seconds at most.
2. Worker restart between pages → `snapshot_unavailable`; fresh call recovers.
3. Rebind between pages → `cursor_binding_mismatch`.
4. Changed filter with a cursor → `cursor_filter_mismatch`.
5. `list_screens` timing with composition `Count` (keep or revert, §3.7).
6. `read_cross_references` with `{hmi}` for a tag used in an alarm and on a screen: both
   references reported.
7. `validate` `screenItems` on the fixture screen reports the broken binding; `memberTags` and
   `loggingTags` run on the fixtures.
8. Comfort `get_runtime_settings` is complete, with notes for the allow-listed properties.
9. Each §7.2 fixture is read correctly.
10. The 42-case `hmi_read` regression set.

The report goes to `docs/superpowers/acceptance/reports/`.

### 7.4 Documentation (last task)

`HMI_OPERATIONS_SUMMARY.md` (cursor, categories, known limits, GMP permanently unread), the
cross-reference sections of `PLC_OPERATIONS_SUMMARY.md` and `MULTIUSER_OPERATIONS_SUMMARY.md`
where they describe selectors, `ARCHITECTURE.md` §7a (worker snapshot store), `IMPROVEMENT_LOG.md`
(close finished items, keep watch items), `docs/README.md` and `docs/superpowers/README.md` entries
for this spec, its plan and the acceptance report, and the AGENTS.md HMI convention line.

## Appendix A. Handoff to `hmi_write`

`hmi_write` gets its own live write spike, then its own spec, after this track ships.

**v1 scope (decided 2026-10-11):** PLC→HMI data plumbing — tag tables, HMI tags bound to PLC tags,
discrete and analog alarms on those tags. Classic HMIs stay rejected. Safety model follows
`plc_write`: guarded single-call pipeline, `dryRun`, ≤50 items, stop at first failure, no rollback,
read-back verification, no acknowledge guards, no elicitation.

**Write-spike questions:**

1. Must `Comment`, `DisplayName`, `EventText` and `InfoText` writes be formatted-text wrapped, and
   in which form (`<body><p>…</p></body>` or a full `ProjectText` document)?
2. Does `Create` or any tag setter, or `SetAttributes`, reach the `ConfirmationType` adapter that
   crashed Portal on Comfort?
3. Does `RaisedStateTag` take an HMI tag name, and is `TriggerBitAddress` derived after the set?
4. Can the default tag table be deleted? What happens to a table's tags on delete?
5. Is `HmiTag.Name` writable (rename)? Are tag names unique across the whole HMI?
6. Analog alarm create/update, including the `ConditionValue` variant type.
7. Behaviour of string references (`Connection`, `PlcTag`, `AlarmClass`) to objects that do not
   exist, and what `Validate()` reports afterwards.

**Registration hazards already found:**

- `ProjectCapabilityCatalog.OperationFor` maps every `hmi_*` name except `hmi_read` to `hmi_read`
  (`:85`), so `hmi_write` would silently pass the local-session gate under the read capability.
  It needs a write-aware branch and `ProjectContentOperations` entries.
- New worker methods need `OperationPolicyCatalog` `ProjectMutation` rows; today every `hmi_*` row
  is `Observe`.
- The CI WinCC Unified stub has no setters, `Create`, `Delete` or `Import`; every write member must
  be declared and the stub regenerated.
- `WorkerRequest` carries only `HmiQuery`; writes need their own request DTO and a host
  `HmiOperationRequest` counterpart.
- Mutations must drop that software's cursor snapshots (§4.4).
- The `{hmi}` cross-reference selector (§5) is the delete-guard evidence source for tags.
- Tool counts move to 7 / 17 / 17; `HMI_OPERATIONS_SUMMARY.md` "There are no HMI writes" and the
  AGENTS.md "`hmi_read` is read-only" line change with it.
