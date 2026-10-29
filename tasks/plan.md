# Capability Map: SSD Image Capture Admin and Capture Improvements

| Module | Responsibility | Depends on |
|---|---|---|
| mobile-capture | Capture form, note persistence, date/time, map image overlay, secure API settings | existing Flutter app |
| api-schema-auth | Notes/client fields, SQL migration, API authorization and token lifecycle | existing API/SQL Server |
| desktop-admin | Windows admin UI, connection test, filtered preview, XLSX export, settings and token generation | API/database contract |
| docs-verification | Setup, deployment commands, test checklist | all modules |

Build order: api-schema-auth → mobile-capture, desktop-admin → docs-verification.

# Implementation Plan

## Assumptions
- Existing stack remains Flutter + ASP.NET Core 8 Minimal API + SQL Server/EF Core.
- The desktop app will use .NET 8 WinForms (simpler than WPF for a small admin utility), ClosedXML for workbook output, and DPAPI for local connection string protection.
- Admin app connects directly to the same SQL Server; API URL and mobile token are administration settings, with the API hash persisted server-side in SQL.
- Token generation is performed by a separate explicit admin action; initial provisioning is via a one-time secure bootstrap secret in environment/user-secrets, not an unauthenticated token endpoint.
- Map overlay will use a configurable licensed static-map provider, defaulting to Geoapify Static Maps after checking account/usage terms; OpenStreetMap data attribution will be burned into the image. If no valid map configuration/network, save photo without map overlay.
- Existing date/time convention remains +08:00. Client is optional text for export/filter; Note is nullable text.

## Tasks
1. API and SQL model/migration: Note, Client, token hash table, authentication middleware, secure first-token bootstrap, token rotation endpoint and settings values. Keep health endpoint public only if explicitly designated; protected photo endpoints always require token.
2. Flutter capture: SQLite v2 migration, Name persisted, Note transient and saved per photo, auto +08:00 date display, note sync, secure API URL/token settings, watermarked photo with best-effort map overlay.
3. WinForms admin: encrypted local connection string, DB connectivity test, API URL settings, token display/copy/hide/regenerate confirmation, table preview/filter and XLSX export.
4. README and test checklist; run available API/mobile/admin tests and builds. Do not apply SQL migration to any live database without separately confirming target/database.

## Risks
- Token hash provisioning/rotation must not become publicly accessible; initial bootstrap secret must be treated as sensitive.
- OSM data licensing does not grant unrestricted use of public tile servers; select a static-image provider with explicit terms and attribution requirements; do not stitch tiles from tile.openstreetmap.org.
- Existing Android plugin cache contains a local concurrent-futures fix; unrelated cache state may affect builds on clean machines.
- No Git commits exist in this repository; changes are uncommitted and hard to roll back.
- SQL Server is not configured in this environment; migration can be generated and SQL reviewed but not applied/verified against real DB.
