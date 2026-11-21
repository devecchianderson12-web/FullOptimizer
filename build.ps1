# Compila FullOptimizer.exe con el compilador C# que trae Windows (.NET Framework 4.x).
# Uso: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'No se encontró csc.exe (.NET Framework 4.x).' }
if (-not (Test-Path 'assets\FullOptimizer.ico')) { & "$PSScriptRoot\tools\make-icon.ps1" }
New-Item -ItemType Directory -Force dist | Out-Null
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
  /out:dist\FullOptimizer.exe `
  /win32icon:assets\FullOptimizer.ico `
  /win32manifest:src\app.manifest `
  /resource:assets\FullOptimizer.ico,FullOptimizer.ico `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.ServiceProcess.dll `
  src\FullOptimizer.cs
if ($LASTEXITCODE -ne 0) { throw "Error de compilación ($LASTEXITCODE)" }
Write-Host "OK -> $(Resolve-Path dist\FullOptimizer.exe)"
