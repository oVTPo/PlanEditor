param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
$Root = (Resolve-Path "$PSScriptRoot\..\..").Path
$Project = Join-Path $Root "PlanEditor.App\PlanEditor.App.csproj"
$Version = (Get-Content (Join-Path $Root "packaging\common\version.txt") -Raw).Trim()
$ShortVersion = ($Version -split "-")[0]
$Publish = Join-Path $Root "build\beta\windows\$Rid\publish"
$Dist = Join-Path $Root "dist\beta"
$AppIcon = Join-Path $Root "PlanEditor.App\Assets\AppIcon\app.ico"
$ProjectIcon = Join-Path $Root "PlanEditor.App\Assets\AppIcon\pas-project.ico"
$Iss = Join-Path $Root "packaging\windows\PA-S-Beta.iss"

if (-not (Test-Path $Project)) { throw "Không tìm thấy project: $Project" }
if (-not (Test-Path $AppIcon)) { throw "Thiếu app.ico: $AppIcon" }
if (-not (Test-Path $ProjectIcon)) { Write-Host "Chưa có pas-project.ico -> dùng app.ico tạm thời."; Copy-Item $AppIcon $ProjectIcon -Force }
if (Test-Path $Publish) { Remove-Item $Publish -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Publish | Out-Null
New-Item -ItemType Directory -Force -Path $Dist | Out-Null

dotnet publish $Project -c Release -r $Rid --self-contained true `
  -p:UseAppHost=true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -p:Version=$ShortVersion -o $Publish

$PasExe = Join-Path $Publish "PA-S.exe"
$OldExe = Join-Path $Publish "PlanEditor.App.exe"
if (-not (Test-Path $PasExe) -and (Test-Path $OldExe)) { Rename-Item $OldExe "PA-S.exe" }
if (-not (Test-Path $PasExe)) { throw "Không tìm thấy PA-S.exe. Hãy đặt <AssemblyName>PA-S</AssemblyName> trong csproj." }

Copy-Item $AppIcon (Join-Path $PSScriptRoot "app.ico") -Force
Copy-Item $ProjectIcon (Join-Path $PSScriptRoot "pas-project.ico") -Force

$PortableZip = Join-Path $Dist "PA-S-$Version-Windows-x64-Portable.zip"
if (Test-Path $PortableZip) { Remove-Item $PortableZip -Force }
Compress-Archive -Path "$Publish\*" -DestinationPath $PortableZip -CompressionLevel Optimal

$Candidates = @(
  "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
  "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$ISCC = $null
foreach ($Candidate in $Candidates) { if ($Candidate -and (Test-Path $Candidate)) { $ISCC = $Candidate; break } }
if (-not $ISCC) {
  Write-Host "Portable ZIP đã xong: $PortableZip"
  Write-Host "Cài Inno Setup 6 rồi chạy lại để tạo Setup.exe + Uninstall."
  exit 0
}
& $ISCC "/DMyAppVersion=$Version" "/DSourceDir=$Publish" "/DOutputDir=$Dist" $Iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup build thất bại." }
Write-Host "DONE"
Write-Host "Portable: $PortableZip"
Write-Host "Installer: $Dist\PA-S-$Version-Windows-x64-Setup.exe"
