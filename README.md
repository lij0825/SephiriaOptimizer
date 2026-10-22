# SephiriaOptimizer

스팀 게임 **세피리아(Sephiria)** 인벤토리의 아티팩트(`Charm_Basic`), 석판(`StoneTablet`), 세트 콤보(`#태그`), 그리고 재능(`Passive`/`StatusInstance`) 규칙을 실시간 분석하여 **1) 단축키 한 번으로 최적 슬롯 자동 재배치(`F5`)**, **2) 슬롯 위 시각적 오버레이 가이드(`F6`)**, **3) 빌드 맞춤 우선순위·콤보·금빛침 설정(`F7`)**을 제공하는 하이브리드 인게임 모드입니다.

---

## 🚀 일반 유저용 3초 원클릭 자동 설치 (폴더 찾기·빌드 불필요)

별도의 모드 로더(`BepInEx`) 설치나 스팀 폴더 경로를 직접 찾을 필요 없이 아래 방법 중 하나로 즉시 설치할 수 있습니다. (스팀 레지스트리와 모든 드라이브를 자동 탐색해 세피리아 설치 경로를 스스로 찾아 설치하며, 윈도우 외부 DLL 차단 해제까지 자동으로 처리합니다.)

### 방법 1. ZIP 다운로드 후 더블클릭 (가장 추천)
1. **[👉 SephiriaOptimizer_v2.0.0.zip 바로 다운로드](https://raw.githubusercontent.com/lij0825/SephiriaOptimizer/main/Dist/SephiriaOptimizer_v2.0.0.zip)** 를 클릭해 다운로드하고 압축을 풉니다.
2. 폴더 안의 **`설치하기_Install.bat`** 파일을 **더블클릭**하면 설치 완료 알림창이 뜹니다!
   - 모드를 삭제하고 싶을 때는 동봉된 **`제거하기_Uninstall.bat`**만 더블클릭하면 깔끔하게 삭제됩니다.

### 방법 2. 압축 풀기도 귀찮다면 — PowerShell 한 줄 복사·붙여넣기
윈도우 시작 메뉴에서 **PowerShell**을 열고 아래 한 줄을 복사해 붙여넣은 뒤 엔터(`Enter`)를 누르면 다운로드부터 설치까지 1초 만에 완료됩니다:
```powershell
irm https://raw.githubusercontent.com/lij0825/SephiriaOptimizer/main/Dist/install.ps1 | iex
```

---

## 주요 기능

### 1. 세피리아 도메인 맞춤형 비동기 최적화 솔버 (`Task.Run`)
- **유효 레벨 클램핑**: 아티팩트 별 개수(`maxLevel`) 상한을 반영하여 $L_i = \min(\text{MaxLevel}_i, \text{BaseLevel}_i + \Delta L_i)$로 유효 레벨을 산출하고 잉여 버프 낭비를 방지합니다.
- **비활성화 페널티 방어 ($L_i \le -1$ 및 `"X"`)**: 석판의 레벨 감소(`-N`) 또는 비활성화(`"X"`) 효과로 인해 아티팩트 레벨이 `-1` 이하로 떨어져 무효화되는 현상을 방지합니다.
- **페널티 증발 및 흡수 최적화**:
  1. 석판의 `-N` / `"X"` 타겟 좌표가 인벤토리 그리드 바깥(잠긴 슬롯 포함)이나 빈 슬롯을 향하도록 배치 및 회전시켜 페널티를 증발시킵니다.
  2. 레벨이 없는 **다른 석판**이나 **`✕희생(Sacrifice)` 등급으로 지정된 콤보 토템 아티팩트**를 `-N` / `"X"` 타겟 슬롯에 배치해 페널티를 안전하게 흡수합니다(세피리아 인게임 규칙상 아티팩트가 비활성화되어도 정적 `#태그` 콤보 개수는 그대로 유지됨).
- **캘세더니 열쇠 (`Charm_3Elemental_ByRow`) & 하얀 종이 (`Charm_WhitePaper`) 동적 `#태그` 콤보 최적화**:
  - 인게임 `GridInventory.ReleasePermission()`의 `Pre -> Normal -> Post` 실행 순서를 비트마스크 연산으로 정확히 시뮬레이션합니다.
  - **캘세더니 열쇠 (`Pre` 단계)**: 배치된 행(`YIdx % 4`)에 따라 결정되는 속성 태그(`0행: 잉걸불/EMBER`, `1행: 빙하/GLACIER`, `2행: 마도공학/MAGITECH`, `3행: 견고함/STURDY`)가 사용자의 목표 콤보 단계(`Breakpoints`)를 달성하도록 최적의 행에 배치합니다.
  - **하얀 종이 (`Post` 단계)**: 좌우 인접 칸(`[X-1, Y]`, `[X+1, Y]`)에 배치된 아티팩트들의 정적·동적 카테고리(캘세더니 열쇠 및 금빛침이 획득한 태그 포함)를 검사하여, 목표 콤보 태그를 공유하는 두 아티팩트(`match >= 2`) 사이에 정확히 배치합니다.
- **북향의 금빛침 (`Charm_UpCharmDamage`) 타겟 지정 & 연쇄 시너지 최적화**:
  - 금빛침이 가리키는 방향 오프셋(`(X + xOffset, Y + yOffset)`, 기본 위쪽 `[X, Y-1]`)에 공격 판정 아티팩트(`IAttackableCharm.IsAttackableCharm() == true`)가 오도록 배치하며, 비공격형 아티팩트를 가리켜 효과가 낭비되는 배치를 방지합니다.
  - **등급 조건부 2배 증폭 (`hasDependencyCondition`)**: 강화 대상 루트 아티팩트의 등급이 고급(`Uncommon`) 이하일 때 `dependencyDamageBonusByLevel`이 추가 적용되어 피해 증폭량이 2배가 되는 인게임 메커니즘을 반영합니다.
  - **다중 침 연쇄 & 카테고리 복제**: `금빛침 -> 금빛침 -> 공격형 아티팩트` 연쇄 연결과 루트 아티팩트의 `#태그` 복제(하얀 종이 시너지 연동 포함)를 지원하며, `[F7]` 설정 창에서 금빛침이 강화할 특정 공격 아티팩트를 직접 지정할 수도 있습니다.
- **아티팩트 5단계 강화 우선순위 (`ArtifactPriorityTier`)**:
  - 인벤토리 슬롯 우클릭(Pin 모드 OFF 시) 또는 `[F7]` 설정 창에서 아티팩트별 강화 우선순위를 5단계로 지정할 수 있습니다:
    - `★최우선 (Core, ×3.0)`: 핵심 주력 딜링 아티팩트 (석판 `+레벨` 및 금빛침 최우선 집중)
    - `▲높음 (High, ×1.8)`: 주요 서브 딜링/핵심 유틸 아티팩트
    - `●보통 (Normal, ×1.0)`: 기본 가중치
    - `▼토템 (Low, ×0.33)`: 레벨 효율이 낮거나 자체 효과보다 보유 의미가 큰 아티팩트
    - `✕희생 (Sacrifice)`: 비활성화 페널티 0점 처리 — 정적 `#태그` 콤보 숫자만 채우면서 석판의 `-N` / `"X"` 효과를 대신 맞아주는 고기방패용 아티팩트
- **재능(Talent) 및 특수 빌드 규칙 자동 감지**:
  - **생존 20 (`tabletEngravingCount > 0`)**: 인벤토리 최대 슬롯이 `-12칸` 감소함에 따라 달라지는 제약 조건 경계(`BottomInInventory`, `Outlined`, `Inside`)를 동적으로 재계산하고, 이미 각인된 영구 슬롯 효과(`fixedEngravingsOnServer`, `engravings`)를 반영합니다. 또한 석판 각인 시 그리드 바깥(`PosToIdx >= CurrentInventoryStorage`)을 향하는 `-N`/`"X"` 효과는 영구 증발한다는 점을 이용해, 활성화된 석판의 페널티를 그리드 밖으로 빼서 안전하게 각인할 수 있는 위치에 **`[각인 최적]`** 배지를 표시합니다.
  - **고유한 한 쌍 (`uniquePairCount > 0`)**, **무한 콤보 (`UNLIMITEDCOMBO`)**, **콤보 당 최종 피해 (`COMBOBONUSDAMAGE`)**, **배치 보너스 (`ARRANGEMENTBONUS`)**, **듀얼 아티팩트 비활성화 (`DISABLEDUALARTIFACT`)**, **중복 아티팩트 콤보 제한 (`OVERLAPITEMCOMBO`)** 및 위치 의존 아티팩트(`Charm_NearLevelDamage`, `Charm_CompanionChaos`, `Charm_PlanetModule`, `Charm_NearMagicBullet`, `Charm_WoodenBox`, `Charm_FireIce`) 시너지를 모두 계산합니다.
- **2단계 탐색 알고리즘 & 무할당(`0 Bytes`) 평가 루프**:
  - **Small Scale (해금 슬롯 $\le 12$ 및 아이템 $\le 10$)**: 분기 한정법(Branch & Bound)과 대칭 가지치기(Symmetry Breaking)로 전역 최적해(Global Optimum)를 도출합니다.
  - **Large Scale (대규모 인벤토리)**: 담금질 기법(Simulated Annealing) + 재가열(Reheating) + 결정론적 1-Opt 폴리싱을 통해 `400ms` 이내에 최적 배치를 도출합니다.
  - 내부 평가 함수(`ObjectiveEvaluator.Evaluate`)는 `stackalloc int` 및 `stackalloc uint` 버퍼만 사용하여 단일 평가당 힙 할당 `0 Bytes`를 유지합니다.

### 2. 하이브리드 동작 모드 & 인게임 UI
- **모드 1 — 안전한 자동 스왑 (`[F5]` 키 또는 상단 UI 버튼)**:
  - 현재 배치와 최적 배치 순열을 **순열 사이클 분해(Cycle Decomposition)** 알고리즘으로 분석해 최소 횟수의 2-슬롯 교환(`GridInventory.Swap`) 및 석판 회전(`GridInventory.DoClickAction`) 시퀀스를 생성합니다.
  - 메인 스레드 코루틴에서 프레임 단위로 인게임 정규 메서드를 호출하여 Mirror 네트워크 동기화 및 세트/콤보 재계산이 정상 트리거됩니다.
  - 전투 중(`IsInBattle`), 사망(`IsDead`), 또는 인벤토리 이동 잠금(`!IsPickable`) 상태일 때는 자동 스왑을 중단하고 즉시 **오버레이 가이드 모드**로 폴백합니다.
- **모드 2 — 시각적 오버레이 가이드 (`[F6]` 키 토글)**:
  - 각 슬롯의 화면 좌표(`RectTransform`) 위에 이동 목표 좌표 배지(`→ [x, y]`), 석판 회전 배지(`↻ +90°`), 예상 최종 레벨 배지(`Lv.3 (MAX)`, `-1 (비활성 경고)`, `희생(콤보유지)`), 우선순위 배지(`★최우선`, `✕희생`), 생존 20 각인 추천 배지(`각인 최적`)를 렌더링합니다.
  - **우클릭 빠른 우선순위 변경**: Pin 모드가 꺼져 있을 때 인벤토리 내 아티팩트 슬롯을 우클릭하면 `●보통 → ★최우선 → ▲높음 → ▼토템 → ✕희생` 순으로 강화 우선순위가 즉시 순환됩니다.
- **모드 3 — 빌드 맞춤 설정 패널 (`[F7]` 키 토글)**:
  - **목표 콤보(`#태그`) 선택**: 캘세더니 열쇠와 하얀 종이가 우선적으로 완성할 `#태그`(잉걸불, 빙하, 마도공학, 견고함, 바람노래, 역병, 그림자, 비전 등)를 직접 선택하거나 활성 콤보 기반 자동 추천을 사용할 수 있습니다.
  - **북향의 금빛침 강화 대상 선택**: 인벤토리 내 공격형 아티팩트 중 금빛침이 집중 강화할 대상을 직접 지정할 수 있습니다.
  - **아티팩트별 5단계 우선순위 제어**: 보유한 모든 아티팩트의 강화 우선순위를 한눈에 확인하고 클릭으로 변경할 수 있습니다.

---

## 프로젝트 구조

```text
SephiriaOptimizer/
├── SephiriaOptimizer/
│   ├── Execution/
│   │   └── CycleDecomposition.cs         # 순열 사이클 분해 및 회전 시퀀스 생성
│   ├── Models/
│   │   ├── ArtifactConstraintType.cs     # 10종 아티팩트 제약, 5단계 우선순위, 석판 효과 열거형
│   │   ├── InventorySnapshot.cs          # 스레드 안전 인벤토리 불변 스냅샷
│   │   ├── SlotCoord.cs                  # 그리드 좌표 구조체
│   │   ├── SlotItemSnapshot.cs           # 개별 아이템/석판/동적 아티팩트 스냅샷 및 대칭 그룹 ID
│   │   ├── TabletPlacementRule.cs        # 슬롯·회전별 사전 계산된 석판 규칙
│   │   └── TalentAndComboModels.cs       # 콤보 브레이크포인트, 재능(생존 20 등), 사용자 설정 모델
│   ├── Snapshot/
│   │   └── GameInventoryReader.cs        # 인게임 GridInventory & PlayerAvatar 데이터 리더
│   ├── Solver/
│   │   ├── ObjectiveEvaluator.cs         # stackalloc 기반 무할당 목적 함수 평가기 (Pre->Normal->Post)
│   │   ├── OptimizationResult.cs         # 최적화 결과 및 슬롯별 상세 평가 데이터
│   │   └── OptimizationSolver.cs         # Branch & Bound + Simulated Annealing 솔버
│   ├── UI/
│   │   └── SephiriaOptimizerController.cs # [F5] 자동 스왑, [F6] 오버레이, [F7] 빌드 설정 UI
│   ├── SephiriaOptimizerMod.cs           # 세피리아 내장 HorayModBase 엔트리포인트
│   ├── metadata.json                     # 세피리아 AddOnLoader 메타데이터
│   └── SephiriaOptimizer.csproj          # .NET Standard 2.1 모드 프로젝트
└── SephiriaOptimizer.Tests/
    ├── OptimizationSolverTest.cs         # 솔버, 캘세더니/하얀종이, 금빛침, 우선순위, 생존 20 단위 테스트
    └── SephiriaOptimizer.Tests.csproj    # .NET 8 테스트 프로젝트
```

---

## 설치 및 사용 방법

### 자동 로드 설치 (`AddOns` 내장 모드로더)
세피리아는 별도의 `BepInEx` 설치 없이도 내장 `AddOnLoader`를 통해 모드를 로드합니다.
1. 릴리즈 빌드된 `SephiriaOptimizer.dll`과 `metadata.json`을 아래 경로에 복사합니다:
   ```text
   D:\Steam\steamapps\common\Sephiria\AddOns\SephiriaOptimizer\
   ├── SephiriaOptimizer.dll
   └── metadata.json
   ```
2. 게임을 실행하고 인벤토리 창을 열면 상단에 **Sephiria Optimizer** 컨트롤 패널이 표시됩니다.
   - `F5`: 최적 배치로 자동 스왑 및 석판 회전 실행
   - `F6`: 슬롯 위 시각적 오버레이 가이드 켜기/끄기
   - `F7`: 빌드 맞춤 설정 창(목표 `#태그` 콤보 / 북향의 금빛침 타겟 / 아티팩트 강화 우선순위) 열기/닫기
   - `우클릭`: Pin 모드 OFF 시 아티팩트 강화 우선순위(`●/★/▲/▼/✕`) 순환, Pin 모드 ON 시 슬롯 위치 고정/해제

---

## 빌드 및 테스트

```powershell
# 릴리즈 빌드
dotnet build SephiriaOptimizer/SephiriaOptimizer.csproj -c Release

# 단위 테스트 실행 (9개 도메인 시나리오 검증)
dotnet test SephiriaOptimizer.Tests/SephiriaOptimizer.Tests.csproj -c Release
```
