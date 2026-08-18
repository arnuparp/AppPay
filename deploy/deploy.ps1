<#
.SYNOPSIS
    Deploy Apppay (ASP.NET Core) ไปยัง IIS บน Windows Server
.DESCRIPTION
    ขั้นตอน: backup -> app_offline -> robocopy -> ลบ app_offline -> health check -> rollback ถ้าพัง
    เรียกจาก GitHub Actions (self-hosted runner) หรือรันมือบนเครื่อง VPS ก็ได้
.EXAMPLE
    .\deploy.ps1 -Source "C:\actions-runner\_work\apppay\apppay\publish" -Target "C:\inetpub\apppay"
#>
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$BackupRoot = "C:\deploy\backups\apppay",
    [string]$HealthUrl  = "http://localhost/",
    [int]   $KeepBackups = 5
)

$ErrorActionPreference = "Stop"
function Log($m) { Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $m" }

# ไฟล์ที่ห้ามแตะ: config production กับ log บนเครื่อง server
$ExcludeFiles = @("app_offline.htm", "appsettings.Production.json")
$ExcludeDirs  = @("logs", "App_Data")

if (-not (Test-Path $Source)) { throw "ไม่พบโฟลเดอร์ publish: $Source" }
New-Item -ItemType Directory -Force -Path $Target, $BackupRoot | Out-Null

# ---------- 1) Backup ของเดิมไว้ก่อน เผื่อ rollback ----------
$stamp  = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $BackupRoot $stamp
if ((Get-ChildItem $Target -Force | Measure-Object).Count -gt 0) {
    Log "Backup -> $backup"
    robocopy $Target $backup /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "backup ล้มเหลว (robocopy exit $LASTEXITCODE)" }
}

# ---------- 2) app_offline.htm: สั่งให้ IIS ปล่อย lock ไฟล์ dll ----------
$offline = Join-Path $Target "app_offline.htm"
Log "วาง app_offline.htm (แอปจะหยุดรับ request ชั่วคราว)"
@"
<!doctype html><html lang="th"><head><meta charset="utf-8">
<title>กำลังอัปเดตระบบ</title></head>
<body style="font-family:sans-serif;text-align:center;padding:80px">
<h2>กำลังอัปเดตระบบ</h2><p>อีกสักครู่จะกลับมาใช้งานได้ตามปกติ</p></body></html>
"@ | Set-Content -Path $offline -Encoding UTF8

# รอให้ worker process ปิดตัวและคืน lock
Start-Sleep -Seconds 5

try {
    # ---------- 3) คัดลอกไฟล์ใหม่ทับ (/MIR = ลบไฟล์เก่าที่ไม่มีแล้วด้วย) ----------
    Log "Copy $Source -> $Target"
    robocopy $Source $Target /MIR /R:5 /W:3 /NFL /NDL /NJH /NJS /NP `
        /XF $ExcludeFiles /XD $ExcludeDirs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copy ล้มเหลว (robocopy exit $LASTEXITCODE)" }
    Log "Copy สำเร็จ (robocopy exit $LASTEXITCODE)"
}
finally {
    # ---------- 4) เอา app_offline ออก แอปจะ start ใหม่ + รัน EF Migrate ----------
    Remove-Item $offline -Force -ErrorAction SilentlyContinue
    Log "เอา app_offline.htm ออกแล้ว"
}

# ---------- 5) Health check: ยิงจริงจนกว่าจะตอบ (แอปต้อง warm up + migrate ก่อน) ----------
$ok = $false
foreach ($i in 1..12) {
    try {
        $r = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 15 `
                               -MaximumRedirection 0 -ErrorAction Stop
        $code = $r.StatusCode
    } catch {
        $code = $_.Exception.Response.StatusCode.value__   # 302 ไป /Account/Login ก็ถือว่ารอด
    }
    if ($code -and $code -lt 500) { $ok = $true; Log "Health OK (HTTP $code) รอบที่ $i"; break }
    Log "ยังไม่ตอบ (HTTP $code) รอ 5 วิ... ($i/12)"
    Start-Sleep -Seconds 5
}

# ---------- 6) พังก็ถอยกลับอัตโนมัติ ----------
if (-not $ok) {
    if (Test-Path $backup) {
        Log "!! Health check ไม่ผ่าน -> ROLLBACK กลับไป $stamp"
        Set-Content -Path $offline -Value "<html><body>rolling back</body></html>"
        Start-Sleep -Seconds 5
        robocopy $backup $Target /MIR /R:5 /W:3 /NFL /NDL /NJH /NJS /NP `
            /XF $ExcludeFiles /XD $ExcludeDirs | Out-Null
        Remove-Item $offline -Force -ErrorAction SilentlyContinue
    }
    throw "Deploy ล้มเหลว: แอปไม่ตอบสนองหลัง deploy (ดู log ที่ $Target\logs)"
}

# ---------- 7) เก็บกวาด backup เก่า ----------
Get-ChildItem $BackupRoot -Directory | Sort-Object Name -Descending |
    Select-Object -Skip $KeepBackups | ForEach-Object {
        Log "ลบ backup เก่า $($_.Name)"; Remove-Item $_.FullName -Recurse -Force
    }

Log "Deploy สำเร็จ (backup ล่าสุด: $stamp)"
exit 0
