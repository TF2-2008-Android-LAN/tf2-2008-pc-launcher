param([string]$Destination="", [switch]$SinAccesoDirecto)
$ErrorActionPreference="Stop"
Add-Type -AssemblyName System.Windows.Forms
$src=Split-Path -Parent $MyInvocation.MyCommand.Path
if(!$Destination){
 $dlg=New-Object System.Windows.Forms.FolderBrowserDialog
 $dlg.Description="Elegí dónde querés instalar el launcher de TF2 2008"
 $dlg.ShowNewFolderButton=$true
 $dlg.SelectedPath=[Environment]::GetFolderPath("LocalApplicationData")
 if($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK){Write-Output "Cancelado por el usuario";exit 1}
 $Destination=Join-Path $dlg.SelectedPath "TF2 2008 Launcher"
}
$Destination=[IO.Path]::GetFullPath($Destination)
if($Destination.TrimEnd('\') -eq [IO.Path]::GetPathRoot($Destination).TrimEnd('\')){throw "No instalar en la raíz del disco"}
New-Item -ItemType Directory -Path $Destination -Force|Out-Null
foreach($f in @("TF2_2008_Launcher.exe","updates.ini")){
 $from=Join-Path $src $f
 if(!(Test-Path $from)){throw "Falta $f"}
 Copy-Item $from (Join-Path $Destination $f) -Force
}
New-Item -ItemType Directory -Path (Join-Path $Destination 'assets') -Force | Out-Null
Copy-Item (Join-Path $src 'assets\*') (Join-Path $Destination 'assets') -Recurse -Force
$exe=Join-Path $Destination "TF2_2008_Launcher.exe"
if(!$SinAccesoDirecto){
 $shortcut=Join-Path ([Environment]::GetFolderPath('Desktop')) "TF2 2008 Launcher.lnk"
 $w=New-Object -ComObject WScript.Shell
 $s=$w.CreateShortcut($shortcut)
 $s.TargetPath=$exe
 $s.WorkingDirectory=$Destination
 $s.Description="Launcher y actualizador de TF2 2008 para PC"
 $ico=Join-Path $Destination "assets\icon.ico"
 if(Test-Path $ico){$s.IconLocation=$ico}
 $s.Save()
 Write-Output "Acceso directo: $shortcut"
}
Write-Output "Launcher instalado en: $Destination"
Write-Output "Configuracion personal: $env:LOCALAPPDATA\TF2-2008\launcher.ini"
