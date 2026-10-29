import 'dart:io';
import 'main.dart';
import 'package:camera/camera.dart';
import 'package:flutter/material.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

/// Step 3 of the lifecycle: the job itself. Reopenable while the task is not approved, so a rejected task can be fixed and resubmitted.
class TaskFormPage extends StatefulWidget {
  const TaskFormPage({required this.task, required this.store, super.key});
  final TaskRecord task;
  final LocalStore store;
  @override
  State<TaskFormPage> createState() => _TaskFormPageState();
}

class _TaskFormPageState extends State<TaskFormPage> {
  late final client = TextEditingController(text: widget.task.client ?? '');
  late final oldImei = TextEditingController(text: widget.task.oldImei ?? '');
  late final newImei = TextEditingController(text: widget.task.newImei ?? '');
  late final oldSim = TextEditingController(text: widget.task.oldSim ?? '');
  late final newSim = TextEditingController(text: widget.task.newSim ?? '');
  late final plate = TextEditingController(text: widget.task.plateNumber ?? '');
  late final note = TextEditingController(text: widget.task.note ?? '');
  late String? jobType = kActivities.contains(widget.task.jobType) ? widget.task.jobType : null;
  List<TaskPhotoRecord> photos = [];
  bool busy = false, saving = false;

  @override
  void initState() {
    super.initState();
    loadPhotos();
  }

  @override
  void dispose() {
    for (final c in [client, oldImei, newImei, oldSim, newSim, plate, note]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> loadPhotos() async {
    final r = await widget.store.taskPhotos(widget.task.id, kind: 'extra');
    if (mounted) setState(() => photos = r);
  }

  void message(String s) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(s)));

  /// Each work photo is committed to SQLite as soon as it is taken, so a crash mid-form cannot lose it.
  Future<void> addPhoto() async {
    if (busy) return;
    setState(() => busy = true);
    try {
      final cams = await availableCameras();
      if (cams.isEmpty) throw StateError('No camera available.');
      final camera = CameraController(cams.first, ResolutionPreset.max, enableAudio: false);
      await camera.initialize();
      if (!mounted) {
        await camera.dispose();
        return;
      }
      final shot = await Navigator.push<XFile>(context, MaterialPageRoute(builder: (_) => CameraPage(controller: camera)));
      if (shot == null || !mounted) return;
      final id = newId(), dir = Directory(p.join((await getApplicationSupportDirectory()).path, 'photos'));
      await dir.create(recursive: true);
      final path = p.join(dir.path, '$id.jpg');
      await File(shot.path).copy(path);
      await widget.store.insertTaskPhoto(TaskPhotoRecord(id: id, taskId: widget.task.id, imagePath: path, kind: 'extra', capturedAt: taskStamp().iso));
      await loadPhotos();
    } catch (e) {
      if (mounted) message('Photo failed: $e');
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> removePhoto(TaskPhotoRecord r) async {
    await widget.store.deleteTaskPhoto(r.id);
    final f = File(r.imagePath);
    if (await f.exists()) await f.delete();
    await loadPhotos();
  }

  Future<void> save() async {
    if (jobType == null || saving) return;
    if (client.text.trim().isEmpty) {
      message('Client is required.');
      return;
    }
    setState(() => saving = true);
    try {
      await widget.store.updateTask(widget.task.id, {
        'client': client.text.trim(),
        'job_type': jobType,
        'old_imei': oldImei.text.trim(),
        'new_imei': newImei.text.trim(),
        'old_sim': oldSim.text.trim(),
        'new_sim': newSim.text.trim(),
        'plate_number': plate.text.trim(),
        'note': note.text.trim(),
        // A rejected task is already complete; reopening the form must not send it back to job_saved and lose the time out.
        if (widget.task.status == 'at_client') 'status': 'job_saved',
        'updated_at': taskStamp().iso,
        'sync_status': 'Pending',
        'last_error': null,
      });
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) {
        setState(() => saving = false);
        message('Save failed: $e');
      }
    }
  }

  Widget field(TextEditingController c, String label, {int maxLines = 1}) => Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: TextField(controller: c, maxLines: maxLines, decoration: InputDecoration(labelText: label)),
      );

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Job details')),
        body: ListView(padding: const EdgeInsets.all(16), children: [
          if ((widget.task.reviewReason ?? '').isNotEmpty && widget.task.reviewStatus == 'rejected')
            Padding(
              padding: const EdgeInsets.only(bottom: 14),
              child: Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(color: const Color(0xFFFFE9E9), borderRadius: BorderRadius.circular(14)),
                child: Text('Rejected: ${widget.task.reviewReason}', style: const TextStyle(fontWeight: FontWeight.w600, color: Color(0xFF8A1C1C))),
              ),
            ),
          field(client, 'Client (required)'),
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: DropdownButtonFormField<String>(
              initialValue: jobType,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'Type of job (required)'),
              items: kActivities.map((a) => DropdownMenuItem(value: a, child: Text(a))).toList(),
              onChanged: (v) => setState(() => jobType = v),
            ),
          ),
          OutlinedButton.icon(onPressed: busy ? null : addPhoto, icon: const Icon(Icons.add_a_photo), label: const Text('Add photo')),
          if (photos.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: Wrap(
                spacing: 10,
                runSpacing: 10,
                children: photos
                    .map((r) => Stack(children: [
                          ClipRRect(
                            borderRadius: BorderRadius.circular(12),
                            child: File(r.imagePath).existsSync()
                                ? Image.file(File(r.imagePath), width: 92, height: 92, fit: BoxFit.cover)
                                : Container(width: 92, height: 92, color: const Color(0xFFEDEEF8), child: const Icon(Icons.broken_image, color: Color(0xFF5B5F8A))),
                          ),
                          Positioned(
                            top: -6,
                            right: -6,
                            child: IconButton(
                              icon: const Icon(Icons.cancel, color: Color(0xFF8A1C1C)),
                              onPressed: busy ? null : () => removePhoto(r),
                            ),
                          ),
                        ]))
                    .toList(),
              ),
            ),
          const Padding(padding: EdgeInsets.symmetric(vertical: 20), child: Divider(height: 1)),
          field(oldImei, 'OLD IMEI'),
          field(newImei, 'NEW IMEI'),
          field(oldSim, 'OLD SIM'),
          field(newSim, 'NEW SIM'),
          field(plate, 'Plate number'),
          field(note, 'Note', maxLines: 3),
          FilledButton(onPressed: jobType == null || saving ? null : save, child: const Text('Save')),
        ]),
      );
}
