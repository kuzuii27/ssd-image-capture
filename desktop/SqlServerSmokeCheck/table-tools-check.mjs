import { emptyColumnFilters, filterRows, sortRows } from '../PhotoCapture.Host/wwwroot/table-tools.js';
const photos = [
  { captureDate: '2026-10-21', captureTime: '10:00:00', name: 'Zulu', client: null, note: 'Crack', address: 'North', latitude: 10, longitude: 20 },
  { captureDate: '2026-10-20', captureTime: '11:00:00', name: 'alpha', client: 'Acme', note: null, address: 'South', latitude: 11, longitude: 21 },
];
if (filterRows(photos, { ...emptyColumnFilters(), client: 'acm' }).length !== 1) throw new Error('case-insensitive client filter failed');
if (filterRows(photos, { ...emptyColumnFilters(), date: '2026-10-20' }).length !== 1) throw new Error('date filter failed');
if (sortRows(photos, 'name', 'asc')[0].name !== 'alpha') throw new Error('ascending sort failed');
if (sortRows(photos, 'date', 'desc')[0].captureDate !== '2026-10-21') throw new Error('descending date sort failed');
console.log('PASS: column date/text filters and ascending/descending sorts');
