import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'api_settings_page.dart';
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
class LocalStore {
 Database? _database;
 Future<Database> get db async=>_database??=await openDatabase(p.join((await getApplicationSupportDirectory()).path,'photos.db'),version:5,
   onCreate:(d,_)=>d.execute("CREATE TABLE photos (id TEXT PRIMARY KEY,name TEXT NOT NULL,client TEXT NOT NULL DEFAULT '',activity TEXT NOT NULL DEFAULT '',latitude REAL NOT NULL,longitude REAL NOT NULL,accuracy REAL NOT NULL,captured_at TEXT NOT NULL,capture_date TEXT NOT NULL,capture_time TEXT NOT NULL,image_path TEXT NOT NULL,note TEXT NOT NULL DEFAULT '',address TEXT NOT NULL DEFAULT '',sync_status TEXT NOT NULL,retry_count INTEGER NOT NULL DEFAULT 0,last_error TEXT)"),
   onUpgrade:(d,oldV,newV) async{if(oldV<2)await d.execute("ALTER TABLE photos ADD COLUMN note TEXT NOT NULL DEFAULT ''");if(oldV<3)await d.execute("ALTER TABLE photos ADD COLUMN address TEXT NOT NULL DEFAULT ''");if(oldV<4)await d.execute("ALTER TABLE photos ADD COLUMN client TEXT NOT NULL DEFAULT ''");if(oldV<5)await d.execute("ALTER TABLE photos ADD COLUMN activity TEXT NOT NULL DEFAULT ''");});
 Future<void> save(PhotoRecord r) async=>(await db).insert('photos',r.toMap());
 Future<List<PhotoRecord>> list() async=>(await (await db).query('photos',orderBy:'captured_at DESC')).map(PhotoRecord.fromMap).toList();
 Future<void> mark(PhotoRecord r,String s,{String? error,bool increment=false}) async=>(await db).update('photos',{'sync_status':s,'last_error':error,if(increment)'retry_count':r.retryCount+1},where:'id = ?',whereArgs:[r.id]);
}
class ApiSettings {
 Future<String> get base async=>await _secure.read(key:'api_base')??''; Future<String> get token async=>await _secure.read(key:'api_token')??'';
 Future<void> save(String b,String t) async {final u=Uri.tryParse(b);if(u==null||u.scheme!='https'||u.host.isEmpty)throw const FormatException('Use a valid HTTPS API URL.');await _secure.write(key:'api_base',value:b.replaceFirst(RegExp(r'/+$'),''));await _secure.write(key:'api_token',value:t);}
}
class SyncService {
 SyncService(this.store,this.settings);
 final LocalStore store; final ApiSettings settings; bool running=false;
 String? lastError;
 Future<void> sync({required VoidCallback refresh,required void Function(String,double) progress}) async {
  if(running)return; running=true; lastError=null;
  try {
   final base=await settings.base,token=await settings.token;
   if(base.isEmpty||token.isEmpty)throw StateError('Set HTTPS API URL and token in Settings.');
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
  } catch(e) { lastError=e.toString(); }
  finally {running=false;}
 }
}
class CaptureHome extends StatefulWidget {const CaptureHome({super.key});@override State<CaptureHome> createState()=>_CaptureHomeState();}
class _CaptureHomeState extends State<CaptureHome>{final store=LocalStore(),settings=ApiSettings(),name=TextEditingController();late final sync=SyncService(store,settings);List<PhotoRecord> rows=[];String status='';bool busy=false;Timer? timer;
 @override void initState(){super.initState();load();loadName();WidgetsBinding.instance.addPostFrameCallback((_)async{if((await settings.base).isEmpty&&mounted)showSettings();});timer=Timer.periodic(const Duration(minutes:2),(_)=>syncOnline());Connectivity().onConnectivityChanged.listen((_)=>syncOnline());}
 Future<void> loadName()async{name.text=await _secure.read(key:'last_name')??'';if(mounted)setState((){});} Future<void> load()async{final r=await store.list();if(mounted)setState(()=>rows=r);} Future<void> syncOnline()async{if((await Connectivity().checkConnectivity()).any((x)=>x!=ConnectivityResult.none))await syncNow();}
 Future<Position?> getLocation()async{var x=await Geolocator.checkPermission();if(x==LocationPermission.denied)x=await Geolocator.requestPermission();if(x==LocationPermission.denied||x==LocationPermission.deniedForever||!await Geolocator.isLocationServiceEnabled())return null;try{return await Geolocator.getCurrentPosition(locationSettings:const LocationSettings(accuracy:LocationAccuracy.high,timeLimit:Duration(seconds:15)));}on TimeoutException{return null;}}
 Future<String> reverseGeocode(Position? pos)async{if(pos==null)return '';try{final marks=await Geocoding().placemarkFromCoordinates(pos.latitude,pos.longitude).timeout(const Duration(seconds:10));if(marks.isEmpty)return '';final m=marks.first;return [m.street,m.subLocality,m.locality,m.administrativeArea,m.postalCode,m.country].where((s)=>s!=null&&s.trim().isNotEmpty).join(', ');}catch(_){return '';}}
 Future<String?> askClient()async{final n=TextEditingController();return showDialog<String>(context:context,barrierDismissible:false,builder:(c)=>AlertDialog(title:const Text('Add client'),content:TextField(controller:n,autofocus:true,decoration:const InputDecoration(labelText:'Client (required)',border:OutlineInputBorder())),actions:[FilledButton(onPressed:(){if(n.text.trim().isEmpty)return;Navigator.pop(c,n.text.trim());},child:const Text('Save'))]));}
 Future<String?> askActivity()async{String? chosen;return showDialog<String>(context:context,barrierDismissible:false,builder:(c)=>StatefulBuilder(builder:(c,setS)=>AlertDialog(title:const Text('Select activity'),content:DropdownButtonFormField<String>(initialValue:chosen,isExpanded:true,decoration:const InputDecoration(labelText:'Activity (required)',border:OutlineInputBorder()),items:kActivities.map((a)=>DropdownMenuItem(value:a,child:Text(a))).toList(),onChanged:(v)=>setS(()=>chosen=v)),actions:[FilledButton(onPressed:chosen==null?null:()=>Navigator.pop(c,chosen),child:const Text('Save'))])));}
 Future<String?> askNote()async{final n=TextEditingController();return showDialog<String>(context:context,barrierDismissible:false,builder:(c)=>AlertDialog(title:const Text('Add a note'),content:TextField(controller:n,autofocus:true,maxLines:3,decoration:const InputDecoration(labelText:'Note (required)',border:OutlineInputBorder())),actions:[FilledButton(onPressed:(){if(n.text.trim().isEmpty)return;Navigator.pop(c,n.text.trim());},child:const Text('Save'))]));}
 Future<void> capture()async{if(name.text.trim().isEmpty){message('Enter your name before capturing.');return;}setState(()=>busy=true);try{final cams=await availableCameras();if(cams.isEmpty)throw StateError('No camera available.');final camera=CameraController(cams.first,ResolutionPreset.max,enableAudio:false);await camera.initialize();final shot=await Navigator.push<XFile>(context,MaterialPageRoute(builder:(_)=>CameraPage(controller:camera)));if(shot==null||!mounted)return;final client=await askClient();if(client==null||!mounted)return;final activity=await askActivity();if(activity==null||!mounted)return;final note=await askNote();if(note==null||!mounted)return;final pos=await getLocation();if(pos==null){final ok=await showDialog<bool>(context:context,builder:(c)=>AlertDialog(title:const Text('Location unavailable'),content:const Text('GPS is unavailable or permission denied. Save without location?'),actions:[TextButton(onPressed:()=>Navigator.pop(c,false),child:const Text('Cancel')),FilledButton(onPressed:()=>Navigator.pop(c,true),child:const Text('Save without location'))]));if(ok!=true)return;}final dir=Directory(p.join((await getApplicationSupportDirectory()).path,'photos'));await dir.create(recursive:true);final id=_uuid.v4(),path=p.join(dir.path,'$id.jpg');await File(shot.path).copy(path);final address=await reverseGeocode(pos);final t=DateTime.now().toUtc().add(_offset),iso='${DateFormat("yyyy-MM-dd'T'HH:mm:ss").format(t)}+08:00';final r=PhotoRecord(id:id,name:name.text.trim(),client:client,activity:activity,latitude:pos?.latitude??0,longitude:pos?.longitude??0,accuracy:pos?.accuracy??0,capturedAt:iso,captureDate:DateFormat('yyyy-MM-dd').format(t),captureTime:DateFormat('HH:mm:ss').format(t),imagePath:path,note:note,address:address);await store.save(r);await _secure.write(key:'last_name',value:r.name);status='Saved locally · ${r.displayTime}';await load();if(mounted)await showDialog<void>(context:context,builder:(c)=>AlertDialog(title:const Text('Photo saved'),content:Column(mainAxisSize:MainAxisSize.min,children:[Image.file(File(path),height:200),Text(r.name),Text(r.displayTime),Text(pos==null?'Location not recorded':'${pos.latitude.toStringAsFixed(5)}, ${pos.longitude.toStringAsFixed(5)} ±${pos.accuracy.toStringAsFixed(0)}m')]),actions:[TextButton(onPressed:()=>Navigator.pop(c),child:const Text('Done'))]));}catch(e){if(mounted)message('Capture failed: $e');}finally{if(mounted)setState(()=>busy=false);}}
 Future<void> syncNow()async{if(sync.running)return;setState(()=>busy=true);await sync.sync(progress:(_,__) {},refresh:load);if(mounted){setState(()=>busy=false);await load();if(sync.lastError!=null)message('Sync failed: ${sync.lastError}');else message('Sync complete.');}}
 Future<void> showSettings()async{final init=ApiConfig(await settings.base,await settings.token);if(!mounted)return;final r=await Navigator.push<ApiConfig>(context,MaterialPageRoute(builder:(_)=>ApiSettingsPage(initial:init)));if(r==null)return;try{await settings.save(r.base,r.token);message('Settings saved.');syncOnline();}catch(e){message('$e');}}
 void message(String s)=>ScaffoldMessenger.of(context).showSnackBar(SnackBar(content:Text(s)));@override void dispose(){timer?.cancel();name.dispose();super.dispose();}
 @override Widget build(BuildContext context)=>Scaffold(appBar:AppBar(title:const Text('SSD Image Capture'),actions:[IconButton(onPressed:showSettings,icon:const Icon(Icons.settings))]),body:Column(children:[Padding(padding:const EdgeInsets.fromLTRB(16,8,16,12),child:Column(children:[TextField(controller:name,decoration:const InputDecoration(labelText:'Technician name')),const SizedBox(height:10),Row(children:[Expanded(child:FilledButton.icon(onPressed:busy?null:capture,icon:const Icon(Icons.camera_alt),label:const Text('Capture photo'))),const SizedBox(width:10),Expanded(child:OutlinedButton(onPressed:busy?null:syncNow,child:const Text('Sync now')))]),const SizedBox(height:8),Text(status,style:const TextStyle(color:Color(0xFF5B5F8A))),FutureBuilder(future:store.list(),builder:(_,s)=>Align(alignment:Alignment.centerLeft,child:Text('Pending: ${(s.data??[]).where((r)=>r.status!='Synced').length}',style:const TextStyle(color:Color(0xFF5B5F8A),fontWeight:FontWeight.w600))))])),Expanded(child:RefreshIndicator(onRefresh:load,child:ListView.builder(padding:const EdgeInsets.fromLTRB(16,0,16,16),itemCount:rows.length,itemBuilder:(_,i){final r=rows[i];return Container(margin:const EdgeInsets.only(bottom:10),decoration:BoxDecoration(color:Colors.white,borderRadius:BorderRadius.circular(16),boxShadow:[BoxShadow(color:const Color(0xFF10123A).withOpacity(.06),blurRadius:12,offset:const Offset(0,3))]),child:ListTile(contentPadding:const EdgeInsets.all(12),shape:RoundedRectangleBorder(borderRadius:BorderRadius.circular(16)),leading:ClipRRect(borderRadius:BorderRadius.circular(10),child:File(r.imagePath).existsSync()?Image.file(File(r.imagePath),width:64,height:64,fit:BoxFit.cover):Container(width:64,height:64,color:const Color(0xFFEDEEF8),child:const Icon(Icons.broken_image,color:Color(0xFF5B5F8A)))),title:Text(r.name,style:const TextStyle(fontWeight:FontWeight.w700)),subtitle:Text('${r.displayTime}\n${r.activity}\n${r.address}\n${r.note}\n${r.lastError??""}'),isThreeLine:true,trailing:Container(padding:const EdgeInsets.symmetric(horizontal:10,vertical:5),decoration:BoxDecoration(color:r.status=='Synced'?const Color(0xFFE3F5E8):const Color(0xFFFFF0DE),borderRadius:BorderRadius.circular(99)),child:Text(r.status,style:TextStyle(fontSize:12,fontWeight:FontWeight.w700,color:r.status=='Synced'?const Color(0xFF1D7A3D):const Color(0xFF9A6200))))));}))) ]));}

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






