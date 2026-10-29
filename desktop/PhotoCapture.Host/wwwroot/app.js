import { emptyColumnFilters, filterRows, sortRows } from './table-tools.js';
// All values from the database are inserted with textContent, never innerHTML.
const $ = (sel, root = document) => root.querySelector(sel);
const PAGE_SIZE = 25;
const state = { page: 1, total: 0, token: null, tokenShown: false, current: null, columnFilters: emptyColumnFilters(), sort: 'date', direction: 'desc' };

async function api(path, options = {}) {
  const headers = { 'X-Admin-Request': '1', ...(options.headers || {}) };
  if (options.json !== undefined) {
    headers['Content-Type'] = 'application/json';
    options.body = JSON.stringify(options.json);
  }
  const res = await fetch(path, { ...options, headers });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Request failed (${res.status})`);
  }
  const type = res.headers.get('content-type') || '';
  return type.includes('json') ? res.json() : res;
}

function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (k === 'class') node.className = v;
    else if (k === 'text') node.textContent = v;
    else if (k.startsWith('on')) node.addEventListener(k.slice(2), v);
    else node.setAttribute(k, v);
  }
  for (const c of children) if (c != null) node.append(c);
  return node;
}

function toast(message) {
  const t = $('#toast');
  t.textContent = message;
  t.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (t.hidden = true), 3500);
}

function formatBytes(n) {
  if (n < 1024) return `${n} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let i = -1;
  do { n /= 1024; i++; } while (n >= 1024 && i < units.length - 1);
  return `${n.toFixed(n < 10 ? 1 : 0)} ${units[i]}`;
}

function formatWhen(p) {
  const [y, m, d] = p.captureDate.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1, d)).toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' });
  const [hh, mm] = p.captureTime.split(':').map(Number);
  const hour12 = ((hh + 11) % 12) + 1;
  return `${date}, ${hour12}:${String(mm).padStart(2, '0')} ${hh < 12 ? 'AM' : 'PM'}`;
}

// ---------- tabs ----------
document.querySelectorAll('.tab').forEach((btn) => btn.addEventListener('click', () => {
  document.querySelectorAll('.tab').forEach((b) => b.classList.toggle('active', b === btn));
  $('#tab-tasks').hidden = btn.dataset.tab !== 'tasks';
  $('#tab-photos').hidden = btn.dataset.tab !== 'photos';
  $('#tab-setup').hidden = btn.dataset.tab !== 'setup';
  if (btn.dataset.tab === 'tasks') loadTasks();
  if (btn.dataset.tab === 'photos') loadPhotos();
  if (btn.dataset.tab === 'setup') { loadSettings(); loadRetention(); }
}));

// ---------- photos ----------
function filterParams() {
  const form = new FormData($('#filters'));
  const params = new URLSearchParams();
  for (const [k, v] of form) if (String(v).trim()) params.set(k, String(v).trim());
  for (const [k, v] of Object.entries(state.columnFilters)) if (v) params.set(`${k}Column`, v);
  params.set('sort', state.sort);
  params.set('direction', state.direction);
  return params;
}

async function loadPhotos() {
  const params = filterParams();
  params.set('page', state.page);
  params.set('pageSize', PAGE_SIZE);
  let data;
  try { data = await api(`/admin-api/photos?${params}`); }
  catch (e) { toast(e.message); return; }
  state.total = data.total;
  const body = $('#grid tbody');
  body.replaceChildren();
  const filtered = filterRows(data.items, state.columnFilters);
  const sorted = sortRows(filtered, state.sort, state.direction);
  for (const p of sorted) body.append(row(p));
  $('#empty').hidden = sorted.length > 0;
  const pages = Math.max(1, Math.ceil(data.total / PAGE_SIZE));
  $('#summary').textContent = `${data.total} photo${data.total === 1 ? '' : 's'}`;
  document.querySelectorAll('.column-sort').forEach(button => {
    const active = button.dataset.sort === state.sort;
    button.setAttribute('aria-sort', active ? (state.direction === 'asc' ? 'ascending' : 'descending') : 'none');
    button.querySelector('span').textContent = active ? (state.direction === 'asc' ? ' ▲' : ' ▼') : '';
  });
  $('#pageInfo').textContent = `Page ${state.page} of ${pages}`;
  $('#prev').disabled = state.page <= 1;
  $('#next').disabled = state.page >= pages;
}

function row(p) {
  const img = el('img', { class: 'thumb', src: p.thumbUrl, alt: `Photo by ${p.name}`, loading: 'lazy', width: 84, height: 62 });
  img.addEventListener('click', () => openDetail(p.id));
  const nameCell = el('td', { text: p.name });
  if (p.updatedAt) nameCell.append(el('span', { class: 'badge', text: 'edited' }));
  const location = el('td', { class: 'cell-addr' });
  if (p.address) location.append(el('div', { text: p.address }));
  location.append(el('a', { href: p.mapsUrl, target: '_blank', rel: 'noopener', text: `${p.latitude.toFixed(5)}, ${p.longitude.toFixed(5)}` }));
  return el('tr', {},
    el('td', {}, img),
    el('td', { text: formatWhen(p) }),
    nameCell,
    el('td', { text: p.client || '' }),
    el('td', { text: p.activity || '' }),
    el('td', {}, el('div', { class: 'cell-note', text: p.note || '' })),
    location,
    el('td', {}, el('button', { class: 'small', text: 'Open / edit', onclick: () => openDetail(p.id) })));
}

document.querySelectorAll('.column-filter').forEach(input => input.addEventListener('input', () => {
  state.columnFilters[input.dataset.column] = input.value.trim();
  state.page = 1;
  loadPhotos();
}));
document.querySelectorAll('.column-sort').forEach(button => button.addEventListener('click', () => {
  state.direction = state.sort === button.dataset.sort && state.direction === 'asc' ? 'desc' : 'asc';
  state.sort = button.dataset.sort;
  state.page = 1;
  loadPhotos();
}));
$('#filters').addEventListener('submit', (e) => { e.preventDefault(); state.page = 1; loadPhotos(); });
$('#clear').addEventListener('click', () => {
  $('#filters').reset();
  state.columnFilters = emptyColumnFilters();
  document.querySelectorAll('.column-filter').forEach(input => { input.value = ''; });
  state.page = 1;
  loadPhotos();
});
$('#refresh').addEventListener('click', loadPhotos);
$('#prev').addEventListener('click', () => { state.page--; loadPhotos(); });
$('#next').addEventListener('click', () => { state.page++; loadPhotos(); });
$('#export').addEventListener('click', async () => {
  const button = $('#export');
  button.disabled = true;
  button.textContent = 'Preparing…';
  try {
    const params = filterParams();
    if ($('#withImages').checked) params.set('images', 'true');
    const res = await api(`/admin-api/export.xlsx?${params}`);
    const blob = await res.blob();
    const disposition = res.headers.get('content-disposition') || '';
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(disposition);
    const a = el('a', { href: URL.createObjectURL(blob), download: match ? decodeURIComponent(match[1]) : 'photos.xlsx' });
    document.body.append(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 10000);
    toast('Excel file downloaded.');
  } catch (e) { toast(e.message); }
  finally { button.disabled = false; button.textContent = 'Export to Excel'; }
});

let activitiesLoaded = false;
let activityList = null;
async function activityOptions() {
  if (!activityList) activityList = await api('/admin-api/activities');
  return activityList;
}
async function ensureActivityOptions() {
  if (activitiesLoaded) return;
  try {
    const list = await activityOptions();
    const select = $('#editForm').elements.activity;
    for (const a of list) select.append(el('option', { value: a, text: a }));
    activitiesLoaded = true;
  } catch (e) { toast(e.message); }
}

// ---------- detail / edit ----------
const dialog = $('#detail');
async function openDetail(id) {
  await ensureActivityOptions();
  let data;
  try { data = await api(`/admin-api/photos/${id}`); }
  catch (e) { toast(e.message); return; }
  const p = data.photo;
  state.current = p;
  $('#dPhoto').src = p.imageUrl;
  $('#dPhotoLink').href = p.imageUrl;
  $('#dTitle').textContent = p.name;
  $('#dMeta').textContent = `${formatWhen(p)} · ${p.latitude.toFixed(5)}, ${p.longitude.toFixed(5)} (±${Math.round(p.accuracy)} m)${p.address ? ' · ' + p.address : ''}`;
  $('#dMap').href = p.mapsUrl;
  const form = $('#editForm');
  form.elements.name.value = p.name;
  form.elements.client.value = p.client || '';
  form.elements.activity.value = p.activity || '';
  form.elements.note.value = p.note || '';
  setSaveMessage('');
  renderHistory(data.history);
  if (!dialog.open) dialog.showModal();
}

function renderHistory(items) {
  const list = $('#history');
  list.replaceChildren();
  if (!items.length) { list.append(el('li', { class: 'muted', text: 'No edits yet.' })); return; }
  for (const h of items) {
    const when = new Date(h.editedAt).toLocaleString();
    list.append(el('li', {},
      el('span', { text: `${h.field}: “${h.oldValue ?? ''}” → “${h.newValue ?? ''}”` }),
      el('span', { class: 'muted', text: `${when} · ${h.editedBy}` })));
  }
}

function setSaveMessage(text, kind = '') {
  const m = $('#saveMsg');
  m.textContent = text;
  m.className = `small ${kind}`;
}

$('#editForm').addEventListener('submit', async (e) => {
  e.preventDefault();
  const f = e.target.elements;
  try {
    await api(`/admin-api/photos/${state.current.id}`, { method: 'PUT', json: { name: f.name.value, client: f.client.value, activity: f.activity.value, note: f.note.value } });
    setSaveMessage('Saved.', 'ok');
    const data = await api(`/admin-api/photos/${state.current.id}`);
    state.current = data.photo;
    $('#dTitle').textContent = data.photo.name;
    renderHistory(data.history);
    loadPhotos();
  } catch (err) { setSaveMessage(err.message, 'bad'); }
});

dialog.addEventListener('click', (e) => { if (e.target === dialog) dialog.close(); });

// ---------- tasks ----------
const taskState = { page: 1, current: null, busy: false, reviewer: '' };
const taskDialog = $('#taskDetail');
const TASK_PAGE_SIZE = 25;

function legWhen(leg) {
  return leg && leg.date && leg.time ? formatWhen({ captureDate: leg.date, captureTime: leg.time }) : '';
}

function reviewFace(t) {
  if (t.pendingReview) return { kind: 'warn', text: 'Awaiting review' };
  if (t.reviewStatus === 'approved') return { kind: 'ok', text: 'Approved' };
  if (t.reviewStatus === 'rejected') return { kind: 'bad', text: 'Rejected' };
  return { kind: '', text: 'Not submitted' };
}

function pill(kind, text) {
  return el('span', { class: 'pill' }, el('span', { class: `dot ${kind}` }), el('span', { text }));
}

function taskFilterParams() {
  const form = new FormData($('#taskFilters'));
  const params = new URLSearchParams();
  for (const [k, v] of form) if (String(v).trim()) params.set(k, String(v).trim());
  return params;
}

async function loadTasks() {
  let list;
  try {
    const select = $('#taskFilters').elements.jobType;
    if (select.options.length === 1) for (const a of await activityOptions()) select.append(el('option', { value: a, text: a }));
    const params = taskFilterParams();
    params.set('page', taskState.page);
    params.set('pageSize', TASK_PAGE_SIZE);
    list = await api(`/admin-api/tasks?${params}`);
  } catch (e) { toast(e.message); return; }
  const body = $('#taskGrid tbody');
  body.replaceChildren();
  for (const t of list.items) body.append(taskRow(t));
  $('#taskEmpty').hidden = list.items.length > 0;
  $('#taskSummary').textContent = `${list.total} task${list.total === 1 ? '' : 's'}`;
  const pages = Math.max(1, Math.ceil(list.total / TASK_PAGE_SIZE));
  $('#taskPageInfo').textContent = `Page ${list.page} of ${pages}`;
  $('#taskPrev').disabled = list.page <= 1;
  $('#taskNext').disabled = list.page >= pages;
  refreshPendingBadge();
}

async function refreshPendingBadge() {
  try {
    const s = await api('/admin-api/settings');
    taskState.reviewer = s.reviewerName || '';
    $('#pendingBadge').textContent = s.pendingReviewCount;
  } catch { /* badge only */ }
}

function taskRow(t) {
  const face = reviewFace(t);
  const thumb = t.thumbUrl
    ? el('img', { class: 'thumb', src: t.thumbUrl, alt: `Time-in selfie of ${t.technicianName}`, loading: 'lazy', width: 84, height: 62, onclick: () => openTask(t.id) })
    : el('div', { class: 'thumb' });
  return el('tr', {},
    el('td', {}, thumb),
    el('td', {}, pill(t.status === 'complete' ? 'ok' : 'warn', t.statusLabel || t.status || '')),
    el('td', {}, pill(face.kind, face.text)),
    el('td', { text: t.technicianName || '' }),
    el('td', { text: t.client || '' }),
    el('td', { text: t.jobType || '' }),
    el('td', { text: legWhen(t.timeIn) }),
    el('td', { text: String(t.photoCount) }),
    el('td', {}, el('button', { class: 'small', text: 'Open / review', onclick: () => openTask(t.id) })));
}

$('#taskFilters').addEventListener('submit', (e) => { e.preventDefault(); taskState.page = 1; loadTasks(); });
$('#taskClear').addEventListener('click', () => { $('#taskFilters').reset(); taskState.page = 1; loadTasks(); });
$('#taskRefresh').addEventListener('click', loadTasks);
$('#taskPrev').addEventListener('click', () => { taskState.page--; loadTasks(); });
$('#taskNext').addEventListener('click', () => { taskState.page++; loadTasks(); });
$('#taskExport').addEventListener('click', () => downloadTasks($('#taskExport'), taskFilterParams()));
$('#taskExportApproved').addEventListener('click', () => downloadTasks($('#taskExportApproved'), new URLSearchParams({ reviewStatus: 'approved' })));

async function downloadTasks(button, params) {
  const label = button.textContent;
  button.disabled = true;
  button.textContent = 'Preparing…';
  try {
    const res = await api(`/admin-api/tasks/export.xlsx?${params}`);
    const blob = await res.blob();
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(res.headers.get('content-disposition') || '');
    const a = el('a', { href: URL.createObjectURL(blob), download: match ? decodeURIComponent(match[1]) : 'tasks.xlsx' });
    document.body.append(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 10000);
    toast('Excel file downloaded.');
  } catch (e) { toast(e.message); }
  finally { button.disabled = false; button.textContent = label; }
}

$('#pendingView').addEventListener('click', () => {
  $('#taskFilters').reset();
  $('#taskFilters').elements.reviewStatus.value = 'awaiting';
  taskState.page = 1;
  loadTasks();
});

const SELFIE_LABELS = [['selfie_timein', 'Time in', 'timeIn'], ['selfie_client', 'At client', 'atClient'], ['selfie_timeout', 'Time out', 'timeOut']];

async function openTask(id) {
  let data;
  try { data = await api(`/admin-api/tasks/${id}`); }
  catch (e) { toast(e.message); return; }
  renderTask(data);
  if (!taskDialog.open) taskDialog.showModal();
}

function renderTask(data) {
  const t = data.task;
  taskState.current = t;
  $('#tDeleteMsg').textContent = '';
  $('#tTitle').textContent = t.technicianName || 'Task';
  const parts = [t.statusLabel || t.status, t.client || 'No client yet', legWhen(t.timeIn)].filter(Boolean);
  $('#tMeta').textContent = parts.join(' · ');

  const selfies = $('#tSelfies');
  selfies.replaceChildren();
  for (const [kind, label, legKey] of SELFIE_LABELS) {
    const photo = data.photos.find(p => p.kind === kind);
    const leg = t[legKey];
    const caption = el('figcaption', {}, el('div', { class: 'label', text: label }));
    caption.append(el('div', { text: legWhen(leg) || 'Not captured' }));
    if (leg && leg.address) caption.append(el('div', { class: 'muted', text: leg.address }));
    if (leg && leg.mapsUrl) caption.append(el('a', { href: leg.mapsUrl, target: '_blank', rel: 'noopener', text: 'Open map' }));
    const figure = el('figure', {});
    if (photo) figure.append(el('a', { href: photo.imageUrl, target: '_blank', rel: 'noopener' }, el('img', { src: photo.thumbUrl, alt: `${label} selfie`, loading: 'lazy' })));
    figure.append(caption);
    selfies.append(el('div', { class: 'selfie' }, figure));
  }

  const grid = $('#tWorkPhotos');
  grid.replaceChildren();
  const work = data.photos.filter(p => p.kind === 'extra');
  if (!work.length) grid.append(el('div', { class: 'muted small', text: 'No work photos.' }));
  for (const p of work) grid.append(el('a', { href: p.imageUrl, target: '_blank', rel: 'noopener' }, el('img', { src: p.thumbUrl, alt: 'Work photo', loading: 'lazy' })));

  const f = $('#taskEditForm').elements;
  const select = f.jobType;
  if (select.options.length === 1) activityOptions().then(list => { for (const a of list) select.append(el('option', { value: a, text: a })); select.value = t.jobType || ''; }).catch(() => {});
  f.client.value = t.client || '';
  f.jobType.value = t.jobType || '';
  f.oldImei.value = t.oldImei || '';
  f.newImei.value = t.newImei || '';
  f.oldSim.value = t.oldSim || '';
  f.newSim.value = t.newSim || '';
  f.plateNumber.value = t.plateNumber || '';
  f.note.value = t.note || '';
  setTaskSaveMessage('');

  renderReviewPanel(t);
  renderReviewHistory(data.reviews);
  renderEditHistory(data.edits);
}

function renderReviewPanel(t) {
  const face = reviewFace(t);
  const state = $('#tReviewState');
  state.replaceChildren(pill(face.kind, face.text));
  if (t.reviewReason) state.append(el('div', { class: 'small', text: `Reason: ${t.reviewReason}` }));
  $('#tReviewer').textContent = t.reviewedBy
    ? `Last decision on PC: ${t.reviewedBy}${t.reviewedAt ? ` · ${new Date(t.reviewedAt).toLocaleString()}` : ''}`
    : `Decisions are recorded against the PC user: ${taskState.reviewer}`;
  const reviewable = t.status === 'complete';
  const decided = reviewable && !t.pendingReview;
  $('#tReviewActions').hidden = !reviewable;
  $('#tChangeDecision').hidden = !decided;
  $('#tApprove').hidden = decided;
  $('#tReject').hidden = decided;
  $('#tReasonWrap').hidden = decided;
  $('#tReason').value = '';
  setReviewMessage('');
  $('#tReviewNote').textContent = reviewable ? '' : 'This task can be reviewed once the technician has timed out.';
}

function renderReviewHistory(items) {
  const list = $('#tReviewHistory');
  list.replaceChildren();
  if (!items.length) { list.append(el('li', { class: 'muted', text: 'No decisions yet.' })); return; }
  for (const r of items) {
    const li = el('li', {}, el('span', { text: r.reason ? `${r.decision}: ${r.reason}` : r.decision }));
    li.append(el('span', { class: 'muted', text: `${new Date(r.reviewedAt).toLocaleString()} · ${r.reviewedBy}` }));
    list.append(li);
  }
}

function renderEditHistory(items) {
  const list = $('#tEditHistory');
  list.replaceChildren();
  if (!items.length) { list.append(el('li', { class: 'muted', text: 'No edits yet.' })); return; }
  for (const h of items) {
    list.append(el('li', {},
      el('span', { text: `${h.field}: “${h.oldValue ?? ''}” → “${h.newValue ?? ''}”` }),
      el('span', { class: 'muted', text: `${new Date(h.editedAt).toLocaleString()} · ${h.editedBy}` })));
  }
}

function setTaskSaveMessage(text, kind = '') {
  const m = $('#taskSaveMsg');
  m.textContent = text;
  m.className = `small ${kind}`;
}
function setReviewMessage(text, kind = '') {
  const m = $('#tReviewMsg');
  m.textContent = text;
  m.className = `small ${kind}`;
}

$('#taskEditForm').addEventListener('submit', async (e) => {
  e.preventDefault();
  const f = e.target.elements;
  // The server replaces all eight fields, so every one is sent, blank included.
  const body = {
    client: f.client.value, jobType: f.jobType.value, oldImei: f.oldImei.value, newImei: f.newImei.value,
    oldSim: f.oldSim.value, newSim: f.newSim.value, plateNumber: f.plateNumber.value, note: f.note.value,
  };
  try {
    await api(`/admin-api/tasks/${taskState.current.id}`, { method: 'PUT', json: body });
    renderTask(await api(`/admin-api/tasks/${taskState.current.id}`));
    setTaskSaveMessage('Saved.', 'ok');
    loadTasks();
  } catch (err) { setTaskSaveMessage(err.message, 'bad'); }
});

async function decide(decision, reason) {
  if (taskState.busy) return;
  taskState.busy = true;
  const buttons = [$('#tApprove'), $('#tReject'), $('#tChangeDecision')];
  buttons.forEach(b => (b.disabled = true));
  const id = taskState.current.id;
  try {
    await api(`/admin-api/tasks/${id}/${decision}`, { method: 'POST', json: decision === 'reject' ? { reason } : {} });
    renderTask(await api(`/admin-api/tasks/${id}`));
    setReviewMessage(decision === 'approve' ? 'Approved.' : 'Rejected.', 'ok');
    loadTasks();
  } catch (e) { setReviewMessage(e.message, 'bad'); }
  finally { taskState.busy = false; buttons.forEach(b => (b.disabled = false)); }
}

$('#tApprove').addEventListener('click', () => decide('approve'));
$('#tReject').addEventListener('click', () => {
  const reason = $('#tReason').value.trim();
  if (!reason) { setReviewMessage('A reason is required to reject.', 'bad'); $('#tReason').focus(); return; }
  decide('reject', reason);
});
$('#tChangeDecision').addEventListener('click', () => {
  $('#tChangeDecision').hidden = true;
  $('#tApprove').hidden = false;
  $('#tReject').hidden = false;
  $('#tReasonWrap').hidden = false;
});

$('#tDelete').addEventListener('click', async () => {
  const task = taskState.current;
  if (!task) return;
  const who = task.client || task.technicianName || 'this task';
  const count = task.photoCount ?? 0;
  // Typed confirmation: a misclick here destroys a field visit that cannot be recaptured.
  if (!confirm(`Delete the task for ${who} and its ${count} photo(s)?\n\nThe task, every selfie and work photo, and its review and edit history are removed for good. This cannot be undone.`)) return;
  const button = $('#tDelete');
  button.disabled = true;
  try {
    const r = await api(`/admin-api/tasks/${task.id}`, { method: 'DELETE' });
    taskDialog.close();
    toast(`Task deleted with ${r.photosDeleted} photo(s).`);
    loadTasks();
    loadSettings();
  } catch (e) {
    $('#tDeleteMsg').textContent = e.message;
    $('#tDeleteMsg').className = 'small bad';
  } finally { button.disabled = false; }
});
taskDialog.addEventListener('click', (e) => { if (e.target === taskDialog) taskDialog.close(); });

// ---------- phone setup ----------
async function loadSettings() {
  let s;
  try { s = await api('/admin-api/settings'); } catch (e) { toast(e.message); return; }
  state.token = s.token;
  renderToken();
  $('#phoneUrl').textContent = s.phoneUrl || 'Not available yet. See Tailscale.';
  $('#tokenDate').textContent = new Date(s.tokenCreatedAt).toLocaleString();
  $('#retEnabled').disabled = true;
  $('#retDays').disabled = true;
  $('#retSave').disabled = true;
  $('#retInfo').textContent = 'Automatic cleanup is disabled for the read-only SQL Server connection.';
  $('#dataFolder').textContent = s.dataFolder;
  $('#photoCount').textContent = s.photoCount;
  $('#taskCount').textContent = s.taskCount;
  $('#pendingCount').textContent = s.pendingReviewCount;
  $('#reviewerName').textContent = s.reviewerName || '-';
  $('#pendingBadge').textContent = s.pendingReviewCount;
  $('#dbSize').textContent = formatBytes(s.dbSizeBytes);
  $('#version').textContent = s.version || '';

  const ts = s.tailscale;
  const status = $('#tsStatus');
  status.replaceChildren();
  const line = (kind, text) => status.append(el('div', { class: 'pill' }, el('span', { class: `dot ${kind}` }), el('span', { text })));
  line(ts.installed ? 'ok' : 'bad', ts.installed ? 'Tailscale installed' : 'Tailscale not installed');
  if (ts.installed) line(ts.running ? 'ok' : 'warn', ts.running ? `Connected as ${ts.dnsName}` : 'Not connected');
  if (ts.running) line(ts.serveConfigured ? 'ok' : 'warn', ts.serveConfigured ? 'Phone link is active' : 'Phone link not set up yet');
  $('#tsServe').hidden = !(ts.running && !ts.serveConfigured);
  $('#tsMsg').textContent = ts.message || (!ts.installed ? 'Install Tailscale from tailscale.com/download, sign in, then reopen this page.' : '');

  $('#adminPasswordMsg').textContent = s.hasAdminPassword ? 'A password is set.' : 'No password set yet — remote access stays blocked until you set one.';
  $('#tsServeAdmin').hidden = !(ts.running && s.hasAdminPassword && !ts.adminServeConfigured);
  $('#remoteAdminBox').hidden = !(ts.running && ts.adminServeConfigured && s.remoteAdminUrl);
  $('#remoteAdminUrl').textContent = s.remoteAdminUrl || '-';
  $('#tsAdminMsg').textContent = !s.hasAdminPassword ? '' : (ts.adminServeConfigured ? 'Remote access is on. Sign in with the admin password at the URL above.' : '');

  const box = $('#qrBox');
  box.replaceChildren();
  if (s.phoneUrl && ts.serveConfigured) {
    box.append(el('img', { src: `/admin-api/setup-qr.svg?t=${Date.now()}`, alt: 'Setup QR code' }));
  } else {
    box.append(el('div', { class: 'muted small', text: 'The QR code appears once Tailscale is connected and the phone link is active.' }));
  }
}

function renderToken() {
  $('#tokenText').textContent = state.tokenShown ? state.token : '••••••••••••••••';
  $('#toggleToken').textContent = state.tokenShown ? 'Hide' : 'Show';
}
$('#toggleToken').addEventListener('click', () => { state.tokenShown = !state.tokenShown; renderToken(); });
$('#copyToken').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText(state.token); toast('Token copied.'); }
  catch { toast('Copy failed. Show the token and copy it by hand.'); }
});
$('#regen').addEventListener('click', async () => {
  if (!confirm('Create a new token?\n\nEvery phone stops syncing until it scans the new QR code.')) return;
  try { await api('/admin-api/settings/regenerate-token', { method: 'POST' }); toast('New token created.'); loadSettings(); }
  catch (e) { toast(e.message); }
});
$('#tsServe').addEventListener('click', async () => {
  const b = $('#tsServe');
  b.disabled = true;
  try {
    const r = await api('/admin-api/settings/tailscale-serve', { method: 'POST' });
    toast(r.ok ? 'Phone link is ready.' : r.message);
    $('#tsMsg').textContent = r.ok ? '' : r.message;
  } catch (e) { toast(e.message); }
  finally { b.disabled = false; loadSettings(); }
});

$('#setAdminPassword').addEventListener('click', async () => {
  const pw = $('#adminPassword').value;
  if (pw.length < 8) { $('#adminPasswordMsg').textContent = 'Password must be at least 8 characters.'; return; }
  try {
    await api('/admin-api/settings/admin-password', { method: 'POST', json: { password: pw } });
    $('#adminPassword').value = '';
    toast('Admin password set.');
    loadSettings();
  } catch (e) { $('#adminPasswordMsg').textContent = e.message; }
});

$('#tsServeAdmin').addEventListener('click', async () => {
  const b = $('#tsServeAdmin');
  b.disabled = true;
  try {
    const r = await api('/admin-api/settings/tailscale-serve-admin', { method: 'POST' });
    toast(r.ok ? 'Remote access is on.' : r.message);
    $('#tsAdminMsg').textContent = r.ok ? '' : r.message;
  } catch (e) { toast(e.message); }
  finally { b.disabled = false; loadSettings(); }
});

// ---------- automatic cleanup ----------
function showRetention(r) {
  $("#retEnabled").checked = r.enabled;
  $("#retDays").value = r.days;
  const info = $("#retInfo");
  info.className = "small";
  if (!r.enabled) { info.textContent = "Cleanup is off. Photos are kept until you remove them."; return; }
  const last = r.lastRun ? " Last run: " + new Date(r.lastRun).toLocaleString() + ", " + r.lastDeleted + " deleted." : "";
  info.textContent = r.dueCount > 0
    ? r.dueCount + " photo(s) captured before " + r.cutoff + " are due for deletion." + last
    : "Nothing is due for deletion (cutoff " + r.cutoff + ")." + last;
}
async function loadRetention() {
  try { showRetention(await api("/admin-api/retention")); } catch (e) { toast(e.message); }
}
$("#retDays").addEventListener("input", async () => {
  const days = Number($("#retDays").value);
  if (!Number.isInteger(days) || days < 1 || days > 3650) return;
  try {
    const r = await api("/admin-api/retention?days=" + days);
    const info = $("#retInfo");
    info.className = "small";
    info.textContent = "With " + days + " days, " + r.dueCount + " photo(s) captured before " + r.cutoff + " would be deleted.";
  } catch { /* preview only */ }
});
$("#retSave").addEventListener("click", async () => {
  const enabled = $("#retEnabled").checked;
  const days = Number($("#retDays").value);
  if (!Number.isInteger(days) || days < 1 || days > 3650) { toast("Enter a whole number of days from 1 to 3650."); return; }
  if (enabled) {
    let due = 0;
    try { due = (await api("/admin-api/retention?days=" + days)).dueCount; } catch (e) { toast(e.message); return; }
    const warning = due > 0
      ? due + " photo(s) are already older than " + days + " days and will be deleted right now. This cannot be undone."
      : "No photos are affected right now.";
    if (!confirm("Delete photos older than " + days + " days automatically?\n\n" + warning)) return;
  }
  try {
    const r = await api("/admin-api/retention", { method: "PUT", json: { enabled, days } });
    showRetention(r.status);
    toast(r.deleted > 0 ? r.deleted + " old photo(s) deleted." : "Cleanup setting saved.");
    loadPhotos();
  } catch (e) { toast(e.message); }
});

loadTasks();
setInterval(() => {
  if (!dialog.open && !$('#tab-photos').hidden && state.page === 1) loadPhotos();
  if (!taskDialog.open && !$('#tab-tasks').hidden && taskState.page === 1) loadTasks();
}, 30000);

if (location.hash === "#setup") document.querySelector(".tab[data-tab=setup]").click();
if (location.hash === "#photos") document.querySelector(".tab[data-tab=photos]").click();
