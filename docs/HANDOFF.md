# HANDOFF — 두 세션이 한 저장소를 나눠 쓰는 규칙

좀비퀸의 `docs/HANDOFF.md` 와 같은 역할. **끝난 줄은 지운다** — 지난 일은 git log 에 있다.

## 0. 차선 (파일이 단위다)

| 차선 | 누가 | 파일 |
|---|---|---|
| 로직·레벨 | **A** | `Scripts/Core/**` · `Scripts/Game/LevelRunner.cs` · `Resources/Levels/*.txt` · `tools/sim/**` · `docs/DESIGN.md` |
| 메타 화면 | **A** | `Scripts/Meta/**` (월드맵·스테이지 카드·결과 — 좀비퀸 껍데기, DESIGN §3b) |
| 그리기·편집기 | **B** | `Scripts/View/**` · `Scripts/Game/GameBoot.cs` · `Scripts/Crowd/**` · `Scripts/Editor/**` · `Shaders/**` · `Scenes/**` · `tools/check.ps1` · `tools/CR.Check.csproj` · `ProjectSettings/**` · `docs/M0_CROWD.md` |

처음 표에는 `Scripts/Game/**` 가 통째로 A 였는데, B 가 `GameBoot.cs` 를 거기 두었다
(`c0c3d40`). 이음매가 깨끗해서 그대로 둔다 — A 는 `LevelRunner`(판을 여는 문), B 는
`GameBoot`(장면을 세우는 곳). **표를 현실에 맞춘 것이고, 차선을 넘은 것을 탓하는 것이 아니다.**

차선 밖 파일은 **읽기만** 한다. 고쳐야 하면 메시지로 부탁한다.

## 0a. ⚠ 파일을 만들기 전에 **그 자리를 본다** (2026-10-09, A 가 틀렸다)

A 가 `tools/CR.Check.csproj` 와 `tools/check.ps1` 을 **새로 만든다고 생각하고 `cat >` 로
덮어썼다.** 둘 다 B 가 `a46dce5` 에서 이미 만들어 둔 것이었고, 그 주석 안에는 *"PowerShell 이
한글을 변수명으로 읽어서 8 개를 세어 놓고 빈칸을 찍었다"* 같은 **되찾을 수 없는 기록**이
들어 있었다. `git checkout --` 으로 되돌렸지만, 커밋 전에 `git status` 를 보지 않았다면
B 의 작업이 사라진 채 올라갔다.

왜 그랬나: *"이 저장소에는 Unity 쪽 컴파일 검사가 없다"* 를 **디렉터리를 보지 않고**
좀비퀸의 기억으로 단정했다. 없다는 것은 **확인해야 아는 사실**이다.

**규칙**: 파일을 새로 만들 때는 `ls` 와 `git log -- <경로>` 를 먼저 본다. 특히
**차선 밖**이거나 `tools/`·`docs/` 처럼 양쪽이 쓰는 자리는 반드시. 그리고 `cat > <파일>` 은
있는 파일을 **말없이 지운다** — 새 파일이라고 믿을 때만 쓴다.

## 1. 검사기가 둘이다 — **둘 다 돌려야 전부 본 것이다**

| 도구 | 보는 것 | 주인 |
|---|---|---|
| `cd tools\sim ; dotnet run -c Release` | `Core/**` 를 Unity 없이 컴파일 + 레벨 10 판 전 경로 판정 | A |
| `powershell -File tools\check.ps1` | `Scripts/**` 전체(UnityEngine 쓰는 코드)를 Unity 없이 컴파일 | B |

`tools/sim` 만 돌리면 `Game/`·`Crowd/` 는 **아무도 보지 않는다.**
`check.ps1` 만 돌리면 레벨이 깨 지는지 **모른다.**

## 2. 지금 A → B 로 묻는 것

- 편집기가 만든 것들이 아직 추적 밖이다: `Assets/**/*.meta` · `Assets/_Game/Scenes/` ·
  `ProjectSettings/**` · `Packages/packages-lock.json`. **B 차선**이라 A 는 손대지 않았다.
  `.meta` 가 추적 밖이면 다른 PC 에서 GUID 가 새로 생겨 **참조가 끊긴다** — 올려 주면 좋겠다.

## 3. 지금 B → A 로 묻는 것

(없음 — `897e369` 에서 `LevelRunner.Load(name)` 와 `Sim.BlockingZ` 로 답했다)

## 4. ~~A 가 다음에 만드는 것~~ — `Scripts/Meta/**` **섰다** (`3522a27`)

월드맵 → 스테이지 카드 → 결과. `GameBoot.Runner.Load(code)` 로 들어가고 `LevelRunner.Paused`
로 멈춘다. 레이아웃 숫자는 **짐작**이라 세션 B 의 `MetaShots` 세 장을 기다린다.
레벨 문법과 짜는 법은 `docs/LEVELS.md`.

### 원래 적어 둔 것

좀비퀸의 **월드맵 → 스테이지 카드 → 결과** 를 가져온다 (오너 지시, DESIGN §3b). 진입은
B 가 열어 둔 `GameBoot.Go(code)` / `GameBoot.Runner.Load(code)` 를 쓴다 — 장면을 세우는
일은 B 쪽이므로 메타가 직접 `GameObject` 를 만들지 않는다.

이미 올라간 발판: 레벨마다 `rating <☣> <☣☣> <☣☣☣>` (`b286ea6`) — 잰 수이고, 닿지 않는
목표는 검증기가 거절한다.
