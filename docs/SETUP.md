# SSD Photo Capture: setup and run

## What you get

- **PC app** (`SSDPhotoCapture.exe`, one installer): receives photos from phones, stores them, and shows a local admin web page with photos, filters, editing and Excel export. It runs in the notification area (tray) and starts with Windows.
- **Phone app** (Flutter): captures photos offline and syncs them when the server is reachable, on Wi-Fi or mobile data.
- **Data** lives in one folder you choose: the SQL Server LocalDB files (`SSD_mobileCamera.mdf`, `SSD_mobileCamera_log.ldf`), `photos\` (JPEGs), `thumbs\`, `settings.json`, `logs\`. Nothing else needs installing by hand, except LocalDB itself (see below).

```
Phone --(HTTPS via Tailscale)--> PC app :5080 (phone API, token required)
Browser on the PC ------------> PC app :5081 (admin page, this PC only)
```

Both ports listen on the loopback address only. Phones reach the phone API through `tailscale serve`; the admin page and the access-token QR code are never exposed to the network.

## Install on a PC

1. Install **SQL Server Express LocalDB** (free, from Microsoft: search "SQL Server Express LocalDB download", or install via the SQL Server Express installer and choose LocalDB). The app needs this to store photo records; without it, uploads fail.
2. Install **Tailscale** and sign in (the installer offers the download page if it is missing). Sign in to the same Tailscale account on each phone.
3. Run `SSDPhotoCapture-Setup-x.y.z.exe`. Choose the data folder when asked. On first run, the app creates the database inside that folder automatically.
4. The admin page opens in the browser. Go to **Settings**:
   - If Tailscale is connected, click **Set up phone link** (one time). This publishes only the phone API to your Tailscale network.
   - A **QR code** appears, containing the phone address and the access token.
5. On the phone, open the app, **Settings**, **Scan setup QR**, then **Save**. The app tests the connection for you.

If port 5080 or 5081 is already used by another program, the app tells you. Ports can be changed in `settings.json` (`ApiPort`, `AdminPort`) with the app closed; then run `tailscale serve --bg <ApiPort>` again.

## Automatic cleanup (optional, off by default)

Nothing is deleted unless you turn this on. Under **Settings**, **Automatic cleanup**, tick the box and choose the number of days (30 suggested): photos whose **capture date** is older than that are then deleted automatically.

- Checked at startup and every 6 hours. Saving the setting also runs it immediately, after a confirmation that shows how many photos will go.
- Removes the JPEG, its thumbnail, the database record and the edit history. This cannot be undone: export to Excel first if you need a copy.
- A photo that reached the PC in the last 24 hours is never deleted, so a phone that was offline for weeks does not lose its pictures as it syncs. They are removed on a later check.
- Each run that deletes something is written to `logs\app.log` in the data folder.
- Only the PC's copy is affected. Photos stored on a phone are not touched.

## Moving to another PC

1. Close the app (tray icon, **Exit**) and copy the whole data folder to the new PC. This folder now contains the database files too, so one copy carries everything: photos, records, settings, and the token.
2. Install **SQL Server Express LocalDB** on the new PC if it is not already there (see step 1 under "Install on a PC").
3. Install the app on the new PC and choose the copied folder in the installer (or use the tray menu, **Change data folder…**).
4. Existing data and the access token are kept, so phones keep working once the new PC has the same Tailscale address. A different PC name means a new address: scan the QR code again.

## Building the installer (developers)

Requires the .NET SDK and Inno Setup 6.

```
powershell -File desktop\build-installer.ps1
```

This publishes a self-contained app (target PCs need no .NET) and creates `desktop\installer\output\SSDPhotoCapture-Setup-1.0.0.exe`.

## Phone app (developers)

```
cd mobile
flutter pub get
flutter run
```

Grant camera and location permission. The server address must be HTTPS; Tailscale provides a trusted certificate.

## Security notes

- The access token is stored in `settings.json` in the data folder so the admin page can show the QR code again. Treat that folder like a password store. **Create new token** in the admin page signs out all phones.
- The admin page only accepts requests addressed to `localhost`, and edits need a custom header and a matching origin, which blocks other websites from driving it.
- A single shared token is suitable for a small trusted team. For individual accountability, move to one token per phone or user.
- Edits to Name, Client and Note are recorded in a history (who, when, old and new value). Photo, GPS and time are read-only.
- Back up the data folder; it holds everything, including the database.

## Test checklist

1. Capture offline; the item is Pending. Turn on Tailscale; it becomes Synced, on Wi-Fi and on mobile data.
2. The photo appears in the admin page with thumbnail, date/time, name, note, address and map link.
3. Edit Name, Client and Note; the change history lists each field.
4. Filter by date range, name, client and free text; export to Excel with and without photos.
5. Send an upload with a wrong or missing token; it is rejected (401).
6. Copy the data folder to another PC, install, choose the folder; all photos and edits are present.
7. Only if cleanup is switched on and set to 30 days, a photo captured more than 30 days ago (and received more than a day ago) disappears with its thumbnail and history; newer photos stay.
8. Create a new token; the old phone fails the connection test until it scans the new QR code.
