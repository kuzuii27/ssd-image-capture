// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.


import 'package:flutter_test/flutter_test.dart';

import 'package:ssd_image_capture/main.dart';

void main() {
  testWidgets('capture app renders its main controls', (WidgetTester tester) async {
      await tester.pumpWidget(const CaptureApp());
      expect(find.text('SSD Image Capture'), findsOneWidget);
      expect(find.text('Capture photo'), findsOneWidget);
      expect(find.text('Sync now'), findsOneWidget);
    });
}
