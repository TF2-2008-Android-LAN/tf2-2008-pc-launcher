using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Web.Script.Serialization;

namespace TF2008Updater {
 internal sealed class ManifestFile {
  internal string Path, Hash, Pack;
  internal long Bytes;
  internal bool Preserve;
 }
 internal sealed class ManifestPack {
  internal string Name, AssetName, Hash, Url;
  internal long Bytes;
 }
 internal sealed class GameRelease {
  internal string Version, BaseUrl, ReleaseUrl, ManifestUrl, ManifestHash;
  internal long ManifestBytes;
  internal List<ManifestPack> Packs=new List<ManifestPack>();
  internal Dictionary<string,ManifestFile> Files=new Dictionary<string,ManifestFile>(StringComparer.OrdinalIgnoreCase);
 }
 internal sealed class LauncherRelease {
  internal string Version,Url,Hash;
  internal long Bytes;
 }
 internal sealed class UpdaterSettings {
  internal string GameManifestUrl="https://raw.githubusercontent.com/TF2-2008-Android-LAN/tf2-2008-pc-game/main/release.json";
  internal string LauncherManifestUrl="https://raw.githubusercontent.com/TF2-2008-Android-LAN/tf2-2008-pc-launcher/main/update.json";
  internal string Channel="qa";
  internal static string IniPath {get{return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"updates.ini");}}
  internal static UpdaterSettings Load() {
   UpdaterSettings s=new UpdaterSettings();
   if(!File.Exists(IniPath))return s;
   foreach(string line in File.ReadAllLines(IniPath,Encoding.UTF8)){
    string t=line.Trim();if(t.StartsWith("#")||t.StartsWith(";"))continue;
    int i=t.IndexOf('=');if(i<1)continue;
    string name=t.Substring(0,i).Trim().ToLowerInvariant(), val=t.Substring(i+1).Trim();
    if(name=="game_manifest_url")s.GameManifestUrl=val;
    if(name=="launcher_manifest_url")s.LauncherManifestUrl=val;
    if(name=="channel")s.Channel=val;
   }
   return s;
  }
 }
 internal static class UpdateCore {
  internal const string Version="0.3.0";
  static readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=int.MaxValue, RecursionLimit=75 };
  internal static string Cache {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TF2-2008","downloads");}}
  internal static Action<string> Status = delegate {};
  internal static Action<int> Progress = delegate {};
  internal static Func<bool> Cancelled = delegate {return false;};
  static void Say(string s){Status(s);}
  static void Percent(int x){Progress(Math.Max(0,Math.Min(100,x)));}
  static void CheckCancel(){if(Cancelled())throw new OperationCanceledException("Se canceló sin borrar los archivos instalados.");}
  static Dictionary<string,object> ObjectOf(object value,string name){
   var o=value as Dictionary<string,object>;
   if(o==null)throw new InvalidDataException("JSON inválido: "+name);
   return o;
  }
  static IEnumerable ArrayOf(object value,string name){
   var a=value as IEnumerable;
   if(a==null)throw new InvalidDataException("Lista JSON inválida: "+name);
   return a;
  }
  static object Get(Dictionary<string,object> o,string key){
   object v;return o!=null&&o.TryGetValue(key,out v)?v:null;
  }
  static string StringOf(Dictionary<string,object> o,string key,string fallback=""){
   object v=Get(o,key);return v==null?fallback:Convert.ToString(v);
  }
  static long LongOf(Dictionary<string,object> o,string key){
   object v=Get(o,key);return v==null?0:Convert.ToInt64(v);
  }
  static bool BoolOf(Dictionary<string,object> o,string key){
   object v=Get(o,key);return v!=null&&Convert.ToBoolean(v);
  }
  static string Required(string v,string name){
   if(String.IsNullOrWhiteSpace(v))throw new InvalidDataException("Falta "+name+" en el manifiesto.");
   return v.Trim();
  }
  static string Sha(string v){
   v=Required(v,"SHA-256").ToLowerInvariant();
   if(v.Length!=64)throw new InvalidDataException("Hash SHA-256 con largo incorrecto.");
   foreach(char c in v)if(!Uri.IsHexDigit(c))throw new InvalidDataException("Hash SHA-256 inválido.");
   return v;
  }
  static bool IsHttpOrFile(string u){
   Uri uri;return Uri.TryCreate(u,UriKind.Absolute,out uri) &&
    (uri.Scheme=="https"||(uri.Scheme=="http"&&uri.IsLoopback)||uri.Scheme=="file");
  }
  static Uri ParseUrl(string u){
   if(!IsHttpOrFile(u))throw new InvalidDataException("URL inválida. Debe empezar por https:// o ser archivo local de pruebas.");
   return new Uri(u);
  }
  static WebRequest NewRequest(Uri url){
   WebRequest q=WebRequest.Create(url);
   if(q is HttpWebRequest){
    var h=(HttpWebRequest)q;h.UserAgent="TF2-PC-Updater/"+Version;
    h.Timeout=30000;h.ReadWriteTimeout=90000;
    h.AllowAutoRedirect=true;
    h.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
   }
   return q;
  }
  internal static string UrlJoin(string baseUrl,string name){
   var b=ParseUrl(baseUrl);
   return new Uri(b,name).AbsoluteUri;
  }
  internal static byte[] DownloadBytes(string url){
   CheckCancel();
   using(WebResponse r=NewRequest(ParseUrl(url)).GetResponse())
   using(Stream s=r.GetResponseStream())
   using(MemoryStream m=new MemoryStream()) {
    byte[] buf=new byte[65536];int n;
    while((n=s.Read(buf,0,buf.Length))>0){CheckCancel();m.Write(buf,0,n);if(m.Length>16000000)throw new InvalidDataException("Manifiesto remoto demasiado grande.");}
    return m.ToArray();
   }
  }
  internal static string DownloadText(string url){
   return Encoding.UTF8.GetString(DownloadBytes(url)).TrimStart('\uFEFF');
  }
  internal static string HashFile(string path){
   using(var sha=SHA256.Create())using(var f=File.OpenRead(path)){
    return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();
   }
  }
  internal static string HashBytes(byte[] data){
   using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
  }
  internal static string Relative(string value){
   value=Required(value,"ruta").Replace('\\','/');
   if(value.StartsWith("/")||value.StartsWith("//")||value.Contains(":")||value.Contains("\0"))
    throw new InvalidDataException("Ruta del paquete no permitida: "+value);
   string[] bits=value.Split('/');
   foreach(string b in bits){
    if(b==""||b=="."||b==".."||b.EndsWith(".")||b.EndsWith(" "))
     throw new InvalidDataException("Ruta del paquete no segura: "+value);
   }
   if(value.Length>240)throw new InvalidDataException("Ruta demasiado larga: "+value);
   return value;
  }
  internal static string SafeRoot(string dest){
   if(String.IsNullOrWhiteSpace(dest))throw new IOException("Elegí una carpeta de instalación.");
   dest=Path.GetFullPath(dest.Trim());
   string drive=Path.GetPathRoot(dest);
   if(String.Equals(dest.TrimEnd('\\'),drive.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
    throw new IOException("No se permite instalar en la raíz del disco.");
   string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
   string[] forb={windows,Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)};
   foreach(string f in forb)if(!String.IsNullOrEmpty(f)&&dest.StartsWith(f.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))
    throw new IOException("Elegí una carpeta fuera de Windows o Archivos de programa.");
   if(dest.IndexOf("\\steamapps\\common\\",StringComparison.OrdinalIgnoreCase)>=0)
    throw new IOException("No instales este port encima de los juegos originales de Steam.");
   return dest;
  }
  static void EnsureNoLinks(string root,string target){
   string current=target;
   while(current!=null && current.Length>=root.Length){
    if(Directory.Exists(current)||File.Exists(current)){
     FileAttributes attr=File.GetAttributes(current);
     if((attr&FileAttributes.ReparsePoint)!=0)throw new IOException("Hay un enlace/junction en la ruta del juego: "+current);
    }
    if(String.Equals(current,root,StringComparison.OrdinalIgnoreCase))break;
    current=Path.GetDirectoryName(current);
   }
  }
  internal static string Destination(string root,string relative){
   root=SafeRoot(root);
   relative=Relative(relative);
   string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
   if(!full.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("El archivo intenta salir de la carpeta elegida.");
   EnsureNoLinks(root,Path.GetDirectoryName(full));
   if(File.Exists(full)&&((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0))
    throw new IOException("Archivo enlazado no permitido: "+full);
   return full;
  }
  internal static GameRelease ReadGame(string releaseUrl){
   Say("Leyendo la versión publicada del juego…");
   var map=ObjectOf(json.DeserializeObject(DownloadText(releaseUrl)),"release.json");
   if(StringOf(map,"schema")!="tf2-pc-release-v1")throw new InvalidDataException("Formato de release.json no reconocido.");
   if(BoolOf(map,"published")==false)throw new InvalidDataException("Esta versión está en preparación y todavía no fue publicada.");
   var game=new GameRelease();
   game.Version=Required(StringOf(map,"version"),"version");
   game.ReleaseUrl=releaseUrl;
   game.BaseUrl=StringOf(map,"baseUrl");
   var man=ObjectOf(Get(map,"manifest"),"manifest");
   game.ManifestHash=Sha(StringOf(man,"sha256"));
   game.ManifestBytes=LongOf(man,"bytes");
   if(game.ManifestBytes<2||game.ManifestBytes>16000000)throw new InvalidDataException("Manifiesto de archivos demasiado grande.");
   game.ManifestUrl=UrlJoin(releaseUrl,Relative(StringOf(man,"assetName","files.json")));
   foreach(object o in ArrayOf(Get(map,"packs"),"packs")){
    var item=ObjectOf(o,"pack");
    var p=new ManifestPack();
    p.Name=Relative(Required(StringOf(item,"name"),"pack.name"));
    p.AssetName=Relative(StringOf(item,"assetName",Path.GetFileName(p.Name)));
    p.Hash=Sha(StringOf(item,"sha256"));
    p.Bytes=LongOf(item,"bytes");
    p.Url=StringOf(item,"url");
    if(p.Bytes<1||p.Bytes>2200000000L)throw new InvalidDataException("Tamaño del ZIP fuera de rango: "+p.Name);
    if(!String.IsNullOrWhiteSpace(p.Url))ParseUrl(p.Url);
    game.Packs.Add(p);
   }
   if(game.Packs.Count==0||game.Packs.Count>1000)throw new InvalidDataException("Lista de ZIP inválida.");
   return game;
  }
  internal static LauncherRelease ReadLauncher(string url){
   Say("Leyendo la versión del launcher…");
   var map=ObjectOf(json.DeserializeObject(DownloadText(url)),"update.json");
   if(StringOf(map,"schema")!="tf2-pc-launcher-release-v1")throw new InvalidDataException("Formato update.json no reconocido.");
   if(!BoolOf(map,"published"))throw new InvalidDataException("El launcher todavía no tiene una versión publicada.");
   var o=ObjectOf(Get(map,"asset"),"asset");
   var x=new LauncherRelease();
   x.Version=Required(StringOf(map,"version"),"launcher.version");
   x.Url=Required(StringOf(o,"url"),"asset.url");
   x.Hash=Sha(StringOf(o,"sha256"));
   x.Bytes=LongOf(o,"bytes");
   if(x.Bytes<30000||x.Bytes>100000000)throw new InvalidDataException("Tamaño del launcher fuera de rango.");
   ParseUrl(x.Url);
   return x;
  }
  internal static bool IsNewer(string newer,string current){
   System.Version a,b;
   return System.Version.TryParse(newer,out a)&&System.Version.TryParse(current,out b)&&a>b;
  }
  static void DownloadFile(string url,string target,long length,string hash,int basePercent,int span){
   CheckCancel();
   if(File.Exists(target)&&new FileInfo(target).Length==length&&HashFile(target)==hash){Say("Archivo en caché validado: "+Path.GetFileName(target));return;}
   Directory.CreateDirectory(Path.GetDirectoryName(target));
   string part=target+".part";
   try{
    Say("Descargando "+Path.GetFileName(target)+"…");
    using(WebResponse response=NewRequest(ParseUrl(url)).GetResponse())
    using(Stream src=response.GetResponseStream())
    using(var dest=new FileStream(part,FileMode.Create,FileAccess.Write,FileShare.None,131072)){
     byte[] buffer=new byte[131072];int count;long done=0;
     while((count=src.Read(buffer,0,buffer.Length))>0){
      CheckCancel();done+=count;
      if(done>length)throw new IOException("El servidor envió más datos de lo indicado.");
      dest.Write(buffer,0,count);
      if(length>0)Percent(basePercent+(int)(span*done/length));
     }
    }
    if(new FileInfo(part).Length!=length||HashFile(part)!=hash)
     throw new InvalidDataException("Hash o tamaño incorrecto. Descarga rechazada: "+Path.GetFileName(target));
    if(File.Exists(target))File.Delete(target);
    File.Move(part,target);
   } finally {if(File.Exists(part))try{File.Delete(part);}catch{}}
  }
  internal static void ReadFiles(GameRelease game){
   Say("Verificando lista de 19 mil archivos (SHA-256)…");
   byte[] data=DownloadBytes(game.ManifestUrl);
   if(data.LongLength!=game.ManifestBytes||HashBytes(data)!=game.ManifestHash)
    throw new InvalidDataException("El índice files.json no pasó la verificación de SHA-256.");
   var map=ObjectOf(json.DeserializeObject(Encoding.UTF8.GetString(data).TrimStart('\uFEFF')),"files.json");
   if(StringOf(map,"schema")!="tf2-pc-files-v1")throw new InvalidDataException("Formato files.json distinto del esperado: "+StringOf(map,"schema"));
   var packs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in game.Packs)packs.Add(p.Name);
   foreach(object entry in ArrayOf(Get(map,"files"),"files")){
    var o=ObjectOf(entry,"file");
    var f=new ManifestFile();
    f.Path=Relative(StringOf(o,"path"));f.Hash=Sha(StringOf(o,"sha256"));
    f.Bytes=LongOf(o,"bytes");f.Pack=Relative(StringOf(o,"pack"));f.Preserve=BoolOf(o,"preserveOnUpdate");
    if(f.Bytes<0||!packs.Contains(f.Pack))throw new InvalidDataException("Entrada inválida: "+f.Path);
    if(game.Files.ContainsKey(f.Path))throw new InvalidDataException("Archivo duplicado: "+f.Path);
    game.Files.Add(f.Path,f);
   }
   Say("Índice verificado: "+game.Files.Count+" archivos.");
  }
  static bool AlwaysPreserve(string relative){
   string path=relative.Replace('\\','/').ToLowerInvariant();
   string name=Path.GetFileName(path);
   if(path.StartsWith("tf_port/custom/")||path.StartsWith("tf_port/download/")||
      path.StartsWith("tf_port/downloads/")||path.StartsWith("tf_port/screenshots/")||
      path.StartsWith("tf_port/addons/")||path.StartsWith("tf_port/replays/")||
      path.StartsWith("tf_port/save/")||path.StartsWith("tf_port/saves/"))return true;
   return name=="launcher.ini"||name=="video.txt"||name=="stats.txt"||
          name.EndsWith(".cfg")||name.EndsWith(".log")||name.EndsWith(".sav")||
          name.EndsWith(".dem")||name==".tf2updater-state.json";
  }
  // Never delete unknown files. Store durable copies of every existing tracked file
  // before replacing it. Backups are outside tf_port so Source never loads them.
  static string BackupExisting(string root,string rel,string oldFile,string backupSession){
   string bak=Destination(root,".tf2-update-backups/"+backupSession+"/"+rel);
   Directory.CreateDirectory(Path.GetDirectoryName(bak));
   if(File.Exists(bak))throw new IOException("La copia de seguridad ya existe: "+bak);
   File.Copy(oldFile,bak,false);
   if(new FileInfo(bak).Length!=new FileInfo(oldFile).Length||HashFile(bak)!=HashFile(oldFile))
    throw new IOException("La copia de seguridad no superó SHA-256: "+rel);
   return bak;
  }
  internal static string GameStateVersion(string root){
   try{
    string p=Path.Combine(SafeRoot(root),".tf2updater-state.json");
    if(!File.Exists(p))return "sin instalar";
    return StringOf(ObjectOf(json.DeserializeObject(File.ReadAllText(p,Encoding.UTF8)),"state"),"version","sin instalar");
   }catch{return "sin instalar";}
  }
  internal static string UpdateGame(string releaseUrl,string root,bool repair) {
   root=SafeRoot(root);
   CheckCancel();
   foreach(Process process in Process.GetProcessesByName("hl2_launcher")){
    try{if(String.Equals(process.MainModule.FileName,Path.Combine(root,"hl2_launcher.exe"),StringComparison.OrdinalIgnoreCase))
      throw new IOException("Cerrá el juego antes de instalar o reparar esta carpeta.");}
    catch(System.ComponentModel.Win32Exception){}
   }
   GameRelease g=ReadGame(releaseUrl);
   ReadFiles(g);
   Directory.CreateDirectory(root);
   var missingByPack=new Dictionary<string,List<ManifestFile>>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in g.Packs)missingByPack[p.Name]=new List<ManifestFile>();
   int scan=0,skipped=0,changed=0,backedUp=0;
   string backupSession=DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
   Say(repair?"Revisando archivos para reparar…":"Comparando archivos de la versión "+g.Version+"…");
   foreach(var item in g.Files.Values){
    CheckCancel();
    string dest=Destination(root,item.Path);
    scan++;
    if(File.Exists(dest)&&(item.Preserve||AlwaysPreserve(item.Path))){skipped++;continue;}
    if(File.Exists(dest)&&new FileInfo(dest).Length==item.Bytes&&HashFile(dest)==item.Hash){skipped++;continue;}
    missingByPack[item.Pack].Add(item);changed++;
    if(scan%140==0)Percent((int)(10.0*scan/g.Files.Count));
   }
   if(changed==0){WriteState(root,g.Version);Percent(100);Say("Todos los archivos están correctos. No hace falta descargar nada.");return "Sin cambios: "+skipped+" archivos correctos.";}
   Say(changed+" archivos requieren descarga o reparación en "+g.Packs.Count+" paquetes posibles.");
   int packCount=0;int actualPacks=0;
   foreach(var entry in missingByPack)if(entry.Value.Count>0)actualPacks++;
   foreach(var pack in g.Packs){
    var needed=missingByPack[pack.Name];if(needed.Count==0)continue;
    CheckCancel();
    string cacheName=pack.Hash+"-"+pack.AssetName;
    string cachePath=Path.Combine(Cache,cacheName);
    string url=!String.IsNullOrWhiteSpace(pack.Url)?pack.Url:(!String.IsNullOrWhiteSpace(g.BaseUrl)?UrlJoin(g.BaseUrl,pack.AssetName):UrlJoin(g.ReleaseUrl,pack.Name));
    int baseProgress=10+(int)(87.0*packCount/actualPacks);
    int span=Math.Max(1,87/actualPacks);
    DownloadFile(url,cachePath,pack.Bytes,pack.Hash,baseProgress,span/2);
    CheckCancel();
    Say("Verificando y extrayendo "+pack.AssetName+" ("+needed.Count+" archivos)…");
    string stage=Path.Combine(Cache,"stage_"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(stage);
    try{
     var wanted=new Dictionary<string,ManifestFile>(StringComparer.OrdinalIgnoreCase);
     foreach(var f in needed)wanted.Add(f.Path,f);
     var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
     using(var z=ZipFile.OpenRead(cachePath)){
      foreach(ZipArchiveEntry e in z.Entries){
       CheckCancel();
       if(String.IsNullOrEmpty(e.Name))continue;
       string rel=Relative(e.FullName);
       ManifestFile f;
       if(!wanted.TryGetValue(rel,out f))continue;
       if(!seen.Add(rel))throw new InvalidDataException("Archivo duplicado dentro del ZIP: "+rel);
       if(e.Length!=f.Bytes)throw new InvalidDataException("Tamaño del archivo del ZIP inválido: "+rel);
       string dest=Destination(stage,rel);
       Directory.CreateDirectory(Path.GetDirectoryName(dest));
       using(Stream from=e.Open())using(FileStream into=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
        byte[] buf=new byte[131072];int n;long wrote=0;
        while((n=from.Read(buf,0,buf.Length))>0){CheckCancel();wrote+=n;if(wrote>f.Bytes)throw new InvalidDataException("ZIP excede tamaño autorizado.");into.Write(buf,0,n);}
       }
       if(HashFile(dest)!=f.Hash)throw new InvalidDataException("Hash del archivo extraído incorrecto: "+rel);
      }
     }
     if(seen.Count!=wanted.Count)throw new InvalidDataException("Faltan "+(wanted.Count-seen.Count)+" archivos dentro de "+pack.AssetName);
     Say("Instalando los archivos verificados de "+pack.AssetName+"…");
     foreach(var f in needed){
      CheckCancel();
      string dest=Destination(root,f.Path);
      Directory.CreateDirectory(Path.GetDirectoryName(dest));
      string src=Destination(stage,f.Path);
      string incoming=dest+".incoming-tf2";
      string backup=dest+".previous-tf2";
      if(File.Exists(incoming))File.Delete(incoming);
      if(File.Exists(backup))File.Delete(backup);
      File.Copy(src,incoming,true);
      if(HashFile(incoming)!=f.Hash)throw new InvalidDataException("Falló el control al escribir "+f.Path);
      if(File.Exists(dest)){
       // Make a verified, permanent copy BEFORE overwriting any existing file.
       // Nothing is silently deleted, including modified models/maps/materials.
       BackupExisting(root,f.Path,dest,backupSession);
       backedUp++;
       try{File.Replace(incoming,dest,backup,true);}
       catch {
        if(File.Exists(incoming))File.Delete(incoming);
        throw;
       }
       if(File.Exists(backup))File.Delete(backup);
      } else File.Move(incoming,dest);
     }
    } finally {try{Directory.Delete(stage,true);}catch{}}
    packCount++;
    Percent(10+(int)(87.0*packCount/actualPacks));
   }
   WriteState(root,g.Version);Percent(100);Say("Juego actualizado y archivos verificados: "+g.Version);
   return "Versión "+g.Version+": "+changed+" instalados o reparados; "+skipped+
          " correctos. Archivos anteriores respaldados: "+backedUp+
          (backedUp>0?" (carpeta .tf2-update-backups en el juego).":".");
  }
  static void WriteState(string root,string version){
   string p=Path.Combine(root,".tf2updater-state.json");
   var data=new Dictionary<string,object>();
   data["schema"]="tf2-pc-installed-v1";data["version"]=version;
   data["updatedUtc"]=DateTime.UtcNow.ToString("o");
   string temp=p+".tmp";File.WriteAllText(temp,json.Serialize(data),new UTF8Encoding(false));
   if(File.Exists(p))File.Delete(p);
   File.Move(temp,p);
  }
  internal static string CheckRemote(string gameUrl,string launcherUrl,string root){
   string status="";
   try{GameRelease g=ReadGame(gameUrl);status+="Juego: "+g.Version+" (instalado: "+GameStateVersion(root)+")\r\n";}
   catch(Exception ex){status+="Juego: no publicado o inaccesible. "+ex.Message+"\r\n";}
   try{LauncherRelease l=ReadLauncher(launcherUrl);status+="Launcher: "+l.Version+" (actual: "+Version+")"+(IsNewer(l.Version,Version)?" — actualización disponible":" — al día");}
   catch(Exception ex){status+="Launcher: no publicado o inaccesible. "+ex.Message;}
   Percent(100);
   return status;
  }
  internal static string AutomaticStartup(string launcherManifestUrl,string gameManifestUrl,string root){
   var report=new List<string>();
   try{
    Say("Comprobando automáticamente el launcher…");
    string next=DownloadLauncher(launcherManifestUrl);
    if(next!=null)return "SELFUPDATE:"+next;
    report.Add("Launcher "+Version+" al día.");
   }catch(OperationCanceledException){throw;}
   catch(Exception ex){report.Add("Launcher: no se pudo comprobar. "+ex.Message);}
   if(!GameLauncher.Valid(root)){
    report.Add("Juego no instalado en esta carpeta; elegí una carpeta para instalarlo.");
    return String.Join(" ",report.ToArray());
   }
   try{
    Say("Comprobando automáticamente el juego…");
    GameRelease g=ReadGame(gameManifestUrl);
    string installed=GameStateVersion(root);
    if(String.Equals(installed,g.Version,StringComparison.OrdinalIgnoreCase)){
     report.Add("Juego "+g.Version+" al día.");
    }else{
     Say("Actualización encontrada: "+installed+" -> "+g.Version);
     report.Add(UpdateGame(gameManifestUrl,root,false));
    }
   }catch(OperationCanceledException){throw;}
   catch(Exception ex){report.Add("Actualización del juego pendiente: "+ex.Message);}
   return String.Join(" ",report.ToArray());
  }
  internal static string DownloadLauncher(string launcherManifestUrl){
   LauncherRelease l=ReadLauncher(launcherManifestUrl);
   if(!IsNewer(l.Version,Version))return null;
   string target=Path.Combine(Cache,"launcher-"+l.Version.Replace('.','_')+"-"+l.Hash+".exe");
   DownloadFile(l.Url,target,l.Bytes,l.Hash,0,100);
   return target;
  }
  internal static int FinishSelfUpdate(string[] args){
   try{
    // --apply-launcher-update PARENT_PID TARGET_EXE SOURCE_EXE EXPECTED_SHA256
    if(args.Length!=5)return 10;
    int pid;if(!int.TryParse(args[1],out pid))return 11;
    string target=Path.GetFullPath(args[2]),source=Path.GetFullPath(args[3]),hash=Sha(args[4]);
    if(!File.Exists(source)||HashFile(source)!=hash)return 12;
    try {Process p=Process.GetProcessById(pid);if(!p.WaitForExit(30000))return 14;}catch(ArgumentException){}
    if(Process.GetProcessesByName("TF2_2008_Launcher").Length>1){} // no process killing
    string incoming=target+".incoming";
    string backup=target+".previous.exe";
    File.Copy(source,incoming,true);
    if(HashFile(incoming)!=hash)return 13;
    if(File.Exists(target))File.Replace(incoming,target,backup,true);
    else File.Move(incoming,target);
    Process.Start(new ProcessStartInfo(target){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(target)});
    return 0;
   } catch(Exception ex){
    try{string p=Path.Combine(Cache,"launcher-update-error.txt");Directory.CreateDirectory(Cache);File.WriteAllText(p,ex.ToString());}catch{}
    return 20;
   }
  }
  internal static void StartSelfUpdate(string source){
   string self=Process.GetCurrentProcess().MainModule.FileName;
   string sha=HashFile(source);
   var info=new ProcessStartInfo(source);
   info.UseShellExecute=false;
   info.WorkingDirectory=Path.GetDirectoryName(self);
   info.Arguments="--apply-launcher-update "+Process.GetCurrentProcess().Id+" "+Quote(self)+" "+Quote(source)+" "+sha;
   Process.Start(info);
  }
  static string Quote(string s){return "\""+s.Replace("\"","\\\"")+"\"";}
 }
}
