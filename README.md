# SSD Image Capture Sync

Offline-first photo capture for field technicians: Flutter stores records and originals locally, then uploads one image+metadata pair at a time to an ASP.NET Core API and SQL Server.

## Project layout

```
api/PhotoCapture.Api/       ASP.NET Core 8 API, SQL Server, EF Core migration
mobile/                     Flutter app
  lib/main.dart              Capture UI, local queue, location, sync
  android/app/src/main/      Android permissions and app configuration
  ios/Runner/                iOS location/camera usage descriptions
  pubspec.yaml
  analysis_options.yaml
docs/SETUP.md                Setup, network safety, and test plan
```

## Status

Project source is being prepared. See `docs/SETUP.md` for prerequisites and run instructions. Never expose SQL Server directly; only expose the HTTPS API over a trusted LAN or VPN.
