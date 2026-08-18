<#
.SYNOPSIS
    Deploy Apppay (ASP.NET Core) to IIS on Windows Server.
.DESCRIPTION
    Steps: backup -> app_offline -> robocopy -> remove app_offline -> health check -> rollback on failure.
    Called from GitHub Actions (self-hosted runner) or run manually on the VPS.
.NOTES
    ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less .ps1 files as ANSI,
    so any non-ASCII character here would corrupt the whole script.
.EXAMPLE
    .\deploy.ps1 -Source "C:\actions-runner\_work\AppPay\AppPay\publish" -Target "C:\inetpub\apppay"
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

# Files that must survive a deploy: production config and logs written on the server.
$ExcludeFiles = @("app_offline.htm", "appsettings.Production.json")
$ExcludeDirs  = @("logs", "App_Data")

if (-not (Test-Path $Source)) { throw "Publish folder not found: $Source" }
New-Item -ItemType Directory -Force -Path $Target, $BackupRoot | Out-Null

# ---------- 1) Back up the current release so we can roll back ----------
$stamp  = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $BackupRoot $stamp
$hasBackup = $false
if ((Get-ChildItem $Target -Force | Measure-Object).Count -gt 0) {
    Log "Backup -> $backup"
    robocopy $Target $backup /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Backup failed (robocopy exit $LASTEXITCODE)" }
    $hasBackup = $true
}

# ---------- 2) app_offline.htm makes IIS shut the app down and release file locks ----------
$offline = Join-Path $Target "app_offline.htm"
Log "Writing app_offline.htm (app stops serving requests)"
$offlineHtml = @"
<!doctype html><html lang="th"><head><meta charset="utf-8">
<title>&#3585;&#3635;&#3621;&#3633;&#3591;&#3629;&#3633;&#3611;&#3648;&#3604;&#3605;&#3619;&#3632;&#3610;&#3610;</title></head>
<body style="font-family:sans-serif;text-align:center;padding:80px">
<h2>&#3585;&#3635;&#3621;&#3633;&#3591;&#3629;&#3633;&#3611;&#3648;&#3604;&#3605;&#3619;&#3632;&#3610;&#3610;</h2><p>&#3629;&#3637;&#3585;&#3626;&#3633;&#3585;&#3588;&#3619;&#3641;&#3656;&#3592;&#3632;&#3585;&#3621;&#3633;&#3610;&#3617;&#3634;&#3651;&#3594;&#3657;&#3591;&#3634;&#3609;&#3652;&#3604;&#3657;&#3605;&#3634;&#3617;&#3611;&#3585;&#3605;&#3636;</p></body></html>
"@
Set-Content -Path $offline -Value $offlineHtml -Encoding UTF8

# Give the worker process time to shut down and release the DLLs
Start-Sleep -Seconds 5

try {
    # ---------- 3) Copy the new release over (/MIR also deletes files that no longer exist) ----------
    Log "Copy $Source -> $Target"
    robocopy $Source $Target /MIR /R:5 /W:3 /NFL /NDL /NJH /NJS /NP `
        /XF $ExcludeFiles /XD $ExcludeDirs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed (robocopy exit $LASTEXITCODE)" }
    Log "Copy OK (robocopy exit $LASTEXITCODE)"
}
finally {
    # ---------- 4) Remove app_offline: app starts again and runs EF migrations ----------
    Remove-Item $offline -Force -ErrorAction SilentlyContinue
    Log "Removed app_offline.htm"
}

# ---------- 5) Health check: the app needs time to warm up and migrate ----------
$ok = $false
foreach ($i in 1..12) {
    $code = $null
    try {
        $r = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 15 `
                               -MaximumRedirection 0 -ErrorAction Stop
        $code = $r.StatusCode
    } catch {
        # A 302 to /Account/Login still means the app is alive
        $code = $_.Exception.Response.StatusCode.value__
    }
    if ($code -and $code -lt 500) { $ok = $true; Log "Health OK (HTTP $code) on attempt $i"; break }
    Log "No response yet (HTTP $code), retrying in 5s... ($i/12)"
    Start-Sleep -Seconds 5
}

# ---------- 6) Roll back automatically if the app never came up ----------
if (-not $ok) {
    if ($hasBackup) {
        Log "!! Health check failed -> ROLLING BACK to $stamp"
        Set-Content -Path $offline -Value "<html><body>rolling back</body></html>"
        Start-Sleep -Seconds 5
        robocopy $backup $Target /MIR /R:5 /W:3 /NFL /NDL /NJH /NJS /NP `
            /XF $ExcludeFiles /XD $ExcludeDirs | Out-Null
        Remove-Item $offline -Force -ErrorAction SilentlyContinue
    }
    throw "Deploy failed: app did not respond after deploy (check $Target\logs)"
}

# ---------- 7) Prune old backups ----------
Get-ChildItem $BackupRoot -Directory | Sort-Object Name -Descending |
    Select-Object -Skip $KeepBackups | ForEach-Object {
        Log "Removing old backup $($_.Name)"; Remove-Item $_.FullName -Recurse -Force
    }

Log "Deploy succeeded (latest backup: $stamp)"
exit 0
