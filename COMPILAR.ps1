$ErrorActionPreference="Stop"
$root=Split-Path -Parent $MyInvocation.MyCommand.Path
$csc="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$source=@((Join-Path $root "TF2Updater.cs"),(Join-Path $root "TF2Launcher.cs"))
$refs=@("/r:System.Windows.Forms.dll","/r:System.Drawing.dll","/r:System.Core.dll","/r:System.Web.Extensions.dll","/r:System.IO.Compression.dll","/r:System.IO.Compression.FileSystem.dll")
$out=Join-Path $root "TF2_2008_Launcher.exe"
$icon=Join-Path $root "assets\icon.ico"
& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ ($refs+@("/win32icon:$icon","/out:$out")+$source)
if($LASTEXITCODE -ne 0){throw "Compilacion Windows fallo"}
$console=Join-Path $root "qa\LauncherQAConsole.exe"
& $csc /nologo /codepage:65001 /target:exe /platform:anycpu /optimize+ ($refs+@("/out:$console")+$source)
if($LASTEXITCODE -ne 0){throw "Compilacion QA Console fallo"}
Write-Output "COMPILED:"
Get-Item $out,$console | Select-Object FullName,Length,LastWriteTime
Get-FileHash $out -Algorithm SHA256
