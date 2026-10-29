# Work items

- [ ] API/database schema, migration and secure token lifecycle
  - Acceptance: Note and Client fields stored; mobile endpoints reject missing/invalid bearer; token rotation atomically invalidates prior hash; first provisioning requires configured bootstrap secret.
  - Verify: dotnet build/test; endpoint tests for 401, valid upload, rotation invalidation; generated SQL migration reviewed.
- [ ] Mobile capture updates
  - Acceptance: auto +08:00 date, remembered Name, transient Note, saved note sync, no manual date filter, best-effort attributed 30%-opacity static map overlay, secure URL/token settings.
  - Verify: flutter analyze/test/build; migration test for existing SQLite v1 database; manual camera/GPS/online/offline check.
- [ ] Windows admin and Excel export
  - Acceptance: connection string DPAPI-protected, test DB connection, filters and preview, token administration controls, styled XLSX export with maps links.
  - Verify: dotnet build/test/publish win-x64; test Excel file opens and data/formulas/links are correct; test DB connection on configured server.
- [ ] Documentation and final verification
  - Acceptance: README includes setup/publish path and token/API/mobile guidance; checklist covers requested scenarios.
  - Verify: commands match actual project; no secrets committed.

Checkpoint: no live SQL migration or token rotation until user confirms target database/API.
