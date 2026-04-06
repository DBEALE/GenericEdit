# Frontend

Angular 20 standalone UI for schema management, dataset editing, and API query simulation.

## Development server

Start from repo root (recommended):

```powershell
./start-dev.ps1
```

This launches the frontend on `http://localhost:4300`.

You can also run frontend only:

```bash
npm install
npm start -- --port 4300
```

## Build

```bash
npm run build
```

## Unit tests

```bash
npm run test -- --watch=false --browsers=ChromeHeadless
```

## Current defaults

- Default simulated user: `Admin`
- Default simulated roles: `Reader,Writer,Approver,Admin,DatasetAdmin`
- New schema default permissions:
	- Read roles: `Reader`
	- Write roles: `Writer`
	- Signoff roles: `Approver`
	- Dataset admin roles: `Admin`

## Grid and import behavior

- Select/lookup editors use large AG Grid popup editors to improve usability in compact row layouts.
- Clipboard import supports CSV and Excel-style tab-delimited content.

## Runtime UI debug flag

UI API debug behavior is controlled at runtime in [public/runtime-config.js](public/runtime-config.js).

- `globalThis.__datasetUiDebugIncludeInternalInfo = true` forces Dataset Workspace API reads to use `includeInternalInfo=true`.
- Set it to `false` to disable.
