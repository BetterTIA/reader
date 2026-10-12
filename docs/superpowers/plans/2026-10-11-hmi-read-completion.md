# `hmi_read` completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the `hmi_read` follow-ups: review cleanups, an opt-in snapshot cursor for paged
operations, a provisional HMI tag selector in `read_cross_references`, and three name-scoped
`validate` categories.

**Architecture:** Worker readers keep their shape; paging moves behind one snapshot-aware
`HmiPager.Page` overload backed by a worker `HmiSnapshotStore`. The host seals the worker's
snapshot id into an authenticated cursor (`HmiPageCursorCodec`, mirroring
`ProjectTreeCursorCodec`) and validates it before any worker call. The cross-reference resolver gets
an HMI branch that reuses the HMI locator and a shared tag lookup.

**Tech Stack:** .NET 10 host, net48 worker, xunit, Siemens Openness V21 (CI stubs under
`reference-stubs/`).

**Spec:** `docs/superpowers/specs/2026-10-11-hmi-read-completion-design.md`

## Global Constraints

- Tool counts stay 7 / 16 / 16; no new tool. `HmiContractVersion.Current` becomes `"1.1"`.
- Snapshot store: at most 4 snapshots, LRU eviction, 10-minute sliding TTL.
- Cursor categories (existing constants only): `invalid_cursor`, `cursor_filter_mismatch`,
  `cursor_binding_mismatch`, `cursor_snapshot_mismatch`, `snapshot_unavailable`. `cursor_out_of_range`
  is not used.
- `queryHash` covers operation, `hmiName`, `tableName`, `language`, `alarmKind`, `dataLogName`,
  `tagName`, `screenName`, `category`, `name`; it excludes `limit`.
- `limit` stays 1–2000; batch 1–50 items; `MaxMemberDepth` stays 8.
- `GmpRelevant`, `MandatoryCommenting`, `ConfirmationType` are never read.
- Conditional members (`[JsonIgnore(Condition = WhenWritingNull)]`) are registered in
  `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs` with a "when it appears" description.
- Worker identity comparisons of live Openness objects use `object.Equals`, never `ReferenceEquals`.
- Run `dotnet` with `/p:` flags from PowerShell, not Bash. Build serialized: `-m:1`.
- Commit after every task, one-line conventional message, no body or trailers.
- Documentation edits happen only in Task 12, after live acceptance.

## Review Focus

1. A `cursor` sent on an unpaged operation (`get_tag`, `list_screens`, …) should be a
   `validation_error`, not silently ignored → Task 6 test `Cursor_OnUnpagedOperation_IsValidationError`.
2. A cursor issued before a host process restart should fail `invalid_cursor` (process-scoped key),
   not reach the worker → Task 6 test `Cursor_FromOtherProcessKey_IsInvalidCursor`.
3. A single-page or last-page result must store no snapshot and return no `NextCursor`, so the store
   does not fill with dead entries → Task 5 test `Page_SinglePage_StoresNoSnapshot`.
4. An object deleted between pages fails only its own row, the other rows on the page still read →
   Task 5 test `Continuation_DeletedObject_FailsOnlyItsRow`.
5. A dotted `tagName` (`Motor_1.Speed`) in the `{hmi}` selector should be `target_not_found` with a
   message that member paths are unsupported, not a generic miss → Task 9 test
   `HmiSelector_MemberPath_IsTargetNotFoundWithGuidance`.

## File Structure

| File | Responsibility | Tasks |
|---|---|---|
| `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiReadLog.cs` | sole home of `Guard`, `ReadText`, `ReadVariant`, `Note`/`Fail` | 1, 2 |
| `…/Openness/Hmi/HmiTagReader.cs`, `HmiAlarmReader.cs`, `HmiScreenReader.cs` | readers; lose duplicate helpers | 1, 4, 5 |
| `…/Openness/Hmi/HmiSoftwareInfoReader.cs` | M9 allow-list | 2 |
| `…/Openness/Hmi/HmiValidationReader.cs` | M8, new categories | 2, 8 |
| `…/Openness/Hmi/HmiSoftwareLocator.cs` | Classic-only failure | 3 |
| `…/Openness/Hmi/HmiTagLookup.cs` (new) | shared `FindUnique` tag lookup | 4, 9 |
| `…/Openness/Hmi/HmiSnapshotStore.cs` (new) | worker snapshot store | 5 |
| `…/Openness/Hmi/HmiPager.cs`, `HmiReadDispatch.cs` | snapshot-aware paging, dispatch | 5 |
| `TiaMcpServer.Contracts/Hmi/HmiPage.cs`, `HmiQueryInfo.cs` | `SnapshotId`, `NextCursor` | 5, 6 |
| `TiaMcpServer/Hmi/HmiPageCursorCodec.cs` (new) | seal/validate cursors | 6 |
| `TiaMcpServer/Hmi/HmiOperationCatalog.cs`, `HmiOperationRequest.cs`, `HmiWorkerInvoker.cs`, `HmiPayloadContract.cs`, `HmiReadTools.cs`, `HmiContractVersion.cs` | request field, validation, cursor wiring, description | 6, 8 |
| `TiaMcpServer.FakeWorker/Program.cs` | `hmi-read-paged` scenario | 7 |
| `TiaMcpServer.Contracts/CrossReferences/CrossReferenceSelectorInfo.cs`, `TiaMcpServer/CrossReferences/CrossReferenceTarget.cs`, `CrossReferenceReadTools.cs`, `…/Openness/CrossReferences/CrossReferenceTargetResolver.cs` | `{hmi}` selector | 9 |
| `reference-stubs/Siemens.Engineering.WinCCUnified/*.cs` | members the new code calls | 4, 9 (as needed) |
| `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` | link new host files (`Hmi\*.cs` are listed one by one, `:161-167`) | 6 |

---

### Task 1: One helper set and one completeness idiom (M3, M4)

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiReadLog.cs`, `HmiTagReader.cs` (delete `Guard` :438, `Variant` :321, `ReadComment` :200, public `ReadRow(HmiTag, string?, ICollection<string>)` :74), `HmiAlarmReader.cs` (move `ReadText` :213, `ReadVariant` :236), `HmiScreenReader.cs` (:177, :194 callers)
- Test: `TiaMcpServer.Tests/Hmi/HmiTagReaderTests.cs`, `HmiAlarmReaderTests.cs`

**Interfaces:**
- Produces: `HmiReadLog.ReadText(Func<MultilingualText?> read, string? language, string what) : List<HmiText>?` (instance; skipped items call `Fail`); `HmiReadLog.ReadVariant(Func<object?> read, string what) : HmiVariant?` (instance).

- [ ] **Step 1: Write failing tests**
  - `ListTags_SkippedCommentText_MarksIncomplete`: a tag double whose `Comment` has an item the mapper skips → `result.IsComplete == false` and the skip message present.
  - `GetTag_MemberRowCap_StaysComplete`: a tag with 101 members → `IsComplete == true`, cap message present (pins current behaviour).
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiTagReaderTests"` — expect the first to FAIL, the second to PASS.
- [ ] **Step 3: Move helpers to `HmiReadLog`, delete the duplicates, replace every `Messages.Count == 0` in `HmiTagReader` with `log.IsComplete`.** Row-cap message uses `Note`. Leave `HmiSoftwareInfoReader.ListDevices` unchanged unless moving it onto `HmiReadLog` keeps every existing `HmiSoftwareInfoReaderTests` assertion green without edits.
- [ ] **Step 4: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Hmi"` — PASS.
- [ ] **Step 5: Commit** `refactor: share hmi read helpers through HmiReadLog`

### Task 2: `validate` without system tags (M8) and complete Comfort runtime settings (M9)

**Files:**
- Modify: `…/Openness/Hmi/HmiValidationReader.cs:117-134`, `…/Openness/Hmi/HmiSoftwareInfoReader.cs:239-248`
- Test: `HmiValidationReaderTests.cs`, `HmiSoftwareInfoReaderTests.cs:56-78`

**Interfaces:**
- Produces: `HmiSoftwareInfoReader` private allow-list `KnownUnsupported : IReadOnlySet<string>` = `{ "GMPEnabled", "GeneralESIGCommentsStrategy", "TagOptimizationActive" }`.

- [ ] **Step 1: Write failing tests**
  - `Validate_Tags_ExcludesSystemTags`: software with 2 user tags and 3 `@` system tags → `Scanned == 2`, no system tag in `NotValidatable`.
  - `Validate_All_ExcludesSystemTags`: same for `all`.
  - Flip the Comfort test to `ReadRuntimeSettings_ComfortKnownUnsupported_IsCompleteWithNote`: `IsComplete == true`, a message names `GMPEnabled`.
  - `ReadRuntimeSettings_UnknownPropertyThrows_IsIncomplete`: a property outside the allow-list throwing `EngineeringTargetInvocationException` → `IsComplete == false`.
  - `ReadRuntimeSettings_KnownPropertyNonRecoverableThrow_Propagates`: allow-listed property throwing a non-recoverable exception is not swallowed.
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiValidationReaderTests|FullyQualifiedName~HmiSoftwareInfoReaderTests"` — new tests FAIL.
- [ ] **Step 3: Drop `NamedSystemTags` from the `tags` and `all` cases; in `Section`, a recoverable exception on an allow-listed property name calls `log.Note`, everything else unchanged.**
- [ ] **Step 4: Run** the same filter — PASS.
- [ ] **Step 5: Commit** `fix: drop system tags from validate and note known unsupported settings`

### Task 3: Classic-only locator failure

**Files:**
- Modify: `…/Openness/Hmi/HmiSoftwareLocator.cs:64-102` (and the `:97` message)
- Test: `HmiSoftwareLocatorTests.cs`

- [ ] **Step 1: Write failing tests**
  - `FindUnique_NoName_OnlyClassic_IsTargetKindUnsupported`: category `target_kind_unsupported`.
  - `FindUnique_NoName_NoHmiAtAll_IsTargetNotFound`: unchanged.
  - `FindUnique_ClassicMessage_DoesNotNameTool`: message does not contain `hmi_read`; it says only WinCC Unified HMIs are supported.
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiSoftwareLocatorTests"` — FAIL.
- [ ] **Step 3: When no Unified entry matches and at least one Classic entry was walked, throw `target_kind_unsupported`.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `fix: report classic-only hmi projects as unsupported`

### Task 4: Quick performance wins and the shared tag lookup

**Files:**
- Create: `…/Openness/Hmi/HmiTagLookup.cs`
- Modify: `HmiTagReader.cs` (remove private `FindUniqueTag` :373), `HmiScreenReader.cs` (`ScreenInfos` :184-203, `FindScreen` :266), stub files under `reference-stubs/Siemens.Engineering.WinCCUnified/` only if `Count`/`Find` are missing
- Test: `HmiTagReaderTests.cs`, `HmiScreenReaderTests.cs`

**Interfaces:**
- Produces: `internal static class HmiTagLookup { internal static HmiTag FindUnique(HmiSoftware software, string tagName); }` — exact `Tags.Find(tagName)` hit first; on `null`, the existing case-insensitive scan with `target_ambiguous` / `target_not_found`.

Note: `HmiReadDispatch.Software()` already resolves once per item; the spec's "resolve once" win is
delivered by the cursor path (Task 5), which skips the device walk. No separate change.

- [ ] **Step 1: Write failing tests**
  - `GetTag_ExactName_UsesFind_NoEnumeration`: a tag composition double counting enumerations → 0 enumerations.
  - `GetTag_CaseVariant_FallsBackToScan`: `Find` returns null, scan finds `motor_1` for `Motor_1`.
  - `ListScreens_ItemCount_UsesCompositionCount`: screen-items double counting enumerations → 0 enumerations, `ItemCount` correct.
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiTagReaderTests|FullyQualifiedName~HmiScreenReaderTests"` — FAIL.
- [ ] **Step 3: Implement `HmiTagLookup.FindUnique`; `GetTag` and `FindScreen` use composition `Find` first; `ScreenInfos` uses `ScreenItems.Count`.** Extend stubs if the stub build lacks the members.
- [ ] **Step 4: Run** the filter — PASS; then `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` and `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1` — both succeed.
- [ ] **Step 5: Commit** `perf: use composition find and count in hmi reads`

### Task 5: Worker snapshot store and snapshot-aware paging

**Files:**
- Create: `…/Openness/Hmi/HmiSnapshotStore.cs`
- Modify: `TiaMcpServer.Contracts/Hmi/HmiPage.cs`, `TiaMcpServer.Contracts/Hmi/HmiQueryInfo.cs`, `…/Openness/Hmi/HmiPager.cs`, `HmiReadDispatch.cs`, the seven paged readers (`ListTags`, `ListSystemTags`, `ListAlarms`, `ListLoggingTags`, `ListScreenItems`, `ListFaceplateInstances`, `HmiValidationReader.Validate`)
- Test: create `TiaMcpServer.Tests/Hmi/HmiSnapshotStoreTests.cs`; extend `HmiPagerTests.cs`

**Interfaces:**
- Produces (contracts):
  - `HmiPage(int Offset, int Limit, int Total, int? NextOffset)` gains init properties `string? SnapshotId` (worker → host only; conditional) and `string? NextCursor` (host → caller only; conditional).
  - `HmiQueryInfo.SnapshotId : string?`.
- Produces (worker):
  - `sealed class HmiSnapshotStore(Func<DateTime> utcNow, int capacity = 4, TimeSpan? idle = null)`; `static HmiSnapshotStore Shared`.
  - `string Put(object project, HmiSoftware software, string operation, IReadOnlyList<object> ordered)`.
  - `HmiSnapshot Take(object project, string snapshotId, string operation)` → throws `WorkerOperationException(snapshot_unavailable)` when unknown/expired/evicted, `cursor_snapshot_mismatch` when `!object.Equals(entry.Project, project)` or the operation differs. A project mismatch also evicts the entry.
  - `sealed record HmiSnapshot(HmiSoftware Software, IReadOnlyList<object> Ordered)`.
  - `readonly record struct HmiPaging(int Offset, int Limit, string Operation, HmiSnapshot? Snapshot)`.
  - `HmiPager.Page<T>(Func<IEnumerable<T>> scan, Func<T, string> name, HmiPaging paging, object project, HmiSoftware software) : (IReadOnlyList<T> Items, HmiPage Page)` — with `paging.Snapshot` it pages `Snapshot.Ordered.Cast<T>()` and never calls `scan`; otherwise it scans, orders, and calls `Put` only when `NextOffset` is non-null. Returned `Page.SnapshotId` is set exactly when `NextOffset` is.
- Dispatch rule: for a paged method with `query.SnapshotId`, `HmiReadDispatch` takes the snapshot and uses `snapshot.Software` (no `FindUnique` walk); otherwise unchanged.
- Spec §4.2 interpretation: "clears the store on binding change or detach" and "software identity
  differs" are enforced at `Take` — the host rejects a changed binding before the call, and the
  worker evicts an entry whose recorded project no longer equals the active `ProjectBase`. The
  software itself is fixed by the sealed `hmiName` in `queryHash`. No detach hook is added.

- [ ] **Step 1: Write failing tests** (manual clock)
  - `Put_Fifth_EvictsLeastRecentlyUsed`; `Take_AfterIdleTimeout_IsSnapshotUnavailable`; `Take_RefreshesSlidingExpiry`.
  - `Take_OtherProject_IsCursorSnapshotMismatchAndEvicts`; `Take_OtherOperation_IsCursorSnapshotMismatch`.
  - `Page_SinglePage_StoresNoSnapshot` (Review Focus 3); `Page_LastPage_HasNoSnapshotId`.
  - `Page_WithSnapshot_DoesNotScan`: `scan` delegate throws if called.
  - `Continuation_DeletedObject_FailsOnlyItsRow` (Review Focus 4): a held tag double whose `Name` read throws `EngineeringObjectDisposedException` → that row reported in messages, other rows present, `IsComplete == false`.
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiSnapshotStoreTests|FullyQualifiedName~HmiPagerTests"` — FAIL.
- [ ] **Step 3: Implement the store, the pager overload, dispatch wiring, and switch the seven readers to the overload.** Faceplate and screen-item snapshots hold the per-row tuple the reader already builds. Remove the old `Page(source, name, offset, limit)` overload once no caller remains.
- [ ] **Step 4: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Hmi"` — PASS.
- [ ] **Step 5: Commit** `feat: hold hmi page snapshots in the worker`

### Task 6: Host cursor codec and request wiring

**Files:**
- Create: `TiaMcpServer/Hmi/HmiPageCursorCodec.cs`
- Modify: `HmiOperationRequest.cs` (add `Cursor`), `HmiOperationCatalog.cs` (`ValidateItem`, `ToQuery`), `HmiWorkerInvoker.cs`, `HmiPayloadContract.cs`, `HmiReadTools.cs` (DI of codec, description), `HmiContractVersion.cs` (`"1.1"`), `TiaMcpServer/Program.cs` / tool registration where the codec is constructed, `TiaMcpServer.Tests.csproj`, `ConditionalMemberRegisterTests.cs`
- Test: create `TiaMcpServer.Tests/Hmi/HmiPageCursorCodecTests.cs`; extend `HmiReadToolsTests.cs`, `HmiPayloadContractDecodeTests.cs`

**Interfaces:**
- Consumes: `AuthenticatedCursorProtector` (`CreateProcessScoped()`), `ProjectBindingCursorState` and its `Matches(ProjectBindingSnapshot, preserveUnboundEpoch: true)`, `OpennessWorkerClient.BindingSnapshot`.
- Produces:
  - `internal sealed record HmiPageCursorState(string SnapshotId, string Operation, string QueryHash, int Offset, ProjectBindingCursorState HostBinding)`.
  - `internal sealed class HmiPageCursorCodec(AuthenticatedCursorProtector protector)`: `string Encode(HmiPageCursorState)`; `HmiPageCursorState Decode(string cursor)` (throws `HmiPageCursorException(category)`); `static string QueryHash(HmiOperationRequest)` per Global Constraints.
  - `HmiWorkerInvoker.InvokeReadAsync(OpennessWorkerClient client, HmiPageCursorCodec codec, HmiOperationRequest operation, CancellationToken)` — decodes and validates the cursor before the worker call (filters, binding), then sends `Offset = state.Offset`, `SnapshotId = state.SnapshotId`; cursor failures return `WorkerCallResult.Fail(category, message)` so they are item failures.
  - `HmiPayloadContract.Project(…)` gains the codec and binding: when the decoded payload has `page.snapshotId`, it is removed and replaced by `page.nextCursor` sealed at offset `page.nextOffset`.

- [ ] **Step 1: Write failing tests**
  - Codec: `EncodeDecode_RoundTrips`; `Decode_Tampered_IsInvalidCursor`; `Cursor_FromOtherProcessKey_IsInvalidCursor` (Review Focus 2); `QueryHash_IgnoresLimit`; `QueryHash_DiffersByEachFilter` (theory over the ten fields).
  - Tool: `Cursor_WithOffset_IsValidationError`; `Cursor_OnUnpagedOperation_IsValidationError` (Review Focus 1); `Cursor_ChangedFilter_IsCursorFilterMismatch_NoWorkerCall`; `Cursor_AfterRebind_IsCursorBindingMismatch_NoWorkerCall`.
  - Projection: `Project_PageWithSnapshotId_EmitsNextCursorAndNoSnapshotId`; `Project_LastPage_HasNoNextCursor`.
  - Register: `HmiPage.NextCursor` and `HmiPage.SnapshotId` rows added to `ConditionalMemberRegisterTests`.
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiPageCursorCodecTests|FullyQualifiedName~HmiReadToolsTests|FullyQualifiedName~HmiPayloadContractDecodeTests|FullyQualifiedName~ConditionalMemberRegisterTests"` — FAIL.
- [ ] **Step 3: Implement codec, catalog validation, invoker and projection; bump the contract version; extend the tool description** with: paged operations return `nextCursor`; pass it as `cursor` (not with `offset`) to continue the same snapshot; objects added after the first page are not included; whole-project scans take seconds on large stations, so narrow with `screenName` / `tableName`.
- [ ] **Step 4: Run** the filter, then `dotnet test TiaMcpServer.Tests` — PASS.
- [ ] **Step 5: Commit** `feat: add hmi_read snapshot cursors`

### Task 7: FakeWorker paged round trip

**Files:**
- Modify: `TiaMcpServer.FakeWorker/Program.cs` (new scenario beside `hmi-read-roundtrip` :811)
- Test: `TiaMcpServer.Tests/Hmi/HmiReadFakeWorkerTests.cs`

**Interfaces:**
- Produces: scenario `hmi-read-paged` — `hmi_list_tags` with no `snapshotId` returns 2 of 5 rows and `page.snapshotId = "s1"`; with `snapshotId = "s1"` it pages the same 5 rows by `offset`; with any other id it fails `snapshot_unavailable`.

- [ ] **Step 1: Write failing tests** `Paged_FirstContinuationLast_FollowsCursor` (rows 1–2, 3–4, 5; last page has no `nextCursor`), `Paged_UnknownSnapshot_IsSnapshotUnavailable` (cursor sealed by the host for id `s2`).
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiReadFakeWorkerTests"` — FAIL.
- [ ] **Step 3: Implement the scenario.**
- [ ] **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `test: cover hmi cursor round trip through the worker protocol`

### Task 8: Name-scoped `validate` categories

**Files:**
- Modify: `HmiOperationCatalog.cs:57-60` (categories) and `ValidateValues`, `HmiValidationReader.cs` (`Objects` takes `name`), `HmiTagReader.cs` (expose member and logging-tag walks as `internal` enumerations), `HmiReadTools.cs` (description)
- Test: `HmiValidationReaderTests.cs`, `HmiOperationCatalogTests.cs`

**Interfaces:**
- Consumes: `HmiTagLookup.FindUnique` (Task 4), `HmiPager.Page` overload (Task 5).
- Produces: `internal static IEnumerable<(object Item, string Name)> HmiTagReader.MemberObjects(HmiTag tag, string tagName)` (recursive, `MaxMemberDepth`, names `Parent.Member`); `internal static IEnumerable<(object Item, string Name)> HmiTagReader.LoggingTagObjects(HmiTag tag, string tagName)` (`Tag/LoggingTag`); screen items named `Screen/Item`.

- [ ] **Step 1: Write failing tests**
  - Catalog: `Validate_NewCategory_WithoutName_IsValidationError` (theory over `memberTags`, `loggingTags`, `screenItems`); `Validate_NewCategoriesAccepted`.
  - Reader: `Validate_MemberTags_RecursesWithDottedNames` (depth 2 double → `Motor_1.Speed`, `Motor_1.Pid.Kp`); `Validate_MemberTags_StopsAtMaxDepth`; `Validate_LoggingTags_NamesTagSlashLog`; `Validate_ScreenItems_OnlyNamedScreen`; `Validate_All_UnchangedObjectSet`.
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiValidationReaderTests|FullyQualifiedName~HmiOperationCatalogTests"` — FAIL.
- [ ] **Step 3: Implement the categories; update the description's category list and the "name required for memberTags, loggingTags, screenItems" rule.**
- [ ] **Step 4: Run** the filter — PASS.
- [ ] **Step 5: Commit** `feat: validate hmi member tags, logging tags and screen items`

### Task 9: Provisional `{hmi}` selector in `read_cross_references`

**Files:**
- Modify: `TiaMcpServer.Contracts/CrossReferences/CrossReferenceSelectorInfo.cs`, `TiaMcpServer/CrossReferences/CrossReferenceTarget.cs`, `CrossReferenceReadTools.cs` (validation, description), `…/Openness/CrossReferences/CrossReferenceTargetResolver.cs:50-101`, `ConditionalMemberRegisterTests.cs`, stubs if `HmiBase.GetService<T>` is missing
- Test: `TiaMcpServer.Tests/CrossReferences/CrossReferenceReadToolsTests.cs`, `TiaMcpServer.Tests/Block/CrossReferenceTargetResolverTests.cs`

**Interfaces:**
- Consumes: `HmiSoftwareLocator.FindUnique`, `HmiTagLookup.FindUnique`.
- Produces:
  - Contracts: `CrossReferenceSelectorInfo.Hmi : CrossReferenceHmiSelectorInfo?` (conditional) with `HmiName : string`, `TagName : string`.
  - Host: `CrossReferenceTargetSelector.Hmi : CrossReferenceHmiSelector?` with `[JsonUnmappedMemberHandling(Disallow)]`, `HmiName`, `TagName`.
  - Worker: `Resolve` branches on `selector.Hmi` → `ResolvedCrossReferenceTarget(canonical: selector, leafService: tag.GetService<CrossReferenceService>(), container: software)`.

- [ ] **Step 1: Write failing tests**
  - Host: `HmiSelector_WithPath_IsValidationError`; `Selector_NeitherPathNorHmi_IsValidationError`; `HmiSelector_UnknownKey_IsValidationError`; `HmiSelector_BlankHmiName_IsValidationError`; `HmiSelector_BlankTagName_IsValidationError`.
  - Worker: `HmiSelector_ResolvesTagService`; `HmiSelector_NullService_IsTargetKindUnsupported`; `HmiSelector_Classic_IsTargetKindUnsupported`; `HmiSelector_MemberPath_IsTargetNotFoundWithGuidance` (Review Focus 5: message says UDT member paths are not supported).
  - Register: `CrossReferenceSelectorInfo.Hmi` row.
- [ ] **Step 2: Run** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~CrossReference"` — FAIL.
- [ ] **Step 3: Implement selector, validation, resolver branch; description marks the `hmi` variant provisional, tags only, `hmiName` required.**
- [ ] **Step 4: Run** the filter, the stub build and `verify-reference-stubs.ps1` — PASS.
- [ ] **Step 5: Commit** `feat: resolve unified hmi tags in read_cross_references`

### Task 10: Offline gate and candidate

- [ ] **Step 1:** `dotnet build TiaMcpServer.slnx -m:1` and `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` — both succeed with no new warnings.
- [ ] **Step 2:** `dotnet test TiaMcpServer.Tests` with the repository's coverage collection (as in CI) — all pass, coverage gate held.
- [ ] **Step 3:** `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1` — PASS.
- [ ] **Step 4:** whole-branch review (requesting-code-review); fix CRITICAL/HIGH findings, one commit per fix.
- [ ] **Step 5:** record the candidate commit for Task 11.

### Task 11: Live acceptance (maintainer-run environment)

Requires the maintainer: a disposable copy of the V21 project with the spec §7.2 fixtures added by
hand, and the candidate installed as a global tool per `docs/development/packaging.md` with the MCP
server restarted.

- [ ] **Step 1:** confirm the installed build is the candidate (tool version string) and the tool list still has 7 / 16 / 16 tools.
- [ ] **Step 2:** run spec §7.3 cases 1–10, recording timings and responses.
- [ ] **Step 3:** decide `list_screens` `Count` keep/revert from case 5; revert in its own commit if no saving.
- [ ] **Step 4:** write `docs/superpowers/acceptance/reports/2026-10-1X-hmi-read-completion-live-acceptance.md` (actual date), naming every unexecuted case.
- [ ] **Step 5: Commit** `docs: add hmi_read completion live acceptance report`

### Task 12: Documentation

**Files:** `docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md`, the selector sections of
`docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` and `MULTIUSER_OPERATIONS_SUMMARY.md`,
`docs/ARCHITECTURE.md` §7a, `docs/IMPROVEMENT_LOG.md`, `docs/README.md`, `docs/superpowers/README.md`,
`AGENTS.md` HMI convention line.

- [ ] **Step 1:** document cursor, staleness rule, new categories, M8/M9 behaviour, Classic-only failure, GMP permanently unread, provisional `{hmi}` selector, worker snapshot store; close finished improvement-log items and keep watch items.
- [ ] **Step 2:** index this spec, this plan and the Task 11 report in `docs/README.md` and `docs/superpowers/README.md`.
- [ ] **Step 3:** check every link resolves (relative links in `docs/`; README.md absolute GitHub URLs only).
- [ ] **Step 4: Commit** `docs: document hmi_read completion`
