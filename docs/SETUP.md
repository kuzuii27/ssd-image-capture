# SSD Photo Capture: setup and run

## What you get

- **PC app** (`SSDPhotoCapture.exe`, one installer): receives tasks and their photos from phones, stores them, and shows a local admin web page where you review each task, approve or reject it, edit its details, filter, and export to Excel. It runs in the notification area (tray) and starts with Windows.
- **Phone app** (Flutter): a technician records a task as a sequence of steps — time in, arrival at the client, the job form with its work photos, time out — offline, and syncs it when the server is reachable, on Wi-Fi or mobile data. A sync also brings back your approval or rejection.
- **Data** lives in one folder you choose: the SQL Server LocalDB files (`SSD_mobileCamera.mdf`, `SSD_mobileCamera_log.ldf`), `photos\` (JPEGs), `thumbs\`, `settings.json`, `logs\`. Nothing else needs installing by hand, except LocalDB itself (see below).

```
Phone --(HTTPS via Tailscale)--> PC app :5080 (phone API, token required)
Browser on the PC ------------> PC app :5081 (admin page, this PC only)
```

Both ports listen on the loopback address only. Phones reach the phone API through `tailscale serve`; the admin page and the access-token QR code are never exposed to the network.

## Install on a PC

1. Install **SQL Server Express LocalDB** (free, from Microsoft: search "SQL Server Express LocalDB download", or install via the SQL Server Express installer and choose LocalDB). The app needs this to store task and photo records; without it, uploads fail. It is not bundled in the installer.
2. Install **Tailscale** and sign in (the installer offers the download page if it is missing). Sign in to the same Tailscale account on each phone.
3. Run `SSDPhotoCapture-Setup-x.y.z.exe`. Choose the data folder when asked. On first run, the app creates the database inside that folder automatically.
4. The admin page opens in the browser. Go to **Settings**:
   - If Tailscale is connected, click **Set up phone link** (one time). This publishes only the phone API to your Tailscale network.
   - A **QR code** appears, containing the phone address and the access token.
5. On the phone, open the app, **Settings**, **Scan setup QR**, then **Save**. The app tests the connection for you.

If port 5080 or 5081 is already used by another program, the app tells you. Ports can be changed in `settings.json` (`ApiPort`, `AdminPort`) with the app closed; then run `tailscale serve --bg <ApiPort>` again.

## On the phone: one task, five steps

The home screen shows the technician name (typed once, then remembered), **[Sync]**, **[Add task]**, and a list of task cards. Each card shows its status, the client and job type once entered, date and time, the address, how many photos it holds, Pending or Synced, and the review result.

**Tapping a task performs the action for its current status** — there is no separate button per step. The only extra buttons are **[Finish]** at the end and **[Resubmit]** after a rejection.

1. **Time in.** **[Add task]** opens the camera for a selfie, then you tap **Save time in**. Location, date and time are saved with it. Status: **Timed in**. The task can sync straight away, even though nothing else is filled in.
2. **Client arrival.** Tapping a *Timed in* task asks "Are you in client?". **Yes** takes the **selfie first**, then asks for the **client name** (required), then saves. Status: **At client**. **No** just closes the popup and changes nothing. The time-in values and the client-arrival values are stored separately; neither overwrites the other.
3. **Job form.** Tapping an *At client* task opens the job form: client name (editable), **type of job** (required, chosen from the fixed 20-value activity list), **+ Add photo** for as many work photos as the job needs (each removable before you save), then optional OLD IMEI, NEW IMEI, OLD SIM, NEW SIM, Plate number and Note. **[Save] stays disabled until a job type is chosen.** Status: **Job saved**. The form can be reopened and edited for as long as the task is not finished.
4. **Finish.** Tapping a *Job saved* task offers **[Finish]**, which opens the camera for a **time out** selfie and saves its own location, date and time. Status: **Job complete**, awaiting review. Finish is unavailable until the job form has been saved, so steps cannot be skipped.
5. **Review result.** The card then shows **Pending review** (read-only), **Approved** (locked for good, read-only), or **Rejected** together with the admin's reason.

Completing a task is what puts it in front of the admin; there is no extra step to make it "pending".

**Syncing** works at any time, including for unfinished tasks — a half-filled task syncs with whatever it has, and later syncs update the same task on the server. A sync also **pulls back the review result**, so an approval or rejection appears without the technician doing anything. Offline, the last known review state stays on screen. Sync triggers are unchanged: every 2 minutes, on any connectivity change, and the manual **[Sync]** button.

### If a task is rejected

The reason the admin typed is shown on the card, and the **job form unlocks** so the technician can correct client, job type, photos, IMEIs, SIMs, plate number or note. **[Resubmit]** then sends it back for review — no new time-out selfie is taken, and the original time-out location, date and time are kept.

## On the PC: reviewing tasks

The admin page opens on the **Tasks** tab, which is now the default view. **Photos** is the legacy view of the old one-photo-per-record data: it is kept and still readable, but it has no review actions. **Settings** is unchanged.

- **Filter bar:** From, To, Technician, Client, Status, Review, Job type, and free-text search.
- **Pending review** quick view, with a **count badge**: one click filters to the tasks awaiting a decision. A freshly completed task arrives with no review state set at all, and the page treats "unset" and "pending" alike, so the badge counts every completed task nobody has decided on yet.
- **Opening a task** shows all three selfies side by side and labelled (Time in / At client / Time out), each with its own date, time, address and map link; the work photos as a thumbnail grid; an edit form for client, job type, OLD/NEW IMEI, OLD/NEW SIM, plate number and note; the review panel; and the two histories.
- **[Approve]** and **[Reject]** appear only for a task that is complete and awaiting a decision. **Rejecting requires a reason** (up to 500 characters). A decision can be reopened later — approved to rejected, or rejected to approved — behind **Change decision**, and every decision is recorded.
- **Every admin field edit is logged**: which field, the old value, the new value, who and when, shown in the task's history. The old photo-edit history never actually recorded anything; this one does.

### Who the reviewer is

There is no login. Each decision records the **Windows user name of the PC**, and the page shows it as "Reviewed on PC: *name*". Remote admins reach the page through the shared password, so every remote decision is recorded under that same PC user name. **It identifies the PC, not the person.** If you need to know which person decided, you have to track that outside the app.

## Excel export

- **Tasks tab:** one row per task — status, review status and reason, technician, client, job type, all three legs' date/time/location, the IMEIs, the SIMs, plate number, note and photo count. **Data only, no embedded images**, deliberately. There is also a one-click **export approved only**.
- **Photos tab (legacy):** unchanged, and still has its **Include photos in Excel** option.

## Deleting a task

Nothing is ever deleted automatically. Automatic cleanup is **disabled** for the SQL Server data path, so tasks, photos and histories are kept permanently — that is the chosen retention policy, not an oversight. Plan disk space accordingly and keep backups of the data folder.

To remove a record, delete it by hand: open the task on the **Tasks** tab, scroll to **Delete this task** and click **Delete task…**. A confirmation shows the client and how many photos will go. Deleting removes the task, every selfie and work photo (the files on disk as well as the records), and the task's review and edit history. **This cannot be undone** — export to Excel first if you need a record of it. Each deletion is written to `logs\app.log` with the PC user name.

There is no bulk delete and no delete on the phone: one task at a time, from the PC only.

## Moving to another PC

1. Close the app (tray icon, **Exit**) and copy the whole data folder to the new PC. This folder contains the database files too, so one copy carries everything: photos, tasks, review decisions, histories, settings, and the token.
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
- The admin page only accepts requests addressed to `localhost`, and edits need a custom header and a matching origin, which blocks other websites from driving it. Remote admin access over Tailscale is protected by the shared admin password.
- A single shared token is suitable for a small trusted team. For individual accountability, move to one token per phone or user.
- Admin field edits and review decisions are recorded with a timestamp and the PC's Windows user name. As above, that names the PC, not the person.
- Selfies, work photos, GPS positions and times are read-only on the PC; only the typed fields can be edited.
- Capture is offline-first, timestamps are recorded at +08:00, and photos are JPEG-compressed before upload.
- Back up the data folder; it holds everything, including the database.

## Test checklist: first real hardware test

The new task lifecycle has been checked by static analysis, a successful build, a unit test of the exact JSON the phone sends, and a clean launch on a real device — but **nobody has tapped through it yet**. The camera, GPS, the job form, Finish and Resubmit have never run against real hardware plus a real server. This checklist is that first pass.

If a step does not produce what it says, stop and report **the step number and exactly what you saw** (the screen, any message, the status on the card). Do not work around it.

1. **Time in, offline.** Turn the phone's Wi-Fi and mobile data off. Type your technician name, tap **[Add task]**, take the selfie, tap **Save time in**. *See:* a new card with status **Timed in**, today's date and time, an address, and **Pending**.
2. **Sync it.** Turn connectivity back on and tap **[Sync]**. *See:* the same card turns **Synced**, with no change to its status or its time-in date, time and address.
3. **Answer No.** Tap that card and answer **No** to "Are you in client?". *See:* the popup closes and the card is exactly as before — still **Timed in**, no client name, no new photo, photo count unchanged.
4. **Client selfie comes first.** Tap the card, answer **Yes**. *See:* the camera opens **before** anything is typed; only after the selfie are you asked for the client name.
5. **Blank client name is refused.** Leave the client name empty and try to save. *See:* it does not save and tells you the client name is required. Now type a real client name and save. *See:* status **At client**, the client name on the card, photo count 2.
6. **Save stays disabled without a job type.** Tap the *At client* card to open the job form. *See:* **[Save]** is greyed out. Edit the client name and type a note, still choosing no job type. *See:* **[Save]** is still greyed out.
7. **Work photos, including a removal.** Tap **+ Add photo** three times, then remove one of them before saving. *See:* the form shows exactly two work photos.
8. **Save the job.** Choose a job type from the dropdown. *See:* **[Save]** becomes available. Fill OLD IMEI, NEW IMEI, OLD SIM, NEW SIM, plate number and note, then save. *See:* status **Job saved**, the job type on the card, photo count 4 (two selfies plus two work photos).
9. **Reopen and edit.** Tap the *Job saved* card. *See:* the job form reopens with everything you typed still in it. Change the note, save, then reopen again. *See:* the new note is there.
10. **Finish is blocked before the job form is saved.** On a second task, get it as far as **At client** and look for **[Finish]**. *See:* no Finish button is offered; tapping the task opens the job form instead.
11. **Time out.** Back on the *Job saved* task, tap it, use **[Finish]** and take the time-out selfie. *See:* status **Job complete** / **Pending review**, photo count 5, and the time-out date, time and address are from this moment while the time-in and at-client values on the card are unchanged — each leg keeps its own.
12. **No GPS.** On another task, turn location off (or deny the permission) and take a leg's photo. *See:* a **"Save without location?"** confirmation. Confirm it. *See:* the leg saves with no address. If this was the time out, tap **[Sync]**. *See:* it still syncs, because the server requires only the time-out **timestamp**, not its location.
13. **Task in the admin page.** On the PC, open the admin page. *See:* it opens on the **Tasks** tab, and your task is listed with technician, client, job type and status.
14. **Task detail.** Open the task. *See:* all three selfies side by side and labelled Time in / At client / Time out, each with its own date, time, address and map link, plus your two work photos as thumbnails.
15. **Pending review badge.** Look at the **Pending review** quick view. *See:* its badge count includes your completed task, and one click filters the list to tasks awaiting a decision with your task among them.
16. **Approve.** Click **[Approve]**. *See:* the task shows Approved with "Reviewed on PC: *your Windows user name*", it drops out of the Pending review list, and the badge count falls by one.
17. **Approval reaches the phone.** On the phone, tap **[Sync]**. *See:* the card shows **Approved**, and tapping it opens nothing editable — it is read-only.
18. **Blank rejection is refused.** Take a second completed task, click **[Reject]** and leave the reason empty. *See:* the rejection is refused and asks for a reason.
19. **Reject with a reason.** Type a reason and reject. *See:* the task shows **Rejected** with that reason, and it drops out of the Pending review list.
20. **Rejection reaches the phone.** On the phone, tap **[Sync]**. *See:* the card shows **Rejected** with the admin's exact reason text.
21. **Fix and resubmit.** Tap the rejected card. *See:* the job form opens for editing. Change a field, save, then tap **[Resubmit]**. *See:* no camera opens, and the card returns to awaiting review.
22. **Resubmission returns to the PC.** Refresh the admin page. *See:* that task is awaiting a decision again, the **Pending review** badge counts it, the field you changed shows the new value, and the time-out date, time and location are still the originals.
23. **Field edits are logged.** On the PC, change a task's client name and job type and save. *See:* the task's history lists each field by name with its old value and its new value, the PC user name and a timestamp.
24. **Tasks Excel export.** Export from the Tasks tab and open the file. *See:* one row per task with status, review status and reason, technician, client, job type, all three legs' date/time/location, IMEIs, SIMs, plate number, note and photo count — and **no images anywhere in the sheet**. Then use the approved-only export. *See:* only approved tasks in that file.
25. **Legacy photos.** Open the **Photos** tab. *See:* the old one-photo-per-record entries are still listed and readable, and its export still offers **Include photos in Excel**.
26. **Filters.** Filter the Tasks tab by date range, technician, client, status, review and free text. *See:* the list narrows to matching tasks each time.
27. **Token still enforced.** Send an upload with a wrong or missing token. *See:* it is rejected (401).
28. **New token.** Click **Create new token**. *See:* the phone fails its connection test until it scans the new QR code.
29. **Data folder still carries everything.** Copy the data folder to another PC, install there, choose the folder. *See:* the tasks, all their photos, the review decisions and the histories are all present.
30. **Delete a task.** Pick a test task you no longer need, open it, scroll to **Delete this task** and click **Delete task…**. *See:* a confirmation naming the client and the photo count. Confirm it. *See:* the dialog closes, the task is gone from the list, and the Pending review badge drops by one if it was awaiting a decision. Check the data folder. *See:* that task's photo files are gone from `photos\` too, while every other task's photos are untouched.
