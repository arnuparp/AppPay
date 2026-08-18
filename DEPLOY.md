# Auto Deploy: Apppay → VPS (Windows Server + IIS)

คู่มือนี้พาไปตั้งแต่ศูนย์จนถึง **push code แล้วขึ้น production เอง** ตั้งครั้งเดียวจบ หลังจากนั้นแค่ `git push`

---

## 0. ภาพรวม — มันทำงานยังไง

```
เครื่องคุณ (Windows)                GitHub                    VPS 165.101.65.249
─────────────────────              ──────────                ─────────────────────────
git push  ───────────────────────►  repo                      ┌──────────────────────┐
                                     │                        │ Actions Runner (svc) │
                                     │  ◄── runner ถามทุก ~วิ ──┤  "มีงานให้ทำมั้ย?"     │
                                     └── "มี! deploy commit นี้" ►│                      │
                                                              │  1. git checkout     │
                                                              │  2. dotnet publish   │
                                                              │  3. deploy.ps1       │
                                                              │     └► C:\inetpub\apppay
                                                              │           ▲          │
                                                              │       IIS │ :80      │
                                                              │       SQL Server     │
                                                              └──────────────────────┘
```

**ทำไมเลือก self-hosted runner (ไม่ใช่ FTP / Web Deploy / SSH)**

| ข้อ | เหตุผล |
|---|---|
| ไม่ต้องเปิดพอร์ตขาเข้าเลย | runner วิ่ง **ออก** ไปหา GitHub (outbound 443) ไม่ต้องเปิด 8172 / 5985 / 22 ให้โลกยิงเข้ามา |
| ฟรี ไม่จำกัดนาที | private repo บน GitHub Free จำกัด 2,000 นาที/เดือน แต่ **self-hosted runner ไม่นับนาที** |
| ไม่ต้องเก็บรหัส VPS ใน GitHub | ไม่มี SSH key / password ใน Secrets ให้หลุด |
| build บนเครื่องจริง | environment เหมือน production 100% |

**ข้อควรระวัง:** runner รันโค้ดจาก repo บนเครื่อง VPS ด้วยสิทธิ์ที่เราให้ → ใช้กับ **private repo หรือ repo ที่เราคุมคนเข้าถึงได้เท่านั้น** (public repo ที่รับ PR จากคนนอก ห้ามใช้ self-hosted เด็ดขาด เพราะ PR แก้ workflow ให้รันอะไรก็ได้บนเครื่องเรา)

---

## 1. เตรียมโค้ดฝั่งเรา (ทำบนเครื่องตัวเอง)

### 1.1 แยก secret ออกจาก repo — *ยังไม่ต้องทำตอนนี้ ทำตอนขึ้นใช้จริง*

ตอนนี้ยังเป็นช่วงเทสต์ connection string เลยยังอยู่ใน `appsettings.json` ตามเดิม ใช้งานได้ปกติ ข้ามข้อนี้ไปข้อ 1.2 ได้เลย

ส่วนด้านล่างนี้คือสิ่งที่ต้องทำ **วันที่ระบบเริ่มมีข้อมูลจริง** — pipeline ที่เหลือรองรับไว้ให้แล้ว (`.gitignore` กันไฟล์ Production ไว้ และ `deploy.ps1` มี `/XF` ไม่ทับไฟล์นั้นบน server) เปลี่ยนวันไหนก็ไม่ต้องแก้ workflow

ค่าจริงจะย้ายไปอยู่ 2 ที่:

| ที่เก็บ | ใช้ตอน | ทำไมปลอดภัย |
|---|---|---|
| **user-secrets** | dev บนเครื่องตัวเอง | ไฟล์อยู่นอกโฟลเดอร์โปรเจกต์ (`%APPDATA%\Microsoft\UserSecrets`) ไม่มีทางหลุดขึ้น git |
| **appsettings.Production.json** | บน VPS | วางมือครั้งเดียวบน server, อยู่ใน `.gitignore`, และ deploy script ข้ามไฟล์นี้ไม่ทับ |

ตั้ง user-secrets สำหรับ dev (รันในโฟลเดอร์ `Apppay\Apppay`):

```bash
dotnet user-secrets init
```

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=PayDB_Dev;Trusted_Connection=True;TrustServerCertificate=True;"
```

แล้วเปลี่ยน `appsettings.json` ในโค้ดให้ `"DefaultConnection": ""` (ค่าจริงมาจาก 2 ที่ข้างบนแทน)

### 1.2 เรื่อง HTTPS ต้องตัดสินใจตอนนี้

`Program.cs` มี `app.UseHttpsRedirection()` แต่ VPS มีแค่ IP ไม่มีโดเมน → **Let's Encrypt ออกใบรับรองให้ IP เปล่า ๆ ไม่ได้** ถ้า IIS เปิดแค่พอร์ต 80 แอปจะ redirect ไป `https://` ที่ไม่มีใครฟัง = เว็บเปิดไม่ขึ้น

เลือกทางใดทางหนึ่ง:

**(ก) ยังไม่มีโดเมน — ปิด redirect ไปก่อน** แก้ `Program.cs`:

```csharp
if (app.Configuration.GetValue<bool>("UseHttpsRedirect"))
{
    app.UseHttpsRedirection();
}
```

แล้วค่อยเติม `"UseHttpsRedirect": true` ใน `appsettings.Production.json` วันที่มี cert แล้ว

**(ข) มีโดเมน (แนะนำ)** — ชี้ A record มาที่ `165.101.65.249` แล้วใช้ [win-acme](https://www.win-acme.com/) ออก cert ฟรี ต่ออายุอัตโนมัติ (ข้อ 9.1) โค้ดเดิมใช้ได้เลย ไม่ต้องแก้

### 1.3 push ขึ้น GitHub

สร้าง repo แบบ **Private** ชื่อ `apppay` บน GitHub แล้วรันที่ `D:\apppay\Apppay`:

```bash
git init -b main
```

```bash
git add . && git status
```

ตรวจให้แน่ใจว่า **ไม่มี** `appsettings.Production.json`, `bin-v2/`, `obj-v2/`, `.vs/` อยู่ในลิสต์ แล้วค่อย:

```bash
git commit -m "Initial commit: Apppay + auto deploy pipeline"
```

```bash
git remote add origin https://github.com/<user>/apppay.git && git push -u origin main
```

รอบแรก workflow จะรัน **แล้วค้าง/พัง** เพราะยังไม่มี runner — ปกติ ไปทำข้อ 2 ต่อได้เลย

---

## 2. เตรียม VPS — ติดตั้ง IIS (RDP เข้าไปทำครั้งเดียว)

เปิด **PowerShell แบบ Run as Administrator** บน VPS

### 2.1 ติดตั้ง IIS

```powershell
Install-WindowsFeature -Name Web-Server,Web-Mgmt-Console,Web-Http-Logging -IncludeManagementTools
```

### 2.2 ติดตั้ง .NET 9 Hosting Bundle

ตัวนี้คือของที่ทำให้ IIS รัน ASP.NET Core ได้ (ให้ **ASP.NET Core Module V2** มา) — ต้องเป็น **Hosting Bundle** ไม่ใช่ Runtime เฉย ๆ

```powershell
Invoke-WebRequest -Uri "https://aka.ms/dotnet/9.0/dotnet-hosting-win.exe" -OutFile "$env:TEMP\hosting.exe"
Start-Process "$env:TEMP\hosting.exe" -ArgumentList "/quiet /norestart" -Wait
net stop was /y; net start w3svc
```

เช็คผล:

```powershell
dotnet --list-runtimes
```

### 2.3 ติดตั้ง .NET 9 SDK (runner ต้อง build บนเครื่องนี้)

```powershell
winget install Microsoft.DotNet.SDK.9
```

### 2.4 สร้างโฟลเดอร์ + App Pool + Site

```powershell
New-Item -ItemType Directory -Force -Path C:\inetpub\apppay, C:\inetpub\apppay\logs, C:\deploy\backups\apppay
Import-Module WebAdministration

# ปลดพอร์ต 80 จาก Default Web Site ก่อน
Stop-Website "Default Web Site" -ErrorAction SilentlyContinue
Set-ItemProperty "IIS:\Sites\Default Web Site" -Name serverAutoStart -Value $false -ErrorAction SilentlyContinue

# App Pool: ASP.NET Core ต้องเป็น "No Managed Code"
New-WebAppPool -Name "apppay"
Set-ItemProperty IIS:\AppPools\apppay -Name managedRuntimeVersion -Value ""
Set-ItemProperty IIS:\AppPools\apppay -Name startMode -Value "AlwaysRunning"
Set-ItemProperty IIS:\AppPools\apppay -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

# Site
New-Website -Name "apppay" -PhysicalPath "C:\inetpub\apppay" -ApplicationPool "apppay" -Port 80 -Force
Start-Website "apppay"
```

> `AlwaysRunning` + `idleTimeout 0` = แอปไม่หลับตอนไม่มีคนใช้ (EF + Identity warm up ช้า ครั้งแรกจะรอนาน)

### 2.5 เปิดไฟร์วอลล์เฉพาะเว็บ

```powershell
New-NetFirewallRule -DisplayName "HTTP 80" -Direction Inbound -Protocol TCP -LocalPort 80 -Action Allow
```

### 2.6 ให้สิทธิ์ App Pool

```powershell
icacls "C:\inetpub\apppay" /grant "IIS AppPool\apppay:(OI)(CI)RX" /T
icacls "C:\inetpub\apppay\logs" /grant "IIS AppPool\apppay:(OI)(CI)M" /T
```

---

## 3. Config production + ล็อกดาวน์ฐานข้อมูล

> **ช่วงเทสต์ข้ามทั้งข้อ 3 ได้** — connection string ยังอยู่ใน `appsettings.json` ที่ deploy ไปพร้อมโค้ด แอปทำงานได้เลย
> ทำข้อนี้ตอนระบบเริ่มมีข้อมูลจริง

### 3.1 วางไฟล์ config จริงบน VPS (ครั้งเดียว ไม่เกี่ยวกับ git)

ดูเทมเพลตที่ `deploy\appsettings.Production.json.example` แล้วสร้างของจริง:

```powershell
@'
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=PayDB;User Id=apppay;Password=<รหัสใหม่ที่ตั้งเอง>;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;"
  },
  "Logging": { "LogLevel": { "Default": "Warning", "Apppay": "Information" } }
}
'@ | Set-Content C:\inetpub\apppay\appsettings.Production.json -Encoding UTF8
```

> ASP.NET Core อ่านไฟล์นี้อัตโนมัติ เพราะเมื่อไม่ได้ตั้ง `ASPNETCORE_ENVIRONMENT` ค่าดีฟอลต์คือ **Production**
> และ `deploy.ps1` มี `/XF appsettings.Production.json` → deploy กี่รอบไฟล์นี้ก็ไม่โดนทับหรือลบ

### 3.2 สร้าง SQL login แยกให้แอป (เลิกใช้ sa)

```sql
CREATE LOGIN apppay WITH PASSWORD = '<รหัสยาว ๆ>', CHECK_POLICY = ON;
USE PayDB;
CREATE USER apppay FOR LOGIN apppay;
ALTER ROLE db_owner ADD MEMBER apppay;   -- ต้อง db_owner เพราะแอปรัน EF Migrate ตอน start
```

### 3.3 ปิด 1433 ไม่ให้เข้าจากอินเทอร์เน็ต

ตอนนี้ทั้งโลกยิง `165.101.65.249:1433` ได้ และ `sa` เปิดอยู่ = โดน brute force แน่นอน แอปกับ DB อยู่เครื่องเดียวกันแล้ว ไม่ต้องออกเน็ต:

```powershell
Get-NetFirewallRule -DisplayName "*SQL*" | Set-NetFirewallRule -Enabled False
New-NetFirewallRule -DisplayName "Block SQL 1433 from Internet" -Direction Inbound -Protocol TCP -LocalPort 1433 -Action Block
```

ถ้ายังต้องต่อจากเครื่องตัวเองเพื่อดูข้อมูล ให้อนุญาตเฉพาะ IP ตัวเอง:

```powershell
New-NetFirewallRule -DisplayName "SQL 1433 - my office" -Direction Inbound -Protocol TCP -LocalPort 1433 -RemoteAddress <IP ออฟฟิศ> -Action Allow
```

---

## 4. ติดตั้ง GitHub Actions Runner บน VPS (หัวใจของ auto deploy)

### 4.1 เอา token จาก GitHub

repo → **Settings** → **Actions** → **Runners** → **New self-hosted runner** → **Windows / x64**
หน้านั้นจะโชว์คำสั่งพร้อม token (token หมดอายุใน 1 ชั่วโมง ใช้ไม่ทันก็กดใหม่)

### 4.2 ติดตั้งบน VPS (PowerShell as Administrator)

```powershell
mkdir C:\actions-runner; cd C:\actions-runner
Invoke-WebRequest -Uri https://github.com/actions/runner/releases/download/v2.328.0/actions-runner-win-x64-2.328.0.zip -OutFile runner.zip
Expand-Archive -Path runner.zip -DestinationPath . -Force
```

> เลขเวอร์ชันให้ก๊อปจากหน้า New self-hosted runner ของ GitHub ให้ตรง

ตั้งค่า — **`--labels` ต้องตรงกับ `runs-on` ใน workflow**:

```powershell
.\config.cmd --url https://github.com/<user>/apppay --token <TOKEN> --name apppay-vps --labels self-hosted,windows,apppay --work _work --runasservice --unattended
```

`--runasservice` = ติดตั้งเป็น Windows Service ชื่อ `actions.runner.<user>-apppay.apppay-vps` → **รีบูตเครื่องแล้วขึ้นเอง**

### 4.3 ให้ service มีสิทธิ์เขียนโฟลเดอร์ IIS

ดีฟอลต์ service รันเป็น `NT AUTHORITY\NETWORK SERVICE` ซึ่งเขียน `C:\inetpub` ไม่ได้ เลือกทางใดทางหนึ่ง:

**(ก) ให้สิทธิ์เฉพาะที่จำเป็น — แนะนำ ปลอดภัยกว่า**

```powershell
icacls "C:\inetpub\apppay" /grant "NETWORK SERVICE:(OI)(CI)F" /T
icacls "C:\deploy" /grant "NETWORK SERVICE:(OI)(CI)F" /T
```

**(ข) รัน service เป็น account ที่เป็น Administrator** — ตอน `config.cmd` เติม:

```
--windowslogonaccount "<DOMAIN\user>" --windowslogonpassword "<password>"
```

### 4.4 เช็คว่า runner ขึ้นแล้ว

```powershell
Get-Service actions.runner.* | Select-Object Name, Status
```

กลับไปหน้า **Settings → Actions → Runners** ต้องเห็น 🟢 **Idle**

---

## 5. ไฟล์ที่มีอยู่ในโปรเจกต์

```
Apppay/
├─ .github/workflows/deploy.yml           ← สูตร CI/CD (GitHub อ่านไฟล์นี้เอง)
├─ deploy/
│  ├─ deploy.ps1                          ← สคริปต์ deploy จริงที่รันบน VPS
│  └─ appsettings.Production.json.example ← เทมเพลต config ฝั่ง server
├─ .gitignore                             ← กัน secret / bin / obj หลุด
└─ DEPLOY.md                              ← ไฟล์นี้
```

### 5.1 `deploy.yml` อ่านทีละท่อน

| ท่อน | ทำอะไร |
|---|---|
| `on: push: branches: [main]` | push เข้า `main` เมื่อไหร่ = deploy ทันที (แก้แค่ `.md` ไม่ deploy) |
| `on: workflow_dispatch` | เพิ่มปุ่ม **Run workflow** ในหน้า Actions ไว้สั่งเอง |
| `concurrency: deploy-production` | กัน 2 deploy วิ่งชนกัน มีค้างอยู่ให้ต่อคิว |
| `runs-on: [self-hosted, windows, apppay]` | เลือกเครื่อง — ต้องตรงกับ `--labels` ตอน config runner |
| `actions/checkout@v4` | ดึงโค้ด commit นั้นลง `C:\actions-runner\_work\apppay\apppay` |
| `dotnet restore / build / publish` | คอมไพล์ — ถ้า error ตรงนี้ **หยุด ไม่แตะ production เลย** |
| `deploy.ps1` | เอาผลลัพธ์ที่ publish ไปวาง IIS (ข้อ 5.2) |
| `Summary` | สรุปผลโชว์ในหน้า run |

### 5.2 `deploy.ps1` ทำอะไรบ้าง

1. **Backup** ของเดิมไป `C:\deploy\backups\apppay\<วันเวลา>` — เก็บ 5 ชุดล่าสุด
2. **วาง `app_offline.htm`** — ท่ามาตรฐานของ ASP.NET Core บน IIS พอเจอไฟล์นี้ IIS จะปิดแอป **คืน lock ไฟล์ .dll** และโชว์หน้า "กำลังอัปเดต" ให้ผู้ใช้ (ไม่ต้องมีสิทธิ์ stop app pool)
3. **robocopy /MIR** — คัดลอกทับ + ลบไฟล์เก่าที่ไม่มีแล้ว โดย `/XF` ข้าม `appsettings.Production.json` และ `/XD` ข้าม `logs`
4. **ลบ `app_offline.htm`** — แอป start ใหม่ แล้วรัน `db.Database.Migrate()` อัปเดต schema เอง
5. **Health check** — ยิง `http://localhost/` ได้ถึง 12 รอบ (รวม ~1 นาที) รับทุก HTTP < 500 (302 เด้งไป `/Account/Login` ก็ถือว่าปกติ)
6. **Rollback อัตโนมัติ** — health ไม่ผ่าน = คืนไฟล์จาก backup แล้ว fail workflow

> เกร็ด: `robocopy` คืน exit code 1–7 แปลว่า **สำเร็จ** (0=ไม่มีอะไรเปลี่ยน, 1=copy แล้ว, 3=copy+ลบ) สคริปต์เลยเช็ค `-ge 8` ถึงถือว่าพัง — คนเขียนเองมักลืมข้อนี้แล้ว pipeline fail ทั้งที่ deploy สำเร็จ

---

## 6. ลองของจริง

```bash
git commit --allow-empty -m "test: trigger deploy" && git push
```

ไปที่ repo → แท็บ **Actions** → คลิก run ล่าสุด → กดดู log ทีละ step
เสร็จแล้วเปิด `http://165.101.65.249/`

ต่อจากนี้ชีวิตประจำวันเหลือแค่:

```bash
git add . && git commit -m "แก้หน้าจ่ายเงิน" && git push
```

จบ — ที่เหลือระบบทำเอง ประมาณ 2–4 นาที

---

## 7. Rollback ด้วยมือ (ตอนโค้ดขึ้นแล้วแต่ logic ผิด)

health check จับได้แค่ "แอปไม่ตาย" ถ้าแอปรอดแต่ทำงานผิด ต้องถอยเอง — มี 2 ทาง:

**(ก) ถอยจาก backup บน VPS — เร็วสุด ~10 วินาที**

```powershell
Get-ChildItem C:\deploy\backups\apppay | Sort-Object Name -Descending | Select-Object -First 5
```

```powershell
$b = "C:\deploy\backups\apppay\20260818-143000"   # เลือกชุดที่ต้องการ
Set-Content C:\inetpub\apppay\app_offline.htm "rolling back"
Start-Sleep 5
robocopy $b C:\inetpub\apppay /MIR /XF app_offline.htm appsettings.Production.json /XD logs
Remove-Item C:\inetpub\apppay\app_offline.htm
```

**(ข) ถอยผ่าน git — ดีกว่าในระยะยาว เพราะโค้ดกับ production ตรงกันเสมอ**

```bash
git revert <commit ที่พัง> && git push
```

> **ระวังเรื่อง migration:** ถ้า commit ที่พังมี EF migration ที่ลบคอลัมน์ไปแล้ว การ rollback โค้ดอย่างเดียวไม่คืนข้อมูลให้ — ต้องกู้จาก backup ของ SQL ดูข้อ 9.3

---

## 8. Log อยู่ไหน / แก้ปัญหาที่เจอบ่อย

### จุดที่ต้องดู log

| อาการ | ดูที่ |
|---|---|
| build พัง | GitHub → Actions → run นั้น → step ที่แดง |
| deploy พัง / rollback | step **Deploy to IIS** (สคริปต์ print ทุกขั้นพร้อมเวลา) |
| แอปพังตอนรัน | **Event Viewer → Windows Logs → Application** (source: `IIS AspNetCore Module V2`) |
| ขอ log ละเอียด | เปิด stdout log ชั่วคราว (ดูด้านล่าง) |

เปิด stdout log บน VPS ชั่วคราว — แก้ `C:\inetpub\apppay\web.config` ให้เป็น `stdoutLogEnabled="true"` แล้วดูไฟล์ใน `C:\inetpub\apppay\logs\`

```powershell
(Get-Content C:\inetpub\apppay\web.config) -replace 'stdoutLogEnabled="false"','stdoutLogEnabled="true"' | Set-Content C:\inetpub\apppay\web.config
Get-ChildItem C:\inetpub\apppay\logs | Sort LastWriteTime -Desc | Select -First 1 | Get-Content -Tail 50
```

> อย่าลืมปิดกลับ ไม่งั้นไฟล์บวมเรื่อย ๆ (และ deploy รอบหน้า `web.config` ก็จะถูกทับด้วยของใหม่อยู่แล้ว)

### ตารางอาการ → สาเหตุ

| อาการ | สาเหตุที่พบบ่อย | วิธีแก้ |
|---|---|---|
| workflow ค้าง "Waiting for a runner" | label ไม่ตรง หรือ service ไม่ได้รัน | `Get-Service actions.runner.*` / เช็ค `--labels` ให้ตรง `runs-on` |
| `HTTP Error 500.19` | ไม่ได้ลง Hosting Bundle | ทำข้อ 2.2 แล้ว `net stop was /y; net start w3svc` |
| `HTTP Error 500.30` — start ไม่ขึ้น | connection string ผิด / ต่อ SQL ไม่ได้ / migration พัง | ดู Event Viewer, ทดสอบ connection string, เช็คว่า `appsettings.Production.json` ยังอยู่ |
| `HTTP Error 502.5` | .NET runtime version ไม่ตรงกับที่ publish | ลง Hosting Bundle 9.0 ให้ตรง |
| deploy แล้วไฟล์ไม่เปลี่ยน / access denied | app_offline ไม่ทัน หรือ NETWORK SERVICE ไม่มีสิทธิ์ | เพิ่ม `Start-Sleep` เป็น 10 วิ / ทำข้อ 4.3 |
| เว็บเด้ง https แล้วเปิดไม่ได้ | `UseHttpsRedirection` ทั้งที่ไม่มี cert | ข้อ 1.2 |
| เข้าเว็บแล้วขึ้นหน้า "กำลังอัปเดตระบบ" ค้าง | deploy พังกลางทาง `app_offline.htm` ค้าง | `Remove-Item C:\inetpub\apppay\app_offline.htm` |
| ครั้งแรกหลัง deploy ช้ามาก | EF + Identity warm up | ตั้ง `AlwaysRunning` แล้วตามข้อ 2.4 |

---

## 9. ขั้นถัดไป (ทำเมื่อพร้อม ไม่ต้องรีบ)

### 9.1 โดเมน + HTTPS ฟรี

ซื้อโดเมนถูก ๆ ชี้ A record มาที่ `165.101.65.249` แล้วบน VPS:

```powershell
winget install WinAcme.WinAcme
wacs.exe
```

เลือก `N` (create certificate) → เลือก site `apppay` → ใส่โดเมน → เสร็จแล้ว win-acme ตั้ง scheduled task ต่ออายุให้เองทุก 60 วัน แล้วค่อยเปิด `UseHttpsRedirect` กลับ

### 9.2 แยก build ออกจาก deploy

ถ้าไม่อยากลง SDK บน VPS หรืออยากให้ VPS ทำงานเบา ๆ: ให้ GitHub-hosted runner build แล้วส่ง artifact มาให้ self-hosted runner แค่ deploy

```yaml
jobs:
  build:
    runs-on: windows-latest        # เครื่องของ GitHub
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '9.0.x' }
      - run: dotnet publish Apppay/Apppay.csproj -c Release -o publish
      - uses: actions/upload-artifact@v4
        with: { name: app, path: publish }

  deploy:
    needs: build
    runs-on: [self-hosted, windows, apppay]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/download-artifact@v4
        with: { name: app, path: publish }
      - shell: pwsh
        run: .\deploy\deploy.ps1 -Source "${{ github.workspace }}\publish" -Target "C:\inetpub\apppay"
```

ข้อแลกเปลี่ยน: กินนาที GitHub (private repo มี 2,000 นาที/เดือน)

### 9.3 Backup ฐานข้อมูลก่อน deploy

ตอนนี้ `Program.cs` รัน `db.Database.Migrate()` ทุกครั้งที่แอป start = schema เปลี่ยนเองอัตโนมัติ สะดวกแต่เสี่ยงถ้า migration ลบคอลัมน์ เพิ่ม step นี้ก่อน deploy:

```powershell
$stamp = Get-Date -Format yyyyMMdd-HHmmss
sqlcmd -S localhost -E -Q "BACKUP DATABASE PayDB TO DISK='C:\deploy\backups\db\PayDB-$stamp.bak' WITH INIT, COMPRESSION"
```

### 9.4 Environment แยก staging

สร้าง site ที่ 2 พอร์ต 8081 ชี้ `C:\inetpub\apppay-staging` + branch `develop` → workflow แยกไฟล์ → ทดสอบก่อนขึ้นจริงทุกครั้ง

### 9.5 ป้องกัน main

repo → Settings → Branches → Add rule: บังคับผ่าน PR ก่อน merge เข้า `main` = ไม่มีใคร (รวมถึงตัวเอง) push ตรงขึ้น production ได้

### 9.6 แจ้งเตือนเข้า Line / Slack

เพิ่ม step ท้าย workflow ยิง webhook ตอน `if: failure()` จะได้รู้ทันทีว่า deploy พัง

---

## 10. สรุปเป็นเช็คลิสต์

- [ ] 1.2 ตัดสินใจเรื่อง HTTPS
- [ ] 1.3 สร้าง private repo + push
- [ ] 2.1–2.6 VPS: IIS + Hosting Bundle + SDK + site/app pool + firewall + สิทธิ์
- [ ] 4.1–4.4 ติดตั้ง runner เป็น service + ให้สิทธิ์ + เห็น 🟢 Idle
- [ ] 6 push ทดสอบ แล้วเปิดเว็บได้
- [ ] 7 ลอง rollback ดูสักครั้งตอนยังไม่มีผู้ใช้จริง (สำคัญ — อย่ารอให้พังจริงแล้วค่อยหัดถอย)
