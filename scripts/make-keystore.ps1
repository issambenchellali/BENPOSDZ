# make-keystore.ps1 — توليد Keystore توقيع Release لأندرويد (مرة واحدة فقط)
# الاستخدام: powershell -ExecutionPolicy Bypass -File scripts\make-keystore.ps1
# ينتج: keys\benpos-release.keystore + keys\keystore-password.txt
# تنبيه: لا ترفع keys\ إلى git إطلاقاً. استخدم كلمة المرور المخزنة لملء أسرار GitHub.

param(
    [string]$KeyStorePath = (Join-Path $PSScriptRoot "..\keys\benpos-release.keystore"),
    [string]$Alias = "benpos",
    [string]$StorePass = "",
    [string]$KeyPass = "",
    [string]$Dn = "CN=BENPOSDZ POS, OU=POS, O=BENPOSDZ, L=Algiers, C=DZ",
    [int]$ValidityDays = 10000
)

$ErrorActionPreference = "Stop"
$keyStorePath = [System.IO.Path]::GetFullPath($KeyStorePath)
$passFile = [System.IO.Path]::ChangeExtension($keyStorePath, ".password.txt")
$keyDir = [System.IO.Path]::GetDirectoryName($keyStorePath)

if (Test-Path -LiteralPath $keyStorePath) {
    Write-Host "الملف موجود مسبقاً: $keyStorePath — لن يُعاد توليده." -ForegroundColor Yellow
    exit 0
}

# تحديد keytool (من JAVA_HOME أو PATH)
$keytool = Join-Path $env:JAVA_HOME "bin\keytool.exe"
if (-not (Test-Path -LiteralPath $keytool)) {
    $candidate = Get-Command keytool -ErrorAction SilentlyContinue
    if ($candidate) { $keytool = $candidate.Source } else { throw "لم يُعثر على keytool. اضبط JAVA_HOME." }
}

# كلمة مرور عشوائية قوية إن لم تُمرَّر
if (-not $StorePass) { $StorePass = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ }) }
if (-not $KeyPass) { $KeyPass = $StorePass }

New-Item -ItemType Directory -Force -Path $keyDir | Out-Null

& $keytool -genkeypair -v `
    -keystore $keyStorePath `
    -alias $Alias `
    -keyalg RSA -keysize 2048 `
    -validity $ValidityDays `
    -storepass $StorePass -keypass $KeyPass `
    -dname $Dn

if ($LASTEXITCODE -ne 0) { throw "فشل keytool (رمز $LASTEXITCODE)" }

# تخزين كلمة المرور محلياً (لا تُرفع إلى git)
@{
    storePath = $keyStorePath
    alias     = $Alias
    storePass = $StorePass
    keyPass   = $KeyPass
} | ConvertTo-Json | Set-Content -Path $passFile -Encoding UTF8

Write-Host ""
Write-Host "تم توليد Keystore:" -ForegroundColor Green
Write-Host "  الملف : $keyStorePath"
Write-Host "  كلمة المرور مخزنة في: $passFile (محلية فقط — لا ترفعها!)"
Write-Host ""
Write-Host "لتفعيل التوقيع في CI، عيّن أسرار GitHub التالية:" -ForegroundColor Cyan
Write-Host "  ANDROID_KEYSTORE      = محتوى ملف الـ keystore (Base64)"
Write-Host "  ANDROID_KEYSTORE_PASS = $StorePass"
Write-Host "  ANDROID_ALIAS          = $Alias"
