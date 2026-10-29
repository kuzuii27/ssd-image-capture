import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'api_settings_page.dart';
import 'task_form_page.dart';
import 'package:camera/camera.dart';
import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show rootBundle;
import 'package:flutter_image_compress/flutter_image_compress.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:geocoding/geocoding.dart';
import 'package:geolocator/geolocator.dart';
import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';
import 'package:intl/intl.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'package:sqflite/sqflite.dart';
import 'package:uuid/uuid.dart';

const _uuid=Uuid(), _secure=FlutterSecureStorage(), _offset=Duration(hours:8);
const kActivities=['Installation','Check up','Installation/Check up','Upgrade','Pullout/Transfer','Collection with OR','Errand','Transfer','Pull out','Meeting','Dispatch','Check up/Recalibrate','Collection without OR','Demo Installation','Re-install','Change device','Office','Migrate','Replacement','Platform training'];
const kStatusLabels={'timed_in':'Timed in','at_client':'At client','job_saved':'Job saved'};
String newId()=>_uuid.v4();
/// One wall-clock reading in the fixed +08:00 field timezone, in the three shapes the server cross-validates.
({String iso,String date,String time}) taskStamp(){final t=DateTime.now().toUtc().add(_offset);return (iso:'${DateFormat("yyyy-MM-dd'T'HH:mm:ss").format(t)}+08:00',date:DateFormat('yyyy-MM-dd').format(t),time:DateFormat('HH:mm:ss').format(t));}
String displayStamp(String? iso)=>iso==null||iso.isEmpty?'':DateFormat('dd MMM yyyy, h:mm a').format(DateTime.parse(iso));
Future<void> main() async { WidgetsFlutterBinding.ensureInitialized(); SecurityContext.defaultContext.setTrustedCertificatesBytes((await rootBundle.load('assets/ca.pem')).buffer.asUint8List()); runApp(const CaptureApp()); }
class CaptureApp extends StatelessWidget { const CaptureApp({super.key}); @override Widget build(BuildContext context)=>MaterialApp(title:'SSD Image Capture',theme:ThemeData(
  colorSchemeSeed:const Color(0xFF00008B),useMaterial3:true,scaffoldBackgroundColor:const Color(0xFFF2F3FA),
  appBarTheme:const AppBarTheme(backgroundColor:Color(0xFFF2F3FA),foregroundColor:Color(0xFF10123A),elevation:0,centerTitle:false,titleTextStyle:TextStyle(fontSize:20,fontWeight:FontWeight.w700,color:Color(0xFF10123A),letterSpacing:-.2)),
  filledButtonTheme:FilledButtonThemeData(style:FilledButton.styleFrom(backgroundColor:const Color(0xFF00008B),foregroundColor:Colors.white,padding:const EdgeInsets.symmetric(vertical:14),shape:RoundedRectangleBorder(borderRadius:BorderRadius.circular(14)),textStyle:const TextStyle(fontWeight:FontWeight.w600,fontSize:16))),
  outlinedButtonTheme:OutlinedButtonThemeData(style:OutlinedButton.styleFrom(foregroundColor:const Color(0xFF00008B),side:const BorderSide(color:Color(0xFF00008B),width:1.3),padding:const EdgeInsets.symmetric(vertical:14),shape:RoundedRectangleBorder(borderRadius:BorderRadius.circular(14)),textStyle:const TextStyle(fontWeight:FontWeight.w600,fontSize:16))),
  inputDecorationTheme:InputDecorationTheme(filled:true,fillColor:Colors.white,contentPadding:const EdgeInsets.symmetric(horizontal:16,vertical:14),border:OutlineInputBorder(borderRadius:BorderRadius.circular(14),borderSide:BorderSide(color:Colors.grey.shade300)),enabledBorder:OutlineInputBorder(borderRadius:BorderRadius.circular(14),borderSide:BorderSide(color:Colors.grey.shade300)),focusedBorder:OutlineInputBorder(borderRadius:BorderRadius.circular(14),borderSide:const BorderSide(color:Color(0xFF00008B),width:1.6))),
  cardTheme:CardThemeData(elevation:0,color:Colors.white,shape:RoundedRectangleBorder(borderRadius:BorderRadius.circular(16)),margin:const EdgeInsets.symmetric(vertical:6)),
  dialogTheme:DialogThemeData(shape:RoundedRectangleBorder(borderRadius:BorderRadius.circular(20)),backgroundColor:Colors.white),
),home:const CaptureHome()); }
class PhotoRecord {
 const PhotoRecord({required this.id,required this.name,required this.client,required this.activity,required this.latitude,required this.longitude,required this.accuracy,required this.capturedAt,required this.captureDate,required this.captureTime,required this.imagePath,required this.note,required this.address,this.status='Pending',this.retryCount=0,this.lastError});
 final String id,name,client,activity,capturedAt,captureDate,captureTime,imagePath,note,address,status; final double latitude,longitude,accuracy; final int retryCount; final String? lastError;
 String get displayTime=>DateFormat('dd MMM yyyy, h:mm a').format(DateTime.parse(capturedAt).toUtc().add(_offset));
 Map<String,Object?> toMap()=>{'id':id,'name':name,'client':client,'activity':activity,'latitude':latitude,'longitude':longitude,'accuracy':accuracy,'captured_at':capturedAt,'capture_date':captureDate,'capture_time':captureTime,'image_path':imagePath,'note':note,'address':address,'sync_status':status,'retry_count':retryCount,'last_error':lastError};
 factory PhotoRecord.fromMap(Map<String,Object?> m)=>PhotoRecord(id:m['id']! as String,name:m['name']! as String,client:m['client'] as String? ?? '',activity:m['activity'] as String? ?? '',latitude:(m['latitude']! as num).toDouble(),longitude:(m['longitude']! as num).toDouble(),accuracy:(m['accuracy']! as num).toDouble(),capturedAt:m['captured_at']! as String,captureDate:m['capture_date']! as String,captureTime:m['capture_time']! as String,imagePath:m['image_path']! as String,note:m['note'] as String? ?? '',address:m['address'] as String? ?? '',status:m['sync_status']! as String,retryCount:m['retry_count']! as int,lastError:m['last_error'] as String?);
}
class TaskRecord {
 const TaskRecord({required this.id,required this.technicianName,required this.status,required this.updatedAt,required this.syncStatus,this.client,this.jobType,this.oldImei,this.newImei,this.oldSim,this.newSim,this.plateNumber,this.note,this.timeinSelfiePath,this.timeinLatitude,this.timeinLongitude,this.timeinAccuracy,this.timeinAddress,this.timeinCapturedAt,this.timeinDate,this.timeinTime,this.clientSelfiePath,this.clientLatitude,this.clientLongitude,this.clientAccuracy,this.clientAddress,this.clientCapturedAt,this.clientDate,this.clientTime,this.timeoutSelfiePath,this.timeoutLatitude,this.timeoutLongitude,this.timeoutAccuracy,this.timeoutAddress,this.timeoutCapturedAt,this.timeoutDate,this.timeoutTime,this.reviewStatus='none',this.reviewReason,this.reviewedAt,this.retryCount=0,this.lastError});
 final String id,technicianName,status,updatedAt,syncStatus,reviewStatus; final int retryCount;
 final String? client,jobType,oldImei,newImei,oldSim,newSim,plateNumber,note,reviewReason,reviewedAt,lastError;
 final String? timeinSelfiePath,timeinAddress,timeinCapturedAt,timeinDate,timeinTime; final double? timeinLatitude,timeinLongitude,timeinAccuracy;
 final String? clientSelfiePath,clientAddress,clientCapturedAt,clientDate,clientTime; final double? clientLatitude,clientLongitude,clientAccuracy;
 final String? timeoutSelfiePath,timeoutAddress,timeoutCapturedAt,timeoutDate,timeoutTime; final double? timeoutLatitude,timeoutLongitude,timeoutAccuracy;
 /// Once the technician has timed out, the admin decision is what the card reports.
 String get label=>status!='complete'?kStatusLabels[status]??status:switch(reviewStatus){'approved'=>'Approved','rejected'=>'Rejected','resubmit_pending'=>'Resubmitting',_=>'Pending review'};
 bool get editable=>reviewStatus=='rejected'||status!='complete';
 bool get canFinish=>status=='job_saved'&&(jobType??'').isNotEmpty;
 Map<String,Object?> toMap()=>{'id':id,'technician_name':technicianName,'status':status,'client':client,'job_type':jobType,'old_imei':oldImei,'new_imei':newImei,'old_sim':oldSim,'new_sim':newSim,'plate_number':plateNumber,'note':note,
  'timein_selfie_path':timeinSelfiePath,'timein_latitude':timeinLatitude,'timein_longitude':timeinLongitude,'timein_accuracy':timeinAccuracy,'timein_address':timeinAddress,'timein_captured_at':timeinCapturedAt,'timein_date':timeinDate,'timein_time':timeinTime,
  'client_selfie_path':clientSelfiePath,'client_latitude':clientLatitude,'client_longitude':clientLongitude,'client_accuracy':clientAccuracy,'client_address':clientAddress,'client_captured_at':clientCapturedAt,'client_date':clientDate,'client_time':clientTime,
  'timeout_selfie_path':timeoutSelfiePath,'timeout_latitude':timeoutLatitude,'timeout_longitude':timeoutLongitude,'timeout_accuracy':timeoutAccuracy,'timeout_address':timeoutAddress,'timeout_captured_at':timeoutCapturedAt,'timeout_date':timeoutDate,'timeout_time':timeoutTime,
  'review_status':reviewStatus,'review_reason':reviewReason,'reviewed_at':reviewedAt,'updated_at':updatedAt,'sync_status':syncStatus,'retry_count':retryCount,'last_error':lastError};
 factory TaskRecord.fromMap(Map<String,Object?> m){double? d(String k)=>(m[k] as num?)?.toDouble();String? s(String k)=>m[k] as String?;return TaskRecord(id:m['id']! as String,technicianName:m['technician_name']! as String,status:m['status']! as String,updatedAt:m['updated_at']! as String,syncStatus:m['sync_status']! as String,
  client:s('client'),jobType:s('job_type'),oldImei:s('old_imei'),newImei:s('new_imei'),oldSim:s('old_sim'),newSim:s('new_sim'),plateNumber:s('plate_number'),note:s('note'),
  timeinSelfiePath:s('timein_selfie_path'),timeinLatitude:d('timein_latitude'),timeinLongitude:d('timein_longitude'),timeinAccuracy:d('timein_accuracy'),timeinAddress:s('timein_address'),timeinCapturedAt:s('timein_captured_at'),timeinDate:s('timein_date'),timeinTime:s('timein_time'),
  clientSelfiePath:s('client_selfie_path'),clientLatitude:d('client_latitude'),clientLongitude:d('client_longitude'),clientAccuracy:d('client_accuracy'),clientAddress:s('client_address'),clientCapturedAt:s('client_captured_at'),clientDate:s('client_date'),clientTime:s('client_time'),
  timeoutSelfiePath:s('timeout_selfie_path'),timeoutLatitude:d('timeout_latitude'),timeoutLongitude:d('timeout_longitude'),timeoutAccuracy:d('timeout_accuracy'),timeoutAddress:s('timeout_address'),timeoutCapturedAt:s('timeout_captured_at'),timeoutDate:s('timeout_date'),timeoutTime:s('timeout_time'),
  reviewStatus:s('review_status')??'none',reviewReason:s('review_reason'),reviewedAt:s('reviewed_at'),retryCount:(m['retry_count'] as num?)?.toInt()??0,lastError:s('last_error'));}
}
class TaskPhotoRecord {
 const TaskPhotoRecord({required this.id,required this.taskId,required this.imagePath,required this.kind,required this.capturedAt,this.status='Pending'});
 final String id,taskId,imagePath,kind,capturedAt,status;
 Map<String,Object?> toMap()=>{'id':id,'task_id':taskId,'image_path':imagePath,'kind':kind,'captured_at':capturedAt,'sync_status':status};
 factory TaskPhotoRecord.fromMap(Map<String,Object?> m)=>TaskPhotoRecord(id:m['id']! as String,taskId:m['task_id']! as String,imagePath:m['image_path']! as String,kind:m['kind']! as String,capturedAt:m['captured_at']! as String,status:m['sync_status']! as String);
}
const _createTasks="CREATE TABLE tasks (id TEXT PRIMARY KEY,technician_name TEXT NOT NULL,status TEXT NOT NULL,client TEXT,job_type TEXT,old_imei TEXT,new_imei TEXT,old_sim TEXT,new_sim TEXT,plate_number TEXT,note TEXT,timein_selfie_path TEXT,timein_latitude REAL,timein_longitude REAL,timein_accuracy REAL,timein_address TEXT,timein_captured_at TEXT,timein_date TEXT,timein_time TEXT,client_selfie_path TEXT,client_latitude REAL,client_longitude REAL,client_accuracy REAL,client_address TEXT,client_captured_at TEXT,client_date TEXT,client_time TEXT,timeout_selfie_path TEXT,timeout_latitude REAL,timeout_longitude REAL,timeout_accuracy REAL,timeout_address TEXT,timeout_captured_at TEXT,timeout_date TEXT,timeout_time TEXT,review_status TEXT NOT NULL DEFAULT 'none',review_reason TEXT,reviewed_at TEXT,updated_at TEXT NOT NULL,sync_status TEXT NOT NULL,retry_count INTEGER NOT NULL DEFAULT 0,last_error TEXT)";
const _createTaskPhotos="CREATE TABLE task_photos (id TEXT PRIMARY KEY,task_id TEXT NOT NULL,image_path TEXT NOT NULL,kind TEXT NOT NULL,captured_at TEXT NOT NULL,sync_status TEXT NOT NULL)";
const _createTaskPhotoIndex="CREATE INDEX idx_task_photos_task ON task_photos (task_id)";
class LocalStore {
 Database? _database;
 Future<Database> get db async=>_database??=await openDatabase(p.join((await getApplicationSupportDirectory()).path,'photos.db'),version:6,
   onCreate:(d,_) async{await d.execute("CREATE TABLE photos (id TEXT PRIMARY KEY,name TEXT NOT NULL,client TEXT NOT NULL DEFAULT '',activity TEXT NOT NULL DEFAULT '',latitude REAL NOT NULL,longitude REAL NOT NULL,accuracy REAL NOT NULL,captured_at TEXT NOT NULL,capture_date TEXT NOT NULL,capture_time TEXT NOT NULL,image_path TEXT NOT NULL,note TEXT NOT NULL DEFAULT '',address TEXT NOT NULL DEFAULT '',sync_status TEXT NOT NULL,retry_count INTEGER NOT NULL DEFAULT 0,last_error TEXT)");await d.execute(_createTasks);await d.execute(_createTaskPhotos);await d.execute(_createTaskPhotoIndex);},
   onUpgrade:(d,oldV,newV) async{if(oldV<2)await d.execute("ALTER TABLE photos ADD COLUMN note TEXT NOT NULL DEFAULT ''");if(oldV<3)await d.execute("ALTER TABLE photos ADD COLUMN address TEXT NOT NULL DEFAULT ''");if(oldV<4)await d.execute("ALTER TABLE photos ADD COLUMN client TEXT NOT NULL DEFAULT ''");if(oldV<5)await d.execute("ALTER TABLE photos ADD COLUMN activity TEXT NOT NULL DEFAULT ''");if(oldV<6){await d.execute(_createTasks);await d.execute(_createTaskPhotos);await d.execute(_createTaskPhotoIndex);}});
 Future<void> save(PhotoRecord r) async=>(await db).insert('photos',r.toMap());
 Future<List<PhotoRecord>> list() async=>(await (await db).query('photos',orderBy:'captured_at DESC')).map(PhotoRecord.fromMap).toList();
 Future<void> mark(PhotoRecord r,String s,{String? error,bool increment=false}) async=>(await db).update('photos',{'sync_status':s,'last_error':error,if(increment)'retry_count':r.retryCount+1},where:'id = ?',whereArgs:[r.id]);
 Future<void> insertTask(TaskRecord t) async=>(await db).insert('tasks',t.toMap());
 Future<void> updateTask(String id,Map<String,Object?> values) async=>(await db).update('tasks',values,where:'id = ?',whereArgs:[id]);
 Future<List<TaskRecord>> listTasks() async=>(await (await db).query('tasks',orderBy:'timein_captured_at DESC')).map(TaskRecord.fromMap).toList();
 Future<TaskRecord?> getTask(String id) async{final r=await (await db).query('tasks',where:'id = ?',whereArgs:[id],limit:1);return r.isEmpty?null:TaskRecord.fromMap(r.first);}
 Future<void> markTask(String id,String s,{String? error,int? retryCount}) async=>(await db).update('tasks',{'sync_status':s,'last_error':error,if(retryCount!=null)'retry_count':retryCount},where:'id = ?',whereArgs:[id]);
 Future<void> applyReview(String id,String reviewStatus,String? reason,String? reviewedAt) async=>(await db).update('tasks',{'review_status':reviewStatus,'review_reason':reason,'reviewed_at':reviewedAt},where:'id = ?',whereArgs:[id]);
 Future<void> insertTaskPhoto(TaskPhotoRecord r) async=>(await db).insert('task_photos',r.toMap());
 Future<List<TaskPhotoRecord>> taskPhotos(String taskId,{String? kind}) async=>(await (await db).query('task_photos',where:kind==null?'task_id = ?':'task_id = ? AND kind = ?',whereArgs:kind==null?[taskId]:[taskId,kind],orderBy:'captured_at')).map(TaskPhotoRecord.fromMap).toList();
 Future<int> taskPhotoCount(String taskId) async=>Sqflite.firstIntValue(await (await db).rawQuery('SELECT COUNT(*) FROM task_photos WHERE task_id = ?',[taskId]))??0;
 Future<Map<String,int>> taskPhotoCounts() async=>{for(final r in await (await db).rawQuery('SELECT task_id, COUNT(*) AS c FROM task_photos GROUP BY task_id')) r['task_id']! as String:(r['c']! as num).toInt()};
 Future<void> deleteTaskPhoto(String id) async=>(await db).delete('task_photos',where:'id = ?',whereArgs:[id]);
 Future<List<TaskPhotoRecord>> unsyncedTaskPhotos() async=>(await (await db).query('task_photos',where:"sync_status <> 'Synced'",orderBy:'captured_at')).map(TaskPhotoRecord.fromMap).toList();
 Future<void> markTaskPhoto(String id,String s) async=>(await db).update('task_photos',{'sync_status':s},where:'id = ?',whereArgs:[id]);
}
class ApiSettings {
 Future<String> get base async=>await _secure.read(key:'api_base')??''; Future<String> get token async=>await _secure.read(key:'api_token')??'';
 Future<void> save(String b,String t) async {final u=Uri.tryParse(b);if(u==null||u.scheme!='https'||u.host.isEmpty)throw const FormatException('Use a valid HTTPS API URL.');await _secure.write(key:'api_base',value:b.replaceFirst(RegExp(r'/+$'),''));await _secure.write(key:'api_token',value:t);}
}
/// camelCase body for PUT /api/tasks/{id}. Each location triple and timestamp group is sent whole or omitted, which is what the server validates.
Map<String,Object?> taskPutBody(TaskRecord t)=>{'id':t.id,'technicianName':t.technicianName,'status':t.status,'updatedAt':t.updatedAt,
 if((t.client??'').isNotEmpty)'client':t.client,if((t.jobType??'').isNotEmpty)'jobType':t.jobType,
 if((t.oldImei??'').isNotEmpty)'oldImei':t.oldImei,if((t.newImei??'').isNotEmpty)'newImei':t.newImei,
 if((t.oldSim??'').isNotEmpty)'oldSim':t.oldSim,if((t.newSim??'').isNotEmpty)'newSim':t.newSim,
 if((t.plateNumber??'').isNotEmpty)'plateNumber':t.plateNumber,if((t.note??'').isNotEmpty)'note':t.note,
 if(t.timeinLatitude!=null&&t.timeinLongitude!=null&&t.timeinAccuracy!=null)...{'timeInLatitude':t.timeinLatitude,'timeInLongitude':t.timeinLongitude,'timeInAccuracy':t.timeinAccuracy},
 if((t.timeinAddress??'').isNotEmpty)'timeInAddress':t.timeinAddress,
 if(t.timeinCapturedAt!=null)...{'timeInCapturedAt':t.timeinCapturedAt,'timeInDate':t.timeinDate,'timeInTime':t.timeinTime},
 if(t.clientLatitude!=null&&t.clientLongitude!=null&&t.clientAccuracy!=null)...{'clientLatitude':t.clientLatitude,'clientLongitude':t.clientLongitude,'clientAccuracy':t.clientAccuracy},
 if((t.clientAddress??'').isNotEmpty)'clientAddress':t.clientAddress,
 if(t.clientCapturedAt!=null)...{'clientCapturedAt':t.clientCapturedAt,'clientDate':t.clientDate,'clientTime':t.clientTime},
 if(t.timeoutLatitude!=null&&t.timeoutLongitude!=null&&t.timeoutAccuracy!=null)...{'timeOutLatitude':t.timeoutLatitude,'timeOutLongitude':t.timeoutLongitude,'timeOutAccuracy':t.timeoutAccuracy},
 if((t.timeoutAddress??'').isNotEmpty)'timeOutAddress':t.timeoutAddress,
 if(t.timeoutCapturedAt!=null)...{'timeOutCapturedAt':t.timeoutCapturedAt,'timeOutDate':t.timeoutDate,'timeOutTime':t.timeoutTime}};
class SyncService {
 SyncService(this.store,this.settings);
 final LocalStore store; final ApiSettings settings; bool running=false;
 String? lastError;
 Future<void> sync({required VoidCallback refresh,required void Function(String,double) progress}) async {
  if(running)return; running=true; lastError=null;
  try {
   final base=await settings.base,token=await settings.token;
   if(base.isEmpty||token.isEmpty)throw StateError('Set HTTPS API URL and token in Settings.');
   await _syncPhotos(base,token,refresh,progress);
   await _syncTasks(base,token,refresh);
   await _syncTaskPhotos(base,token,refresh,progress);
   await _syncResubmits(base,token,refresh);
   await _pullReviews(base,token,refresh);
  } catch(e) { lastError=e.toString(); }
  finally {running=false;}
 }
 Future<void> _syncPhotos(String base,String token,VoidCallback refresh,void Function(String,double) progress) async {
  for(final r in (await store.list()).where((x)=>x.status!='Synced')){
   XFile? small;
   try {
    if(!await File(r.imagePath).exists())throw const FileSystemException('Original image missing.');
    small=await FlutterImageCompress.compressAndGetFile(r.imagePath,p.join((await getTemporaryDirectory()).path,'${r.id}.jpg'),minWidth:1920,minHeight:1920,quality:80,format:CompressFormat.jpeg);
    if(small==null)throw const FormatException('JPEG compression failed.');
    final q=http.MultipartRequest('POST',Uri.parse('$base/api/photos'))..headers['Authorization']='Bearer $token';
    q.fields.addAll({'id':r.id,'name':r.name,'client':r.client,'activity':r.activity,'note':r.note,'address':r.address,'latitude':r.latitude.toString(),'longitude':r.longitude.toString(),'accuracy':r.accuracy.toString(),'captured_at':r.capturedAt,'capture_date':r.captureDate,'capture_time':r.captureTime});
    final smallPath=small.path;final length=await small.length();
    q.files.add(http.MultipartFile('image',http.ByteStream(File(smallPath).openRead().transform(StreamTransformer.fromHandlers(handleData:(d,s){progress(r.id,(d.length/length).clamp(0,1));s.add(d);}))),length,filename:'${r.id}.jpg',contentType:MediaType('image','jpeg')));
    final response=await http.Response.fromStream(await q.send().timeout(const Duration(minutes:3)));
    if(response.statusCode!=200&&response.statusCode!=201)throw HttpException('API ${response.statusCode}: ${response.body}');
    final result=jsonDecode(response.body);
    if(result is! Map || result['status']!='saved'&&result['status']!='already-saved')throw const FormatException('Server did not confirm that the photo was saved.');
    await store.mark(r,'Synced');
   } catch(e){lastError=e.toString();await store.mark(r,'Pending',error:e.toString(),increment:true);}
   finally{if(small!=null)await File(small.path).delete();refresh();}
  }
 }
 Future<void> _syncTasks(String base,String token,VoidCallback refresh) async {
  for(final t in (await store.listTasks()).where((x)=>x.syncStatus!='Synced')){
   try {
    final response=await http.put(Uri.parse('$base/api/tasks/${t.id}'),headers:{'Authorization':'Bearer $token','Content-Type':'application/json'},body:jsonEncode(taskPutBody(t))).timeout(const Duration(minutes:2));
    // A 400 is a validation refusal, so resending the same body would loop forever; it stays Pending with the reason recorded.
    if(response.statusCode==400){lastError=response.body;await store.markTask(t.id,'Pending',error:response.body,retryCount:t.retryCount+1);continue;}
    if(response.statusCode!=200)throw HttpException('API ${response.statusCode}: ${response.body}');
    final result=jsonDecode(response.body);
    if(result is! Map)throw const FormatException('Server did not confirm that the task was saved.');
    final state=result['status'];
    if(state!='saved'&&state!='ignored-stale'&&state!='locked')throw const FormatException('Server did not confirm that the task was saved.');
    // locked means an admin approved it, so the phone must lock down even before the review pull runs.
    if(state=='locked')await store.applyReview(t.id,'approved',null,t.reviewedAt);
    await store.markTask(t.id,'Synced');
   } catch(e){lastError=e.toString();await store.markTask(t.id,'Pending',error:e.toString(),retryCount:t.retryCount+1);}
   finally{refresh();}
  }
 }
 Future<void> _syncTaskPhotos(String base,String token,VoidCallback refresh,void Function(String,double) progress) async {
  for(final r in await store.unsyncedTaskPhotos()){
   XFile? small;
   try {
    if(!await File(r.imagePath).exists())throw const FileSystemException('Original image missing.');
    small=await FlutterImageCompress.compressAndGetFile(r.imagePath,p.join((await getTemporaryDirectory()).path,'${r.id}.jpg'),minWidth:1920,minHeight:1920,quality:80,format:CompressFormat.jpeg);
    if(small==null)throw const FormatException('JPEG compression failed.');
    final q=http.MultipartRequest('POST',Uri.parse('$base/api/tasks/${r.taskId}/photos'))..headers['Authorization']='Bearer $token';
    q.fields.addAll({'id':r.id,'kind':r.kind,'captured_at':r.capturedAt});
    final smallPath=small.path;final length=await small.length();
    q.files.add(http.MultipartFile('image',http.ByteStream(File(smallPath).openRead().transform(StreamTransformer.fromHandlers(handleData:(d,s){progress(r.id,(d.length/length).clamp(0,1));s.add(d);}))),length,filename:'${r.id}.jpg',contentType:MediaType('image','jpeg')));
    final response=await http.Response.fromStream(await q.send().timeout(const Duration(minutes:3)));
    if(response.statusCode!=200)throw HttpException('API ${response.statusCode}: ${response.body}');
    final result=jsonDecode(response.body);
    if(result is! Map || result['status']!='saved'&&result['status']!='already-saved')throw const FormatException('Server did not confirm that the photo was saved.');
    await store.markTaskPhoto(r.id,'Synced');
   } catch(e){lastError=e.toString();}
   finally{if(small!=null)await File(small.path).delete();refresh();}
  }
 }
 Future<void> _syncResubmits(String base,String token,VoidCallback refresh) async {
  for(final t in (await store.listTasks()).where((x)=>x.reviewStatus=='resubmit_pending')){
   try {
    final response=await http.post(Uri.parse('$base/api/tasks/${t.id}/resubmit'),headers:{'Authorization':'Bearer $token'}).timeout(const Duration(minutes:1));
    // 409 means the admin state already moved on, so the pull below is the truth and retrying forever would be pointless. 404 waits for the PUT to land.
    if(response.statusCode==200||response.statusCode==409)await store.applyReview(t.id,'pending',null,t.reviewedAt);
    else if(response.statusCode!=404)throw HttpException('API ${response.statusCode}: ${response.body}');
   } catch(e){lastError=e.toString();}
   finally{refresh();}
  }
 }
 Future<void> _pullReviews(String base,String token,VoidCallback refresh) async {
  final local=(await store.listTasks()).where((x)=>x.status=='complete').toList();
  if(local.isEmpty)return;
  for(var i=0;i<local.length;i+=500){
   final chunk=local.sublist(i,i+500>local.length?local.length:i+500);
   try {
    final response=await http.post(Uri.parse('$base/api/tasks/review-states'),headers:{'Authorization':'Bearer $token','Content-Type':'application/json'},body:jsonEncode({'ids':chunk.map((x)=>x.id).toList()})).timeout(const Duration(minutes:1));
    if(response.statusCode!=200)throw HttpException('API ${response.statusCode}: ${response.body}');
    final rows=jsonDecode(response.body);
    if(rows is! List)throw const FormatException('Unexpected review-states response.');
    final byId={for(final row in rows.whereType<Map>()) '${row['id']}'.toLowerCase():row};
    for(final t in chunk){
     final row=byId[t.id.toLowerCase()];
     // An absent id only means the server has not got this task yet, so the last known state stays. A queued resubmit also keeps its local flag.
     if(row==null||t.reviewStatus=='resubmit_pending')continue;
     await store.applyReview(t.id,row['reviewStatus'] as String? ??'none',row['reviewReason'] as String?,row['reviewedAt'] as String?);
    }
   } catch(e){lastError=e.toString();}
   finally{refresh();}
  }
 }
}
class CaptureHome extends StatefulWidget {const CaptureHome({super.key});@override State<CaptureHome> createState()=>_CaptureHomeState();}
class _CaptureHomeState extends State<CaptureHome>{final store=LocalStore(),settings=ApiSettings(),name=TextEditingController();late final sync=SyncService(store,settings);List<TaskRecord> rows=[];Map<String,int> counts={};int legacyPending=0;String status='';bool busy=false;Timer? timer;
 @override void initState(){super.initState();load();loadName();WidgetsBinding.instance.addPostFrameCallback((_)async{if((await settings.base).isEmpty&&mounted)showSettings();});timer=Timer.periodic(const Duration(minutes:2),(_)=>syncOnline());Connectivity().onConnectivityChanged.listen((_)=>syncOnline());}
 Future<void> loadName()async{name.text=await _secure.read(key:'last_name')??'';if(mounted)setState((){});}
 Future<void> load()async{final r=await store.listTasks(),c=await store.taskPhotoCounts();final legacy=await store.list();if(mounted)setState((){rows=r;counts=c;legacyPending=legacy.where((x)=>x.status!='Synced').length;});}
 Future<void> syncOnline()async{if((await Connectivity().checkConnectivity()).any((x)=>x!=ConnectivityResult.none))await syncNow();}
 Future<Position?> getLocation()async{var x=await Geolocator.checkPermission();if(x==LocationPermission.denied)x=await Geolocator.requestPermission();if(x==LocationPermission.denied||x==LocationPermission.deniedForever||!await Geolocator.isLocationServiceEnabled())return null;try{return await Geolocator.getCurrentPosition(locationSettings:const LocationSettings(accuracy:LocationAccuracy.high,timeLimit:Duration(seconds:15)));}on TimeoutException{return null;}}
 Future<String> reverseGeocode(Position? pos)async{if(pos==null)return '';try{final marks=await Geocoding().placemarkFromCoordinates(pos.latitude,pos.longitude).timeout(const Duration(seconds:10));if(marks.isEmpty)return '';final m=marks.first;return [m.street,m.subLocality,m.locality,m.administrativeArea,m.postalCode,m.country].where((s)=>s!=null&&s.trim().isNotEmpty).join(', ');}catch(_){return '';}}
 Future<String?> askClient({String? initial})async{final n=TextEditingController(text:initial??'');return showDialog<String>(context:context,barrierDismissible:false,builder:(c)=>AlertDialog(title:const Text('Client name'),content:TextField(controller:n,autofocus:true,decoration:const InputDecoration(labelText:'Client (required)',border:OutlineInputBorder())),actions:[FilledButton(onPressed:(){if(n.text.trim().isEmpty)return;Navigator.pop(c,n.text.trim());},child:const Text('Save'))]));}
 Future<bool?> confirm(String title,String body,String yes,{String no='Cancel'})=>showDialog<bool>(context:context,builder:(c)=>AlertDialog(title:Text(title),content:Text(body),actions:[TextButton(onPressed:()=>Navigator.pop(c,false),child:Text(no)),FilledButton(onPressed:()=>Navigator.pop(c,true),child:Text(yes))]));
 /// A fresh fix plus matching stamps for one leg of the lifecycle. Null means the technician declined to save without location.
 Future<({double? lat,double? lng,double? acc,String address,String iso,String date,String time})?> captureLeg()async{final pos=await getLocation();if(pos==null){if(!mounted)return null;if(await confirm('Location unavailable','GPS is unavailable or permission denied. Save without location?','Save without location')!=true)return null;}final address=await reverseGeocode(pos);final t=taskStamp();return (lat:pos?.latitude,lng:pos?.longitude,acc:pos?.accuracy,address:address,iso:t.iso,date:t.date,time:t.time);}
 Future<XFile?> shoot()async{final cams=await availableCameras();if(cams.isEmpty)throw StateError('No camera available.');final camera=CameraController(cams.first,ResolutionPreset.max,enableAudio:false);await camera.initialize();if(!mounted){await camera.dispose();return null;}return Navigator.push<XFile>(context,MaterialPageRoute(builder:(_)=>CameraPage(controller:camera)));}
 Future<String> storeImage(XFile shot,String photoId)async{final dir=Directory(p.join((await getApplicationSupportDirectory()).path,'photos'));await dir.create(recursive:true);final path=p.join(dir.path,'$photoId.jpg');await File(shot.path).copy(path);return path;}
 Future<void> addTask()async{
  if(name.text.trim().isEmpty){message('Enter your name before capturing.');return;}
  if(busy)return;setState(()=>busy=true);
  try{
   final shot=await shoot();if(shot==null||!mounted)return;
   if(await confirm('Time in','Save this selfie as your time in?','Save time in')!=true||!mounted)return;
   final leg=await captureLeg();if(leg==null||!mounted)return;
   final taskId=newId(),photoId=newId(),path=await storeImage(shot,photoId);
   await store.insertTask(TaskRecord(id:taskId,technicianName:name.text.trim(),status:'timed_in',updatedAt:leg.iso,syncStatus:'Pending',timeinSelfiePath:path,timeinLatitude:leg.lat,timeinLongitude:leg.lng,timeinAccuracy:leg.acc,timeinAddress:leg.address,timeinCapturedAt:leg.iso,timeinDate:leg.date,timeinTime:leg.time));
   await store.insertTaskPhoto(TaskPhotoRecord(id:photoId,taskId:taskId,imagePath:path,kind:'selfie_timein',capturedAt:leg.iso));
   await _secure.write(key:'last_name',value:name.text.trim());
   status='Timed in · ${displayStamp(leg.iso)}';
   await load();syncOnline();
  }catch(e){if(mounted)message('Time in failed: $e');}
  finally{if(mounted)setState(()=>busy=false);}
 }
 Future<void> arriveAtClient(TaskRecord t)async{
  if(await confirm('Client arrival','Are you in client?','Yes',no:'No')!=true||!mounted)return;
  if(busy)return;setState(()=>busy=true);
  try{
   final shot=await shoot();if(shot==null||!mounted)return;
   final leg=await captureLeg();if(leg==null||!mounted)return;
   final client=await askClient(initial:t.client);if(client==null||!mounted)return;
   final photoId=newId(),path=await storeImage(shot,photoId);
   await store.updateTask(t.id,{'status':'at_client','client':client,'client_selfie_path':path,'client_latitude':leg.lat,'client_longitude':leg.lng,'client_accuracy':leg.acc,'client_address':leg.address,'client_captured_at':leg.iso,'client_date':leg.date,'client_time':leg.time,'updated_at':taskStamp().iso,'sync_status':'Pending','last_error':null});
   await store.insertTaskPhoto(TaskPhotoRecord(id:photoId,taskId:t.id,imagePath:path,kind:'selfie_client',capturedAt:leg.iso));
   status='At client · $client';
   await load();syncOnline();
  }catch(e){if(mounted)message('Client arrival failed: $e');}
  finally{if(mounted)setState(()=>busy=false);}
 }
 Future<void> finishTask(TaskRecord t)async{
  if(!t.canFinish){message('Save the job form first.');return;}
  if(busy)return;setState(()=>busy=true);
  try{
   final shot=await shoot();if(shot==null||!mounted)return;
   final leg=await captureLeg();if(leg==null||!mounted)return;
   final photoId=newId(),path=await storeImage(shot,photoId);
   await store.updateTask(t.id,{'status':'complete','timeout_selfie_path':path,'timeout_latitude':leg.lat,'timeout_longitude':leg.lng,'timeout_accuracy':leg.acc,'timeout_address':leg.address,'timeout_captured_at':leg.iso,'timeout_date':leg.date,'timeout_time':leg.time,'updated_at':taskStamp().iso,'sync_status':'Pending','last_error':null});
   await store.insertTaskPhoto(TaskPhotoRecord(id:photoId,taskId:t.id,imagePath:path,kind:'selfie_timeout',capturedAt:leg.iso));
   status='Timed out · ${displayStamp(leg.iso)}';
   await load();syncOnline();
  }catch(e){if(mounted)message('Time out failed: $e');}
  finally{if(mounted)setState(()=>busy=false);}
 }
 Future<void> resubmit(TaskRecord t)async{await store.updateTask(t.id,{'review_status':'resubmit_pending','review_reason':null,'updated_at':taskStamp().iso,'sync_status':'Pending','last_error':null});status='Sent back for review';await load();syncOnline();}
 Future<void> openTask(TaskRecord t)async{
  if(t.status=='timed_in'){await arriveAtClient(t);return;}
  if(!t.editable){message(t.reviewStatus=='approved'?'This task is approved and locked.':'This task is waiting for the admin review.');return;}
  final saved=await Navigator.push<bool>(context,MaterialPageRoute(builder:(_)=>TaskFormPage(task:t,store:store)));
  await load();if(saved==true)syncOnline();
 }
 Future<void> syncNow()async{if(sync.running)return;setState(()=>busy=true);await sync.sync(progress:(_,__) {},refresh:load);if(mounted){setState(()=>busy=false);await load();if(sync.lastError!=null)message('Sync failed: ${sync.lastError}');else message('Sync complete.');}}
 Future<void> showSettings()async{final init=ApiConfig(await settings.base,await settings.token);if(!mounted)return;final r=await Navigator.push<ApiConfig>(context,MaterialPageRoute(builder:(_)=>ApiSettingsPage(initial:init)));if(r==null)return;try{await settings.save(r.base,r.token);message('Settings saved.');syncOnline();}catch(e){message('$e');}}
 void message(String s)=>ScaffoldMessenger.of(context).showSnackBar(SnackBar(content:Text(s)));@override void dispose(){timer?.cancel();name.dispose();super.dispose();}
 Widget chip(String text,Color background,Color foreground)=>Container(padding:const EdgeInsets.symmetric(horizontal:10,vertical:5),decoration:BoxDecoration(color:background,borderRadius:BorderRadius.circular(99)),child:Text(text,style:TextStyle(fontSize:12,fontWeight:FontWeight.w700,color:foreground)));
 Widget card(TaskRecord t){
  final synced=t.syncStatus=='Synced',rejected=t.reviewStatus=='rejected';
  final lines=[displayStamp(t.timeinCapturedAt),if((t.client??'').isNotEmpty)'Client: ${t.client}',if((t.jobType??'').isNotEmpty)'Job: ${t.jobType}',if((t.timeinAddress??'').isNotEmpty)t.timeinAddress!,'Photos: ${counts[t.id]??0}',if(rejected&&(t.reviewReason??'').isNotEmpty)'Reason: ${t.reviewReason}',if((t.lastError??'').isNotEmpty)t.lastError!];
  return Container(margin:const EdgeInsets.only(bottom:10),decoration:BoxDecoration(color:Colors.white,borderRadius:BorderRadius.circular(16),boxShadow:[BoxShadow(color:const Color(0xFF10123A).withOpacity(.06),blurRadius:12,offset:const Offset(0,3))]),child:InkWell(borderRadius:BorderRadius.circular(16),onTap:busy?null:()=>openTask(t),child:Padding(padding:const EdgeInsets.all(14),child:Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
   Row(children:[Expanded(child:Text(t.label,style:const TextStyle(fontWeight:FontWeight.w700,fontSize:16,color:Color(0xFF10123A)))),chip(synced?'Synced':'Pending',synced?const Color(0xFFE3F5E8):const Color(0xFFFFF0DE),synced?const Color(0xFF1D7A3D):const Color(0xFF9A6200))]),
   const SizedBox(height:6),
   Text(lines.where((s)=>s.isNotEmpty).join('\n'),style:const TextStyle(color:Color(0xFF5B5F8A),height:1.35)),
   if(t.canFinish)...[const SizedBox(height:10),FilledButton.icon(onPressed:busy?null:()=>finishTask(t),icon:const Icon(Icons.logout),label:const Text('Finish'))],
   if(rejected)...[const SizedBox(height:10),OutlinedButton(onPressed:busy?null:()=>resubmit(t),child:const Text('Resubmit'))],
  ]))));
 }
 @override Widget build(BuildContext context)=>Scaffold(appBar:AppBar(title:const Text('SSD Image Capture'),actions:[IconButton(onPressed:showSettings,icon:const Icon(Icons.settings))]),body:Column(children:[Padding(padding:const EdgeInsets.fromLTRB(16,8,16,12),child:Column(children:[TextField(controller:name,decoration:const InputDecoration(labelText:'Technician name')),const SizedBox(height:10),Row(children:[Expanded(child:FilledButton.icon(onPressed:busy?null:addTask,icon:const Icon(Icons.camera_alt),label:const Text('Add task'))),const SizedBox(width:10),Expanded(child:OutlinedButton(onPressed:busy?null:syncNow,child:const Text('Sync')))]),const SizedBox(height:8),Text(status,style:const TextStyle(color:Color(0xFF5B5F8A))),Align(alignment:Alignment.centerLeft,child:Text('Pending: ${rows.where((r)=>r.syncStatus!='Synced').length+legacyPending}',style:const TextStyle(color:Color(0xFF5B5F8A),fontWeight:FontWeight.w600)))])),Expanded(child:RefreshIndicator(onRefresh:load,child:rows.isEmpty?ListView(padding:const EdgeInsets.all(32),children:const [Text('No tasks yet. Tap Add task to time in.',textAlign:TextAlign.center,style:TextStyle(color:Color(0xFF5B5F8A)))]):ListView.builder(padding:const EdgeInsets.fromLTRB(16,0,16,16),itemCount:rows.length,itemBuilder:(_,i)=>card(rows[i]))))]));
}

class CameraPage extends StatefulWidget{const CameraPage({required this.controller,super.key});final CameraController controller;@override State<CameraPage> createState()=>_CameraPageState();}

class _CameraPageState extends State<CameraPage>{late CameraController controller=widget.controller;bool switching=false;
 @override void dispose(){controller.dispose();super.dispose();}
 Future<void> switchCamera()async{if(switching)return;setState(()=>switching=true);try{final cams=await availableCameras();final current=controller.description;final next=cams.firstWhere((c)=>c.lensDirection!=current.lensDirection,orElse:()=>current);if(next.lensDirection==current.lensDirection)return;final old=controller;await old.dispose();final fresh=CameraController(next,ResolutionPreset.max,enableAudio:false);await fresh.initialize();if(!mounted){await fresh.dispose();return;}setState(()=>controller=fresh);}catch(e){if(mounted)ScaffoldMessenger.of(context).showSnackBar(SnackBar(content:Text('Camera switch failed: $e')));}finally{if(mounted)setState(()=>switching=false);}}
 @override Widget build(BuildContext context) {
  return Scaffold(backgroundColor:Colors.black,body:Stack(fit:StackFit.expand,children:[
    CameraPreview(controller,key:ValueKey(controller)),
    IgnorePointer(child:LayoutBuilder(builder:(context,c){
      final fullH=c.maxHeight, fullW=c.maxWidth;
      final cropH=(fullW*3/2).clamp(0,fullH).toDouble();
      final barH=((fullH-cropH)/2).clamp(0,fullH).toDouble();
      return Stack(children:[
        Positioned(top:0,left:0,right:0,height:barH,child:Container(color:Colors.black.withOpacity(.55))),
        Positioned(bottom:0,left:0,right:0,height:barH,child:Container(color:Colors.black.withOpacity(.55))),
      ]);
    })),
    Align(alignment:Alignment.topRight,child:SafeArea(child:Padding(padding:const EdgeInsets.all(12),child:IconButton.filled(onPressed:switching?null:switchCamera,icon:const Icon(Icons.cameraswitch))))),
    Align(alignment:Alignment.bottomCenter,child:Padding(padding:const EdgeInsets.only(bottom:36),child:GestureDetector(onTap:()async{try{final shot=await controller.takePicture();if(context.mounted)Navigator.pop(context,shot);}catch(e){if(context.mounted)ScaffoldMessenger.of(context).showSnackBar(SnackBar(content:Text('Camera error: $e')));}},child:Container(width:76,height:76,decoration:BoxDecoration(shape:BoxShape.circle,border:Border.all(color:Colors.white,width:4),color:Colors.transparent),child:Center(child:Container(width:62,height:62,decoration:const BoxDecoration(shape:BoxShape.circle,color:Colors.white))))))),
  ]));
 }
}
