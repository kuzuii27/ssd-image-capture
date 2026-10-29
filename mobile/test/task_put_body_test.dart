import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:ssd_image_capture/main.dart';

/// Guards the PUT contract: camelCase keys, +08:00 stamps, and date/time matching capturedAt.
void main() {
  final s = taskStamp();
  test('timed_in body has no client and only the time-in group', () {
    final body = taskPutBody(TaskRecord(
      id: '11111111-1111-1111-1111-111111111111', technicianName: 'Juan Dela Cruz', status: 'timed_in',
      updatedAt: s.iso, syncStatus: 'Pending',
      timeinLatitude: 14.5547, timeinLongitude: 121.0244, timeinAccuracy: 8.3,
      timeinAddress: '12 Shaw Blvd, Mandaluyong, Metro Manila, Philippines',
      timeinCapturedAt: s.iso, timeinDate: s.date, timeinTime: s.time,
    ));
    print('timed_in => ${jsonEncode(body)}');
    expect(body.containsKey('client'), isFalse);
    expect(body.containsKey('clientLatitude'), isFalse);
    expect(body['timeInCapturedAt'], endsWith('+08:00'));
    expect(body['updatedAt'], endsWith('+08:00'));
    expect(body['timeInCapturedAt'].toString().substring(0, 10), body['timeInDate']);
    expect(body['timeInCapturedAt'].toString().substring(11, 19), body['timeInTime']);
    for (final k in body.keys) {
      expect(k.contains('_'), isFalse, reason: '$k must be camelCase');
    }
  });
  test('complete body carries all three legs and the job fields', () {
    final body = taskPutBody(TaskRecord(
      id: '22222222-2222-2222-2222-222222222222', technicianName: 'Juan Dela Cruz', status: 'complete',
      updatedAt: s.iso, syncStatus: 'Pending', client: 'Acme Logistics', jobType: 'Installation',
      oldImei: '350000000000001', newImei: '350000000000002', oldSim: '63900000001', newSim: '63900000002',
      plateNumber: 'NAC 1234', note: 'Unit mounted under the dash.',
      timeinLatitude: 14.5547, timeinLongitude: 121.0244, timeinAccuracy: 8.3, timeinAddress: 'Mandaluyong',
      timeinCapturedAt: s.iso, timeinDate: s.date, timeinTime: s.time,
      clientLatitude: 14.5600, clientLongitude: 121.0300, clientAccuracy: 6.1, clientAddress: 'Pasig',
      clientCapturedAt: s.iso, clientDate: s.date, clientTime: s.time,
      timeoutLatitude: 14.5610, timeoutLongitude: 121.0310, timeoutAccuracy: 5.4, timeoutAddress: 'Pasig',
      timeoutCapturedAt: s.iso, timeoutDate: s.date, timeoutTime: s.time,
    ));
    print('complete => ${jsonEncode(body)}');
    for (final k in ['timeOutLatitude', 'timeOutCapturedAt', 'timeOutDate', 'timeOutTime', 'plateNumber', 'jobType', 'oldImei']) {
      expect(body.containsKey(k), isTrue, reason: '$k missing');
    }
    for (final k in body.keys) {
      expect(k.contains('_'), isFalse, reason: '$k must be camelCase');
    }
  });
  test('a leg with a partial fix sends no coordinates at all', () {
    final body = taskPutBody(TaskRecord(
      id: '33333333-3333-3333-3333-333333333333', technicianName: 'T', status: 'timed_in',
      updatedAt: s.iso, syncStatus: 'Pending', timeinLatitude: 14.0,
      timeinCapturedAt: s.iso, timeinDate: s.date, timeinTime: s.time,
    ));
    expect(body.containsKey('timeInLatitude'), isFalse);
    expect(body.containsKey('timeInAccuracy'), isFalse);
  });
}
