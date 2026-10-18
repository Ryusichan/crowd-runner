# 외부 자산 등록부 (2026-10-18 시작)

자산을 들여온 **같은 커밋**에 한 줄을 더한다 (좀비퀸 `docs/THIRD_PARTY_ASSETS.md` 규칙 그대로).
라이선스가 CC0 가 아닌 것은 빌드에 넣기 전에 이 표에서 조건을 확인한다.

| ID | 자산 | 출처 | 라이선스 | 들여온 날 | 경로 | 쓰는 곳 |
|---|---|---|---|---|---|---|
| CF_CHAR_001 | **Ultimate Animated Character Pack** — `BlueSoldier_Male/Female.fbx` · `BaseCharacter.fbx`(클립 17 개의 원본) | [Quaternius](https://quaternius.com/packs/ultimatedanimatedcharacter.html) (좀비퀸 CH1_CHAR_010 에서 복사) | **CC0** | 2026-10-18 | `Assets/_Game/ThirdParty/Quaternius/Characters/` | `Editor/CrowdBake` → `Resources/M0/standin_vat_soldier.*` (아군 VAT). FBX 자체는 Resources 밖이라 빌드에 안 실린다 |
| CF_CHAR_002 | 〃 `Zombie_Male/Female.fbx` | 〃 (좀비퀸 CH1_CHAR_010b) | **CC0** | 2026-10-18 | 〃 | `CrowdBake` → `standin_vat_zombie.*` (적 VAT) |
| CF_CHAR_003 | **Animated Zombie Pack** 베이크 클립 `ZombieRun.anim` (UACP 좀비 리그용으로 좀비퀸이 구운 파생물) | [Quaternius](https://quaternius.com/packs/animatedzombie.html) (좀비퀸 CH1_CHAR_004b) | **CC0** | 2026-10-18 | `Assets/_Game/ThirdParty/Quaternius/Clips/` | `CrowdBake` 좀비 달리기 |
| CF_PROP_001 | **Zombie Apocalypse — Weapons** `Rifle.fbx` · `SMG.fbx` | Quaternius (좀비퀸 CH1_ENV_010) | **CC0** | 2026-10-18 | `Assets/_Game/ThirdParty/Quaternius/Weapons/` | `CrowdBake` 가 군인 손(`Fist.R`)에 붙여 같이 굽는다 — 런타임 로드 없음 |
| CF_PROP_002 | **Zombie Apocalypse — Environment** `Barrel.fbx` | Quaternius (좀비퀸 CH1_ENV_010b) | **CC0** | 2026-10-18 | `Assets/_Game/ThirdParty/Quaternius/Environment/` | 드럼통 모델 (③ 단계에서 `LevelView.Barrel` 이 쓴다) |
| CF_FONT_001 | **Jua** (Woowahan) · 폴백 고딕 | Google Fonts | OFL (`Resources/Fonts/*OFL.txt`) | 2026-10-09 | `Assets/_Game/Resources/Fonts/` | `UiKit.Font` — 웹엔 OS 폰트가 없다 |

`License.txt` (Quaternius, CC0 고지) 를 `Assets/_Game/ThirdParty/Quaternius/` 에 같이 둔다.

## 쓰지 않는 것
- **라스트워: 서바이벌** 의 어떤 파일도 쓰지 않는다. 가져오는 것은 구조·연출뿐이다 (`REF_LASTWAR.md` §0).
