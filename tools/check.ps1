# 오프라인 컴파일 검사 — Unity 라이선스도 에디터도 필요 없다 (~6 s).
# 코드를 고칠 때마다 이것을 먼저 돌린다. `error CS...` 줄과 요약만 찍는다.
#
# 세션 A 의 `tools/sim` 과 역할이 다르다: 저쪽은 `Scripts/Core/` 를 **Unity 없이** 돌려
# 로직이 혼자 서는지를 보고, 이쪽은 **UnityEngine 을 쓰는 코드**가 컴파일되는지를 본다.
# 둘 다 필요하다 — 저쪽만 있으면 렌더링 코드는 APK 빌드(몇 분)까지 가야 오류가 나온다.
#
# ⚠ **이것이 통과해도 패키지 유무는 못 본다.** 이 검사기는 설치된 Unity 의 DLL 을 직접
#    참조하므로 *"이 타입이 존재하나"* 까지만 답한다 — *"이 프로젝트가 그 패키지를 쓰나"* 는
#    원리상 못 본다. 2026-10-09 에 두 번 났다: `UnityEngine.UI`(uGUI 미설치)와
#    `Texture2D.EncodeToPNG`(imageconversion 미설치). 둘 다 **오류 0** 뒤에 Unity 가 터졌다.
#
#    기계로 막아 보려고 `modcheck.py`(csproj 참조 ↔ 매니페스트 대조)를 썼다가 **지웠다**:
#    매니페스트에 없어도 들어가는 모듈이 많아 **거짓 경보 30 개**가 났다 (`Animator` 를 쓰는
#    M0 빌드가 실제로 돌았다). 틀린 검사기는 없는 검사기보다 나쁘다.
#
#    그래서 규칙으로 둔다: **한 단위를 끝냈다고 말하기 전에 Unity 배치를 한 번 돌린다.**
#    비싸니 다른 일(빌드·감사·화면)과 **묶어서** 돌린다.
#
# ⚠ 이것이 통과해도 **플랫폼 전용 API 는 못 잡는다** (`Handheld` 류는 에디터 DLL 에도 있다).
#    좀비퀸에서 그걸로 WebGL 빌드가 죽었다 — 플랫폼 전용은 처음부터 `#if` 로 감싼다.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "CR.Check.csproj"
$dotnet = "D:\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$sw = [Diagnostics.Stopwatch]::StartNew()
$out = & $dotnet build $proj -v q --nologo 2>&1 | Out-String
$sw.Stop()

$errors = @($out -split "`n" | Where-Object { $_ -match "error CS" })
if ($errors.Count -gt 0) { $errors | ForEach-Object { $_.TrimEnd() } }

# **컴파일한 파일 수를 같이 찍는다.** 좀비퀸에서 csproj 가 한쪽 PC 의 절대 경로를 보고 있어
# **0 파일을 컴파일하고 "오류 0"** 을 찍은 적이 있다. 수가 0 이면 초록이 거짓이다.
# 두 번 틀린 자리다. ① `(...).Count` 가 결과 없으면 $null 이라 아래 `-eq 0` 가 안 걸렸다.
# ② 찍는 쪽이 `$n개` 였는데 **PowerShell 이 `n개` 라는 변수로 읽는다** (한글이 변수명에 허용된다) —
#    그래서 8 개를 세어 놓고도 빈칸이 찍혔고, 빈칸은 0 과 달라서 가드도 안 걸렸다.
#    **세는 것과 찍는 것이 따로 틀릴 수 있다**: 둘 다 봐야 한다.
# `@()` 로 감싸는 것이 핵심이다. 처음엔 `(...).Count` 였는데 결과가 없으면 **$null** 이고,
# 그러면 아래 `-eq 0` 가 안 걸려 **"파일 (빈칸)개 · 오류 0개"** 로 초록이 나왔다 —
# 이 줄이 막으려던 바로 그 모양을 이 줄이 만들었다.
# **csproj 가 컴파일하는 두 곳을 다 센다.** 한 곳만 세면 다른 곳이 빠져도 수가 그럴듯하다
$n = @(Get-ChildItem -Path (Join-Path $root "..\Assets\_Game\Scripts"), (Join-Path $root "..\Assets\Tests") -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue).Count
""
"오류 $($errors.Count)개 · 파일 $($n)개 · $([int]$sw.Elapsed.TotalSeconds)초"
if ($n -eq 0) { "파일이 0개다 — csproj 의 Compile Include 경로를 보라 (초록이 거짓이다)"; exit 1 }
if ($errors.Count -gt 0) { exit 1 }
