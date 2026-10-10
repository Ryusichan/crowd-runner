# WebGL 빌드(Builds\WebGL)를 public 리포에 올려 GitHub Pages 로 서빙한다.
#
# 사용: powershell -File tools\deploy-web.ps1 -OwnerApproved
#       (빌드가 먼저다 — 세션 B 의 WebGL 빌드가 Builds\WebGL 에 들어 있어야 한다)
#
# ⚠ 이 파일은 **BOM 있는 UTF-8** 이어야 한다. Windows PowerShell 5.1 은 BOM 이 없으면
#   ANSI 로 읽어 한글이 깨지고, 깨진 글자가 따옴표를 삼켜 구문 오류가 난다.
#
# 좀비퀸 tools\deploy-web.ps1 에서 가져왔다 (37836840). 거기서 비싸게 배운 둘이 들어 있다:
#   ① 캐시 무효화 — Pages 가 Build 파일에 max-age=600 을 주는데 파일명은 매 빌드 같다.
#      재배포 직후 10 분 동안 브라우저가 옛 파일과 새 파일을 섞어 받아 로딩이 깨진다.
#   ② index.html 을 UTF-8 로 명시해 읽고 쓴다. Get-Content 로 읽으면 줄바꿈까지 삼켜서
#      JS 가 통째로 주석 처리되고 Unity 가 아예 시작을 못 한다 (오너 2026-09-18 "로딩이 멈췄어").
param([string]$Repo = "Ryusichan/crowd-runner-demo", [switch]$OwnerApproved)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

# 공개 데모는 오너가 "올려줘" 라고 한 턴에만 바뀐다. 세션이 혼자 올리지 않는다
if (-not $OwnerApproved) { throw "deploy blocked: 공개 데모 배포는 오너 지시가 있을 때만. 요청받은 턴이면 -OwnerApproved 를 붙여 실행" }

$proj = Split-Path $PSScriptRoot -Parent
$src  = Join-Path $proj "Builds\WebGL"
if (-not (Test-Path (Join-Path $src "index.html"))) {
    throw "WebGL 빌드가 없다: $src — 먼저 빌드해야 한다 (세션 B 의 Editor 빌드 경로)"
}

# ⚠ **낡은 빌드를 올리지 않는다.**
# 오늘 하루 같은 결함류를 다섯 번 봤다: 재는 것이 생각한 것과 다른데 **수가 그럴듯해서**
# 아무도 안 묻는다 (`DESIGN.md` §3i 네 번째 칸). 배포에도 그 자리가 있다 — 코드를 고치고
# 빌드를 안 낸 채 올리면 **고쳐진 줄 알고 시험**하게 되고, 그 10 분은 통째로 버려진다.
# 그래서 소스가 빌드보다 새로우면 멈춘다.
# ⚠ **`index.html` 의 시각은 빌드 시각이 아니다.** Unity 는 내용이 같으면 그 파일을 다시
# 쓰지 않는다 — 3 차 빌드에서 `Build/WebGL.data` 는 15:32 인데 `index.html` 은 15:21 로
# 남아 있었고, 그래서 **멀쩡한 빌드를 낡았다고 막았다.** 낡은 것을 막으려고 만든 검사가
# 정작 **틀린 파일을 쟀다** (`DESIGN.md` §3i 네 번째 칸, 내가 만든 검사에서).
# 그러니 **산출물 중 가장 새것**을 빌드 시각으로 본다.
$built = (Get-ChildItem $src -Recurse -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime

# 그리고 **테스트는 빼고 센다.** `Assets/Tests` 는 플레이어 빌드에 안 들어가므로, 거기서
# 바뀐 것 때문에 배포가 막히면 그 빗장은 곧 꺼진다 — 틀린 경고가 검사를 죽인다
# 그리고 **플레이어 빌드에 들어가는 폴더만 본다.** `Assets/Tests` 는 빌드에 안 들어가므로
# 거기서 바뀐 것 때문에 배포가 막히면 그 빗장은 곧 꺼진다 — **틀린 경고가 검사를 죽인다.**
$watch = @('_Game/Scripts','_Game/Resources','_Game/Shaders','_Game/Plugins') |
         ForEach-Object { Join-Path $proj (Join-Path 'Assets' $_) } | Where-Object { Test-Path $_ }
$newer = @($watch | ForEach-Object { Get-ChildItem $_ -Recurse -File -Include *.cs,*.txt,*.jslib,*.shader -ErrorAction SilentlyContinue } |
           Where-Object { $_.LastWriteTime -gt $built })
if ($newer.Count -gt 0) {
    $top = ($newer | Sort-Object LastWriteTime -Descending | Select-Object -First 3 |
            ForEach-Object { "  " + $_.FullName.Substring($proj.Length + 1) + "  (" + $_.LastWriteTime.ToString("HH:mm") + ")" }) -join "`n"
    throw "빌드가 낡았다 — 빌드 시각 $($built.ToString('HH:mm')) 뒤에 바뀐 파일 $($newer.Count) 개:`n$top`n다시 빌드하고 올릴 것"
}
Write-Host "build: $($built.ToString('yyyy-MM-dd HH:mm')) · 그 뒤로 바뀐 소스 없음"

$work = Join-Path $env:TEMP "cr-web-deploy"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item (Join-Path $src "*") $work -Recurse -Force
New-Item -ItemType File -Force (Join-Path $work ".nojekyll") | Out-Null   # Build/ 를 Jekyll 이 건드리지 않게

$sha = (git -C $proj rev-parse --short HEAD)
$verFile = Join-Path $proj "docs\WEB_VERSION.txt"
$ver = if (Test-Path $verFile) { (Get-Content $verFile -First 1).Trim() } else { "0.0.0" }

# ── index.html 손보기 ──────────────────────────────────────────────────────────
$index = Join-Path $work "index.html"
$utf8 = New-Object System.Text.UTF8Encoding($false)
$html = [System.IO.File]::ReadAllText($index, $utf8)

# ① 캐시 무효화 — 빌드마다 다른 URL 이 되게
# Unity 6 템플릿은 `buildUrl + "/WebGL.data.unityweb"` 처럼 **따옴표 안에** 적는다.
# 좀비퀸 때의 `Build/...` 통짜 경로 정규식은 여기서 **0 곳**을 고쳤고, 그러면 캐시 무효화가
# 조용히 아무 일도 안 한다 — 또 네 번째 칸이다. 그래서 아래에서 고친 수를 세고 0 이면 멈춘다.
$html = [regex]::Replace($html, '("/[A-Za-z0-9_.\-]+\.(?:data|wasm|js|symbols\.json)(?:\.br|\.gz|\.unityweb)?)"', "`$1?v=$sha`"")
$n = ([regex]::Matches($html, [regex]::Escape("?v=$sha"))).Count
if ($n -eq 0) { throw "캐시 무효화가 한 곳도 안 붙었다 — index.html 모양이 바뀌었다. 정규식을 맞추기 전에는 올리지 않는다 (옛 파일과 새 파일이 섞여 로딩이 깨진다)" }

# ② **폰에서 놀 수 있게 만든다.** Unity 기본 템플릿은 데스크톱용이라 그대로 올리면 못 논다.
#    헤드리스 크롬으로 열어 보고 찾은 셋이다 (2026-10-09, 첫 배포):
#
#    ⓐ **`viewport` 메타가 아예 없다** → 폰이 데스크톱 너비로 그리고 전체를 축소한다.
#    ⓑ **캔버스가 960×600 고정 가로**다 → 세로 화면에 가로 상자가 앉고 스크롤바가 생긴다.
#    ⓒ **`touch-action` 이 없다** → 손가락을 끌면 **게임이 아니라 페이지가 스크롤된다.**
#       이 게임의 **유일한 조작이 드래그**라 이것 하나로 못 노는 게임이 된다.
#
#    ⓒ 가 제일 조용하다 — 화면은 멀쩡히 떠 있으니 "왜 안 움직이지" 로만 보인다.
$fit = @'
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no,viewport-fit=cover">
<style>
  html,body{margin:0;padding:0;height:100%;background:#1b1426;overflow:hidden;overscroll-behavior:none}
  #unity-container{position:fixed;inset:0;display:flex;align-items:center;justify-content:center}
  /* 9:16 을 지키며 화면에 꽉 채운다 — 가로가 넓으면 높이에 맞춰 좌우를 비운다 */
  #unity-canvas{width:min(100vw,56.25vh);height:min(177.78vw,100vh);display:block;background:#1b1426;
                touch-action:none;-ms-touch-action:none}
  #unity-footer{display:none}
  #unity-loading-bar{position:fixed;left:50%;top:50%;transform:translate(-50%,-50%)}
</style>
'@
if ($html -match '</head>') { $html = $html -replace '</head>', ($fit + '</head>') }
else { throw "index.html 에 </head> 가 없다 — 템플릿이 바뀌었다" }

# ③ 버전 글자 — 지금 보고 있는 것이 어느 빌드인지 눈으로 안다.
#    캐시에 옛 페이지가 남아 있으면 고친 것을 안 고쳐진 상태로 시험하게 되는데, 이 글자가 그걸 드러낸다
# ⚠ **길 위에 겹쳐 보였다** (오너 2026-10-10 폰 사진). 캔버스가 화면을 꽉 채우므로
# "오른쪽 아래" 가 곧 **게임 화면 위**다. 지울 수는 없다 — 캐시에 옛 페이지가 남으면
# 고친 것을 안 고쳐진 상태로 시험하게 되고, 이 글자가 그걸 드러내는 유일한 것이다.
# 그래서 **어두운 알약 안에 넣고 흐리게** 한다: 찾으면 읽히고, 안 찾으면 안 보인다.
$stamp = '<div style="position:fixed;right:5px;bottom:4px;z-index:9999;font:9px/1 monospace;' +
         'color:#9a8bb4;background:rgba(14,10,20,.55);border-radius:6px;padding:2px 5px;' +
         'opacity:.45;pointer-events:none">v' + $ver + ' ' + $sha + '</div>'
if ($html -match '</body>') { $html = $html -replace '</body>', ($stamp + '</body>') }
[System.IO.File]::WriteAllText($index, $html, $utf8)
Write-Host "version: $ver ($sha) · cache-bust $n 곳"

"# Crowd Runner — WebGL demo`n`nBuilt from crowd-runner-unity @ $sha ($(Get-Date -Format 'yyyy-MM-dd HH:mm')). 세로 화면 · 폰에서 열 것." |
    Set-Content -Encoding utf8 (Join-Path $work "README.md")

# ── 올리기 ────────────────────────────────────────────────────────────────────
# ⚠ **네이티브 명령의 실패는 `try/catch` 가 못 잡는다.** 좀비퀸에서 가져온 원본이
# `try { gh repo view } catch { $exists = $false }` 였는데, `gh` 가 404 를 내도 **catch 가 안
# 돌아** `$exists` 가 true 로 남았다. 그래서 리포를 안 만들고 push 로 직행해 실패했다.
# PowerShell 에서 외부 프로그램의 성패는 **`$LASTEXITCODE`** 로 본다.
# ⚠⚠ **그리고 `$ErrorActionPreference = 'Stop'` 이 켜져 있으면 네이티브 명령의 stderr 한 줄이
# 통째로 종료 오류가 된다** (PowerShell 5.1 의 `NativeCommandError`). `gh` 가 "없는 리포" 를
# 알리려고 stderr 에 쓰는 순간 스크립트가 죽는다 — **정상 흐름이 오류로 둔갑한다.**
# 그래서 외부 프로그램을 부르는 동안만 'Continue' 로 내리고 `$LASTEXITCODE` 로 판단한다.
$hasGh = [bool](Get-Command gh -ErrorAction SilentlyContinue)
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
if ($hasGh) { gh repo view $Repo --json name 2>&1 | Out-Null }
else { git ls-remote "https://github.com/$Repo.git" HEAD 2>&1 | Out-Null }
$exists = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = $prevEAP
if (-not $exists) {
    if (-not $hasGh) { throw "$Repo 가 없고 gh 도 없다 — 리포를 먼저 만들어 주세요" }
    gh repo create $Repo --public --description "Crowd Runner — WebGL demo build (Unity)" | Out-Null
    Write-Host "created $Repo"
}

$ErrorActionPreference = 'Continue'   # 아래는 전부 외부 프로그램이다 (위 주석 참고)
Push-Location $work
git init -q
git checkout -q -b main
git add -A
git -c user.name="Ryusichan" -c user.email="godtheenell@gmail.com" commit -q -m "deploy: crowd-runner-unity @ $sha"
git remote add origin "https://github.com/$Repo.git"
git push -q -f origin main
$pushed = ($LASTEXITCODE -eq 0)
Pop-Location
if (-not $pushed) { $ErrorActionPreference = $prevEAP; throw "push 실패 — 리포($Repo)가 있는지, 로그인됐는지 확인" }

if ($hasGh) {
    try { gh api -X POST "repos/$Repo/pages" -f "source[branch]=main" -f "source[path]=/" | Out-Null; Write-Host "pages enabled" } catch { }
    try { $url = (gh api "repos/$Repo/pages" --jq .html_url) } catch { $url = $null }
}
if (-not $url) { $p = $Repo.Split('/'); $url = "https://$($p[0].ToLower()).github.io/$($p[1])/" }
Write-Host "URL: $url"
