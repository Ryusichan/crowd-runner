# 레퍼런스 — 라스트워: 서바이벌 (Last War: Survival) 의 레인 슈팅 (2026-10-18)

오너 지시: *"퀄리티가 처참하다. 디테일 신경써서 라스트워처럼 진행해줘"* · *"라스트워 디테일하게
인터넷 등 정보 찾아서 카피캣처럼 만들어줘."*

> **가져오는 것은 구조·연출·조작감이다.** 게임 방식과 화면 구성은 저작권 대상이 아니다
> (`REF_TOPWAR.md` 와 같은 원칙). **그쪽 그림·캐릭터·로고·사운드 파일은 쓰지 않는다** — 같은
> 모양을 우리가 가진 CC0 / 구매 자산으로 낸다 (`THIRD_PARTY_ASSETS.md`).

## 1. 조사에서 확인된 것

출처 — [BlueStacks 입문](https://www.bluestacks.com/blog/game-guides/last-warsurvival-game/lws-beginners-guide-en.html) ·
[BlueStacks 팁](https://www.bluestacks.com/blog/game-guides/last-warsurvival-game/lws-tips-tricks-en.html) ·
[PocketGamer](https://www.pocketgamer.com/last-war-survival/guide/) ·
[f2p.org](https://www.f2p.org/free-to-play/strategy/last-war-survival-game-lane-strategy-and-reflexes-meet-survival-in-zombie-combat/) ·
[Fake Ad Games](https://fakeadgames.com/archive/last-war-survival) ·
[나무위키](https://en.namu.wiki/w/%EB%9D%BC%EC%8A%A4%ED%8A%B8%20%EC%9B%8C:%20%EC%84%9C%EB%B0%94%EC%9D%B4%EB%B2%8C) ·
[App Store](https://apps.apple.com/us/app/last-war-survival/id6448786147) · [Google Play](https://play.google.com/store/apps/details?id=com.last.wargame.warstudio&hl=en)

| 항목 | 확인된 내용 | 우리 상태 |
|---|---|---|
| 조작 | **좌우만.** *"your characters automatically shoot at zombies approaching from the top of the screen"* — 사격은 자동 | ✅ 같음 |
| 적 | **위에서 내려온다.** 좀비, 뒤로 갈수록 센 종류, 끝에 보스 | ✅ 구조 같음 · 모양은 캡슐 |
| 게이트 | **반투명** `+10` · `×3` 판, **빨간 감산 게이트**, 가시·폭발통 같은 함정. **게이트를 지나기 전에 쏘면 수가 올라간다** | ⚠ 불투명 네모 · "쏴서 올리기" 없음 |
| 통 | **쏴서 깬다** → 병사 · 무기 · 강화. 영웅 전용 통(그 영웅 1 레벨). 통은 **피해량**을 받고 벽은 **타격 수**를 받는다 — 초반엔 통이 유리 | ✅ 통 있음 · 영웅통/무기통 없음 |
| 분대 | 영웅 **5 명**(보병·장갑차·헬기), 탱커 앞 · 화력/지원 뒤. 각자 HP 바 | ⚠ 전원 같은 병사 · HP 바 없음 |
| 길이 | *"levels are much shorter than they might look"* — 짧고 빠르다 | ⚠ 60 초 |
| 느낌 | *"action and particle-packed"*, *"flashy"* | ✗ 연출 빈약 |
| 광고 구조 | 복도를 달리는 소대 · 문 둘(`+50` / `×3`) · 좀비 웨이브 · 레일 위 보스전 | ✅ 구조 같음 |

## 2. 카피캣 사양 — 화면에서 읽히는 순서대로

### 2a. 캐릭터 (가장 큰 차이)
- **파란 헬멧 군인**이 소총을 들고 달리며 쏜다. 우리는 Quaternius `BlueSoldier_Male/Female`(CC0) + ZA `Rifle`(CC0) 을 손 뼈(`Fist.R`)에 붙여 VAT 로 굽는다.
- **좀비**는 Quaternius `Zombie_Male/Female` + `ZombieRun.anim`. 회녹색 피부, 찢어진 옷 — 모델이 이미 그렇다.
- 정점색: UACP 는 머티리얼 단색이라 **굽는 쪽이 머티리얼 색을 정점색으로 옮긴다** (`CrowdBake`). 셰이더가 정점색 × 팀 틴트.

### 2b. 카메라
- 뒤 위에서 내려다본다. 소대가 화면 **아래 1/3** 에, 사람 키가 화면 높이의 **1/9~1/7**. 지금(9.2 m 위 · 10.5 m 뒤 · 30°)은 사람이 점이다 → **6.5 m 위 · 7.5 m 뒤 · 34°**, FOV 46 에서 시작해 사진으로 맞춘다.

### 2c. 게이트
- **반투명 판** (알파 0.45) + 밝은 테두리 + 큰 숫자. 더하기 = 파랑, 곱하기 = 초록, 빼기 = **빨강**. 숫자 글꼴은 **흰색 + 짙은 외곽선** (TextMesh 는 외곽선이 없으니 **검은 사본을 살짝 뒤에** 하나 더 그린다).
- **쏘면 수가 오른다** (라스트워 고유): 게이트에 체력 대신 **카운터**가 있고, 맞을 때마다 값이 오른다(`+10` → `+11` …). 통과 전까지 올린 값이 적용된다. → Core 작업, 2 단계.

### 2d. 통
- ZA `Barrel.fbx`(나무 판자 + 쇠테). 숫자는 통 앞면에 **외곽선 글자**. 맞는 동안 붉게 + 체력 띠. 깨지면 **판자 조각 4~6 개가 튀고** 병사가 쏟아진다.
- 무기통(2 단계): 깨면 무기 등급이 오른다(권총 → 소총 → 기관총). 발사 간격·탄 수 변경은 `Tuning`.

### 2e. 길과 배경
- 아스팔트(어두운 회색) + 흰 차선 파선 + 노란 가장자리 선. 양옆에 **콘크리트 방호벽** 연속, 그 밖에 폐건물·버려진 차 (SimplePoly/Kenney, 2 단계). 하늘 그라데이션 + 멀리 안개.
- 방향광 + 블롭 그림자(이미 있음) — 그림자가 있어야 사람이 땅에 붙는다.

### 2f. 총격 연출
- 총구 화염: 밝은 노랑 **스프라이트 쿼드**(지금은 네모 덩어리), 2 프레임.
- 예광탄: 얇고 길고 밝은 선(지금은 두껍다), 색은 노란 흰색.
- 피격: 적이 **뒤로 밀리며 쓰러지고**(VAT 2 클립이 없으니 **눕히는 회전** 0.35 초 + 가라앉기), 바닥에 흔적.
- 통 깨짐: 조각 + 번쩍임. 게이트 통과: 숫자 팝(있음).

### 2g. HUD
- 소대 **아래** 큰 외곽선 숫자(병력). 소대 위에 **얇은 초록 HP 바**(전체 체력 비율). 상단 왼쪽 일시정지, 상단 중앙 진행 띠(스테이지 진행률 + 보스 아이콘), 적 웨이브 수 배지.

### 2h. 페이싱
- 한 판 **35~45 초**. 적은 끊임없이, 끝에 보스 줄. 지금 60 초인 이유는 적 2,200 명을 지우는 시간이다 — 수를 줄이는 것이 아니라 **`StreamPerSec` 와 화력**을 올려 밀도를 유지하면서 짧게 간다 (B 와 상의, `HANDOFF` §11).

## 3. 하지 않는 것
- 라스트워의 **4X 본편**(기지 건설·동맹·가챠). 우리는 레인 슈팅 한 판이 게임이다.
- 그쪽 자산 복제. 모델·텍스처·UI 그림은 전부 우리 것.

## 4. 진행 순서 (단위마다 사진 → 커밋)
① 캐릭터(군인+소총, 좀비) → ② 카메라 → ③ 통·게이트 모델 + 외곽선 숫자 → ④ 길·방호벽·하늘 → ⑤ 총격 연출(화염·예광탄·쓰러짐·조각) → ⑥ HUD → ⑦ 2 단계(게이트 쏘기 · 무기통 · 영웅)
