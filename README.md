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

1. [릴리즈 페이지](https://github.com/lij0825/SephiriaOptimizer/releases/latest)에서 `SephiriaOptimizer_v2.5.0.zip`을 받아 압축을 풉니다.
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
| `[1] [2] [3]` 대안 선택 | 동점·근접 최적 배치가 2개 이상 존재할 때만 조건부 표시되어 예상 DPS·스왑 횟수·차이를 미리보고 적용합니다. |
| `가이드: 켜짐 / 꺼짐` | 칸 위에 이동할 위치(`→[열,행]`), 회전 각도(`↻`), 예상 레벨을 표시합니다. |
| `빌드 설정` | 캘세더니 열쇠 속성, 목표 세트, 금빛침 대상, 아티팩트 우선순위를 정합니다. |
| `슬롯 고정(Pin)` | 켜 둔 상태에서 칸을 우클릭하면 그 자리는 옮기지 않고 고정합니다. |
| `동료 휘장 고려: ON / OFF` | `ON` 시 헌신의 휘장 및 동료 휘장 아티팩트의 스탯·시너지·행 버프를 포함해 최적화하고, `OFF` 시 핵심 아티팩트·석판 위주로 배치하며 휘장은 페널티·여분 슬롯으로 분리합니다. |
| 우클릭 (기본) | 아티팩트 우선순위(최상 ~ 최하)를 바꿉니다. 최하는 마이너스 칸을 대신 막는 용도로 들어갑니다. |

- 전투 중이거나 마우스로 아이템을 들고 있을 때는 자동 정렬을 눌러도 아이템을 직접 옮기지 않고 가이드만 표시합니다.
- 새 버전이 나오면 패널 위쪽에 `원클릭 업데이트` 버튼이 나타납니다.

## 계산에 반영하는 것

- **글로벌 조합 최적화 탐색 엔진**:
  - **소규모 인벤토리(`12슬롯·10아이템 이하`)**: 실제 기대 DPS 기반의 수학적 상한선(Upper Bound) 분기 한정(`Branch & Bound`)으로 전역 최적해를 보장합니다.
  - **대규모 인벤토리**: 전체 그리드 결합 `Beam Search` 시드 합성 + 다중 시작점 스케일-불변 `LAHC + Simulated Annealing + Tabu Search` 하이브리드 탐색(석판·아티팩트 연동 블록 이동, 페널티 상쇄 이동, 동료 혼돈 행 정렬, 금빛침/나침반 연쇄, 3×3 조화 허브, 1×3 하얀 종이 샌드위치, 2-opt/3-opt 순환) → 결정론적 전수 폴리싱을 수행하며, 평가 핫패스는 힙 할당 **0 Bytes**를 유지합니다.
- **다중 최상 배치 자동 해소 및 대안 선택**: 동일 아이템(`SymmetryGroupId`) 및 대칭 회전 중복을 정규화 해시로 제거한 뒤 `최고 예상 DPS → 하드 제약 위반 0건 → 유틸리티 유효 레벨 합 → 평가 점수 → 최소 스왑/회전 비용` 사전식 우선순위로 기본 해(`#1`)를 확정하며, 동점/근접 대안이 2개 이상일 때만 `[1] [2] [3]` 전환 버튼을 노출합니다.
- **인게임 실코드 정합성 (`Sephiria 1.1.4`)**:
  - 2-Pass 수렴형 버프·동적 카테고리·신비(Mystic) 평가(`releasePermission`), 석판 `+`/`-`/`×`/배율·바닥 각인·비정형 해금 슬롯·`Y_PLUS` 자기 버프·만렙 초과 상쇄
  - 신비 2/4세트 가산 배율(`+2`, 각인 레벨 포함 증폭 후 `MaxLevel` 클램핑) 및 추가 신비 슬롯 반영
  - 북향의 금빛침·나침반 계층(`parentOffsets`), 조화의 수정 주변 8칸 음수 차감 및 합연산 모든 피해(%) 반영
  - **헌신의 휘장 및 동료 계열 아티팩트**: `Charm_CompanionChaos` 동일 행 혼돈 피해 증폭(`+25%`) 및 동료 공격·능력치(`FOLLOWERDAMAGE`, `FOLLOWERATTACKSPEED`, `FOLLOWERCRITICAL`) DPS 반영
  - 캘세더니 열쇠, 하얀 종이, 목각 상자, 화염과 냉기, 무한 콤보(`surplus × 수치`), 4방향 인접 배치 보너스 및 실제 무기/스탯 버킷 기준 허수아비 예상 DPS 산출

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
