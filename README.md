# SephiriaOptimizer

스팀 게임 **세피리아(Sephiria)** 인벤토리의 아티팩트(`Charm_Basic`)와 석판(`StoneTablet`) 배치를 실시간 분석하여 **1) 단축키 한 번으로 최적 슬롯 자동 재배치(`F5`)** 및 **2) 슬롯 위 시각적 오버레이 가이드(`F6`)**를 제공하는 하이브리드 인게임 모드입니다.

---

## 주요 기능

### 1. 세피리아 도메인 맞춤형 비동기 최적화 솔버 (`Task.Run`)
- **유효 레벨 클램핑**: 아티팩트 별 개수(`maxLevel`) 상한을 반영하여 $L_i = \min(\text{MaxLevel}_i, \text{BaseLevel}_i + \Delta L_i)$로 유효 레벨을 산출하고 잉여 버프 낭비를 방지합니다.
- **비활성화 페널티 방어 ($L_i \le -1$ 및 `"X"`)**: 석판의 레벨 감소(`-N`) 또는 비활성화(`"X"`) 효과로 인해 아티팩트 레벨이 `-1` 이하로 떨어져 무효화되는 현상을 방지합니다.
- **페널티 증발 최적화**: 석판의 `-N` / `"X"` 타겟 좌표가 인벤토리 그리드 바깥(잠긴 슬롯 포함), 빈 슬롯, 혹은 레벨이 없는 다른 석판을 향하도록 배치 및 회전시켜 페널티를 무효화합니다.
- **10종 아티팩트 제약 조건 & 제약 조건 무시(`IgnoreCriteria`) 시너지**:
  - `TopInInventory`, `BottomInInventory`, `SideEnd`, `Outlined`, `Inside`, `BothSideCharm`, `BothSidesAreEmpty`, `Near8MagicBook`, `NeighborsAreFull`, `FullHP` 등 인게임 10종 제약 조건을 모두 판정합니다.
  - 제약 조건이 달린 아티팩트를 석판의 `IGNORECRITERIA` 타겟 칸에 배치하여 강제 활성화 시너지를 극대화합니다.
- **2단계 탐색 알고리즘 & 무할당(`0 Bytes`) 평가 루프**:
  - **Small Scale (해금 슬롯 $\le 12$ 및 아이템 $\le 10$)**: 분기 한정법(Branch & Bound)과 동일 아이템/석판 대칭 가지치기(Symmetry Breaking)로 전역 최적해(Global Optimum)를 도출합니다.
  - **Large Scale (대규모 인벤토리)**: 담금질 기법(Simulated Annealing) + 정체 시 재가열(Reheating) + 결정론적 1-Opt 폴리싱을 통해 `400ms` 이내에 최적 배치를 도출합니다.
  - 내부 평가 함수(`ObjectiveEvaluator.Evaluate`)는 `stackalloc int` 버퍼만 사용하여 단일 평가당 힙 할당 `0 Bytes`를 유지합니다.

### 2. 하이브리드 동작 모드
- **모드 1 — 안전한 자동 스왑 (`[F5]` 키 또는 상단 UI 버튼)**:
  - 현재 배치와 최적 배치 순열을 **순열 사이클 분해(Cycle Decomposition)** 알고리즘으로 분석해 최소 횟수의 2-슬롯 교환(`GridInventory.Swap`) 및 석판 회전(`GridInventory.DoClickAction`) 시퀀스를 생성합니다.
  - 메인 스레드 코루틴에서 프레임 단위로 인게임 정규 메서드를 호출하여 Mirror 네트워크 동기화 및 세트/콤보 재계산이 정상 트리거됩니다.
  - 전투 중(`IsInBattle`), 사망(`IsDead`), 또는 인벤토리 이동 잠금(`!IsPickable`) 상태일 때는 자동 스왑을 중단하고 즉시 **오버레이 가이드 모드**로 폴백합니다.
- **모드 2 — 시각적 오버레이 가이드 (`[F6]` 키 토글)**:
  - 각 슬롯의 화면 좌표(`RectTransform`) 위에 이동 목표 좌표 배지(`→ [x, y]`), 석판 회전 배지(`↻ +90°`), 예상 최종 레벨 배지(`Lv.3 (MAX)`, `-1 (비활성 경고)`)를 렌더링합니다.
  - 슬롯에 마우스를 올리거나 클릭하면 이동해야 할 목표 슬롯까지 하이라이트 연결선을 표시합니다.
  - **슬롯 고정(Pin) 모드**: 상단 패널에서 Pin 모드를 켜고 슬롯을 우클릭하면 특정 아이템의 위치와 회전을 고정한 채로 나머지 슬롯만 최적화할 수 있습니다.

---

## 프로젝트 구조

```text
SephiriaOptimizer/
├── SephiriaOptimizer/
│   ├── Execution/
│   │   └── CycleDecomposition.cs         # 순열 사이클 분해 및 회전 시퀀스 생성
│   ├── Models/
│   │   ├── ArtifactConstraintType.cs     # 10종 아티팩트 제약 및 석판 효과 열거형
│   │   ├── InventorySnapshot.cs          # 스레드 안전 인벤토리 불변 스냅샷
│   │   ├── SlotCoord.cs                  # 그리드 좌표 구조체
│   │   ├── SlotItemSnapshot.cs           # 개별 아이템/석판 스냅샷 및 대칭 그룹 ID
│   │   └── TabletPlacementRule.cs        # 슬롯·회전별 사전 계산된 석판 규칙
│   ├── Snapshot/
│   │   └── GameInventoryReader.cs        # 인게임 GridInventory 데이터 리더
│   ├── Solver/
│   │   ├── ObjectiveEvaluator.cs         # stackalloc 기반 무할당 목적 함수 평가기
│   │   ├── OptimizationResult.cs         # 최적화 결과 및 슬롯별 상세 평가 데이터
│   │   └── OptimizationSolver.cs         # Branch & Bound + Simulated Annealing 솔버
│   ├── UI/
│   │   └── SephiriaOptimizerController.cs # [F5] 자동 스왑 코루틴 및 [F6] 오버레이 UI
│   ├── SephiriaOptimizerMod.cs           # 세피리아 내장 HorayModBase 엔트리포인트
│   ├── metadata.json                     # 세피리아 AddOnLoader 메타데이터
│   └── SephiriaOptimizer.csproj          # .NET Standard 2.1 모드 프로젝트
└── SephiriaOptimizer.Tests/
    ├── OptimizationSolverTest.cs         # 솔버 및 사이클 분해 xUnit 단위 테스트
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
   - `Pin 모드`: 활성화 후 슬롯 우클릭 시 해당 슬롯 아이템 고정/해제

---

## 빌드 및 테스트

```powershell
# 릴리즈 빌드
dotnet build SephiriaOptimizer/SephiriaOptimizer.csproj -c Release

# 단위 테스트 실행
dotnet test SephiriaOptimizer.Tests/SephiriaOptimizer.Tests.csproj -c Release
```
