# M0 측정 — APK 를 폰에 넣고 돌리고 **로그만 거둬서** 끈다.
#
# 오너 2026-10-08: Unity 를 화면에 띄우지 않는다 · 끝나면 끈다 · 주행을 아낀다.
# 이 스크립트는 Unity 를 아예 안 쓴다 (빌드는 `M0Build.Android` 가 이미 했다) —
# 하는 일은 `adb` 셋뿐이다: 설치 · 실행 · 로그 거두기. 그리고 끝나면 앱을 강제로 끈다.
#
# 화면을 봐야 하는 것(군중이 제대로 그려지나)은 `-Shot` 으로 **폰 화면을 떠서** 본다.
# 에디터도, 오너 모니터도 쓰지 않는다.
param(
  [switch]$Shot,          # 중간에 폰 화면을 한 장 뜬다
  [int]$WaitSeconds = 260 # 측정 계획 전체 길이 + 여유 (M0Bench.Plan 합계 = 150 s)
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$adb = "D:\Unity\6000.3.24f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$pkg = "com.ryusichan.crowdrunner.m0"
$apk = Join-Path $root "Builds\M0.apk"
$out = Join-Path $root "docs\m0-raw.log"

if (-not (Test-Path $apk)) { "APK 가 없다: $apk — M0Build.Android 를 먼저 돌려라"; exit 1 }
# **실기 하나를 골라 그것에만 말한다.** 에뮬레이터가 떠 있으면 `adb` 가 "more than one device" 로
# 거부하고, 더 나쁘게는 **에뮬레이터에서 재면 성능 수가 통째로 거짓**이다 (GPU 가 다른 것이다).
# 이름이 `emulator-` 로 시작하는 것은 제외한다.
$all = @(& $adb devices | Select-String "`tdevice$" | ForEach-Object { ($_ -split "`t")[0].Trim() })
$real = @($all | Where-Object { $_ -notlike "emulator-*" })
if ($real.Count -eq 0) { "실기가 없다 (붙어 있는 것: $($all -join ', ')) — 폰을 꽂고 USB 디버깅을 켜라"; exit 1 }
if ($real.Count -gt 1) { "실기가 둘 이상이다: $($real -join ', ') — 하나만 꽂아라"; exit 1 }
$serial = $real[0]
if ($all.Count -gt $real.Count) { "에뮬레이터 무시: $(@($all | Where-Object { $_ -like 'emulator-*' }) -join ', ')" }
# 이 뒤의 모든 adb 호출이 이 기기로 간다
$adbArgs = @("-s", $serial)

"기기: $((& $adb @adbArgs shell getprop ro.product.model).Trim()) · APK $([int]((Get-Item $apk).Length/1MB))MB"
& $adb @adbArgs install -r $apk | Select-Object -Last 1
# **지난 로그를 지운다.** 안 지우면 앞 주행의 줄이 섞이고, 섞인 줄은 **이번 수로 읽힌다**
# **폰을 깨우고 깨어 있게 한다.** 1 차 측정이 117 초에 9 프레임이었다 — 화면이 잠기면서
# Unity 가 멈췄고, 그 정지가 `p95=117267ms` 로 찍혔다. 측정기가 정지를 재고 있었다.
& $adb @adbArgs shell svc power stayon usb | Out-Null
# **절전 모드를 끈다 — 측정 중에만.** 3 차 주행에서 `low_power=1` 이었다: 절전이면 클럭과
# 새로고침률이 제한되고, 그걸 모른 채 읽은 수는 *기기의 한계* 가 아니라 *그때 설정* 이다.
# 오너 폰이므로 **원래 값을 기억해 두고 끝나면 되돌린다**.
$lowWas = (& $adb @adbArgs shell settings get global low_power).Trim()
if ($lowWas -eq "1") { & $adb @adbArgs shell settings put global low_power 0 | Out-Null; "절전 모드를 껐다 (끝나면 되돌린다)" }
& $adb @adbArgs shell input keyevent KEYCODE_WAKEUP | Out-Null
& $adb @adbArgs shell input keyevent KEYCODE_MENU | Out-Null      # 잠금화면 밀기 (암호가 걸려 있으면 사람이 풀어야 한다)
& $adb @adbArgs logcat -c
& $adb @adbArgs shell am force-stop $pkg | Out-Null
# **액티비티 이름을 박지 않는다.** `UnityPlayerActivity` 로 박았더니 Unity 6 에서는
# `UnityPlayerGameActivity` 라 `Error type 3` 로 아예 시작도 못 했다. 기기에 물어서 쓴다 —
# 다음 Unity 버전에서 또 바뀔 자리이고, 그때 이 스크립트는 안 고쳐도 된다.
$act = (@(& $adb @adbArgs shell cmd package resolve-activity --brief $pkg | ForEach-Object { $_.Trim() } |
         Where-Object { $_ -like "$pkg/*" }) | Select-Object -Last 1)
if (-not $act) { "런처 액티비티를 못 찾았다 — 설치가 됐는지 보라"; exit 1 }
"실행: $act"
& $adb @adbArgs shell am start -n $act | Out-Null

Start-Sleep -Seconds 6
$fg = (& $adb @adbArgs shell dumpsys activity activities | Select-String "mResumedActivity" | Out-String)
if ($fg -notmatch [regex]::Escape($pkg)) { "⚠ 앱이 앞에 없다 — 잠금화면일 수 있다. 수는 정지를 잴 것이다:"; $fg.Trim() }
"측정 중… $WaitSeconds 초 (계획 6300 프레임 ≈ 105 s + 여유)"
if ($Shot) {
  Start-Sleep -Seconds 45
  $png = Join-Path $root "docs\m0-screen.png"
  & $adb @adbArgs exec-out screencap -p > $png
  "화면 한 장: docs/m0-screen.png ($([int]((Get-Item $png).Length/1KB))KB)"
}
Start-Sleep -Seconds $WaitSeconds

# `-d` = 지금까지 쌓인 것만 찍고 빠진다 (안 붙어 있으면 영원히 안 끝난다)
& $adb @adbArgs logcat -d -s Unity | Out-File -FilePath $out -Encoding utf8
# **끝나면 끈다** (오너 규칙)
& $adb @adbArgs shell am force-stop $pkg | Out-Null
& $adb @adbArgs shell svc power stayon false | Out-Null   # 켜 둔 것을 되돌린다 (오너 폰이다)
if ($lowWas -eq "1") { & $adb @adbArgs shell settings put global low_power 1 | Out-Null; "절전 모드를 되돌렸다" }

$lines = @(Get-Content $out | Where-Object { $_ -match "\[M0\]" })
""
if ($lines.Count -eq 0) {
  "⚠ [M0] 줄이 하나도 없다. 앱이 죽었거나 시작도 못 했다 — docs/m0-raw.log 를 보라"
  @(Get-Content $out | Select-Object -Last 15) | ForEach-Object { $_ }
  exit 1
}
$lines | ForEach-Object { ($_ -replace '^.*?\[M0\]', '[M0]') }
""
"원본: docs/m0-raw.log ($(@(Get-Content $out).Count) 줄)"
