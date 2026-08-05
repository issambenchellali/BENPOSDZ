# ============================================================
# publish.ps1 — بناء حزمة التحديث وإصدارها محلياً أو عبر GitHub CLI
#
# مثال:
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -Version 1.1.0
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -Version 1.1.0 -Repo "BENPOSDZ/BENPOSDZ" -Notes "إصلاحات وتحسينات"
#
# ينتج في مجلد releases\:
#   BENPOSDZ_<version>.zip   ← حزمة التحديث
#   version.json             ← ملف الإصدار (يفحصه البرنامج)
#   BENPOSDZ_Setup_<version>.exe ← مثبّت ويندوز (إن وُجد Inno Setup)
# ============================================================

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Repo = "",
    [string]$Notes = "تحديث BENPOSDZ"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$releasesDir = Join-Path $root "releases"
$tmp = Join-Path $env:TEMP "benpos_publish"
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

# كشف مسار ISCC.exe (Inno Setup)
$iscc = ""
$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe"
)
foreach ($c in $isccCandidates) { if (Test-Path $c) { $iscc = $c; break } }

Write-Host "1/4 نشر التطبيق الرئيسي..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "BENPOSDZ\BENPOSDZ.csproj") -c Release -f net8.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:WindowsPackageType=None -p:ApplicationDisplayVersion=$Version -p:ApplicationVersion=1
if ($LASTEXITCODE -ne 0) { throw "فشل نشر التطبيق الرئيسي" }

Write-Host "2/4 نشر أداة التحديث..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "BENPOSUpdater\BENPOSUpdater.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $tmp "updater")
if ($LASTEXITCODE -ne 0) { throw "فشل نشر أداة التحديث" }
$pub = Join-Path $root "BENPOSDZ\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
Copy-Item (Join-Path $tmp "updater\BENPOSUpdater.exe") $pub -Force

Write-Host "3/4 تجميع الحزمة..." -ForegroundColor Cyan
$pkg = Join-Path $tmp "package"
New-Item -ItemType Directory -Force -Path $pkg | Out-Null
Copy-Item (Join-Path $pub "*") $pkg -Recurse -Force
Copy-Item (Join-Path $tmp "updater\BENPOSUpdater.exe") $pkg -Force
Get-ChildItem $pkg -Filter *.pdb | Remove-Item -Force

$zip = Join-Path $releasesDir "BENPOSDZ_$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg "*") -DestinationPath $zip -Force

Write-Host "4/4 توليد version.json..." -ForegroundColor Cyan
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$zipUrl = if ($Repo -ne "") { "https://github.com/$Repo/releases/latest/download/BENPOSDZ_$Version.zip" } else { "" }
$json = @{ version = $Version; notes = $Notes; sha256 = $hash; zipUrl = $zipUrl } | ConvertTo-Json
$jsonPath = Join-Path $releasesDir "version.json"
Set-Content -Path $jsonPath -Value $json -Encoding UTF8

Write-Host "`nتم بنجاح:" -ForegroundColor Green
Write-Host "  ZIP : $zip"
Write-Host "  JSON: $jsonPath"
Write-Host "  SHA256: $hash"

# بناء المثبّت (Inno Setup) إن وُجد
$setup = ""
if ($iscc -ne "") {
    Write-Host "`n5/5 بناء المثبّت (Inno Setup)..." -ForegroundColor Cyan
    $issFile = Join-Path $root "installer\BENPOSDZ.iss"
    & $iscc $issFile /DAppVersion=$Version /DOutputDir=$releasesDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "فشل بناء المثبّت" }
    $setup = Join-Path $releasesDir "BENPOSDZ_Setup_$Version.exe"
    if (Test-Path $setup) { Write-Host "  SETUP: $setup" -ForegroundColor Green } else { Write-Host "  (لم يُعثر على ملف المثبّت)" }
} else {
    Write-Host "`n⚠ لم يُعثر على Inno Setup — لن يُبنى المثبّت. ثبّته ثم أعد التشغيل." -ForegroundColor Yellow
}

# رفع اختياري عبر GitHub CLI (يتطلب gh loginned)
if ($Repo -ne "" -and (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "`nرفع إلى GitHub Release v$Version..." -ForegroundColor Cyan
    gh release create "v$Version" $zip $jsonPath --repo $Repo --title "BENPOSDZ v$Version" --notes $Notes
    if ($LASTEXITCODE -eq 0) { Write-Host "تم الرفع بنجاح" -ForegroundColor Green }
}
