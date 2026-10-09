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
$built = (Get-Item (Join-Path $src "index.html")).LastWriteTime
$newer = @(Get-ChildItem (Join-Path $proj "Assets") -Recurse -File -Include *.cs,*.txt,*.jslib,*.shader -ErrorAction SilentlyContinue |
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
$html = [regex]::Replace($html, '(Build/[A-Za-z0-9_.\-]+\.(?:data|wasm|js)(?:\.br|\.gz|\.unityweb)?)(?![?\w])', "`$1?v=$sha")
$n = ([regex]::Matches($html, [regex]::Escape("?v=$sha"))).Count

# ② 버전 글자 — 지금 보고 있는 것이 어느 빌드인지 눈으로 안다.
#    캐시에 옛 페이지가 남아 있으면 고친 것을 안 고쳐진 상태로 시험하게 되는데, 이 글자가 그걸 드러낸다
$stamp = '<div style="position:fixed;right:6px;bottom:4px;z-index:9999;font:10px/1 monospace;color:#6b5a85;pointer-events:none">v' + $ver + ' ' + $sha + '</div>'
if ($html -match '</body>') { $html = $html -replace '</body>', ($stamp + '</body>') }
[System.IO.File]::WriteAllText($index, $html, $utf8)
Write-Host "version: $ver ($sha) · cache-bust $n 곳"

"# Crowd Runner — WebGL demo`n`nBuilt from crowd-runner-unity @ $sha ($(Get-Date -Format 'yyyy-MM-dd HH:mm')). 세로 화면 · 폰에서 열 것." |
    Set-Content -Encoding utf8 (Join-Path $work "README.md")

# ── 올리기 ────────────────────────────────────────────────────────────────────
$hasGh = [bool](Get-Command gh -ErrorAction SilentlyContinue)
$exists = $true
if ($hasGh) { try { gh repo view $Repo --json name | Out-Null } catch { $exists = $false } }
else { git ls-remote "https://github.com/$Repo.git" HEAD *> $null; $exists = ($LASTEXITCODE -eq 0) }
if (-not $exists) {
    if (-not $hasGh) { throw "$Repo 가 없고 gh 도 없다 — 리포를 먼저 만들어 주세요" }
    gh repo create $Repo --public --description "Crowd Runner — WebGL demo build (Unity)" | Out-Null
    Write-Host "created $Repo"
}

Push-Location $work
git init -q
git checkout -q -b main
git add -A
git -c user.name="Ryusichan" -c user.email="godtheenell@gmail.com" commit -q -m "deploy: crowd-runner-unity @ $sha"
git remote add origin "https://github.com/$Repo.git"
git push -q -f origin main
Pop-Location

if ($hasGh) {
    try { gh api -X POST "repos/$Repo/pages" -f "source[branch]=main" -f "source[path]=/" | Out-Null; Write-Host "pages enabled" } catch { }
    try { $url = (gh api "repos/$Repo/pages" --jq .html_url) } catch { $url = $null }
}
if (-not $url) { $p = $Repo.Split('/'); $url = "https://$($p[0].ToLower()).github.io/$($p[1])/" }
Write-Host "URL: $url"
