param([string]$Configuration = "Release", [string]$ActPath = "C:\Program Files (x86)\Advanced Combat Tracker\Advanced Combat Tracker.exe")
$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $ActPath)) { throw "Advanced Combat Tracker.exe was not found. Pass -ActPath." }
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path -LiteralPath $msbuild)) { throw "Visual Studio MSBuild was not found." }
& $msbuild (Join-Path $PSScriptRoot "ChatRelay.sln") /t:Build "/p:Configuration=$Configuration" "/p:ActPath=$ActPath" /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$testExe = Join-Path $PSScriptRoot "tests\ChatRelay.Tests\bin\$Configuration\net48\ChatRelay.Tests.exe"
Push-Location $PSScriptRoot
try { & $testExe } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
$dll = Join-Path $PSScriptRoot "src\ChatRelay\bin\$Configuration\net48\ChatRelay.dll"
$version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)
$dist = Join-Path $PSScriptRoot "dist"; $stage = Join-Path $dist "package"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $stage "ChatRelay.dll") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "README.md") -Destination (Join-Path $stage "README.md") -Force
$zipName = "ChatRelay-v$version.zip"; $zip = Join-Path $dist $zipName
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $dist "ChatRelay-v$version.sha256") -Value "$hash  $zipName" -Encoding ascii
& (Join-Path $PSScriptRoot 'verify-release.ps1') -ZipPath $zip -Version $version
Write-Host "Built $dll"; Write-Host "Release asset $zip"


