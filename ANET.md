# ANet fork of Czarnak/tia-portal-mcp

Upstream: https://github.com/Czarnak/tia-portal-mcp (MIT, (c) Czarnak — LICENSE kept unchanged).
Goal: let an AI session *see* everything Openness V21 can expose, read-only by default.

## Changes vs upstream
| Version | Change | Upstream PR candidate |
|---|---|---|
| 3.0.1-anet.1 | Hardware enumeration also walks `Project.UngroupedDevicesGroup` (decentral ET 200SP / GSD / switches were invisible: "No device named …"). Locator `ungroupedDevices/{i}`, owner-location code `ungrouped`. | yes |

## Build
Double-click `anet-build.cmd` (needs .NET SDK >= 10.0.400, TIA Portal V21 installed). Log: `anet-build.log`. Version from `anet-version.txt`.
