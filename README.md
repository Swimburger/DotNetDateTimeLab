# .NET DateTime Lab

An executable quick reference for parsing, inspecting, and converting dates exactly as .NET does. The app runs entirely in the browser with Blazor WebAssembly and makes the semantics of `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, cultures, and time zones visible.

## What it does

- Parses with `Parse` or `ParseExact`, a selected culture, and common `DateTimeStyles` presets.
- Inspects `Kind`, offsets, ticks, Unix time, and round-trip representations.
- Converts instants to UTC, browser-local time, and a selected time zone.
- Detects invalid and ambiguous daylight-saving wall times and shows both possible UTC instants.
- Generates the minimal C# needed to reproduce the selected behavior.
- Includes curated edge cases and contextual links to the official .NET documentation.

Input never leaves the browser. Results reflect the bundled .NET 11 runtime, ICU globalization data, and time-zone database.

## Prerequisites

- .NET SDK 11.0.100 or later in the 11.0 feature band
- .NET `wasm-tools` workload
- Node.js 22.18 or later
- pnpm 12

## Develop and test

```sh
dotnet workload install wasm-tools
dotnet restore
dotnet test
pnpm install
pnpm run build:cloudflare
pnpm exec playwright install chromium
pnpm run test:e2e
```

`pnpm run test:e2e` starts the built Cloudflare project on `http://127.0.0.1:8791`.

## Deploy

The project uses Cloudflare Workers Static Assets through `cloudflare.config.ts` and the `cf` CLI:

```sh
pnpm run deploy
```

The production custom domain is [datetime.swimburger.net](https://datetime.swimburger.net/).

## Scope

This lab focuses on parsing, type semantics, time-zone interpretation, and conversion. It intentionally does not try to be a comprehensive custom-format-string designer; that is better served by a separate focused playground.

## License

[MIT](LICENSE)
