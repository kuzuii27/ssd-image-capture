import 'dart:async';
import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:mobile_scanner/mobile_scanner.dart';

/// Server address + access token. Returned to the caller on Save.
class ApiConfig {
  const ApiConfig(this.base, this.token);
  final String base, token;
}

/// Parses the admin page's setup QR: {"u": "https://host", "t": "token"}.
ApiConfig? parseSetupCode(String raw) {
  try {
    final data = jsonDecode(raw.trim());
    if (data is! Map) return null;
    final u = data['u'], t = data['t'];
    if (u is! String || t is! String || t.isEmpty) return null;
    final uri = Uri.tryParse(u);
    if (uri == null || uri.scheme != 'https' || uri.host.isEmpty) return null;
    return ApiConfig(u.replaceFirst(RegExp(r'/+$'), ''), t);
  } catch (_) {
    return null;
  }
}

enum ConnectionResult { ok, unreachable, wrongToken, badAddress }

Future<ConnectionResult> testConnection(String base, String token) async {
  final uri = Uri.tryParse('${base.trim().replaceFirst(RegExp(r'/+$'), '')}/api/health');
  if (uri == null || uri.scheme != 'https' || uri.host.isEmpty) return ConnectionResult.badAddress;
  try {
    final r = await http.get(uri, headers: {'Authorization': 'Bearer ${token.trim()}'}).timeout(const Duration(seconds: 10));
    if (r.statusCode != 200) return ConnectionResult.unreachable;
    final body = jsonDecode(r.body);
    return body is Map && body['authorized'] == true ? ConnectionResult.ok : ConnectionResult.wrongToken;
  } catch (_) {
    return ConnectionResult.unreachable;
  }
}

class ApiSettingsPage extends StatefulWidget {
  const ApiSettingsPage({required this.initial, super.key});
  final ApiConfig initial;
  @override
  State<ApiSettingsPage> createState() => _ApiSettingsPageState();
}

class _ApiSettingsPageState extends State<ApiSettingsPage> {
  late final base = TextEditingController(text: widget.initial.base);
  late final token = TextEditingController(text: widget.initial.token);
  bool showToken = false, testing = false;
  String? message;
  bool? good;

  @override
  void dispose() {
    base.dispose();
    token.dispose();
    super.dispose();
  }

  Future<void> scan() async {
    final raw = await Navigator.push<String>(context, MaterialPageRoute(builder: (_) => const _QrScanPage()));
    if (raw == null || !mounted) return;
    final cfg = parseSetupCode(raw);
    if (cfg == null) {
      setState(() { message = 'That QR code is not a setup code from the SSD admin page.'; good = false; });
      return;
    }
    base.text = cfg.base;
    token.text = cfg.token;
    await test();
  }

  Future<void> test() async {
    setState(() { testing = true; message = null; });
    final r = await testConnection(base.text, token.text);
    if (!mounted) return;
    setState(() {
      testing = false;
      good = r == ConnectionResult.ok;
      message = switch (r) {
        ConnectionResult.ok => 'Connected. Photos will sync to this server.',
        ConnectionResult.wrongToken => 'The server answered, but the token was rejected. Scan the QR code again.',
        ConnectionResult.badAddress => 'Use a full HTTPS address, for example https://pc-name.tailnet.ts.net',
        ConnectionResult.unreachable => 'Cannot reach the server. Check that Tailscale is on, and that SSD Photo Capture is running on the PC.',
      };
    });
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('Server settings')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        FilledButton.icon(onPressed: scan, icon: const Icon(Icons.qr_code_scanner), label: const Text('Scan setup QR')),
        const SizedBox(height: 6),
        Text('Open the PC admin page, then Settings, and scan the code shown there.', style: Theme.of(context).textTheme.bodySmall),
        const SizedBox(height: 20),
        TextField(controller: base, keyboardType: TextInputType.url, autocorrect: false, decoration: const InputDecoration(labelText: 'Server address (HTTPS)', border: OutlineInputBorder())),
        const SizedBox(height: 12),
        TextField(
          controller: token,
          obscureText: !showToken,
          autocorrect: false,
          enableSuggestions: false,
          decoration: InputDecoration(
            labelText: 'Access token',
            border: const OutlineInputBorder(),
            suffixIcon: IconButton(icon: Icon(showToken ? Icons.visibility_off : Icons.visibility), onPressed: () => setState(() => showToken = !showToken)),
          ),
        ),
        const SizedBox(height: 16),
        Row(children: [
          Expanded(child: OutlinedButton(onPressed: testing ? null : test, child: testing ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2)) : const Text('Test connection'))),
          const SizedBox(width: 12),
          Expanded(child: FilledButton(onPressed: () => Navigator.pop(context, ApiConfig(base.text.trim(), token.text.trim())), child: const Text('Save'))),
        ]),
        if (message != null) ...[
          const SizedBox(height: 14),
          Text(message!, style: TextStyle(color: good == true ? Colors.green.shade700 : scheme.error, fontWeight: FontWeight.w600)),
        ],
      ]),
    );
  }
}

class _QrScanPage extends StatefulWidget {
  const _QrScanPage();
  @override
  State<_QrScanPage> createState() => _QrScanPageState();
}

class _QrScanPageState extends State<_QrScanPage> {
  final controller = MobileScannerController(formats: const [BarcodeFormat.qrCode]);
  bool done = false;

  @override
  void dispose() {
    controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Scan setup QR')),
        body: Stack(children: [
          MobileScanner(
            controller: controller,
            onDetect: (capture) {
              if (done) return;
              final value = capture.barcodes.map((b) => b.rawValue).whereType<String>().firstOrNull;
              if (value == null) return;
              done = true;
              Navigator.pop(context, value);
            },
          ),
          const Align(alignment: Alignment.bottomCenter, child: Padding(padding: EdgeInsets.all(24), child: Text('Point the camera at the QR code on the PC', style: TextStyle(color: Colors.white, fontWeight: FontWeight.w600, backgroundColor: Colors.black54)))),
        ]),
      );
}
