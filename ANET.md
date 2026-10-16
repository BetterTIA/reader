# ANet fork of Czarnak/tia-portal-mcp

Upstream: https://github.com/Czarnak/tia-portal-mcp (MIT, (c) Czarnak — LICENSE kept unchanged).
Goal: let an AI session *see* everything Openness V21 can expose, read-only by default.

## Changes vs upstream
| Version | Change | Upstream PR candidate |
|---|---|---|
| 3.0.1-anet.1 | Hardware enumeration also walks `Project.UngroupedDevicesGroup` (decentral ET 200SP / GSD / switches were invisible: "No device named …"). Locator `ungroupedDevices/{i}`, owner-location code `ungrouped`. | yes |
| 3.0.1-anet.2 | New read tool `export_to_folder`: exports blocks (.s7dcl/.s7res documents, .scl/.db source, SimaticML .xml fallback), PLC types (.udt / .xml) and tag tables (.xml) of one PLC under `--export-root` / `TIA_MCP_EXPORT_ROOT`, chunked by a time budget (runId + nextOffset), `manifest.jsonl` per run. Classified TemporaryExport (allowed in read-only; never writes the project). Software units not yet covered. | maybe |
| 3.0.1-anet.3 to anet.6 | `export_to_folder` reworked (`ProjectFolderExporter`, `--export-root` options); new read tool `export_hmi_to_folder` for WinCC Unified (screens, HMI tags, alarms, connections as JSON; `tools/hmi_render.py` renders screen previews from that export); new read tool `cross_references_for` (cross-references of one object); `anet-watch.cmd` build helper. | maybe |
| 3.0.1-anet.7 | `cross_references_for`: reports `incomplete` / `unreadableMembers` / `warning` instead of silently returning empty values for members Openness refused to read; object lookup also covers software units (`PLC/Units/<unit>/Blocks|Types|Tags/...`) and, by bare name, system blocks. | no |
| 3.0.1-anet.8 | New read tool `read_technology_object`: technology objects (PID_Temp, PID_Compact, axes...) live in `PlcSoftware.TechnologicalObjectGroup`, which the block tools never walk. Without a name it lists them (type, version, DB number, folder); with a name it returns the parameters (`TechnologicalParameter` name / value, e.g. `Config.InputUpperLimit`, `Retain.CtrlParams.Heat.Gain`), optionally narrowed by `parameterPrefixes` (whole path segments); `includeXml` writes the SimaticML export under the export root. Unreadable parameters are counted (`incomplete`). Not yet checked against a project with technology objects. | maybe |

## Build
Double-click `anet-build.cmd` (needs .NET SDK >= 10.0.400, TIA Portal V21 installed). Log: `anet-build.log`. Version from `anet-version.txt`.
