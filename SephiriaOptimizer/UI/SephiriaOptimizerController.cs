using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using SephiriaOptimizer.Execution;
using SephiriaOptimizer.Models;
using SephiriaOptimizer.Snapshot;
using SephiriaOptimizer.Solver;
using SephiriaOptimizer.Update;
using UnityEngine;

namespace SephiriaOptimizer.UI {
	public sealed class SephiriaOptimizerController : MonoBehaviour {
		private const string PrefKeyCollapsed = "SephiriaOptimizer.UI.Collapsed";

		private const float SidePanelWidth = 286f;
		private const float CollapsedPanelWidth = 224f;
		private const float CollapsedPanelHeight = 32f;
		private const float ScreenEdgeMargin = 12f;

		private static readonly (string Id, string KorName)[] ChalcedonyComboTags = new[] {
			("STURDY", "견고"),
			("EMBER", "잉걸불"),
			("GLACIER", "빙하"),
			("MAGITECH", "마법공학")
		};

		private static readonly (string Id, string KorName)[] WhitePaperComboTags = new[] {
			("EMBER", "잉걸불"),
			("FROST", "얼음무구"),
			("GLACIER", "빙하"),
			("MAGITECH", "마법공학"),
			("SHADOW", "그림자"),
			("GUARDIAN", "수호"),
			("WINDSONG", "바람노래"),
			("MYSTIC", "신비"),
			("PLANET", "행성"),
			("COMPANION", "동료"),
			("PRECISION", "정밀"),
			("DARKCLOUD", "먹구름"),
			("STURDY", "견고"),
			("LAKE", "호수"),
			("FLAMESWORD", "태양검"),
			("ACADEMY", "아카데미"),
			("CURSE", "저주"),
			("SAVVY", "교섭"),
			("ELEMENTAL", "원소"),
			("ALCHEMY", "연금술")
		};

		private readonly HashSet<int> pinnedSlotIndices = new HashSet<int>();
		private readonly Dictionary<int, int> customEntityWeights = new Dictionary<int, int>();
		private readonly OptimizerUserSettings userSettings = new OptimizerUserSettings();
		private readonly Vector3[] worldCornersBuffer = new Vector3[4];
		private readonly Rect[] slotScreenRectsBuffer = new Rect[InventorySnapshot.MaxSupportedSlots];
		private readonly List<Rect> potionObstacleRects = new List<Rect>(8);
		private readonly List<Rect> inventoryObstacleRects = new List<Rect>(6);
		private readonly List<Rect> sidePanelObstacleRects = new List<Rect>(6);
		private readonly OptimizationSolver solver = new OptimizationSolver();
		private readonly GitHubReleaseUpdater releaseUpdater = new GitHubReleaseUpdater();

		private InventorySnapshot? latestSnapshot;
		private OptimizationResult? latestResult;
		private GameCompatibilityReport? compatibilityReport;
		private PlayerAvatar? cachedWeaponCtrlAvatar;
		private WeaponControllerSimple? cachedWeaponCtrl;

		private Task<OptimizationResult>? activeSolveTask;
		private InventorySnapshot? pendingTaskSnapshot;

		private string installedModVersion = ModVersionConstants.CurrentModVersion;
		private string modDirectoryPath = string.Empty;
		private bool hasInitializedRuntimeChecks;
		private bool isVersionWarningDismissed;
		private bool isRestartPromptDismissed;

		private bool isPanelCollapsed;
		private Rect lastRenderedSidePanelRect;
		private DpsTooltipCache cachedDpsTooltip = DpsTooltipCache.CreateIdle(string.Empty);

		private bool isOverlayEnabled = true;
		private bool isPinModeEnabled;
		private bool isConfigPanelOpen;
		private bool isAutoSwapRunning;
		private int lastObservedStateHash;
		private int selectedSlotIndex = -1;
		private Vector2 configScrollPos;
		private string statusBannerText = "준비 완료 — 버튼을 클릭하여 자동 정렬 및 빌드 설정";

		private GUIStyle? headerLabelStyle;
		private GUIStyle? subHeaderStyle;
		private GUIStyle? statusWrapStyle;
		private GUIStyle? alertLabelStyle;
		private GUIStyle? buttonStyle;
		private GUIStyle? compactButtonStyle;
		private GUIStyle? activePinButtonStyle;
		private GUIStyle? selectedButtonStyle;
		private GUIStyle? accentUpdateButtonStyle;
		private GUIStyle? badgeStyle;
		private GUIStyle? coordBadgeStyle;
		private GUIStyle? pinTagStyle;

		public void InitializeModContext(string? loadedModVersion, string? loadedModDirectory) {
			if (!string.IsNullOrWhiteSpace(loadedModDirectory)) {
				modDirectoryPath = loadedModDirectory;
			}
			if (!string.IsNullOrWhiteSpace(loadedModVersion)) {
				installedModVersion = VersionCompatibilityChecker.NormalizeVersionText(loadedModVersion);
			}
		}

		private void Start() {
			LoadLayoutPreferences();
			EnsureRuntimeChecksInitialized();
		}

		private void EnsureRuntimeChecksInitialized() {
			if (hasInitializedRuntimeChecks) {
				return;
			}
			hasInitializedRuntimeChecks = true;

			if (string.IsNullOrWhiteSpace(modDirectoryPath)) {
				modDirectoryPath = VersionCompatibilityChecker.ResolveModDirectory();
			}
			if (VersionCompatibilityChecker.TryReadInstalledMetadata(
				modDirectoryPath,
				out string metaModVer,
				out string metaTargetGameVer
			)) {
				installedModVersion = metaModVer;
				compatibilityReport = VersionCompatibilityChecker.EvaluateCompatibility(
					Application.version,
					metaTargetGameVer,
					installedModVersion,
					true
				);
			} else {
				compatibilityReport = VersionCompatibilityChecker.EvaluateRuntimeCompatibility(modDirectoryPath);
			}

			if (compatibilityReport.ShouldWarnUser) {
				Debug.LogWarning($"[SephiriaOptimizer] {compatibilityReport.BannerMessage}");
			}

			StartCoroutine(releaseUpdater.CheckForUpdatesCoroutine(installedModVersion, OnUpdaterStateUpdated));
		}

		private void OnUpdaterStateUpdated() {
			if (releaseUpdater.CurrentState == UpdateCheckState.UpdateAvailable
				&& releaseUpdater.LatestRelease != null) {
				SetStatusBanner(
					$"새 버전 v{releaseUpdater.LatestRelease.NormalizedVersion} 출시됨 — 패널에서 바로 업데이트 가능"
				);
			} else if (releaseUpdater.CurrentState == UpdateCheckState.AppliedPendingRestart) {
				isRestartPromptDismissed = false;
				SetStatusBanner(releaseUpdater.StatusMessage);
			}
		}

		private void Update() {
			EnsureRuntimeChecksInitialized();

			UI_CharacterStatusPanel? statusPanel = GetOpenInventoryPanel();
			if (statusPanel == null) {
				if (activeSolveTask != null && activeSolveTask.IsCompleted) {
					activeSolveTask = null;
				}
				if (latestResult != null || latestSnapshot != null || selectedSlotIndex >= 0) {
					ClearGuides();
				}
				return;
			}

			if (activeSolveTask != null && activeSolveTask.IsCompleted) {
				CompleteBackgroundSolve();
			}

			PlayerAvatar avatar = statusPanel.PlayerAvatar;
			GridInventory? inventory = avatar != null ? avatar.Inventory : null;
			if (avatar == null || inventory == null) {
				ClearGuides();
				return;
			}

			int currentStateHash = 0;
			int occupiedCount = 0;
			try {
				currentStateHash = ComputeInventoryStateHash(avatar, inventory, out occupiedCount);
			} catch (Exception hashEx) {
				Debug.LogError($"[SephiriaOptimizer] State hash failed: {hashEx.Message}");
				SetStatusBanner($"인벤토리 스캔 오류 ({hashEx.GetType().Name}) — 게임 버전을 확인하세요");
				return;
			}

			if (occupiedCount <= 0) {
				if (latestResult != null || latestSnapshot != null) {
					ClearGuides();
				}
				lastObservedStateHash = currentStateHash;
				return;
			}

			if (currentStateHash != lastObservedStateHash && !isAutoSwapRunning) {
				InvalidateStaleGuidesOnInventoryChange(inventory);
				if (activeSolveTask == null) {
					lastObservedStateHash = currentStateHash;
					StartBackgroundSolve(avatar, inventory);
				}
			}
		}

		private void OnGUI() {
			UI_CharacterStatusPanel? statusPanel = GetOpenInventoryPanel();
			if (statusPanel == null) {
				return;
			}

			PlayerAvatar avatar = statusPanel.PlayerAvatar;
			GridInventory? inventory = avatar != null ? avatar.Inventory : null;
			if (avatar == null || inventory == null) {
				return;
			}

			EnsureGuiStyles();

			bool shouldDrawSlotOverlay = isOverlayEnabled && latestSnapshot != null && latestResult != null;
			if (shouldDrawSlotOverlay) {
				DrawSlotOverlays(statusPanel, inventory, latestSnapshot!, latestResult!);
			}

			DrawSideControlPanel(statusPanel, avatar, inventory);

			if (isConfigPanelOpen && latestSnapshot != null) {
				DrawConfigPanel(statusPanel, inventory, latestSnapshot);
			}
		}

		public void ClearGuides() {
			latestResult = null;
			latestSnapshot = null;
			pendingTaskSnapshot = null;
			selectedSlotIndex = -1;
			lastObservedStateHash = 0;
			cachedDpsTooltip = DpsTooltipCache.CreateIdle(string.Empty);
		}

		private void InvalidateStaleGuidesOnInventoryChange(GridInventory inventory) {
			if (latestSnapshot == null || latestResult == null) {
				return;
			}
			int unlocked = Math.Clamp(
				(int)inventory.CurrentInventoryStorage,
				0,
				InventorySnapshot.MaxSupportedSlots
			);
			if (unlocked != latestSnapshot.UnlockedSlots) {
				ClearGuides();
				return;
			}

			int width = Math.Max(1, (int)inventory.Width);
			int liveValidItemCount = 0;
			for (int s = 0; s < unlocked; s++) {
				sbyte gx = (sbyte)(s % width);
				sbyte gy = (sbyte)(s / width);
				NewItemOwnInstance? inst = inventory.FindItem(gx, gy);
				if (inst != null && inst.Entity != null) {
					liveValidItemCount++;
				} else if (selectedSlotIndex == s) {
					selectedSlotIndex = -1;
				}
			}

			if (liveValidItemCount != latestSnapshot.Items.Length) {
				latestResult = null;
				selectedSlotIndex = -1;
			}
		}

		private void StartBackgroundSolve(PlayerAvatar avatar, GridInventory inventory) {
			try {
				InventorySnapshot snapshot = GameInventoryReader.CaptureSnapshot(
					avatar,
					inventory,
					pinnedSlotIndices,
					customEntityWeights,
					userSettings
				);
				pendingTaskSnapshot = snapshot;
				if (latestSnapshot == null) {
					latestSnapshot = snapshot;
					RefreshCachedDpsTooltip();
				}
				activeSolveTask = Task.Run(() => solver.Solve(snapshot));
			} catch (Exception ex) {
				Debug.LogError($"[SephiriaOptimizer] Snapshot capture failed: {ex.Message}");
				SetStatusBanner($"스냅샷 캡처 오류 ({ex.GetType().Name})");
			}
		}

		private void CompleteBackgroundSolve() {
			if (activeSolveTask == null) {
				return;
			}
			try {
				if (activeSolveTask.Status == TaskStatus.RanToCompletion && pendingTaskSnapshot != null) {
					OptimizationResult result = activeSolveTask.Result;
					latestSnapshot = pendingTaskSnapshot;
					latestResult = result;
					RefreshCachedDpsTooltip();
				}
			} catch (Exception ex) {
				Debug.LogError($"[SephiriaOptimizer] Solver task failed: {ex.Message}");
			} finally {
				activeSolveTask = null;
				pendingTaskSnapshot = null;
			}
		}

		private void TriggerAutoSwapOrFallback(
			UI_CharacterStatusPanel statusPanel,
			PlayerAvatar avatar,
			GridInventory inventory
		) {
			if (compatibilityReport != null && compatibilityReport.ShouldLockAutoSwap) {
				isOverlayEnabled = true;
				SetStatusBanner(compatibilityReport.BannerMessage);
				return;
			}

			if (isAutoSwapRunning) {
				SetStatusBanner("자동 재배치가 이미 진행 중입니다.");
				return;
			}

			if (IsCursorHoldingAnyItem()) {
				isOverlayEnabled = true;
				SetStatusBanner("마우스로 집고 있는 아이템을 먼저 내려놓으세요 → 가이드 표시");
				return;
			}

			bool isLockedOrCombat = !CanContinueAutoSwap(statusPanel, avatar, inventory);
			if (isLockedOrCombat) {
				isOverlayEnabled = true;
				string reason = avatar.IsInBattle
					? "전투 중에는 자동 스왑이 제한됩니다"
					: "현재 인벤토리 이동이 잠겨 있습니다";
				SetStatusBanner($"{reason} → 가이드 모드로 전환됨");
				return;
			}

			InventorySnapshot snapshot;
			OptimizationResult result;
			ExecutionPlan plan;
			try {
				snapshot = GameInventoryReader.CaptureSnapshot(
					avatar,
					inventory,
					pinnedSlotIndices,
					customEntityWeights,
					userSettings
				);
				result = solver.Solve(snapshot);
				plan = CycleDecomposition.BuildExecutionPlan(
					snapshot,
					snapshot.InitialSlotToItemIndex,
					result.BestSlotToItemIndex,
					snapshot.InitialItemRotations,
					result.BestItemRotations
				);
			} catch (Exception solveEx) {
				Debug.LogError($"[SephiriaOptimizer] AutoSwap solve failed: {solveEx.Message}");
				SetStatusBanner($"최적화 계산 실패 ({solveEx.GetType().Name})");
				return;
			}

			latestSnapshot = snapshot;
			latestResult = result;
			RefreshCachedDpsTooltip();

			bool noImprovement = result.BestEstimatedDps <= result.InitialEstimatedDps
				&& result.BestScore <= result.InitialScore;
			if (noImprovement || plan.TotalOperations == 0) {
				SetStatusBanner($"이미 최적 배치 상태입니다 (예상 DPS: {result.BestEstimatedDps:N0} DPS).");
				return;
			}

			StartCoroutine(ExecuteAutoSwapCoroutine(statusPanel, avatar, inventory, snapshot, plan, result));
		}

		private IEnumerator ExecuteAutoSwapCoroutine(
			UI_CharacterStatusPanel statusPanel,
			PlayerAvatar avatar,
			GridInventory inventory,
			InventorySnapshot snapshot,
			ExecutionPlan plan,
			OptimizationResult targetResult
		) {
			isAutoSwapRunning = true;
			SetStatusBanner($"자동 정렬 중... (스왑 {plan.SwapSteps.Count}회, 회전 {plan.RotateSteps.Count}개)");

			int unlockedSlots = snapshot.UnlockedSlots;
			int[] expectedSlotInstanceIds = new int[unlockedSlots];
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = snapshot.InitialSlotToItemIndex[s];
				expectedSlotInstanceIds[s] = itemIdx >= 0 && itemIdx < snapshot.Items.Length
					? snapshot.Items[itemIdx].InstanceId
					: -1;
			}

			bool canVerifyPerStep = inventory.isServer;
			if (!canVerifyPerStep && !VerifyAllSlotInstanceIds(inventory, snapshot, expectedSlotInstanceIds)) {
				AbortAutoSwap("슬롯 아이템 상태 변경 감지됨 → 자동 스왑 중단 및 재스캔");
				yield break;
			}

			IReadOnlyList<SlotSwapStep> swaps = plan.SwapSteps;
			for (int i = 0; i < swaps.Count; i++) {
				if (!CanContinueAutoSwap(statusPanel, avatar, inventory)) {
					AbortAutoSwap("이동 제한 감지됨 → 자동 스왑 중단 및 가이드 폴백");
					yield break;
				}
				if (IsCursorHoldingAnyItem()) {
					AbortAutoSwap("커서 아이템 집기 감지됨 → 자동 스왑 중단 및 재스캔");
					yield break;
				}

				SlotSwapStep step = swaps[i];
				if (canVerifyPerStep) {
					bool firstValid = VerifySlotInstanceId(
						inventory,
						step.FirstCoord,
						expectedSlotInstanceIds[step.FirstSlotIndex]
					);
					bool secondValid = VerifySlotInstanceId(
						inventory,
						step.SecondCoord,
						expectedSlotInstanceIds[step.SecondSlotIndex]
					);
					if (!firstValid || !secondValid) {
						AbortAutoSwap("슬롯 InstanceID 불일치 감지됨 → 자동 스왑 중단 및 재스캔");
						yield break;
					}
				}

				inventory.Swap(
					step.FirstCoord.X,
					step.FirstCoord.Y,
					step.SecondCoord.X,
					step.SecondCoord.Y
				);

				int tempInstanceId = expectedSlotInstanceIds[step.FirstSlotIndex];
				expectedSlotInstanceIds[step.FirstSlotIndex] = expectedSlotInstanceIds[step.SecondSlotIndex];
				expectedSlotInstanceIds[step.SecondSlotIndex] = tempInstanceId;
			}

			IReadOnlyList<TabletRotateStep> rotates = plan.RotateSteps;
			for (int i = 0; i < rotates.Count; i++) {
				TabletRotateStep rotStep = rotates[i];
				ItemPosition pos = new ItemPosition(rotStep.TargetCoord.X, rotStep.TargetCoord.Y);
				int expectedId = rotStep.TargetSlotIndex >= 0 && rotStep.TargetSlotIndex < unlockedSlots
					? expectedSlotInstanceIds[rotStep.TargetSlotIndex]
					: -1;

				for (int click = 0; click < rotStep.ClickCount; click++) {
					if (!CanContinueAutoSwap(statusPanel, avatar, inventory)) {
						AbortAutoSwap("이동 제한 감지됨 → 자동 회전 중단 및 가이드 폴백");
						yield break;
					}
					if (IsCursorHoldingAnyItem()) {
						AbortAutoSwap("커서 아이템 집기 감지됨 → 자동 회전 중단 및 재스캔");
						yield break;
					}
					if (canVerifyPerStep && !VerifySlotInstanceId(inventory, rotStep.TargetCoord, expectedId)) {
						AbortAutoSwap("석판 InstanceID 불일치 감지됨 → 자동 회전 중단 및 재스캔");
						yield break;
					}

					inventory.DoClickAction(pos);
				}
			}

			yield return null;

			isAutoSwapRunning = false;
			lastObservedStateHash = 0;
			string pctStr = targetResult.EstimatedDpsGainPercent >= 0.0
				? $"+{targetResult.EstimatedDpsGainPercent:F1}%"
				: $"{targetResult.EstimatedDpsGainPercent:F1}%";
			SetStatusBanner(
				$"재배치 완료: {targetResult.InitialEstimatedDps:N0} DPS -> "
				+ $"{targetResult.BestEstimatedDps:N0} DPS ({pctStr}, {plan.SwapSteps.Count}회 스왑)"
			);
		}

		private void AbortAutoSwap(string reason) {
			isOverlayEnabled = true;
			isAutoSwapRunning = false;
			lastObservedStateHash = 0;
			SetStatusBanner(reason);
		}

		private static bool CanContinueAutoSwap(
			UI_CharacterStatusPanel? statusPanel,
			PlayerAvatar? avatar,
			GridInventory? inventory
		) {
			if (statusPanel == null || !statusPanel.IsOpened || avatar == null || inventory == null) {
				return false;
			}
			if (avatar.IsInBattle || avatar.IsDead || !inventory.IsPickable) {
				return false;
			}
			return statusPanel.InventoryMode == UI_CharacterStatusPanel.EInventoryMode.None;
		}

		private static bool IsCursorHoldingAnyItem() {
			if (UIManager.Instance == null) {
				return false;
			}
			UI_NewItemPicker picker = UIManager.Instance.GetElement<UI_NewItemPicker>();
			if (picker != null && picker.CurrentAny) {
				return true;
			}
			UI_NewItemPicker_Controller controllerPicker = UIManager.Instance.GetElement<UI_NewItemPicker_Controller>();
			if (controllerPicker != null && controllerPicker.CurrentAny) {
				return true;
			}
			return false;
		}

		private static bool VerifySlotInstanceId(
			GridInventory inventory,
			SlotCoord coord,
			int expectedInstanceId
		) {
			NewItemOwnInstance? actualItem = inventory.FindItem(coord.X, coord.Y);
			int actualInstanceId = actualItem != null ? actualItem.InstanceID : -1;
			return actualInstanceId == expectedInstanceId;
		}

		private static bool VerifyAllSlotInstanceIds(
			GridInventory inventory,
			InventorySnapshot snapshot,
			int[] expectedSlotInstanceIds
		) {
			for (int s = 0; s < snapshot.UnlockedSlots; s++) {
				if (!VerifySlotInstanceId(inventory, snapshot.Slots[s], expectedSlotInstanceIds[s])) {
					return false;
				}
			}
			return true;
		}

		private void DrawSideControlPanel(
			UI_CharacterStatusPanel statusPanel,
			PlayerAvatar avatar,
			GridInventory inventory
		) {
			bool showVersionStrip = compatibilityReport != null
				&& compatibilityReport.ShouldWarnUser
				&& (!isVersionWarningDismissed || compatibilityReport.ShouldLockAutoSwap);
			bool showUpdateStrip = ShouldDisplayUpdateStrip();

			float targetWidth = isPanelCollapsed ? CollapsedPanelWidth : SidePanelWidth;
			float targetHeight = isPanelCollapsed
				? CollapsedPanelHeight
				: CalculateExpandedPanelHeight(showVersionStrip, showUpdateStrip);

			Rect panelRect = ComputeSafeSidePanelRect(statusPanel, inventory, targetWidth, targetHeight);
			lastRenderedSidePanelRect = panelRect;

			DrawColoredBox(panelRect, new Color(0.06f, 0.08f, 0.13f, 0.94f));
			Color borderColor = compatibilityReport != null && compatibilityReport.ShouldLockAutoSwap
				? new Color(0.95f, 0.28f, 0.28f, 0.92f)
				: (showUpdateStrip
					? new Color(0.25f, 0.88f, 0.55f, 0.92f)
					: new Color(0.32f, 0.62f, 0.92f, 0.88f));
			DrawRectBorder(panelRect, borderColor, 1.5f);

			if (isPanelCollapsed) {
				DrawCollapsedPanelHeader(panelRect, showVersionStrip || showUpdateStrip);
				return;
			}

			float padX = panelRect.x + 8f;
			float innerWidth = panelRect.width - 16f;
			float curY = panelRect.y + 6f;

			Rect headerRect = new Rect(padX, curY, innerWidth - 30f, 22f);
			GUI.Label(
				headerRect,
				$"Sephiria Optimizer v{installedModVersion}",
				headerLabelStyle!
			);

			if (GUI.Button(new Rect(panelRect.xMax - 31f, curY, 23f, 22f), "—", compactButtonStyle!)) {
				isPanelCollapsed = true;
				SaveLayoutPreferences();
				return;
			}
			curY += 26f;

			if (showVersionStrip && compatibilityReport != null) {
				curY = DrawVersionCompatibilityStrip(padX, curY, innerWidth, compatibilityReport);
			}

			if (showUpdateStrip) {
				curY = DrawReleaseUpdateStrip(padX, curY, innerWidth);
			}

			Rect dpsBox = new Rect(padX, curY, innerWidth, 42f);
			DrawColoredBox(dpsBox, new Color(0.11f, 0.15f, 0.22f, 0.92f));

			GUI.Label(
				new Rect(padX + 6f, curY + 3f, innerWidth - 12f, 19f),
				cachedDpsTooltip.PanelMainDpsLine,
				headerLabelStyle!
			);
			GUI.Label(
				new Rect(padX + 6f, curY + 21f, innerWidth - 12f, 18f),
				cachedDpsTooltip.PanelSubDpsLine,
				subHeaderStyle!
			);
			curY += 47f;

			bool isSwapLocked = compatibilityReport != null && compatibilityReport.ShouldLockAutoSwap;
			string runBtnText = isSwapLocked ? "자동 정렬 잠김 (API 변경)" : "자동 정렬 실행";
			if (GUI.Button(new Rect(padX, curY, innerWidth, 27f), runBtnText, buttonStyle!)) {
				TriggerAutoSwapOrFallback(statusPanel, avatar, inventory);
			}
			curY += 31f;

			float halfW = (innerWidth - 4f) * 0.5f;
			string guideLabel = isOverlayEnabled ? "가이드: 켜짐" : "가이드: 꺼짐";
			GUIStyle guideStyle = isOverlayEnabled ? selectedButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(padX, curY, halfW, 25f), guideLabel, guideStyle)) {
				isOverlayEnabled = !isOverlayEnabled;
				SetStatusBanner(
					isOverlayEnabled
						? "오버레이 가이드가 켜졌습니다."
						: "오버레이 가이드가 숨겨졌습니다."
				);
			}

			string configLabel = isConfigPanelOpen ? "설정 닫기" : "빌드 설정";
			GUIStyle configStyle = isConfigPanelOpen ? selectedButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(padX + halfW + 4f, curY, halfW, 25f), configLabel, configStyle)) {
				isConfigPanelOpen = !isConfigPanelOpen;
				SetStatusBanner(
					isConfigPanelOpen
						? "빌드 맞춤 설정 창을 열었습니다."
						: "설정 창을 닫았습니다."
				);
			}
			curY += 29f;

			int pinCount = pinnedSlotIndices.Count;
			string pinLabel = isPinModeEnabled
				? $"슬롯 고정(Pin): ON ({pinCount}개 고정됨)"
				: $"슬롯 고정(Pin): OFF ({pinCount}개 고정됨)";
			GUIStyle currentPinStyle = isPinModeEnabled ? activePinButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(padX, curY, innerWidth, 25f), pinLabel, currentPinStyle)) {
				isPinModeEnabled = !isPinModeEnabled;
				SetStatusBanner(
					isPinModeEnabled
						? "Pin ON: 슬롯 우클릭으로 위치 고정"
						: "Pin OFF: 아티팩트 우클릭으로 우선순위 변경"
				);
			}
			curY += 29f;

			if (GUI.Button(new Rect(padX, curY, halfW, 23f), "설정/고정 초기화", compactButtonStyle!)) {
				ResetUserCustomizations();
			}

			string checkBtnLabel = releaseUpdater.CurrentState == UpdateCheckState.Checking
				? "확인 중..."
				: "업데이트 확인";
			if (GUI.Button(new Rect(padX + halfW + 4f, curY, halfW, 23f), checkBtnLabel, compactButtonStyle!)) {
				TriggerManualUpdateCheck();
			}
			curY += 27f;

			GUI.Label(
				new Rect(padX + 2f, curY, innerWidth - 4f, 34f),
				statusBannerText,
				statusWrapStyle!
			);
		}

		private void DrawCollapsedPanelHeader(Rect panelRect, bool hasAttentionAlert) {
			float padX = panelRect.x + 8f;
			Rect titleRect = new Rect(padX, panelRect.y + 4f, panelRect.width - 42f, 24f);
			string alertMark = hasAttentionAlert ? " ●" : string.Empty;
			GUI.Label(titleRect, $"Optimizer v{installedModVersion}{alertMark}", headerLabelStyle!);

			if (GUI.Button(new Rect(panelRect.xMax - 30f, panelRect.y + 5f, 23f, 22f), "+", compactButtonStyle!)) {
				isPanelCollapsed = false;
				SaveLayoutPreferences();
			}
		}

		private float DrawVersionCompatibilityStrip(
			float x,
			float y,
			float width,
			GameCompatibilityReport report
		) {
			float stripHeight = 42f;
			Rect stripRect = new Rect(x, y, width, stripHeight);
			Color fill = report.ShouldLockAutoSwap
				? new Color(0.45f, 0.10f, 0.12f, 0.94f)
				: new Color(0.42f, 0.28f, 0.06f, 0.94f);
			Color border = report.ShouldLockAutoSwap
				? new Color(0.95f, 0.30f, 0.30f, 0.95f)
				: new Color(0.95f, 0.72f, 0.20f, 0.95f);

			DrawColoredBox(stripRect, fill);
			DrawRectBorder(stripRect, border, 1f);

			float textWidth = report.ShouldLockAutoSwap ? width - 10f : width - 30f;
			GUI.Label(
				new Rect(x + 6f, y + 3f, textWidth, stripHeight - 6f),
				report.BannerMessage,
				alertLabelStyle!
			);

			if (!report.ShouldLockAutoSwap
				&& GUI.Button(new Rect(x + width - 24f, y + 4f, 20f, 18f), "✕", compactButtonStyle!)) {
				isVersionWarningDismissed = true;
			}
			return y + stripHeight + 5f;
		}

		private float DrawReleaseUpdateStrip(float x, float y, float width) {
			UpdateCheckState state = releaseUpdater.CurrentState;
			GitHubReleaseInfo? release = releaseUpdater.LatestRelease;

			if (state == UpdateCheckState.Downloading) {
				float dlHeight = 34f;
				Rect dlRect = new Rect(x, y, width, dlHeight);
				DrawColoredBox(dlRect, new Color(0.10f, 0.26f, 0.38f, 0.95f));
				DrawRectBorder(dlRect, new Color(0.35f, 0.78f, 1f, 0.9f), 1f);
				int pct = Mathf.Clamp(Mathf.RoundToInt(releaseUpdater.DownloadProgress * 100f), 0, 100);
				GUI.Label(
					new Rect(x + 6f, y + 7f, width - 12f, 20f),
					$"업데이트 다운로드·설치 중... ({pct}%)",
					alertLabelStyle!
				);
				return y + dlHeight + 5f;
			}

			float stripHeight = 52f;
			Rect boxRect = new Rect(x, y, width, stripHeight);
			Color bgColor = state == UpdateCheckState.Failed
				? new Color(0.42f, 0.14f, 0.14f, 0.94f)
				: new Color(0.08f, 0.30f, 0.20f, 0.95f);
			Color lineColor = state == UpdateCheckState.Failed
				? new Color(0.92f, 0.38f, 0.38f, 0.92f)
				: new Color(0.28f, 0.92f, 0.55f, 0.92f);

			DrawColoredBox(boxRect, bgColor);
			DrawRectBorder(boxRect, lineColor, 1f);

			if (state == UpdateCheckState.UpdateAvailable && release != null) {
				GUI.Label(
					new Rect(x + 6f, y + 3f, width - 12f, 20f),
					$"새 버전 v{release.NormalizedVersion} 출시 (현재 v{installedModVersion})",
					alertLabelStyle!
				);
				float primaryW = width - 74f;
				if (release.HasDownloadableAsset) {
					if (GUI.Button(
						new Rect(x + 5f, y + 25f, primaryW, 22f),
						"원클릭 업데이트",
						accentUpdateButtonStyle!
					)) {
						StartCoroutine(releaseUpdater.DownloadAndApplyUpdateCoroutine(
							modDirectoryPath,
							OnUpdaterStateUpdated
						));
					}
				} else if (GUI.Button(
					new Rect(x + 5f, y + 25f, primaryW, 22f),
					"GitHub 다운로드 열기",
					accentUpdateButtonStyle!
				)) {
					Application.OpenURL(release.HtmlUrl);
				}

				if (GUI.Button(new Rect(x + primaryW + 9f, y + 25f, 60f, 22f), "노트", compactButtonStyle!)) {
					Application.OpenURL(release.HtmlUrl);
				}
			} else if (state == UpdateCheckState.AppliedPendingRestart) {
				GUI.Label(
					new Rect(x + 6f, y + 3f, width - 12f, 20f),
					releaseUpdater.StatusMessage,
					alertLabelStyle!
				);
				float restartW = width - 66f;
				if (GUI.Button(
					new Rect(x + 5f, y + 25f, restartW, 22f),
					"지금 게임 재시작",
					accentUpdateButtonStyle!
				)) {
					RestartGameViaSteam();
				}
				if (GUI.Button(new Rect(x + restartW + 9f, y + 25f, 52f, 22f), "나중에", compactButtonStyle!)) {
					isRestartPromptDismissed = true;
				}
			} else if (state == UpdateCheckState.Failed) {
				GUI.Label(
					new Rect(x + 6f, y + 3f, width - 12f, 20f),
					releaseUpdater.StatusMessage,
					alertLabelStyle!
				);
				float halfBtn = (width - 14f) * 0.5f;
				if (GUI.Button(new Rect(x + 5f, y + 25f, halfBtn, 22f), "다시 확인", compactButtonStyle!)) {
					TriggerManualUpdateCheck();
				}
				if (GUI.Button(
					new Rect(x + 9f + halfBtn, y + 25f, halfBtn, 22f),
					"릴리즈 열기",
					compactButtonStyle!
				)) {
					string targetUrl = release != null
						? release.HtmlUrl
						: ModVersionConstants.GitHubReleasesPageUrl;
					Application.OpenURL(targetUrl);
				}
			}

			return y + stripHeight + 5f;
		}

		private bool ShouldDisplayUpdateStrip() {
			return releaseUpdater.CurrentState switch {
				UpdateCheckState.UpdateAvailable => true,
				UpdateCheckState.Downloading => true,
				UpdateCheckState.AppliedPendingRestart => !isRestartPromptDismissed,
				UpdateCheckState.Failed => true,
				_ => false
			};
		}

		private float CalculateExpandedPanelHeight(bool showVersionStrip, bool showUpdateStrip) {
			float totalHeight = 234f;
			if (showVersionStrip) {
				totalHeight += 47f;
			}
			if (showUpdateStrip) {
				totalHeight += releaseUpdater.CurrentState == UpdateCheckState.Downloading ? 39f : 57f;
			}
			return totalHeight;
		}

		private void TriggerManualUpdateCheck() {
			if (releaseUpdater.CurrentState == UpdateCheckState.Checking
				|| releaseUpdater.CurrentState == UpdateCheckState.Downloading) {
				return;
			}
			SetStatusBanner("GitHub Releases 최신 버전을 확인하고 있습니다...");
			StartCoroutine(releaseUpdater.CheckForUpdatesCoroutine(
				installedModVersion,
				() => {
					OnUpdaterStateUpdated();
					if (releaseUpdater.CurrentState == UpdateCheckState.UpToDate) {
						SetStatusBanner(releaseUpdater.StatusMessage);
					}
				}
			));
		}

		private static void RestartGameViaSteam() {
			Application.OpenURL(ModVersionConstants.SteamRunGameUri);
			Application.Quit();
		}

		private void ResetUserCustomizations() {
			pinnedSlotIndices.Clear();
			customEntityWeights.Clear();
			userSettings.InstancePriorities.Clear();
			userSettings.PreferredChalcedonyCategory = string.Empty;
			userSettings.TargetComboCategories.Clear();
			userSettings.PreferredNeedleTargetInstanceId = -1;
			lastObservedStateHash = 0;
			SetStatusBanner("슬롯 고정 및 우선순위·콤보 설정을 초기화했습니다.");
		}

		private Rect ComputeSafeSidePanelRect(
			UI_CharacterStatusPanel statusPanel,
			GridInventory inventory,
			float panelWidth,
			float panelHeight
		) {
			CollectProtectedGameUiRects(statusPanel, inventory);

			Rect centralColumnBounds = BuildCentralColumnBounds(statusPanel, inventory);
			float topAlignY = centralColumnBounds.width > 1f
				? Mathf.Max(ScreenEdgeMargin, centralColumnBounds.yMin)
				: ScreenEdgeMargin;

			float rightAnchorX = Screen.width - panelWidth - ScreenEdgeMargin;
			float rightOfCentralX = centralColumnBounds.width > 1f
				? Mathf.Max(centralColumnBounds.xMax + 12f, rightAnchorX)
				: rightAnchorX;

			Rect candTopRight = ClampRectToScreen(
				new Rect(rightAnchorX, ScreenEdgeMargin, panelWidth, panelHeight)
			);
			Rect candImmediateRight = ClampRectToScreen(
				new Rect(rightOfCentralX, topAlignY, panelWidth, panelHeight)
			);
			Rect candBottomRight = ClampRectToScreen(
				new Rect(
					rightAnchorX,
					Screen.height - panelHeight - ScreenEdgeMargin,
					panelWidth,
					panelHeight
				)
			);

			Rect[] rightCandidates = new[] {
				candTopRight,
				candImmediateRight,
				candBottomRight
			};

			Rect bestRect = rightCandidates[0];
			float bestPenalty = float.MaxValue;
			for (int i = 0; i < rightCandidates.Length; i++) {
				Rect candidate = rightCandidates[i];
				float penalty = EvaluateCandidateOverlapPenalty(candidate) + (i * 2f);
				if (penalty < bestPenalty) {
					bestPenalty = penalty;
					bestRect = candidate;
				}
			}
			return bestRect;
		}

		private void CollectProtectedGameUiRects(
			UI_CharacterStatusPanel statusPanel,
			GridInventory inventory
		) {
			potionObstacleRects.Clear();
			inventoryObstacleRects.Clear();
			sidePanelObstacleRects.Clear();

			TryAddActiveRect(statusPanel.potionZone, potionObstacleRects);
			TryAddActiveRect(statusPanel.potionStorageCover, potionObstacleRects);
			if (statusPanel.potionIcons != null) {
				for (int i = 0; i < statusPanel.potionIcons.Length; i++) {
					UI_NewInventoryIcon pIcon = statusPanel.potionIcons[i];
					if (pIcon != null) {
						TryAddActiveRect(pIcon.rectTransform, potionObstacleRects);
					}
				}
			}

			TryAddActiveRect(statusPanel.inventoryZone, inventoryObstacleRects);
			TryAddActiveRect(statusPanel.inventoryScrollParent, inventoryObstacleRects);
			TryAddActiveRect(statusPanel.subBagZone, inventoryObstacleRects);
			TryAddActiveRect(statusPanel.subBagCover, inventoryObstacleRects);
			if (TryGetInventoryGridBounds(statusPanel, inventory, out Rect gridRect)) {
				inventoryObstacleRects.Add(gridRect);
			}

			TryAddActiveRect(statusPanel.setEffectPanel, sidePanelObstacleRects);
			TryAddActiveRect(statusPanel.skillPanel, sidePanelObstacleRects);
		}

		private Rect BuildCentralColumnBounds(UI_CharacterStatusPanel statusPanel, GridInventory inventory) {
			bool hasAny = false;
			float minX = float.MaxValue;
			float minY = float.MaxValue;
			float maxX = float.MinValue;
			float maxY = float.MinValue;

			for (int i = 0; i < potionObstacleRects.Count; i++) {
				EncapsulateRect(potionObstacleRects[i], ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
			}
			for (int i = 0; i < inventoryObstacleRects.Count; i++) {
				EncapsulateRect(inventoryObstacleRects[i], ref hasAny, ref minX, ref minY, ref maxX, ref maxY);
			}

			if (!hasAny && TryGetInventoryGridBounds(statusPanel, inventory, out Rect fallbackGrid)) {
				return fallbackGrid;
			}
			if (!hasAny) {
				float centerW = 320f;
				float centerH = 320f;
				return new Rect(
					(Screen.width - centerW) * 0.5f,
					(Screen.height - centerH) * 0.5f,
					centerW,
					centerH
				);
			}
			return Rect.MinMaxRect(minX, minY, maxX, maxY);
		}

		private static void EncapsulateRect(
			Rect rect,
			ref bool hasAny,
			ref float minX,
			ref float minY,
			ref float maxX,
			ref float maxY
		) {
			if (rect.width <= 2f || rect.height <= 2f) {
				return;
			}
			hasAny = true;
			if (rect.xMin < minX) {
				minX = rect.xMin;
			}
			if (rect.yMin < minY) {
				minY = rect.yMin;
			}
			if (rect.xMax > maxX) {
				maxX = rect.xMax;
			}
			if (rect.yMax > maxY) {
				maxY = rect.yMax;
			}
		}

		private float EvaluateCandidateOverlapPenalty(Rect candidate) {
			float penalty = 0f;

			for (int i = 0; i < potionObstacleRects.Count; i++) {
				penalty += ComputeOverlapArea(candidate, ExpandRect(potionObstacleRects[i], 6f)) * 1000f;
			}
			for (int i = 0; i < inventoryObstacleRects.Count; i++) {
				penalty += ComputeOverlapArea(candidate, ExpandRect(inventoryObstacleRects[i], 6f)) * 500f;
			}
			for (int i = 0; i < sidePanelObstacleRects.Count; i++) {
				penalty += ComputeOverlapArea(candidate, ExpandRect(sidePanelObstacleRects[i], 4f)) * 75f;
			}
			return penalty;
		}

		private void TryAddActiveRect(RectTransform? rectTransform, List<Rect> targetList) {
			if (rectTransform == null || !rectTransform.gameObject.activeInHierarchy) {
				return;
			}
			Rect screenRect = GetScreenGuiRect(rectTransform);
			if (screenRect.width > 4f && screenRect.height > 4f) {
				targetList.Add(screenRect);
			}
		}

		private static Rect ExpandRect(Rect rect, float padding) {
			return new Rect(
				rect.x - padding,
				rect.y - padding,
				rect.width + (padding * 2f),
				rect.height + (padding * 2f)
			);
		}

		private static float ComputeOverlapArea(Rect first, Rect second) {
			float xOverlap = Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin);
			if (xOverlap <= 0f) {
				return 0f;
			}
			float yOverlap = Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin);
			if (yOverlap <= 0f) {
				return 0f;
			}
			return xOverlap * yOverlap;
		}

		private static Rect ClampRectToScreen(Rect rect) {
			float maxX = Mathf.Max(ScreenEdgeMargin, Screen.width - rect.width - ScreenEdgeMargin);
			float maxY = Mathf.Max(ScreenEdgeMargin, Screen.height - rect.height - ScreenEdgeMargin);
			float clampedX = Mathf.Clamp(rect.x, ScreenEdgeMargin, maxX);
			float clampedY = Mathf.Clamp(rect.y, ScreenEdgeMargin, maxY);
			return new Rect(clampedX, clampedY, rect.width, rect.height);
		}

		private void LoadLayoutPreferences() {
			isPanelCollapsed = PlayerPrefs.GetInt(PrefKeyCollapsed, 0) == 1;
		}

		private void SaveLayoutPreferences() {
			PlayerPrefs.SetInt(PrefKeyCollapsed, isPanelCollapsed ? 1 : 0);
			PlayerPrefs.Save();
		}

		private void DrawConfigPanel(
			UI_CharacterStatusPanel statusPanel,
			GridInventory inventory,
			InventorySnapshot snapshot
		) {
			float width = 530f;
			float height = Mathf.Min(580f, Screen.height - 40f);
			Rect winRect = ComputeConfigWindowRect(statusPanel, inventory, width, height);

			DrawColoredBox(winRect, new Color(0.07f, 0.09f, 0.14f, 0.97f));
			DrawRectBorder(winRect, new Color(0.35f, 0.65f, 0.95f, 0.9f), 2f);

			float panelX = winRect.x;
			float panelY = winRect.y;

			GUI.Label(
				new Rect(panelX + 12f, panelY + 8f, width - 52f, 22f),
				"빌드 맞춤 최적화 설정 (캘세더니 / 하얀 종이 / 금빛침 / 우선순위)",
				headerLabelStyle!
			);
			if (GUI.Button(new Rect(winRect.xMax - 34f, panelY + 7f, 24f, 22f), "✕", compactButtonStyle!)) {
				isConfigPanelOpen = false;
				return;
			}

			Rect viewRect = new Rect(panelX + 8f, panelY + 34f, width - 16f, height - 42f);
			int artifactCount = 0;
			for (int i = 0; i < snapshot.Items.Length; i++) {
				if (snapshot.Items[i].Kind == SlotItemKind.Artifact) {
					artifactCount++;
				}
			}

			float contentHeight = 340f + (artifactCount * 32f);
			Rect contentRect = new Rect(0f, 0f, width - 36f, Mathf.Max(viewRect.height, contentHeight));
			configScrollPos = GUI.BeginScrollView(viewRect, configScrollPos, contentRect);

			float curY = 4f;
			GUI.Label(
				new Rect(4f, curY, contentRect.width - 8f, 20f),
				"1. 캘세더니 열쇠 목표 콤보 선택 (배치 행에 따라 견고/잉걸불/빙하/마법공학 중 택1)",
				subHeaderStyle!
			);
			curY += 22f;

			bool isAutoChalcedony = string.IsNullOrEmpty(userSettings.PreferredChalcedonyCategory);
			GUIStyle autoChalStyle = isAutoChalcedony ? selectedButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(4f, curY, 108f, 24f), "자동 (최적 행)", autoChalStyle)) {
				userSettings.PreferredChalcedonyCategory = string.Empty;
				lastObservedStateHash = 0;
			}

			float chalX = 116f;
			for (int i = 0; i < ChalcedonyComboTags.Length; i++) {
				(string chalId, string chalName) = ChalcedonyComboTags[i];
				bool isSelected = string.Equals(
					userSettings.PreferredChalcedonyCategory,
					chalId,
					StringComparison.OrdinalIgnoreCase
				);
				GUIStyle style = isSelected ? selectedButtonStyle! : buttonStyle!;
				string prefix = isSelected ? "★" : "#";
				if (GUI.Button(new Rect(chalX, curY, 88f, 24f), $"{prefix}{chalName}", style)) {
					userSettings.ToggleChalcedonyCategory(chalId);
					lastObservedStateHash = 0;
				}
				chalX += 92f;
			}
			curY += 32f;

			GUI.Label(
				new Rect(4f, curY, contentRect.width - 8f, 20f),
				"2. 하얀 종이 & 세트 목표 콤보 선택 (양쪽 동일 태그 아티팩트 사이 배치 유도 — 20종)",
				subHeaderStyle!
			);
			curY += 22f;

			bool hasManualCombos = userSettings.TargetComboCategories.Count > 0;
			GUIStyle autoComboStyle = !hasManualCombos ? selectedButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(4f, curY, 165f, 24f), "자동 (활성 콤보 기반)", autoComboStyle)) {
				userSettings.TargetComboCategories.Clear();
				lastObservedStateHash = 0;
			}
			curY += 28f;

			int col = 0;
			float tagBtnW = 114f;
			float tagBtnH = 24f;
			for (int i = 0; i < WhitePaperComboTags.Length; i++) {
				(string tagId, string korName) = WhitePaperComboTags[i];
				bool isSelected = userSettings.TargetComboCategories.Contains(tagId);
				GUIStyle style = isSelected ? selectedButtonStyle! : buttonStyle!;
				string prefix = isSelected ? "★" : "#";
				Rect btnRect = new Rect(4f + (col * (tagBtnW + 4f)), curY, tagBtnW, tagBtnH);
				if (GUI.Button(btnRect, $"{prefix}{korName}", style)) {
					userSettings.ToggleTargetCombo(tagId);
					lastObservedStateHash = 0;
				}
				col++;
				if (col >= 4) {
					col = 0;
					curY += tagBtnH + 4f;
				}
			}
			if (col > 0) {
				curY += tagBtnH + 4f;
			}

			curY += 6f;
			GUI.Label(
				new Rect(4f, curY, contentRect.width - 8f, 20f),
				"3. 북향의 금빛침 강화 대상 선택 — 지정한 공격형 아티팩트 바로 아래 칸에 배치",
				subHeaderStyle!
			);
			curY += 22f;

			bool isAutoNeedle = userSettings.PreferredNeedleTargetInstanceId < 0;
			if (GUI.Button(
				new Rect(4f, curY, 175f, 24f),
				"자동 (최고 가치 공격 아티팩트)",
				isAutoNeedle ? selectedButtonStyle! : buttonStyle!
			)) {
				userSettings.PreferredNeedleTargetInstanceId = -1;
				lastObservedStateHash = 0;
			}

			float needleX = 184f;
			for (int i = 0; i < snapshot.Items.Length; i++) {
				SlotItemSnapshot item = snapshot.Items[i];
				if (item.Kind != SlotItemKind.Artifact || !item.IsAttackableCharm || item.HasDependencyTarget) {
					continue;
				}
				if (needleX + 145f > contentRect.width) {
					needleX = 4f;
					curY += 26f;
				}
				bool isTarget = userSettings.PreferredNeedleTargetInstanceId == item.InstanceId;
				string rareTag = item.RarityTier <= 1 ? "(고급이하★2배)" : "";
				string label = $"{TruncateName(item.DisplayName, 8)}{rareTag}";
				if (GUI.Button(
					new Rect(needleX, curY, 142f, 24f),
					label,
					isTarget ? selectedButtonStyle! : buttonStyle!
				)) {
					userSettings.PreferredNeedleTargetInstanceId = isTarget ? -1 : item.InstanceId;
					lastObservedStateHash = 0;
				}
				needleX += 146f;
			}
			curY += 32f;

			GUI.Label(
				new Rect(4f, curY, contentRect.width - 8f, 20f),
				"4. 아티팩트 강화 우선순위 [최상 / 높음 / 보통 / 낮음 / 최하] (우클릭으로도 순환 가능)",
				subHeaderStyle!
			);
			curY += 24f;

			for (int i = 0; i < snapshot.Items.Length; i++) {
				SlotItemSnapshot item = snapshot.Items[i];
				if (item.Kind != SlotItemKind.Artifact) {
					continue;
				}

				ArtifactPriorityTier tier = item.PriorityTier;
				string itemLabel = $"[{GetPriorityShortLabel(tier)}] {TruncateName(item.DisplayName, 14)} "
					+ $"(Lv.{item.BaseLevel}/{item.MaxLevel})";
				GUI.Label(new Rect(4f, curY + 2f, 210f, 24f), itemLabel, headerLabelStyle!);

				DrawPriorityTierButtons(item.InstanceId, tier, 218f, curY);
				curY += 28f;
			}

			GUI.EndScrollView();
		}

		private Rect ComputeConfigWindowRect(
			UI_CharacterStatusPanel statusPanel,
			GridInventory inventory,
			float width,
			float height
		) {
			Rect central = BuildCentralColumnBounds(statusPanel, inventory);
			if (ScreenEdgeMargin + width <= central.xMin - 6f) {
				return ClampRectToScreen(new Rect(ScreenEdgeMargin, ScreenEdgeMargin, width, height));
			}
			return ClampRectToScreen(
				new Rect(lastRenderedSidePanelRect.xMin - width - 10f, ScreenEdgeMargin, width, height)
			);
		}

		private void DrawPriorityTierButtons(
			int instanceId,
			ArtifactPriorityTier currentTier,
			float startX,
			float y
		) {
			(ArtifactPriorityTier Tier, string Label, float Width)[] tiers = new[] {
				(ArtifactPriorityTier.Core, "최상", 48f),
				(ArtifactPriorityTier.High, "높음", 48f),
				(ArtifactPriorityTier.Normal, "보통", 48f),
				(ArtifactPriorityTier.Low, "낮음", 48f),
				(ArtifactPriorityTier.Sacrifice, "최하", 48f)
			};

			float curX = startX;
			for (int i = 0; i < tiers.Length; i++) {
				(ArtifactPriorityTier tier, string label, float btnWidth) = tiers[i];
				GUIStyle style = currentTier == tier ? selectedButtonStyle! : buttonStyle!;
				if (GUI.Button(new Rect(curX, y, btnWidth, 24f), label, style)) {
					userSettings.InstancePriorities[instanceId] = tier;
					lastObservedStateHash = 0;
				}
				curX += btnWidth + 4f;
			}
		}

		private void DrawSlotOverlays(
			UI_CharacterStatusPanel statusPanel,
			GridInventory inventory,
			InventorySnapshot snapshot,
			OptimizationResult result
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			if (unlockedSlots <= 0) {
				return;
			}

			Rect[] slotScreenRects = slotScreenRectsBuffer;
			Span<byte> hasValidRect = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<int> liveSlotToItem = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> liveSlotRotations = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<SlotGuideOverlayState> guideStates =
				stackalloc SlotGuideOverlayState[InventorySnapshot.MaxSupportedSlots];

			Vector2 mouseGuiPos = Event.current != null ? Event.current.mousePosition : Vector2.zero;
			bool mouseOverSidePanel = lastRenderedSidePanelRect.width > 1f
				&& lastRenderedSidePanelRect.Contains(mouseGuiPos);
			int hoveredSlot = -1;

			for (int s = 0; s < unlockedSlots; s++) {
				hasValidRect[s] = 0;
				SlotCoord coord = snapshot.Slots[s];
				ResolveLiveSlotItem(
					inventory,
					snapshot,
					coord,
					out int resolvedItemIdx,
					out int resolvedRotation
				);
				liveSlotToItem[s] = resolvedItemIdx;
				liveSlotRotations[s] = resolvedRotation;

				UI_NewInventoryIcon icon = statusPanel.GetItemIcon(new ItemPosition(coord.X, coord.Y));
				if (icon == null || icon.rectTransform == null) {
					continue;
				}
				Rect rect = GetScreenGuiRect(icon.rectTransform);
				slotScreenRects[s] = rect;
				bool validRect = rect.width > 4f && rect.height > 4f;
				hasValidRect[s] = validRect ? (byte)1 : (byte)0;
				if (!mouseOverSidePanel && validRect && rect.Contains(mouseGuiPos)) {
					hoveredSlot = s;
				}
			}

			CycleDecomposition.EvaluateSlotGuides(
				snapshot,
				liveSlotToItem.Slice(0, unlockedSlots),
				liveSlotRotations.Slice(0, unlockedSlots),
				result.BestSlotToItemIndex,
				result.BestItemToSlotIndex,
				result.BestItemRotations,
				guideStates.Slice(0, unlockedSlots)
			);

			if (selectedSlotIndex >= 0
				&& (selectedSlotIndex >= unlockedSlots || !guideStates[selectedSlotIndex].HasCurrentItem)) {
				selectedSlotIndex = -1;
			}

			if (Event.current != null && Event.current.type == EventType.MouseDown && hoveredSlot >= 0) {
				if (Event.current.button == 1) {
					if (isPinModeEnabled) {
						if (!pinnedSlotIndices.Add(hoveredSlot)) {
							pinnedSlotIndices.Remove(hoveredSlot);
						}
						lastObservedStateHash = 0;
						Event.current.Use();
					} else {
						int clickedItemIdx = liveSlotToItem[hoveredSlot];
						if (clickedItemIdx >= 0 && snapshot.Items[clickedItemIdx].Kind == SlotItemKind.Artifact) {
							SlotItemSnapshot clickedArt = snapshot.Items[clickedItemIdx];
							userSettings.CyclePriority(clickedArt.InstanceId);
							ArtifactPriorityTier nextTier = userSettings.GetPriority(clickedArt.InstanceId);
							lastObservedStateHash = 0;
							SetStatusBanner(
								$"[{clickedArt.DisplayName}] 우선순위 → [{GetPriorityShortLabel(nextTier)}]"
							);
							Event.current.Use();
						}
					}
				} else if (Event.current.button == 0) {
					if (guideStates[hoveredSlot].HasCurrentItem) {
						selectedSlotIndex = selectedSlotIndex == hoveredSlot ? -1 : hoveredSlot;
					} else {
						selectedSlotIndex = -1;
					}
				}
			}

			int activeHighlightSlot = hoveredSlot >= 0 ? hoveredSlot : selectedSlotIndex;

			for (int s = 0; s < unlockedSlots; s++) {
				if (hasValidRect[s] == 0) {
					continue;
				}
				Rect rect = slotScreenRects[s];

				if (pinnedSlotIndices.Contains(s)) {
					DrawRectBorder(rect, new Color(1f, 0.82f, 0.15f, 0.95f), 2f);
					GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, 38f, 16f), "PIN", pinTagStyle!);
				}

				SlotGuideOverlayState guide = guideStates[s];
				if (!guide.HasCurrentItem) {
					continue;
				}

				int currentItemIdx = liveSlotToItem[s];
				SlotItemSnapshot currentItem = snapshot.Items[currentItemIdx];

				if (guide.ShowPriorityBadge) {
					DrawPriorityBadge(rect, currentItem.PriorityTier);
				}

				if (guide.ShowMoveGuide) {
					SlotCoord targetCoord = snapshot.Slots[guide.TargetDestinationSlot];
					string moveText = $"→[{targetCoord.X},{targetCoord.Y}]";
					Rect moveBadgeRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 18f);
					DrawColoredBox(moveBadgeRect, new Color(0.05f, 0.18f, 0.36f, 0.88f));
					GUI.Label(moveBadgeRect, moveText, coordBadgeStyle!);
				} else if (guide.ShowRotateGuide) {
					string rotText = $"↻ +{guide.RotationDeltaDegrees}°";
					Rect rotBadgeRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 18f);
					DrawColoredBox(rotBadgeRect, new Color(0.35f, 0.15f, 0.45f, 0.88f));
					GUI.Label(rotBadgeRect, rotText, coordBadgeStyle!);
				}

				if (guide.ShowExpectedStatusBadge && s < result.BestSlotDetails.Length) {
					SlotEvaluationDetail detail = result.BestSlotDetails[s];
					if (detail.ItemIndex >= 0 && detail.ItemIndex < snapshot.Items.Length) {
						SlotItemSnapshot placedItem = snapshot.Items[detail.ItemIndex];
						if (placedItem.Kind == SlotItemKind.Artifact) {
							DrawExpectedLevelBadge(rect, detail, placedItem);
						} else if (
							placedItem.Kind == SlotItemKind.StoneTablet
							&& snapshot.TalentRules.TabletEngravingCount > 0
							&& detail.IsEngravingReady
						) {
							DrawTabletEngravingBadge(rect);
						}
					}
				}
			}

			if (activeHighlightSlot >= 0
				&& activeHighlightSlot < unlockedSlots
				&& hasValidRect[activeHighlightSlot] != 0
				&& guideStates[activeHighlightSlot].HasCurrentItem
				&& guideStates[activeHighlightSlot].ShowMoveGuide) {
				int dstSlot = guideStates[activeHighlightSlot].TargetDestinationSlot;
				if (dstSlot >= 0
					&& dstSlot < unlockedSlots
					&& dstSlot != activeHighlightSlot
					&& hasValidRect[dstSlot] != 0) {
					Rect srcRect = slotScreenRects[activeHighlightSlot];
					Rect dstRect = slotScreenRects[dstSlot];
					DrawRectBorder(srcRect, new Color(0.2f, 0.95f, 1f, 1f), 2.5f);
					DrawRectBorder(dstRect, new Color(0.2f, 1f, 0.4f, 1f), 2.5f);
					DrawConnectionLine(srcRect.center, dstRect.center, new Color(0.2f, 0.95f, 1f, 0.95f), 3f);
				}
			}
		}

		private static void ResolveLiveSlotItem(
			GridInventory inventory,
			InventorySnapshot snapshot,
			SlotCoord coord,
			out int resolvedItemIdx,
			out int resolvedRotation
		) {
			resolvedItemIdx = -1;
			resolvedRotation = 0;

			NewItemOwnInstance? liveItem = inventory.FindItem(coord.X, coord.Y);
			if (liveItem == null || liveItem.Entity == null) {
				return;
			}

			int liveInstanceId = liveItem.InstanceID;
			SlotItemSnapshot[] items = snapshot.Items;
			for (int i = 0; i < items.Length; i++) {
				if (items[i].InstanceId == liveInstanceId) {
					resolvedItemIdx = i;
					break;
				}
			}

			if (resolvedItemIdx < 0) {
				return;
			}

			StoneTablet? liveTablet = liveItem.StoneTablet;
			if (liveTablet == null && inventory.stoneTablets != null) {
				inventory.stoneTablets.TryGetValue(new ItemPosition(coord.X, coord.Y), out liveTablet);
			}
			resolvedRotation = liveTablet != null
				? (liveTablet.rotation & 3)
				: (items[resolvedItemIdx].InitialRotation & 3);
		}

		private void DrawPriorityBadge(Rect slotRect, ArtifactPriorityTier tier) {
			Rect tagRect = new Rect(slotRect.xMax - 34f, slotRect.y + 18f, 32f, 15f);
			Color color = tier switch {
				ArtifactPriorityTier.Core => new Color(0.85f, 0.55f, 0.05f, 0.92f),
				ArtifactPriorityTier.High => new Color(0.15f, 0.55f, 0.75f, 0.92f),
				ArtifactPriorityTier.Low => new Color(0.35f, 0.38f, 0.42f, 0.90f),
				ArtifactPriorityTier.Sacrifice => new Color(0.65f, 0.15f, 0.25f, 0.92f),
				_ => new Color(0.2f, 0.2f, 0.2f, 0.8f)
			};
			DrawColoredBox(tagRect, color);
			GUI.Label(tagRect, GetPriorityShortLabel(tier), badgeStyle!);
		}

		private void DrawTabletEngravingBadge(Rect slotRect) {
			Rect badgeRect = new Rect(
				slotRect.x + 2f,
				slotRect.yMax - 18f,
				slotRect.width - 4f,
				16f
			);
			DrawColoredBox(badgeRect, new Color(0.55f, 0.35f, 0.08f, 0.90f));
			GUI.Label(badgeRect, "각인 최적", badgeStyle!);
		}

		private void DrawExpectedLevelBadge(
			Rect slotRect,
			SlotEvaluationDetail detail,
			SlotItemSnapshot placedItem
		) {
			Rect badgeRect = new Rect(
				slotRect.x + 2f,
				slotRect.yMax - 20f,
				slotRect.width - 4f,
				18f
			);

			string label;
			Color bgColor;

			if (!detail.IsEnabled) {
				if (placedItem.PriorityTier == ArtifactPriorityTier.Sacrifice) {
					label = "최하(콤보유지)";
					bgColor = new Color(0.42f, 0.22f, 0.48f, 0.92f);
				} else if (detail.IsDeactivatedByNegativeLevel) {
					label = $"{detail.DisplayedLevel} (비활성 경고)";
					bgColor = new Color(0.72f, 0.12f, 0.12f, 0.92f);
				} else if (detail.IsDeactivatedByCriteria) {
					label = "제약 미충족";
					bgColor = new Color(0.72f, 0.12f, 0.12f, 0.92f);
				} else {
					label = "비활성 (X)";
					bgColor = new Color(0.72f, 0.12f, 0.12f, 0.92f);
				}
			} else if (detail.MaxLevel > 0 && detail.EffectiveLevel >= detail.MaxLevel) {
				label = $"Lv.{detail.EffectiveLevel} (MAX)";
				bgColor = new Color(0.12f, 0.55f, 0.22f, 0.92f);
			} else {
				label = $"Lv.{detail.EffectiveLevel}/{detail.MaxLevel}";
				bgColor = new Color(0.18f, 0.32f, 0.52f, 0.88f);
			}

			DrawColoredBox(badgeRect, bgColor);
			GUI.Label(badgeRect, label, badgeStyle!);
		}

		private static string BuildTalentSummaryBadge(InventorySnapshot? snapshot) {
			if (snapshot == null) {
				return string.Empty;
			}
			List<string> tags = new List<string>(3);
			TalentRuleSnapshot rules = snapshot.TalentRules;
			if (rules.TabletEngravingCount > 0) {
				tags.Add($"각인:{rules.TabletEngravingCount}");
			}
			if (rules.UniquePairCount > 0) {
				tags.Add($"한쌍:{rules.UniquePairCount}");
			}
			if (rules.UnlimitedComboStat > 0) {
				tags.Add("무한콤보");
			}
			if (tags.Count == 0) {
				return string.Empty;
			}
			return " | " + string.Join(",", tags);
		}

		private static string GetPriorityShortLabel(ArtifactPriorityTier tier) {
			return tier switch {
				ArtifactPriorityTier.Core => "최상",
				ArtifactPriorityTier.High => "높음",
				ArtifactPriorityTier.Normal => "보통",
				ArtifactPriorityTier.Low => "낮음",
				ArtifactPriorityTier.Sacrifice => "최하",
				_ => "보통"
			};
		}

		private static string TruncateName(string name, int maxLen) {
			if (string.IsNullOrEmpty(name) || name.Length <= maxLen) {
				return name;
			}
			return name.Substring(0, maxLen) + "..";
		}

		private bool TryGetInventoryGridBounds(
			UI_CharacterStatusPanel statusPanel,
			GridInventory inventory,
			out Rect bounds
		) {
			bounds = default;
			int unlocked = Math.Clamp((int)inventory.CurrentInventoryStorage, 0, InventorySnapshot.MaxSupportedSlots);
			int width = Math.Max(1, (int)inventory.Width);
			if (unlocked <= 0) {
				return false;
			}

			float minX = float.MaxValue;
			float minY = float.MaxValue;
			float maxX = float.MinValue;
			float maxY = float.MinValue;
			bool foundAny = false;

			for (int s = 0; s < unlocked; s++) {
				sbyte gridX = (sbyte)(s % width);
				sbyte gridY = (sbyte)(s / width);
				UI_NewInventoryIcon icon = statusPanel.GetItemIcon(new ItemPosition(gridX, gridY));
				if (icon == null || icon.rectTransform == null) {
					continue;
				}
				Rect rect = GetScreenGuiRect(icon.rectTransform);
				if (rect.width <= 1f) {
					continue;
				}
				foundAny = true;
				if (rect.xMin < minX) {
					minX = rect.xMin;
				}
				if (rect.yMin < minY) {
					minY = rect.yMin;
				}
				if (rect.xMax > maxX) {
					maxX = rect.xMax;
				}
				if (rect.yMax > maxY) {
					maxY = rect.yMax;
				}
			}

			if (!foundAny) {
				return false;
			}
			bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
			return true;
		}

		private Rect GetScreenGuiRect(RectTransform rectTransform) {
			rectTransform.GetWorldCorners(worldCornersBuffer);
			Canvas? canvas = rectTransform.GetComponentInParent<Canvas>();
			Camera? camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
				? canvas.worldCamera
				: null;

			Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, worldCornersBuffer[0]);
			Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, worldCornersBuffer[2]);

			float guiX = Mathf.Min(bottomLeft.x, topRight.x);
			float guiY = Screen.height - Mathf.Max(bottomLeft.y, topRight.y);
			float guiWidth = Mathf.Abs(topRight.x - bottomLeft.x);
			float guiHeight = Mathf.Abs(topRight.y - bottomLeft.y);
			return new Rect(guiX, guiY, guiWidth, guiHeight);
		}

		private static UI_CharacterStatusPanel? GetOpenInventoryPanel() {
			if (UIManager.Instance == null) {
				return null;
			}
			UI_CharacterStatusPanel panel = UIManager.Instance.GetElement<UI_CharacterStatusPanel>();
			if (panel == null || !panel.IsOpened) {
				return null;
			}
			return panel;
		}

		private int ComputeInventoryStateHash(
			PlayerAvatar avatar,
			GridInventory inventory,
			out int occupiedSlotCount
		) {
			occupiedSlotCount = 0;
			int hash = 17;
			int width = Math.Max(1, (int)inventory.Width);
			int unlocked = Math.Clamp((int)inventory.CurrentInventoryStorage, 0, InventorySnapshot.MaxSupportedSlots);
			hash = (hash * 31) + width;
			hash = (hash * 31) + unlocked;
			hash = (hash * 31) + inventory.tabletEngravingCount;
			hash = (hash * 31) + inventory.uniquePairCount;
			int engravedCount = inventory.fixedEngravingsOnServer != null
				? inventory.fixedEngravingsOnServer.Count
				: 0;
			hash = (hash * 31) + engravedCount;
			int mysticPosCount = inventory.mysticPositions != null
				? inventory.mysticPositions.Count
				: 0;
			hash = (hash * 31) + mysticPosCount;
			for (int m = 0; m < mysticPosCount; m++) {
				ItemPosition mPos = inventory.mysticPositions![m];
				hash = (hash * 31) + mPos.x;
				hash = (hash * 31) + mPos.y;
			}

			for (int s = 0; s < unlocked; s++) {
				sbyte gridX = (sbyte)(s % width);
				sbyte gridY = (sbyte)(s / width);
				NewItemOwnInstance item = inventory.FindItem(gridX, gridY);
				if (item != null && item.Entity != null) {
					occupiedSlotCount++;
				}
				int instanceId = item != null ? item.InstanceID : -1;
				StoneTablet? tablet = item != null ? item.StoneTablet : null;
				if (tablet == null && item != null && inventory.stoneTablets != null) {
					inventory.stoneTablets.TryGetValue(new ItemPosition(gridX, gridY), out tablet);
				}
				int rot = tablet != null ? tablet.rotation : 0;
				hash = (hash * 31) + instanceId;
				hash = (hash * 31) + rot;
			}

			if (cachedWeaponCtrlAvatar != avatar) {
				cachedWeaponCtrlAvatar = avatar;
				cachedWeaponCtrl = avatar.GetComponent<WeaponControllerSimple>();
			}
			WeaponSimple? curWeapon = cachedWeaponCtrl != null ? cachedWeaponCtrl.currentWeapon : null;
			hash = (hash * 31) + (curWeapon != null ? curWeapon.GetInstanceID() : 0);
			if (curWeapon != null && curWeapon.addons != null) {
				int activeAddons = 0;
				for (int i = 0; i < curWeapon.addons.Length; i++) {
					WeaponAddon addon = curWeapon.addons[i];
					if (addon != null && addon.enabled && addon.gameObject.activeInHierarchy) {
						activeAddons++;
					}
				}
				hash = (hash * 31) + activeAddons;
			}
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.PhysicalDamage);
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.FireDamage);
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.IceDamage);
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.LightningDamage);
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.AllDamageBonus);
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.WeaponDamageBonus);
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.AttackSpeed);
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.Critical);
			hash = (hash * 31) + avatar.GetCustomStat(ECustomStat.CriticalDamageBonus);

			hash = (hash * 31) + pinnedSlotIndices.Count;
			hash = (hash * 31) + userSettings.ComputeSettingsHash();
			return hash;
		}

		private void SetStatusBanner(string message) {
			statusBannerText = message;
		}

		private void RefreshCachedDpsTooltip() {
			string talentBadge = BuildTalentSummaryBadge(latestSnapshot);
			if (latestResult != null) {
				cachedDpsTooltip = DpsEstimator.BuildTooltipCache(
					latestResult.CurrentBreakdown,
					latestResult.OptimizedBreakdown,
					latestResult.UsedExactBranchAndBound,
					latestResult.ElapsedMilliseconds,
					talentBadge
				);
			} else if (latestSnapshot != null) {
				cachedDpsTooltip = DpsEstimator.BuildTooltipCache(
					latestSnapshot.CurrentBreakdown,
					null,
					false,
					0.0,
					talentBadge
				);
			} else {
				cachedDpsTooltip = DpsTooltipCache.CreateIdle(talentBadge);
			}
		}

		private void EnsureGuiStyles() {
			if (headerLabelStyle != null) {
				return;
			}

			headerLabelStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 12,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(0.95f, 0.96f, 1f, 1f) }
			};
			subHeaderStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 11,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(0.55f, 0.88f, 1f, 1f) }
			};
			statusWrapStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 11,
				wordWrap = true,
				alignment = TextAnchor.UpperLeft,
				normal = { textColor = new Color(0.84f, 0.88f, 0.94f, 1f) }
			};
			alertLabelStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 11,
				fontStyle = FontStyle.Bold,
				wordWrap = true,
				alignment = TextAnchor.MiddleLeft,
				normal = { textColor = new Color(1f, 0.96f, 0.88f, 1f) }
			};
			buttonStyle = new GUIStyle(GUI.skin.button) {
				fontSize = 11,
				fontStyle = FontStyle.Bold
			};
			compactButtonStyle = new GUIStyle(GUI.skin.button) {
				fontSize = 10,
				fontStyle = FontStyle.Bold
			};
			activePinButtonStyle = new GUIStyle(GUI.skin.button) {
				fontSize = 11,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(1f, 0.88f, 0.2f, 1f) }
			};
			selectedButtonStyle = new GUIStyle(GUI.skin.button) {
				fontSize = 11,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(0.25f, 1f, 0.45f, 1f) }
			};
			accentUpdateButtonStyle = new GUIStyle(GUI.skin.button) {
				fontSize = 11,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(0.35f, 1f, 0.65f, 1f) }
			};
			badgeStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 11,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleCenter,
				normal = { textColor = Color.white }
			};
			coordBadgeStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 11,
				fontStyle = FontStyle.Bold,
				alignment = TextAnchor.MiddleCenter,
				normal = { textColor = new Color(0.55f, 0.95f, 1f, 1f) }
			};
			pinTagStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 10,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(1f, 0.85f, 0.1f, 1f) }
			};
		}

		private static void DrawColoredBox(Rect rect, Color color) {
			Color prevColor = GUI.color;
			GUI.color = color;
			GUI.DrawTexture(rect, Texture2D.whiteTexture);
			GUI.color = prevColor;
		}

		private static void DrawRectBorder(Rect rect, Color color, float thickness) {
			Color prevColor = GUI.color;
			GUI.color = color;
			GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, thickness), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), Texture2D.whiteTexture);
			GUI.color = prevColor;
		}

		private static void DrawConnectionLine(Vector2 start, Vector2 end, Color color, float thickness) {
			Vector2 delta = end - start;
			float length = delta.magnitude;
			if (length < 1f) {
				return;
			}
			float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
			Matrix4x4 savedMatrix = GUI.matrix;
			Color savedColor = GUI.color;

			GUI.color = color;
			GUIUtility.RotateAroundPivot(angle, start);
			GUI.DrawTexture(new Rect(start.x, start.y - (thickness * 0.5f), length, thickness), Texture2D.whiteTexture);

			GUI.matrix = savedMatrix;
			GUI.color = savedColor;
		}
	}
}
