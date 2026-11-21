# Compila FullOptimizer.exe con el compilador C# que trae Windows (.NET Framework 4.x).
# Uso: powershell -ExecutionPolicy Bypass -File build.ps1 [-Version 1.0.5]
param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versión inválida: $Version (formato X.Y.Z)" }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'No se encontró csc.exe (.NET Framework 4.x).' }
if (-not (Test-Path 'assets\FullOptimizer.ico')) { & "$PSScriptRoot\tools\make-icon.ps1" }
New-Item -ItemType Directory -Force dist, obj | Out-Null

# Versión del exe (se ve en Propiedades > Detalles y en el título de la ventana)
$info = "[assembly: System.Reflection.AssemblyVersion(`"$Version.0`")]`r`n[assembly: System.Reflection.AssemblyFileVersion(`"$Version.0`")]`r`n[assembly: System.Reflection.AssemblyInformationalVersion(`"$Version`")]"
[IO.File]::WriteAllText("$PSScriptRoot\obj\Version.cs", $info)

& $csc /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
  /out:dist\FullOptimizer.exe `
  /win32icon:assets\FullOptimizer.ico `
  /win32manifest:src\app.manifest `
  /resource:assets\FullOptimizer.ico,FullOptimizer.ico `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.ServiceProcess.dll `
  src\FullOptimizer.cs obj\Version.cs
if ($LASTEXITCODE -ne 0) { throw "Error de compilación ($LASTEXITCODE)" }
Write-Host "OK v$Version -> $(Resolve-Path dist\FullOptimizer.exe)"
