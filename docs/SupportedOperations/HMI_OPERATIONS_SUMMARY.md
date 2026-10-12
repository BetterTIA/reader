# TIA Portal HMI Operations

`hmi_read` is the read-only WinCC Unified HMI tool. It is registered in every access mode (counts
7/16/16), never writes, never compiles, and never opens, saves or closes a project. It runs up to
50 items per call on the structured JSON contract (`contractVersion:"1.1"`); items run
independently, so a failing item does not stop later ones. Design background lives in the
[hmi_read design spec](../superpowers/specs/2026-10-07-hmi-read-design.md), the
[spike report](../superpowers/acceptance/reports/2026-10-07-hmi-read-spike.md), the
[live acceptance report](../superpowers/acceptance/reports/2026-10-08-hmi-read-live-acceptance.md),
the [completion spec](../superpowers/specs/2026-10-11-hmi-read-completion-design.md) and the
[completion live acceptance report](../superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md).

## Operations

Every operation except `list_hmi_devices` and `list_project_languages` takes an optional `hmiName`
(software or device name); omit it only when the project has exactly one Unified HMI. Each item
carries a unique `operationId` (at most 256 characters) and an `operation`. Fields outside an
operation's list are rejected before any worker call.

| Operation | Required | Optional | Paged | Returns |
|---|---|---|---|---|
| `list_hmi_devices` | - | - | no | Unified and Classic HMI devices in the project |
| `list_tag_tables` | - | `hmiName`, `groupPath` | no | Tag-table and group tree |
| `list_tags` | - | `hmiName`, `tableName`, `language` | yes | Tag rows (core fields) |
| `get_tag` | `tagName` | `hmiName`, `language` | no | One tag with detail: members, thresholds, ranges, substitute values |
| `list_system_tags` | - | `hmiName` | yes | System tags |
| `list_connections` | - | `hmiName` | no | Connections, driver properties, initial addresses |
| `list_alarms` | - | `hmiName`, `alarmKind`, `language` | yes | Discrete or analog alarm rows |
| `get_alarm` | `alarmName`, `alarmKind` | `hmiName`, `language` | no | One alarm with detail |
| `list_alarm_classes` | - | `hmiName` | no | Alarm classes, audit classes, OPC UA alarm types |
| `list_logs` | - | `hmiName` | no | Data logs and alarm logs with settings, segments, backup |
| `list_logging_tags` | - | `hmiName`, `dataLogName`, `tagName` | yes | Logging tags |
| `list_screens` | - | `hmiName`, `groupPath`, `language` | no | Screen group tree with screens |
| `list_screen_items` | `screenName` | `hmiName` | yes | Items on one screen |
| `list_faceplate_instances` | - | `hmiName`, `screenName` | yes | Faceplate instances and their bindings |
| `get_screen_navigation` | - | `hmiName` | no | Navigation edges between screens |
| `get_runtime_settings` | - | `hmiName` | no | Runtime settings sections |
| `list_script_modules` | - | `hmiName` | no | JavaScript script modules |
| `list_text_and_graphic_lists` | - | `hmiName` | no | Text lists and graphic lists |
| `list_project_languages` | - | - | no | Project languages |
| `validate` | `category` | `hmiName`, `name` | yes | Openness validation findings |

`alarmKind` is `discrete` or `analog`. `validate` takes `category` `tags`, `alarms`, `screens`,
`connections`, `logs`, `alarmClasses` or `all`; `name` narrows to one object and is not valid with
`all`. The name-scoped categories `memberTags`, `loggingTags` and `screenItems` require `name` (see
[validate](#operation-notes)) and are not part of `all`. `language` is a culture name such as `en-US`; `list_connections` and `list_alarm_classes`
have no `language` because connection comments are plain strings and alarm classes carry no texts.

## Result conventions

- **Paging.** Paged operations take `offset` (0 or greater, default 0) and `limit` (1-2000, default
  100) and return `page { offset, limit, total, nextOffset, nextCursor }`. Order is case-insensitive
  ordinal with a case-sensitive tie-break. A call without `cursor` re-scans the project; see
  [Cursor paging](#cursor-paging) for continuing one scan.
- **Completeness.** Every result carries `isComplete` and `messages`. A property Openness cannot read
  becomes `null` with a message and `isComplete:false`. An unreadable composition fails the item with
  `worker_operation_failed`. Per-property recovery applies only to
  `EngineeringTargetInvocationException` and `EngineeringNotSupportedException`; a dead portal fails
  the item.
- **Values.** `HmiVariant.value` is a real JSON scalar, never JSON inside a string. Texts are
  `HmiText` arrays of `{culture, text}` and are returned raw: alarm event texts keep their
  `<body><p>...</p></body>` markup. A `language` filter naming a culture that is not a project
  language fails the item `target_not_found` and names the project languages.
- **Budgets.** A complete response is capped at 180,000 characters and one value at 60,000; larger
  values are omitted whole with narrowing guidance (lower `limit`, add a narrowing field, or re-run
  the `operationId` alone).
- **Errors.** Classic WinCC (`Siemens.Engineering.Hmi.HmiTarget`) devices are listed by
  `list_hmi_devices` but every other operation fails the item `target_kind_unsupported` ("... is a
  Classic (WinCC) HMI; only WinCC Unified HMIs are supported."). This also applies when `hmiName` is
  omitted and the project has no Unified HMI but at least one Classic HMI. Selector misses are
  `target_not_found` or `target_ambiguous`.

## Cursor paging

The seven paged operations (`list_tags`, `list_system_tags`, `list_alarms`, `list_logging_tags`,
`list_screen_items`, `list_faceplate_instances`, `validate`) return `page.nextCursor` whenever
`nextOffset` is set, and null on the last page. Pass it back as `cursor`, instead of `offset`, with the
same operation and filters to continue the same scan. `limit` may differ between pages. Offset paging
stays fully supported; a call without `cursor` behaves as before and re-scans.

- **Cost.** The worker holds the name-ordered objects of the first scan, so a continuation reads only
  its own rows. Live: 1,286 faceplate instances on a Unified PC took 21 s for the first page and
  0.5-0.8 s per continuation; an offset-only page took 21 s.
- **Staleness.** The object list is frozen when the first page is read; every row is read live. An
  object deleted since then fails its own row with a message and `isComplete:false`; the other rows
  are unaffected. Objects added since then are not included; a call without `cursor` sees them.
- **Lifetime.** The worker keeps at most 4 snapshots, least recently used evicted first, each dropped
  after 10 minutes without a continuation. Every scan with more rows than its page takes a slot,
  including offset-only calls, so a batch with more than 4 multi-page items, or other paged calls in
  between, can evict a cursor that is still in use.
- **Failures.** `cursor` together with `offset`, or `cursor` on an unpaged operation, is a
  `validation_error` that rejects the whole call, like any other field error. Every other cursor
  failure fails only its item. Checks before any worker call:

  | Condition | Category |
  |---|---|
  | blank, malformed or tampered cursor, or one issued by an earlier server process | `invalid_cursor` |
  | different operation or any filter (`hmiName`, `tableName`, `language`, `alarmKind`, `dataLogName`, `tagName`, `screenName`, `category`, `name`) changed, including only its case | `cursor_filter_mismatch` |
  | project binding changed since the cursor was issued | `cursor_binding_mismatch` |

  In the worker, an expired or evicted snapshot is `snapshot_unavailable`, and a snapshot taken from
  another project or operation is `cursor_snapshot_mismatch`. Restart without a cursor in each case.
- **Worker restart.** A worker restart loses every snapshot, but it also invalidates the project
  binding, so the next continuation fails `binding_conflict`, not `snapshot_unavailable`. After
  `bind_project` the old cursor fails `cursor_binding_mismatch`; a call without a cursor recovers.
- **Local sessions.** Cursor paging works on a cold-adopted `.als21` local session (live, 2026-10-12).

## Operation notes

- **`get_tag` members.** Members are read recursively to depth 8 and at most 100 member rows in
  all (every level combined). Hitting either cap sets `membersTruncated:true`; the row cap also adds
  one message. Neither cap makes the result incomplete.
- **`validate`.** Pages over *scanned* objects so the cost of one call is bounded; `total` is the
  number of objects in the category. Clean objects are counted, not listed; each finding carries
  `objectKind` (the Openness type name, such as `HmiIOField`), `name` and `results[]` of
  `{propertyName, errors[], warnings[]}`. Objects without `Validate()` are listed once per kind in
  `notValidatable`. System tags are not scanned or counted (`tags` and `all`). An unreadable
  validation result is never counted clean. Three name-scoped categories reach objects the others
  skip; `name` is required, and they are not part of `all`:

  | Category | `name` | Validates | Finding `name` |
  |---|---|---|---|
  | `memberTags` | top-level tag | every member, recursively to depth 8 | `Motor_1.Speed` |
  | `loggingTags` | top-level tag | the tag's logging tags | `Temp_1/Log_1` |
  | `screenItems` | screen | every item on that screen | `Main/Button_3` |

  Live example: an IO field bound to a deleted tag is reported under `screenItems` as
  `objectKind:"HmiIOField"`, `propertyName:"Dynamization"`, with the error "The tag '...' for
  dynamization of the property 'Process value' does not exist."
- **`list_logging_tags` with `dataLogName`.** A logging tag whose `DataLog` cannot be read is left
  out of the scan with a message and `isComplete:false`. The message comes from the scanning call;
  cursor continuations do not scan again and do not repeat it.
- **`get_runtime_settings`.** Setting values are strings. The OpcUaServer section carries a subset
  of its settings. `GMPEnabled`, `GeneralESIGCommentsStrategy` and `TagOptimizationActive` are on an
  allow-list: when Openness reports one as unsupported on the device, the value is null with a note
  and `isComplete` stays true. Any other unreadable setting still sets `isComplete:false`. Whether
  the GMP and ESIG settings read depends on the device; some Comfort panels never return them.
- **Screen items.** Geometry comes from `IHmiBoxFeature`; items without box geometry (for example
  elliptical arcs) report null geometry with no message. A screen window without a target screen
  yields no navigation edge and a message (`isComplete` stays true).
- **Connections.** Secrets never leave the worker. A driver property whose name contains a
  credential-like word (`password`, `passwd`, `passphrase`, `pwd`, `secret`, `token`, `credential`,
  `privatekey`, any case) is dropped before its value is read, and a property whose name cannot be
  read has its value left unread. The same check drops matching `initialAddress.parsed` keys and sets
  `initialAddress.raw` to null when the address names one. Duplicate (name, value) rows reported by
  Openness are collapsed.
- **Never read.** `HmiTag.ConfirmationType` (it crashed TIA Portal in the spike), and
  `GmpRelevant` / `MandatoryCommenting`. These stay permanently unread; there is no probe.

## Known limits

- Whole-project scans take roughly 21-23 s per call on a large PC station regardless of `limit`;
  narrow with `screenName` or `tableName` and continue with the cursor.
- Every item resolves its HMI again: on a large project one `validate screenItems` item costs about
  6 s, so a batch over many screens takes minutes.
- `get_tag` with a dotted UDT member name fails `target_not_found` without the member-path hint that
  `read_cross_references` gives.
- Floating-point values are written explicitly (`G15`, or `G17` when 15 digits do not round-trip;
  `G7`/`G9` for float), so net10 and net48 produce the same text. A tag's non-integral initial value
  arrives from Openness as a string (`System.String`), so non-integral `Double` encoding is still not
  observed live.
- Live evidence gaps: non-default connection drivers (no OPC UA connection was available), live
  validation warnings, non-default Object values.

See the [improvement log](../IMPROVEMENT_LOG.md#open-hmi_read-follow-ups-2026-10-12) for follow-ups.

## Cross-references of HMI tags

`read_cross_references` takes a provisional `{ hmi: { hmiName, tagName } }` target for one top-level
Unified HMI tag; see
[Cross-reference coverage](PLC_OPERATIONS_SUMMARY.md#cross-reference-coverage-read_cross_references).

## Out of scope

SiVArc, plant views, YAML or JavaScript export content, dynamizations and events, a generic attribute
dump, HMI compile, hardware-layer connections, and reads of Classic WinCC objects. There are no HMI
writes. `compile_check` keeps its PLC-only contract.

Related: [README.md](README.md) for the operation index and
[DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.md) for hardware information.
