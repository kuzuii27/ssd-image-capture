export const emptyColumnFilters = () => ({ date: '', name: '', client: '', activity: '', note: '', location: '' });
export function filterRows(rows, filters) {
  const includes = (value, query) => !query || String(value ?? '').toLocaleLowerCase().includes(query.trim().toLocaleLowerCase());
  return rows.filter(p => (!filters.date || p.captureDate === filters.date) && includes(p.name, filters.name) && includes(p.client, filters.client) && includes(p.activity, filters.activity) && includes(p.note, filters.note) && includes(`${p.address ?? ''} ${p.latitude} ${p.longitude}`, filters.location));
}
export function sortRows(rows, column, direction) {
  const value = p => column === 'date' ? `${p.captureDate}T${p.captureTime}` : column === 'location' ? (p.address ?? `${p.latitude},${p.longitude}`).toLocaleLowerCase() : String(p[column] ?? '').toLocaleLowerCase();
  const factor = direction === 'asc' ? 1 : -1;
  return [...rows].sort((a, b) => factor * (value(a) < value(b) ? -1 : value(a) > value(b) ? 1 : 0));
}

