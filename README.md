<div align="center">

# Sephiria Optimizer

세피리아 인벤토리의 석판과 아티팩트 배치를 계산해서 정렬해 주는 모드

[![Release](https://img.shields.io/github/v/release/lij0825/SephiriaOptimizer?style=flat-square&color=2ea44f)](https://github.com/lij0825/SephiriaOptimizer/releases/latest)
[![Sephiria](https://img.shields.io/badge/Sephiria-1.1.4-3b82f6?style=flat-square)](https://store.steampowered.com/app/2436940/Sephiria/)
[![Platform](https://img.shields.io/badge/Windows%20%7C%20Steam%20Deck%20%7C%20macOS-6366f1?style=flat-square)](#설치)

</div>

인벤토리를 열고 오른쪽 패널의 `[자동 정렬 실행]`을 누르면 석판 버프와 아티팩트 시너지가 가장 잘 들어가는 배치로 아이템을 옮기고 석판도 돌려 줍니다.
직접 옮기고 싶을 때는 가이드를 켜 두고 칸마다 뜨는 위치 표시만 보고 옮기면 됩니다.

게임 기본 애드온 로더로 동작해서 BepInEx 같은 외부 로더는 필요 없습니다.

## 설치

1. [릴리즈 페이지](https://github.com/lij0825/SephiriaOptimizer/releases/latest)에서 `SephiriaOptimizer_v2.6.0.zip`을 받아 압축을 풉니다.
2. 설치 파일을 실행합니다.
   - Windows: `Install.bat` 더블클릭
   - Steam Deck / Linux / macOS: 터미널에서 `chmod +x install.sh && ./install.sh`
3. 게임을 켜고 인벤토리(I 또는 Tab)를 열면 오른쪽에 패널이 보입니다.

스팀 폴더는 알아서 찾고, 못 찾을 때만 폴더 선택 창이 한 번 뜹니다.
지울 때는 `Uninstall.bat` 또는 `./install.sh --uninstall`을 실행하면 됩니다.

## 사용법

| 조작 | 설명 |
| --- | --- |
| `자동 정렬 실행` | 계산된 위치로 아이템을 옮기고 석판을 회전합니다. |
| `[1] [2] [3]` 대안 선택 | 동점·근접 최적 배치가 2개 이상 존재할 때만 조건부 표시되어 시너지 점수·스왑 횟수·차이를 미리보고 적용합니다. |
| `가이드: 켜짐 / 꺼짐` | 칸 위에 이동할 위치(`→[열,행]`), 회전 각도(`↻`), 예상 레벨을 표시합니다. |
| `빌드 설정` | 동료 휘장 아티팩트 고려, 캘세더니 열쇠 속성, 목표 세트, 금빛침 대상, 우선순위를 정합니다. |
| `슬롯 고정(Pin)` | 켜 둔 상태에서 칸을 우클릭하면 그 자리는 옮기지 않고 고정합니다. |
| `동료 휘장 아티팩트 고려: ON / OFF` | `ON` 시 보유 중인 동료 휘장 아티팩트를 통합 최적화 후보군과 시너지·슬롯 평가에 포함하고, `OFF` 시 동료 휘장 아티팩트를 배치 후보군에서 제외합니다. |
| 우클릭 (기본) | 아티팩트와 석판의 우선순위(최상 ~ 최하)를 순환합니다. 최하는 페널티 상쇄·여분 슬롯으로 처리됩니다. |

- 전투 중이거나 마우스로 아이템을 들고 있을 때는 자동 정렬을 눌러도 아이템을 직접 옮기지 않고 가이드만 표시합니다.
- 새 버전이 나오면 패널 위쪽에 `원클릭 업데이트` 버튼이 나타납니다.

## 계산에 반영하는 것

- **4계층 사전식 통합 최적화 엔진 (`Single Source of Truth`, 기대 DPS 배제)**:
  1. **1순위 (최우선)**: 사용자 지정 우선순위(`최상 > 높음 > 보통 > 낮음 > 최하`) 배치·활성화, 금빛침 대상, 캘세더니/목표 콤보 조건 충족도
  2. **2순위**: 활성 페널티 완전 상쇄(`HardViolationCount == 0`) 및 각인 침식 최소화
  3. **3순위**: 전체 석판·아티팩트의 시너지 활성화 수 및 효과 증폭량(신비 콤보, 조화의 수정, 인접/행/열/4연결 버프) 총합 극대화
  4. **4순위**: 불필요한 인벤토리 칸·버프 낭비 및 스왑/회전 실행 비용 최소화
- **동적 규모 적응형 전역/하이브리드 탐색 (`1.5~2초` 타임 버짓 보장)**:
  - **소규모 인벤토리**: 4계층 사전식 상한선(Upper Bound) 기반 분기 한정(`Branch & Bound`)으로 전역 최적해를 보장합니다.
  - **중·대규모 인벤토리**: 다중 시작점 시드 합성 + Simulated Annealing + Tabu Search + 연쇄 페널티 수리(Chained Penalty Repair) + 5대 이웃 연산자(`2-Swap`, `3-Opt 순환`, `연결 블록 평행 이동`, `행/열 대칭 반전·회전`, `미배치 후보 교체 투입`)로 징검다리 페널티 상쇄 배치를 놓치지 않으며, 평가 핫패스는 힙 할당 **0 Bytes**를 유지합니다.
- **인게임 실코드 정합성 (`Sephiria 1.1.4`)**:
  - 2-Pass 수렴형 버프·동적 카테고리·신비(Mystic +2) 평가, 석판 `+`/`-`/`×`/제약무시·바닥 각인·비정형 해금 슬롯·`Y_PLUS` 자기 버프·만렙 초과 상쇄
  - 북향의 금빛침·나침반 계층(`parentOffsets`), 조화의 수정 주변 8칸 음수 차감, `Charm_CompanionChaos` 동일 행 혼돈 시너지(`+25%`)
  - 캘세더니 열쇠, 하얀 종이, 목각 상자, 화염과 냉기, 마법탄 범위(`dy ∈ [-5, 5]`), 무한 콤보, 4방향 인접 배치 보너스 반영

## 자주 묻는 질문

<details>
<summary>Install.bat을 실행했더니 dll을 못 찾는다고 나와요.</summary>

<br>

zip 파일을 풀지 않고 안에서 바로 실행하면 그렇게 뜹니다. 압축을 먼저 풀고 실행해 주세요.

</details>

<details>
<summary>권한이 없다고 나와요.</summary>

<br>

게임이 `C:\Program Files (x86)` 아래에 있으면 관리자 권한이 필요합니다. `Install.bat`을 우클릭해서 관리자 권한으로 실행해 주세요.

</details>

<details>
<summary>자동 정렬을 눌러도 아무 반응이 없어요.</summary>

<br>

- 현재 배치가 이미 최선이어서 바꿀 칸이 없는 경우입니다.
- 전투 중이거나 아이템을 집고 있을 때는 움직이지 않습니다.
- 패널 아래에 오류 문구가 떠 있다면 [Issues](https://github.com/lij0825/SephiriaOptimizer/issues)에 남겨 주세요.

</details>
