<div align="center">

# Sephiria Optimizer

세피리아 인벤토리의 석판과 아티팩트 배치를 계산해서 정렬해 주는 모드

[![Release](https://img.shields.io/github/v/release/lij0825/SephiriaOptimizer?style=flat-square&color=2ea44f)](https://github.com/lij0825/SephiriaOptimizer/releases/latest)
[![Sephiria](https://img.shields.io/badge/Sephiria-1.0.33-3b82f6?style=flat-square)](https://store.steampowered.com/app/2436940/Sephiria/)
[![Platform](https://img.shields.io/badge/Windows%20%7C%20Steam%20Deck%20%7C%20macOS-6366f1?style=flat-square)](#설치)

</div>

인벤토리를 열고 오른쪽 패널의 `[자동 정렬 실행]`을 누르면 석판 버프와 아티팩트 시너지가 가장 잘 들어가는 배치로 아이템을 옮기고 석판도 돌려 줍니다.
직접 옮기고 싶을 때는 가이드를 켜 두고 칸마다 뜨는 위치 표시만 보고 옮기면 됩니다.

게임 기본 애드온 로더로 동작해서 BepInEx 같은 외부 로더는 필요 없습니다.

## 설치

1. [릴리즈 페이지](https://github.com/lij0825/SephiriaOptimizer/releases/latest)에서 `SephiriaOptimizer_v2.2.0.zip`을 받아 압축을 풉니다.
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
| `가이드: 켜짐 / 꺼짐` | 칸 위에 이동할 위치(`→[열,행]`), 회전 각도(`↻`), 예상 레벨을 표시합니다. |
| `빌드 설정` | 캘세더니 열쇠 속성, 목표 세트, 금빛침 대상, 아티팩트 우선순위를 정합니다. |
| `슬롯 고정(Pin)` | 켜 둔 상태에서 칸을 우클릭하면 그 자리는 옮기지 않고 고정합니다. |
| 우클릭 (기본) | 아티팩트 우선순위(최상 ~ 최하)를 바꿉니다. 최하는 마이너스 칸을 대신 막는 용도로 들어갑니다. |

- 전투 중이거나 마우스로 아이템을 들고 있을 때는 자동 정렬을 눌러도 아이템을 직접 옮기지 않고 가이드만 표시합니다.
- 새 버전이 나오면 패널 위쪽에 `원클릭 업데이트` 버튼이 나타납니다.

## 계산에 반영하는 것

- 석판의 +/− 레벨, 배율, 비활성(X), 바닥 각인, 신비 세트 2배 효과 (한 석판의 마이너스가 다른 석판의 플러스와 겹쳐 버프가 깎이는 배치는 피해서 계산)
- 아티팩트별 최대 레벨과 위치 조건 (맨 위, 맨 아래, 양 끝, 테두리, 안쪽 등)
- 북향의 금빛침, 조화의 수정, 캘세더니 열쇠, 하얀 종이, 혼돈의 동료, 행성 모듈, 마법 탄환, 목각 상자, 화염과 냉기 같은 위치 시너지
- 캐릭터 속성 스탯과 착용 중인 무기 속성, 생존 20(석판 각인)·고유한 한 쌍·무한 콤보 등의 재능

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
