using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Reflection;

namespace TF2008Updater {
 internal sealed class PlayerPrefs {
  internal string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TF2-2008","Juego");
  internal string Mode="steam",Name="Jugador",Map="";
  internal bool Windowed=true,Console=true,Insecure=true;
  static string Ini {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TF2-2008","launcher.ini");}}
  static string LegacyIni {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher.ini");}}
  internal static PlayerPrefs Load(){
   var p=new PlayerPrefs();
   string source=File.Exists(Ini)?Ini:LegacyIni;
   if(!File.Exists(source))return p;
   foreach(string line in File.ReadAllLines(source,Encoding.UTF8)){
    int i=line.IndexOf('=');if(i<1)continue;
    string k=line.Substring(0,i).Trim().ToLowerInvariant(),v=line.Substring(i+1).Trim();
    switch(k){
     case "root":if(v==".")v=AppDomain.CurrentDomain.BaseDirectory;if(v!="")p.Root=v;break;
     case "mode":p.Mode=v=="offline"?"offline":"steam";break;
     case "name":p.Name=v;break;
     case "map":p.Map=v;break;
     case "windowed":p.Windowed=v=="1";break;
     case "console":p.Console=v=="1";break;
     case "insecure":p.Insecure=v=="1";break;
    }
   }
   return p;
  }
  internal void Save(){
   string ini="[Launcher]\r\n";
   // Stable key=value format compatible with previous launcher, without needing registry.
   ini="root="+Root+"\r\nmode="+Mode+"\r\nname="+Name+"\r\nmap="+Map+"\r\nwindowed="+(Windowed?"1":"0")+
       "\r\nconsole="+(Console?"1":"0")+"\r\ninsecure="+(Insecure?"1":"0")+"\r\n";
   Directory.CreateDirectory(Path.GetDirectoryName(Ini));
   string tmp=Ini+".new";
   File.WriteAllText(tmp,ini,new UTF8Encoding(false));
   if(File.Exists(Ini))File.Delete(Ini);
   File.Move(tmp,Ini);
  }
 }
 internal static class GameLauncher {
  internal static bool Valid(string root){
   try{root=UpdateCore.SafeRoot(root);return File.Exists(Path.Combine(root,"hl2_launcher.exe")) &&
    File.Exists(Path.Combine(root,"tf_port","gameinfo.txt"));}
   catch{return false;}
  }
  internal static string ValidateName(string s){
   s=(s??"").Trim();
   if(!Regex.IsMatch(s,@"^[\p{L}\p{N} ._-]{1,24}$"))
    throw new ArgumentException("Nombre inválido. Usá entre 1 y 24 letras/números.");
   return s;
  }
  internal static string ValidateMap(string s){
   s=(s??"").Trim();
   if(s==""||s=="Menú principal")return "";
   if(!Regex.IsMatch(s,@"^[a-z0-9_]{3,48}$"))throw new ArgumentException("Nombre de mapa inválido.");
   return s;
  }
  internal static string Params(PlayerPrefs p){
   List<string> flags=new List<string>();
   flags.Add("-game tf_port -novid -dxlevel 95");
   if(p.Windowed)flags.Add("-windowed -w 1280 -h 720");
   if(p.Console)flags.Add("-console -condebug");
   if(p.Insecure||p.Mode=="offline")flags.Add("-insecure");
   if(p.Mode=="steam")flags.Add("-steam");
   else flags.Add("+name \""+ValidateName(p.Name)+"\"");
   string map=ValidateMap(p.Map);
   if(map!="")flags.Add("+sv_lan 1 +maxplayers 16 +map "+map);
   return String.Join(" ",flags.ToArray());
  }
  internal static bool SteamRunning(){
   try{return Process.GetProcessesByName("steam").Length>0;}catch{return false;}
  }
  internal static int Play(PlayerPrefs p){
   string root=UpdateCore.SafeRoot(p.Root);
   if(!Valid(root))throw new FileNotFoundException("Instalá el juego primero o elegí la carpeta correcta.");
   if(p.Mode=="steam"&&!SteamRunning())throw new InvalidOperationException("Abrí Steam para usar ese modo, o elegí 'Sin Steam'.");
   foreach(Process process in Process.GetProcessesByName("hl2_launcher")){
    try{if(String.Equals(process.MainModule.FileName,Path.Combine(root,"hl2_launcher.exe"),StringComparison.OrdinalIgnoreCase))
     throw new InvalidOperationException("Esta instalación del juego ya está abierta.");}
    catch(System.ComponentModel.Win32Exception){}
   }
   string args=Params(p);
   var info=new ProcessStartInfo(Path.Combine(root,"hl2_launcher.exe"),args);
   info.UseShellExecute=false;info.WorkingDirectory=root;
   if(p.Mode=="offline")foreach(string key in new[]{"SteamAppId","SteamGameId","SteamOverlayGameId"})info.EnvironmentVariables.Remove(key);
   return Process.Start(info).Id;
  }
 }
 internal sealed class LauncherWindow:Form {
  readonly Color bg=Color.FromArgb(24,27,32),card=Color.FromArgb(38,43,51),line=Color.FromArgb(60,67,77);
  readonly Color white=Color.FromArgb(235,237,242),muted=Color.FromArgb(171,181,197),blue=Color.FromArgb(47,123,184);
  readonly PlayerPrefs prefs=PlayerPrefs.Load();
  readonly UpdaterSettings urls=UpdaterSettings.Load();
  TextBox rootBox,nameBox,mapBox,logBox;
  RadioButton steamRadio,offlineRadio;
  CheckBox windowedCheck,consoleCheck,insecureCheck;
  Label gameState,updateState,footerState,sourceState,versionState;
  ProgressBar progress;
  Button playButton,playQuickButton,installButton,repairButton,checkButton,launcherButton,cancelButton,chooseButton,saveButton;
  TabControl tabs;
  BackgroundWorker job;
  string pendingAction="";
  internal LauncherWindow(){
   Text="Team Fortress 2 (2008) — Launcher y actualizaciones";
   StartPosition=FormStartPosition.CenterScreen;
   Size=new Size(1000,690);MinimumSize=new Size(780,550);
   AutoScaleMode=AutoScaleMode.Dpi;
   Font=new Font("Segoe UI",10,FontStyle.Regular);
   BackColor=bg;ForeColor=white;
   try{string i=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","icon.ico");if(File.Exists(i))Icon=new Icon(i);}catch{}
   BuildUi();RefreshState();
   Shown+=delegate {
    // The first launch is non-blocking. New launcher versions install first;
    // after restart the same check continues with the game.
    BeginInvoke((Action)(delegate { Run("startup"); }));
   };
  }
  Label L(string text,bool heading=false){
   Label l=new Label();l.Text=text;l.AutoSize=true;l.Margin=new Padding(0,3,6,5);
   l.ForeColor=heading?white:muted;l.Font=new Font("Segoe UI",heading?11:9.5f,heading?FontStyle.Bold:FontStyle.Regular);
   return l;
  }
  Button B(string name,EventHandler handler,bool important=false){
   Button b=new Button();b.Text=name;b.Height=40;b.AutoSize=true;b.MinimumSize=new Size(115,40);
   b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;
   b.BackColor=important?blue:line;b.ForeColor=white;b.Font=new Font("Segoe UI",9.5f,FontStyle.Bold);
   b.Padding=new Padding(9,3,9,3);b.Click+=handler;b.Margin=new Padding(4,4,8,4);return b;
  }
  TextBox T(string value){
   TextBox t=new TextBox();t.Text=value;t.BorderStyle=BorderStyle.FixedSingle;
   t.BackColor=Color.FromArgb(47,54,63);t.ForeColor=white;t.Font=Font;t.Dock=DockStyle.Fill;
   t.Margin=new Padding(0,4,10,9);return t;
  }
  TableLayoutPanel Grid(int count){
   TableLayoutPanel g=new TableLayoutPanel();g.ColumnCount=count;g.AutoSize=true;
   g.Dock=DockStyle.Top;g.AutoSizeMode=AutoSizeMode.GrowAndShrink;
   g.Padding=new Padding(20,16,20,14);g.Margin=new Padding(0);
   for(int i=0;i<count;i++)g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/count));
   return g;
  }
  FlowLayoutPanel Buttons(params Button[] buttons){
   FlowLayoutPanel p=new FlowLayoutPanel();p.AutoSize=true;p.Dock=DockStyle.Fill;
   p.WrapContents=true;p.FlowDirection=FlowDirection.LeftToRight;p.Margin=new Padding(0,12,0,8);
   foreach(var b in buttons)p.Controls.Add(b);return p;
  }
  void AddRow(TableLayoutPanel g,Control x,int colspan=1){
   int row=g.RowCount++;g.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   g.Controls.Add(x,0,row);if(colspan>1)g.SetColumnSpan(x,colspan);
  }
  Panel CardPanel(){
   Panel p=new Panel();p.BackColor=card;p.Padding=new Padding(14);p.Dock=DockStyle.Top;
   p.AutoSize=true;p.Margin=new Padding(15,10,15,7);return p;
  }
  void BuildUi(){
   var outer=new TableLayoutPanel();outer.Dock=DockStyle.Fill;outer.ColumnCount=1;outer.RowCount=3;
   outer.RowStyles.Add(new RowStyle(SizeType.Absolute,118));
   outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   outer.RowStyles.Add(new RowStyle(SizeType.Absolute,100));Controls.Add(outer);
   var header=new TableLayoutPanel();header.Dock=DockStyle.Fill;header.ColumnCount=2;header.Padding=new Padding(22,8,22,5);
   header.BackColor=Color.FromArgb(32,37,44);
   header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,280));
   header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   var logoPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","tf2_logo_original.png");
   if(File.Exists(logoPath)){
    try{PictureBox logo=new PictureBox();logo.Dock=DockStyle.Fill;logo.SizeMode=PictureBoxSizeMode.Zoom;
      using(var img=Image.FromFile(logoPath))logo.Image=new Bitmap(img);header.Controls.Add(logo,0,0);}
    catch{header.Controls.Add(L("TEAM FORTRESS 2",true),0,0);}
   }else header.Controls.Add(L("TEAM FORTRESS 2",true),0,0);
   var texts=new TableLayoutPanel();texts.Dock=DockStyle.Fill;texts.RowCount=2;texts.ColumnCount=1;
   texts.RowStyles.Add(new RowStyle(SizeType.Percent,60));texts.RowStyles.Add(new RowStyle(SizeType.Percent,40));
   var h=L("TF2 2008 • PC",true);h.Font=new Font("Segoe UI",17,FontStyle.Bold);texts.Controls.Add(h,0,0);
   versionState=L("Launcher "+UpdateCore.Version+"  •  actualizador / instalador",false);texts.Controls.Add(versionState,0,1);
   header.Controls.Add(texts,1,0);outer.Controls.Add(header,0,0);
   tabs=new TabControl();tabs.Dock=DockStyle.Fill;tabs.Font=new Font("Segoe UI",10,FontStyle.Bold);
   tabs.Padding=new Point(18,7);
   var game=new TabPage("Jugar");var update=new TabPage("Instalar / Actualizar");var help=new TabPage("Información");
   foreach(TabPage page in new[]{game,update,help}){page.BackColor=bg;page.AutoScroll=true;page.Padding=new Padding(8);tabs.TabPages.Add(page);}
   GameTab(game);UpdatesTab(update);HelpTab(help);
   outer.Controls.Add(tabs,0,1);
   var footer=new TableLayoutPanel();footer.Dock=DockStyle.Fill;footer.Padding=new Padding(20,7,20,8);
   footer.BackColor=Color.FromArgb(32,37,44);footer.RowCount=2;footer.ColumnCount=3;
   footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));
   footer.RowStyles.Add(new RowStyle(SizeType.Absolute,38));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
   footerState=L("Listo.",false);footerState.AutoEllipsis=true;footerState.AutoSize=false;footerState.Dock=DockStyle.Fill;
   footer.Controls.Add(footerState,0,0);
   cancelButton=B("Cancelar",CancelWork);cancelButton.Dock=DockStyle.Fill;cancelButton.Enabled=false;footer.Controls.Add(cancelButton,1,0);
   playQuickButton=B("JUGAR",Play,true);playQuickButton.Dock=DockStyle.Fill;footer.Controls.Add(playQuickButton,2,0);
   progress=new ProgressBar();progress.Dock=DockStyle.Fill;progress.Style=ProgressBarStyle.Continuous;
   progress.Maximum=100;progress.Minimum=0;progress.Value=0;
   progress.Margin=new Padding(0,12,0,4);footer.Controls.Add(progress,0,1);footer.SetColumnSpan(progress,3);
   outer.Controls.Add(footer,0,2);
  }
  void GameTab(TabPage page){
   var content=new TableLayoutPanel();content.Dock=DockStyle.Top;content.AutoSize=true;content.ColumnCount=1;
   page.Controls.Add(content);
   var folder=CardPanel();
   var fg=Grid(2);fg.ColumnStyles[0].SizeType=SizeType.Percent;fg.ColumnStyles[0].Width=80;
   fg.ColumnStyles[1].SizeType=SizeType.Percent;fg.ColumnStyles[1].Width=20;
   AddRow(fg,L("CARPETA DEL JUEGO",true),2);
   int r=fg.RowCount++;fg.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   rootBox=T(prefs.Root);fg.Controls.Add(rootBox,0,r);
   chooseButton=B("Elegir carpeta…",ChooseFolder);chooseButton.Dock=DockStyle.Fill;fg.Controls.Add(chooseButton,1,r);
   gameState=L("",false);AddRow(fg,gameState,2);
   folder.Controls.Add(fg);AddRow(content,folder);
   var options=CardPanel();var op=Grid(2);
   op.ColumnStyles[0].Width=50;op.ColumnStyles[1].Width=50;
   AddRow(op,L("PERFIL DE JUEGO",true),2);
   var mode=new FlowLayoutPanel();mode.Dock=DockStyle.Fill;mode.AutoSize=true;mode.WrapContents=true;mode.Margin=new Padding(0,8,0,10);
   steamRadio=new RadioButton();steamRadio.Text="Usar Steam abierto";steamRadio.AutoSize=true;steamRadio.ForeColor=white;steamRadio.Checked=prefs.Mode=="steam";
   offlineRadio=new RadioButton();offlineRadio.Text="Sin Steam (LAN)";offlineRadio.AutoSize=true;offlineRadio.ForeColor=white;offlineRadio.Checked=prefs.Mode=="offline";
   mode.Controls.Add(steamRadio);mode.Controls.Add(offlineRadio);AddRow(op,mode,2);
   r=op.RowCount++;op.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   op.Controls.Add(L("Nombre del jugador (sin Steam)"),0,r);op.Controls.Add(L("Mapa al iniciar (opcional)"),1,r);
   r=op.RowCount++;op.RowStyles.Add(new RowStyle(SizeType.AutoSize));
   nameBox=T(prefs.Name);mapBox=T(prefs.Map);op.Controls.Add(nameBox,0,r);op.Controls.Add(mapBox,1,r);
   var choices=new FlowLayoutPanel();choices.Dock=DockStyle.Fill;choices.AutoSize=true;choices.WrapContents=true;choices.Margin=new Padding(0,8,0,0);
   windowedCheck=new CheckBox();windowedCheck.Text="Abrir en ventana";windowedCheck.Checked=prefs.Windowed;
   consoleCheck=new CheckBox();consoleCheck.Text="Mostrar consola";consoleCheck.Checked=prefs.Console;
   insecureCheck=new CheckBox();insecureCheck.Text="Modo de pruebas (-insecure)";insecureCheck.Checked=prefs.Insecure;
   foreach(var b in new[]{windowedCheck,consoleCheck,insecureCheck}){b.AutoSize=true;b.ForeColor=white;b.Margin=new Padding(2,5,20,8);choices.Controls.Add(b);}
   AddRow(op,choices,2);options.Controls.Add(op);AddRow(content,options);
   var actions=CardPanel();var ag=Grid(1);
   saveButton=B("Guardar configuración",SavePrefs);
   playButton=B("JUGAR",Play,true);playButton.MinimumSize=new Size(200,45);
   AddRow(ag,Buttons(saveButton,playButton));
   AddRow(ag,L("El juego puede instalarse en cualquier carpeta que elijas. El launcher no toca TF2 de Steam."));
   actions.Controls.Add(ag);AddRow(content,actions);
   rootBox.TextChanged+=delegate{RefreshState();};
   offlineRadio.CheckedChanged+=delegate{nameBox.Enabled=offlineRadio.Checked;};
   nameBox.Enabled=offlineRadio.Checked;
  }
  void UpdatesTab(TabPage page){
   var content=new TableLayoutPanel();content.Dock=DockStyle.Top;content.AutoSize=true;content.ColumnCount=1;
   page.Controls.Add(content);
   var release=CardPanel();var g=Grid(1);
   AddRow(g,L("ACTUALIZACIONES DEL JUEGO",true));
   AddRow(g,L("Instalación de los archivos del motor necesarios para jugar, mapas, recursos, modelos y sonidos. Para reparar, descarga solo los paquetes que tienen archivos faltantes o distintos."));
   installButton=B("Instalar / Actualizar juego",InstallGame,true);
   repairButton=B("Verificar y reparar archivos",RepairGame);
   AddRow(g,Buttons(installButton,repairButton));
   release.Controls.Add(g);AddRow(content,release);
   var launch=CardPanel();var lg=Grid(1);
   AddRow(lg,L("ACTUALIZACIONES DEL LAUNCHER",true));
   AddRow(lg,L("Busca una versión nueva del programa. Descarga, comprueba el SHA-256 y reinicia para reemplazar el ejecutable, sin tocar el juego."));
   launcherButton=B("Actualizar launcher",UpdateLauncher);
   checkButton=B("Buscar versiones",CheckReleases);
   AddRow(lg,Buttons(checkButton,launcherButton));
   launch.Controls.Add(lg);AddRow(content,launch);
   var sources=CardPanel();var sg=Grid(1);
   AddRow(sg,L("FUENTES Y ESTADO",true));
   sourceState=L("Game: "+urls.GameManifestUrl+"\nLauncher: "+urls.LauncherManifestUrl);
   sourceState.MaximumSize=new Size(950,0);sourceState.AutoSize=true;AddRow(sg,sourceState);
   updateState=L("Todavía no se comprobó la publicación en GitHub.");
   updateState.MaximumSize=new Size(950,0);updateState.AutoSize=true;AddRow(sg,updateState);
   sources.Controls.Add(sg);AddRow(content,sources);
  }
  void HelpTab(TabPage page){
   var content=new TableLayoutPanel();content.Dock=DockStyle.Top;content.AutoSize=true;content.ColumnCount=1;
   page.Controls.Add(content);
   var help=CardPanel();var g=Grid(1);
   AddRow(g,L("CÓMO FUNCIONA",true));
   AddRow(g,L("1. Elegí la carpeta del juego en la pestaña Jugar.\n2. Abrí Instalar / Actualizar y pulsá Instalar.\n3. Cuando termine, abrí Jugar.\n4. Si algo se daña, usá Verificar y reparar."));
   AddRow(g,L("Se preservan las configuraciones personales como config.cfg y autoexec.cfg. La comprobación de archivos usa hashes SHA-256 para evitar instalaciones corruptas."));
   AddRow(g,L("Las actualizaciones del launcher y del juego usan repositorios y versiones separados de GitHub. Si los repositorios todavía no están publicados, el programa te avisa y no intenta inventar archivos."));
   AddRow(g,L("No modifica ninguna instalación oficial de Steam. Esta versión está en pruebas y todavía no garantiza compatibilidad de red con Android."));
   help.Controls.Add(g);AddRow(content,help);
   var diagnostics=CardPanel();var dg=Grid(1);AddRow(dg,L("REGISTRO DE LA SESIÓN",true));
   logBox=new TextBox();logBox.Multiline=true;logBox.ReadOnly=true;
   logBox.WordWrap=true;logBox.ScrollBars=ScrollBars.Vertical;logBox.Height=155;logBox.Dock=DockStyle.Top;
   logBox.BackColor=Color.FromArgb(32,37,44);logBox.ForeColor=white;logBox.Text="El launcher está listo para mostrar diagnósticos.";
   AddRow(dg,logBox);diagnostics.Controls.Add(dg);AddRow(content,diagnostics);
  }
  PlayerPrefs FromForm(){
   var p=new PlayerPrefs();p.Root=rootBox.Text.Trim();p.Mode=offlineRadio.Checked?"offline":"steam";
   p.Name=nameBox.Text.Trim();p.Map=mapBox.Text.Trim();p.Windowed=windowedCheck.Checked;
   p.Console=consoleCheck.Checked;p.Insecure=insecureCheck.Checked;
   UpdateCore.SafeRoot(p.Root);GameLauncher.ValidateMap(p.Map);
   if(p.Mode=="offline")GameLauncher.ValidateName(p.Name);
   return p;
  }
  void RefreshState(){
   if(rootBox==null||gameState==null)return;
   string root=rootBox.Text.Trim();
   bool valid=GameLauncher.Valid(root);
   string ver=UpdateCore.GameStateVersion(root);
   gameState.Text=valid?"Juego encontrado • versión registrada: "+ver :
     "El juego todavía no está en esta carpeta. Instalalo desde la pestaña Actualizar.";
   gameState.ForeColor=valid?Color.FromArgb(132,204,165):muted;
   if(playButton!=null)playButton.Enabled=valid;
   if(playQuickButton!=null)playQuickButton.Enabled=valid;
  }
  void SavePrefs(object s,EventArgs e){
   try{var p=FromForm();p.Save();MessageBox.Show(this,"Guardado. Carpeta: "+p.Root,"Configuración");}
   catch(Exception ex){MessageBox.Show(this,ex.Message,"No se pudo guardar",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
  }
  void ChooseFolder(object s,EventArgs e){
   using(var d=new FolderBrowserDialog()){
    d.Description="Elegí una carpeta para instalar TF2 2008 (también puede estar vacía).";
    d.ShowNewFolderButton=true;
    try{string p=rootBox.Text;if(Directory.Exists(p))d.SelectedPath=p;}catch{}
    if(d.ShowDialog(this)==DialogResult.OK)rootBox.Text=d.SelectedPath;
   }
  }
  void Play(object s,EventArgs e){
   try{PlayerPrefs p=FromForm();p.Save();int pid=GameLauncher.Play(p);
    Note("Juego iniciado, PID "+pid);}
   catch(Exception ex){MessageBox.Show(this,ex.Message,"No se pudo iniciar",MessageBoxButtons.OK,MessageBoxIcon.Warning);Note(ex.Message);}
  }
  void Note(string s){
   if(IsDisposed)return;
   footerState.Text=s.Replace("\r"," ").Replace("\n"," ");
   if(logBox!=null)logBox.AppendText("\r\n"+DateTime.Now.ToString("HH:mm:ss")+"  "+s);
  }
  void ToggleBusy(bool busy){
   foreach(var b in new[]{playButton,playQuickButton,installButton,repairButton,checkButton,launcherButton,chooseButton,saveButton})
    if(b!=null)b.Enabled=!busy;
   if(playButton!=null&&!busy)playButton.Enabled=GameLauncher.Valid(rootBox.Text.Trim());
   if(playQuickButton!=null&&!busy)playQuickButton.Enabled=GameLauncher.Valid(rootBox.Text.Trim());
   cancelButton.Enabled=busy;
   rootBox.ReadOnly=busy;
  }
  void Run(string action){
   if(job!=null&&job.IsBusy)return;
   string root;
   try{root=UpdateCore.SafeRoot(rootBox.Text.Trim());}
   catch(Exception ex){MessageBox.Show(this,ex.Message,"Carpeta no válida");return;}
   if(action=="game"||action=="repair"){
    var answer=MessageBox.Show(this,
     (action=="repair"?"Se van a comprobar y reparar archivos en:\n":"Se va a instalar/actualizar el juego en:\n")+
     root+"\n\nNo se sobrescribirán tus configuraciones personales.",
     "Confirmar carpeta",MessageBoxButtons.OKCancel,MessageBoxIcon.Information);
    if(answer!=DialogResult.OK)return;
   }
   if(action!="startup"){
    try{FromForm().Save();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Configuración");return;}
   }
   progress.Value=0;pendingAction=action;ToggleBusy(true);
   job=new BackgroundWorker();job.WorkerReportsProgress=true;job.WorkerSupportsCancellation=true;
   var current=job;
   current.DoWork+=delegate(object sender,DoWorkEventArgs e){
    UpdateCore.Cancelled=delegate{return current.CancellationPending;};
    UpdateCore.Status=delegate(string status){current.ReportProgress(-1,status);};
    UpdateCore.Progress=delegate(int percent){current.ReportProgress(percent,null);};
    if(action=="startup")e.Result=UpdateCore.AutomaticStartup(urls.LauncherManifestUrl,urls.GameManifestUrl,root);
    if(action=="check")e.Result=UpdateCore.CheckRemote(urls.GameManifestUrl,urls.LauncherManifestUrl,root);
    if(action=="game"||action=="repair")e.Result=UpdateCore.UpdateGame(urls.GameManifestUrl,root,action=="repair");
    if(action=="launcher"){
     string asset=UpdateCore.DownloadLauncher(urls.LauncherManifestUrl);
     e.Result=asset==null?"Launcher actualizado, no hace falta descargar nada.":"SELFUPDATE:"+asset;
    }
   };
   current.ProgressChanged+=delegate(object sender,ProgressChangedEventArgs e){
    if(e.ProgressPercentage>=0&&e.ProgressPercentage<=100)progress.Value=e.ProgressPercentage;
    if(e.UserState is string)Note((string)e.UserState);
   };
   current.RunWorkerCompleted+=delegate(object sender,RunWorkerCompletedEventArgs e){
    ToggleBusy(false);RefreshState();
    if(e.Error!=null){
     string message=e.Error is OperationCanceledException?"Se canceló la operación.":e.Error.GetBaseException().Message;
     Note(message);if(!(e.Error is OperationCanceledException))MessageBox.Show(this,message,"Actualizador",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }else if(e.Cancelled){Note("Operación cancelada.");}
    else if(e.Result is string){
     string result=(string)e.Result;
     if(result.StartsWith("SELFUPDATE:")){
      string file=result.Substring("SELFUPDATE:".Length);
      try{
       Note("Launcher verificado; se va a reiniciar para actualizarse.");
       UpdateCore.StartSelfUpdate(file);
       Close();
      }catch(Exception ex){MessageBox.Show(this,ex.Message,"No se pudo reiniciar");}
     }else{
      Note(result);
      if(action=="check")updateState.Text=result;
      if(action=="game"||action=="repair")tabs.SelectedIndex=0;
     }
    }
   };
   current.RunWorkerAsync();
   Note(action=="startup"?"Buscando actualizaciones automáticamente…":action=="check"?"Buscando actualizaciones…":action=="launcher"?"Buscando launcher nuevo…":action=="repair"?"Verificando instalación…":"Instalando/actualizando juego…");
  }
  void InstallGame(object s,EventArgs e){Run("game");}
  void RepairGame(object s,EventArgs e){Run("repair");}
  void CheckReleases(object s,EventArgs e){Run("check");}
  void UpdateLauncher(object s,EventArgs e){Run("launcher");}
  void CancelWork(object s,EventArgs e){if(job!=null&&job.IsBusy){job.CancelAsync();Note("Cancelando al terminar el bloque en curso…");}}
 }
 internal static class Program {
  [STAThread]
  static int Main(string[] args) {
   try{
    System.Net.ServicePointManager.SecurityProtocol=(System.Net.SecurityProtocolType)3072;
    if(args.Length>0&&args[0]=="--apply-launcher-update")return UpdateCore.FinishSelfUpdate(args);
    if(args.Length>0&&args[0]=="--qa-check"){string root=args.Length>3?args[3]:Path.Combine(Path.GetTempPath(),"tf2008qa");Console.WriteLine(UpdateCore.CheckRemote(args[1],args[2],root));return 0;}
    if(args.Length>0&&args[0]=="--qa-install"){string result=UpdateCore.UpdateGame(args[1],args[2],false);Console.WriteLine(result);return 0;}
    if(args.Length>0&&args[0]=="--qa-repair"){string result=UpdateCore.UpdateGame(args[1],args[2],true);Console.WriteLine(result);return 0;}
    if(args.Length>0&&args[0]=="--qa-auto"&&args.Length>=4){
     Console.WriteLine(UpdateCore.AutomaticStartup(args[1],args[2],args[3]));return 0;
    }
    if(args.Length>0&&args[0]=="--qa-selftest"){
     Console.WriteLine("v="+UpdateCore.Version+" gameConfig="+UpdaterSettings.Load().GameManifestUrl);
     Console.WriteLine("configuredRoot="+PlayerPrefs.Load().Root);return 0;
    }
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new LauncherWindow());return 0;
   }catch(Exception e){try{Console.Error.WriteLine(e.ToString());}catch{}return 50;}
  }
 }
}
