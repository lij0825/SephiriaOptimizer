using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using SephiriaOptimizer.Execution;
using SephiriaOptimizer.Models;
using SephiriaOptimizer.Snapshot;
using SephiriaOptimizer.Solver;
using UnityEngine;

namespace SephiriaOptimizer.UI {
	public sealed class SephiriaOptimizerController : MonoBehaviour {
		private static readonly (string Id, string KorName)[] CommonComboTags = new[] {
			("EMBER", "잉걸불"),
			("GLACIER", "빙하"),
			("MAGITECH", "마도공학"),
			("STURDY", "견고함"),
			("WINDSONG", "바람노래"),
			("PLAGUE", "역병"),
			("SHADOW", "그림자"),
			("ARCANE", "비전"),
			("GUARDIAN", "수호"),
			("PRECISION", "정밀"),
			("LIGHTNING", "번개"),
			("LAKE", "호수"),
			("COMPANION", "동료"),
			("PLANET", "행성"),
			("DRUNKEN", "만취"),
			("CURSE", "저주")
		};

		private readonly HashSet<int> pinnedSlotIndices = new HashSet<int>();
		private readonly Dictionary<int, int> customEntityWeights = new Dictionary<int, int>();
		private readonly OptimizerUserSettings userSettings = new OptimizerUserSettings();
		private readonly Vector3[] worldCornersBuffer = new Vector3[4];
		private readonly OptimizationSolver solver = new OptimizationSolver();

		private InventorySnapshot? latestSnapshot;
		private OptimizationResult? latestResult;
		private ExecutionPlan? latestPlan;

		private Task<OptimizationResult>? activeSolveTask;
		private InventorySnapshot? pendingTaskSnapshot;

		private bool isOverlayEnabled = true;
		private bool isPinModeEnabled;
		private bool isConfigPanelOpen;
		private bool isAutoSwapRunning;
		private int lastObservedStateHash;
		private int selectedSlotIndex = -1;
		private Vector2 configScrollPos;
		private string statusBannerText = "준비 완료 — [F5] 자동 배치 / [F6] 가이드 / [F7] 우선순위·콤보 설정";

		private GUIStyle? panelBoxStyle;
		private GUIStyle? headerLabelStyle;
		private GUIStyle? subHeaderStyle;
		private GUIStyle? buttonStyle;
		private GUIStyle? activePinButtonStyle;
		private GUIStyle? selectedButtonStyle;
		private GUIStyle? badgeStyle;
		private GUIStyle? coordBadgeStyle;
		private GUIStyle? pinTagStyle;

		private void Update() {
			UI_CharacterStatusPanel? statusPanel = GetOpenInventoryPanel();
			if (statusPanel == null) {
				if (activeSolveTask != null && activeSolveTask.IsCompleted) {
					activeSolveTask = null;
				}
				selectedSlotIndex = -1;
				return;
			}

			if (activeSolveTask != null && activeSolveTask.IsCompleted) {
				CompleteBackgroundSolve();
			}

			PlayerAvatar avatar = statusPanel.PlayerAvatar;
			GridInventory? inventory = avatar != null ? avatar.Inventory : null;
			if (avatar == null || inventory == null) {
				return;
			}

			int currentStateHash = ComputeInventoryStateHash(inventory);
			if (currentStateHash != lastObservedStateHash && !isAutoSwapRunning && activeSolveTask == null) {
				lastObservedStateHash = currentStateHash;
				StartBackgroundSolve(avatar, inventory);
			}

			if (Input.GetKeyDown(KeyCode.F5)) {
				TriggerAutoSwapOrFallback(statusPanel, avatar, inventory);
			}

			if (Input.GetKeyDown(KeyCode.F6)) {
				isOverlayEnabled = !isOverlayEnabled;
				SetStatusBanner(
					isOverlayEnabled
						? "시각적 오버레이 가이드가 활성화되었습니다."
						: "시각적 오버레이 가이드가 숨겨졌습니다."
				);
			}

			if (Input.GetKeyDown(KeyCode.F7)) {
				isConfigPanelOpen = !isConfigPanelOpen;
				SetStatusBanner(
					isConfigPanelOpen
						? "아티팩트 우선순위 / 목표 콤보 / 금빛침 설정 창이 열렸습니다."
						: "설정 창이 닫혔습니다."
				);
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
			DrawTopSummaryControlPanel(statusPanel, avatar, inventory);

			if (isConfigPanelOpen && latestSnapshot != null) {
				DrawConfigPanel(latestSnapshot);
			}

			if (!isOverlayEnabled || latestSnapshot == null || latestResult == null) {
				return;
			}

			DrawSlotOverlays(statusPanel, latestSnapshot, latestResult);
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
				activeSolveTask = Task.Run(() => solver.Solve(snapshot));
			} catch (Exception ex) {
				Debug.LogError($"[SephiriaOptimizer] Snapshot capture failed: {ex.Message}");
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
					latestPlan = CycleDecomposition.BuildExecutionPlan(
						pendingTaskSnapshot,
						pendingTaskSnapshot.InitialSlotToItemIndex,
						result.BestSlotToItemIndex,
						pendingTaskSnapshot.InitialItemRotations,
						result.BestItemRotations
					);
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
			if (isAutoSwapRunning) {
				SetStatusBanner("자동 재배치가 이미 진행 중입니다.");
				return;
			}

			bool isLockedOrCombat = avatar.IsInBattle
				|| avatar.IsDead
				|| !inventory.IsPickable
				|| statusPanel.InventoryMode != UI_CharacterStatusPanel.EInventoryMode.None;

			if (isLockedOrCombat) {
				isOverlayEnabled = true;
				string reason = avatar.IsInBattle
					? "전투 중에는 자동 스왑이 제한됩니다"
					: "현재 인벤토리 이동이 잠겨 있습니다";
				SetStatusBanner($"{reason} → 오버레이 가이드 모드로 자동 전환됨!");
				return;
			}

			InventorySnapshot snapshot = GameInventoryReader.CaptureSnapshot(
				avatar,
				inventory,
				pinnedSlotIndices,
				customEntityWeights,
				userSettings
			);
			OptimizationResult result = solver.Solve(snapshot);
			ExecutionPlan plan = CycleDecomposition.BuildExecutionPlan(
				snapshot,
				snapshot.InitialSlotToItemIndex,
				result.BestSlotToItemIndex,
				snapshot.InitialItemRotations,
				result.BestItemRotations
			);

			latestSnapshot = snapshot;
			latestResult = result;
			latestPlan = plan;

			if (result.BestScore <= result.InitialScore || plan.TotalOperations == 0) {
				SetStatusBanner($"이미 최적 배치 상태입니다 (점수: {result.BestScore}).");
				return;
			}

			StartCoroutine(ExecuteAutoSwapCoroutine(statusPanel, avatar, inventory, plan, result));
		}

		private IEnumerator ExecuteAutoSwapCoroutine(
			UI_CharacterStatusPanel statusPanel,
			PlayerAvatar avatar,
			GridInventory inventory,
			ExecutionPlan plan,
			OptimizationResult targetResult
		) {
			isAutoSwapRunning = true;
			SetStatusBanner($"자동 정렬 실행 중... (스왑 {plan.SwapSteps.Count}회, 회전 {plan.RotateSteps.Count}개)");

			IReadOnlyList<SlotSwapStep> swaps = plan.SwapSteps;
			for (int i = 0; i < swaps.Count; i++) {
				if (!CanContinueAutoSwap(statusPanel, avatar, inventory)) {
					isOverlayEnabled = true;
					isAutoSwapRunning = false;
					SetStatusBanner("이동 제한 감지됨 → 자동 스왑 중단 및 오버레이 가이드로 폴백");
					yield break;
				}

				SlotSwapStep step = swaps[i];
				inventory.Swap(
					step.FirstCoord.X,
					step.FirstCoord.Y,
					step.SecondCoord.X,
					step.SecondCoord.Y
				);
				yield return null;
			}

			IReadOnlyList<TabletRotateStep> rotates = plan.RotateSteps;
			for (int i = 0; i < rotates.Count; i++) {
				TabletRotateStep rotStep = rotates[i];
				ItemPosition pos = new ItemPosition(rotStep.TargetCoord.X, rotStep.TargetCoord.Y);
				for (int click = 0; click < rotStep.ClickCount; click++) {
					if (!CanContinueAutoSwap(statusPanel, avatar, inventory)) {
						isOverlayEnabled = true;
						isAutoSwapRunning = false;
						SetStatusBanner("이동 제한 감지됨 → 자동 회전 중단 및 오버레이 가이드로 폴백");
						yield break;
					}
					inventory.DoClickAction(pos);
					yield return null;
				}
			}

			isAutoSwapRunning = false;
			lastObservedStateHash = 0;
			SetStatusBanner(
				$"자동 재배치 완료! ({targetResult.InitialScore}점 → {targetResult.BestScore}점, "
				+ $"{plan.SwapSteps.Count}회 스왑)"
			);
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

		private void DrawTopSummaryControlPanel(
			UI_CharacterStatusPanel statusPanel,
			PlayerAvatar avatar,
			GridInventory inventory
		) {
			float panelWidth = 760f;
			float panelHeight = 92f;
			float panelX = Mathf.Max(16f, (Screen.width - panelWidth) * 0.5f);
			float panelY = 12f;

			if (TryGetInventoryGridBounds(statusPanel, inventory, out Rect gridBounds)) {
				panelX = Mathf.Clamp(
					gridBounds.center.x - (panelWidth * 0.5f),
					12f,
					Mathf.Max(12f, Screen.width - panelWidth - 12f)
				);
				panelY = Mathf.Max(10f, gridBounds.yMin - panelHeight - 10f);
			}

			Rect panelRect = new Rect(panelX, panelY, panelWidth, panelHeight);
			GUI.Box(panelRect, GUIContent.none, panelBoxStyle!);

			int curScore = latestResult != null ? latestResult.InitialScore : 0;
			int optScore = latestResult != null ? latestResult.BestScore : 0;
			int delta = optScore - curScore;
			string solverMode = latestResult != null
				? (latestResult.UsedExactBranchAndBound ? "Exact B&B" : "SA+Reheat")
				: "대기 중";
			string timeStr = latestResult != null
				? $"{latestResult.ElapsedMilliseconds:F1}ms"
				: "-";
			string deltaStr = delta >= 0 ? $"+{delta}" : delta.ToString();

			string talentInfo = BuildTalentSummaryBadge(latestSnapshot);
			string scoreHeader = $"[Sephiria Optimizer] 현재: {curScore} vs 최적: {optScore} ({deltaStr}) "
				+ $"| {solverMode} ({timeStr}){talentInfo}";

			GUI.Label(new Rect(panelX + 12f, panelY + 6f, panelWidth - 24f, 22f), scoreHeader, headerLabelStyle!);

			float btnY = panelY + 32f;
			float btnH = 26f;

			if (GUI.Button(new Rect(panelX + 12f, btnY, 138f, btnH), "[F5] 자동 정렬 실행", buttonStyle!)) {
				TriggerAutoSwapOrFallback(statusPanel, avatar, inventory);
			}

			string f6Label = isOverlayEnabled ? "[F6] 가이드: 켜짐" : "[F6] 가이드: 꺼짐";
			if (GUI.Button(new Rect(panelX + 156f, btnY, 132f, btnH), f6Label, buttonStyle!)) {
				isOverlayEnabled = !isOverlayEnabled;
			}

			string f7Label = isConfigPanelOpen ? "[F7] 빌드 설정: 닫기" : "[F7] 우선순위·콤보 설정";
			GUIStyle cfgBtnStyle = isConfigPanelOpen ? selectedButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(panelX + 294f, btnY, 162f, btnH), f7Label, cfgBtnStyle)) {
				isConfigPanelOpen = !isConfigPanelOpen;
			}

			int pinCount = pinnedSlotIndices.Count;
			string pinLabel = isPinModeEnabled
				? $"[Pin: ON] (고정:{pinCount})"
				: $"[Pin: OFF] (고정:{pinCount})";
			GUIStyle currentPinStyle = isPinModeEnabled ? activePinButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(panelX + 462f, btnY, 165f, btnH), pinLabel, currentPinStyle)) {
				isPinModeEnabled = !isPinModeEnabled;
				SetStatusBanner(
					isPinModeEnabled
						? "Pin 모드 ON: 슬롯 우클릭 시 위치 고정 (OFF일 땐 우클릭으로 아티팩트 우선순위 변경)"
						: "Pin 모드 OFF: 아티팩트 슬롯 우클릭 시 강화 우선순위(★/▲/●/▼/✕)가 순환됩니다."
				);
			}

			if (GUI.Button(new Rect(panelX + 633f, btnY, 115f, btnH), "설정/고정 초기화", buttonStyle!)) {
				pinnedSlotIndices.Clear();
				userSettings.InstancePriorities.Clear();
				userSettings.TargetComboCategories.Clear();
				userSettings.PreferredNeedleTargetInstanceId = -1;
				lastObservedStateHash = 0;
				SetStatusBanner("모든 슬롯 고정 및 우선순위/콤보 설정이 초기화되었습니다.");
			}

			GUI.Label(
				new Rect(panelX + 12f, panelY + 64f, panelWidth - 24f, 20f),
				$"상태: {statusBannerText}",
				badgeStyle!
			);
		}

		private void DrawConfigPanel(InventorySnapshot snapshot) {
			float width = 520f;
			float height = Mathf.Min(560f, Screen.height - 130f);
			float panelX = 16f;
			float panelY = 112f;

			Rect winRect = new Rect(panelX, panelY, width, height);
			DrawColoredBox(winRect, new Color(0.07f, 0.09f, 0.14f, 0.96f));
			DrawRectBorder(winRect, new Color(0.35f, 0.65f, 0.95f, 0.9f), 2f);

			GUI.Label(
				new Rect(panelX + 12f, panelY + 8f, width - 24f, 22f),
				"빌드 맞춤 최적화 설정 (캘세더니 열쇠 / 하얀 종이 / 금빛침 / 강화 우선순위)",
				headerLabelStyle!
			);

			Rect viewRect = new Rect(panelX + 8f, panelY + 34f, width - 16f, height - 42f);
			int artifactCount = 0;
			for (int i = 0; i < snapshot.Items.Length; i++) {
				if (snapshot.Items[i].Kind == SlotItemKind.Artifact) {
					artifactCount++;
				}
			}

			float contentHeight = 250f + (artifactCount * 32f);
			Rect contentRect = new Rect(0f, 0f, width - 36f, Mathf.Max(viewRect.height, contentHeight));
			configScrollPos = GUI.BeginScrollView(viewRect, configScrollPos, contentRect);

			float curY = 4f;
			GUI.Label(
				new Rect(4f, curY, contentRect.width - 8f, 20f),
				"1. 목표 콤보(#태그) 선택 — 캘세더니 열쇠 행 배치 & 하얀 종이 태그 복제 유도",
				subHeaderStyle!
			);
			curY += 22f;

			bool hasManualCombos = userSettings.TargetComboCategories.Count > 0;
			GUIStyle autoComboStyle = !hasManualCombos ? selectedButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(4f, curY, 150f, 24f), "자동 (활성 콤보 기반)", autoComboStyle)) {
				userSettings.TargetComboCategories.Clear();
				lastObservedStateHash = 0;
			}
			curY += 28f;

			int col = 0;
			float tagBtnW = 114f;
			float tagBtnH = 24f;
			for (int i = 0; i < CommonComboTags.Length; i++) {
				(string tagId, string korName) = CommonComboTags[i];
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
				"2. 북향의 금빛침 강화 대상 선택 — 지정한 공격형 아티팩트 아래(오프셋 역방향)에 침 배치",
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
				"3. 아티팩트별 강화 우선순위 (우클릭으로도 변경 가능 / ✕희생은 석판 -레벨 흡수용 토템)",
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

		private void DrawPriorityTierButtons(
			int instanceId,
			ArtifactPriorityTier currentTier,
			float startX,
			float y
		) {
			(ArtifactPriorityTier Tier, string Label, float Width)[] tiers = new[] {
				(ArtifactPriorityTier.Core, "★최우선", 56f),
				(ArtifactPriorityTier.High, "▲높음", 48f),
				(ArtifactPriorityTier.Normal, "●보통", 48f),
				(ArtifactPriorityTier.Low, "▼토템", 48f),
				(ArtifactPriorityTier.Sacrifice, "✕희생", 48f)
			};

			float x = startX;
			for (int i = 0; i < tiers.Length; i++) {
				(ArtifactPriorityTier tier, string label, float w) = tiers[i];
				GUIStyle style = currentTier == tier ? selectedButtonStyle! : buttonStyle!;
				if (GUI.Button(new Rect(x, y, w, 24f), label, style)) {
					userSettings.InstancePriorities[instanceId] = tier;
					lastObservedStateHash = 0;
				}
				x += w + 3f;
			}
		}

		private void DrawSlotOverlays(
			UI_CharacterStatusPanel statusPanel,
			InventorySnapshot snapshot,
			OptimizationResult result
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			Rect[] slotScreenRects = new Rect[unlockedSlots];
			bool[] hasValidRect = new bool[unlockedSlots];

			Vector2 mouseGuiPos = Event.current != null ? Event.current.mousePosition : Vector2.zero;
			int hoveredSlot = -1;

			for (int s = 0; s < unlockedSlots; s++) {
				SlotCoord coord = snapshot.Slots[s];
				UI_NewInventoryIcon icon = statusPanel.GetItemIcon(new ItemPosition(coord.X, coord.Y));
				if (icon == null || icon.rectTransform == null) {
					continue;
				}
				Rect rect = GetScreenGuiRect(icon.rectTransform);
				slotScreenRects[s] = rect;
				hasValidRect[s] = rect.width > 4f && rect.height > 4f;
				if (hasValidRect[s] && rect.Contains(mouseGuiPos)) {
					hoveredSlot = s;
				}
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
						int clickedItemIdx = snapshot.InitialSlotToItemIndex[hoveredSlot];
						if (clickedItemIdx >= 0 && snapshot.Items[clickedItemIdx].Kind == SlotItemKind.Artifact) {
							SlotItemSnapshot clickedArt = snapshot.Items[clickedItemIdx];
							userSettings.CyclePriority(clickedArt.InstanceId);
							ArtifactPriorityTier nextTier = userSettings.GetPriority(clickedArt.InstanceId);
							lastObservedStateHash = 0;
							SetStatusBanner(
								$"[{clickedArt.DisplayName}] 강화 우선순위 변경 → {GetPriorityShortLabel(nextTier)}"
							);
							Event.current.Use();
						}
					}
				} else if (Event.current.button == 0) {
					selectedSlotIndex = selectedSlotIndex == hoveredSlot ? -1 : hoveredSlot;
				}
			}

			int activeHighlightSlot = hoveredSlot >= 0 ? hoveredSlot : selectedSlotIndex;

			for (int s = 0; s < unlockedSlots; s++) {
				if (!hasValidRect[s]) {
					continue;
				}
				Rect rect = slotScreenRects[s];

				if (pinnedSlotIndices.Contains(s)) {
					DrawRectBorder(rect, new Color(1f, 0.82f, 0.15f, 0.95f), 2f);
					GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, 38f, 16f), "PIN", pinTagStyle!);
				}

				int currentItemIdx = snapshot.InitialSlotToItemIndex[s];
				if (currentItemIdx >= 0 && currentItemIdx < result.BestItemToSlotIndex.Length) {
					int targetSlot = result.BestItemToSlotIndex[currentItemIdx];
					SlotItemSnapshot currentItem = snapshot.Items[currentItemIdx];

					bool showPriority = currentItem.Kind == SlotItemKind.Artifact
						&& currentItem.PriorityTier != ArtifactPriorityTier.Normal;
					if (showPriority) {
						DrawPriorityBadge(rect, currentItem.PriorityTier);
					}

					if (targetSlot >= 0 && targetSlot != s && targetSlot < unlockedSlots) {
						SlotCoord targetCoord = snapshot.Slots[targetSlot];
						string moveText = $"→[{targetCoord.X},{targetCoord.Y}]";
						Rect moveBadgeRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 18f);
						DrawColoredBox(moveBadgeRect, new Color(0.05f, 0.18f, 0.36f, 0.88f));
						GUI.Label(moveBadgeRect, moveText, coordBadgeStyle!);
					} else if (currentItem.Kind == SlotItemKind.StoneTablet && currentItem.IsRotatable) {
						int curRot = snapshot.InitialItemRotations[currentItemIdx] & 3;
						int bestRot = result.BestItemRotations[currentItemIdx] & 3;
						int rotDiff = (bestRot - curRot + 4) & 3;
						if (rotDiff > 0) {
							string rotText = $"↻ +{rotDiff * 90}°";
							Rect rotBadgeRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 18f);
							DrawColoredBox(rotBadgeRect, new Color(0.35f, 0.15f, 0.45f, 0.88f));
							GUI.Label(rotBadgeRect, rotText, coordBadgeStyle!);
						}
					}
				}

				if (s < result.BestSlotDetails.Length) {
					SlotEvaluationDetail detail = result.BestSlotDetails[s];
					if (detail.ItemIndex >= 0) {
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

			if (activeHighlightSlot >= 0 && activeHighlightSlot < unlockedSlots && hasValidRect[activeHighlightSlot]) {
				int itemIdx = snapshot.InitialSlotToItemIndex[activeHighlightSlot];
				if (itemIdx >= 0 && itemIdx < result.BestItemToSlotIndex.Length) {
					int dstSlot = result.BestItemToSlotIndex[itemIdx];
					bool isValidDestination = dstSlot >= 0
						&& dstSlot < unlockedSlots
						&& dstSlot != activeHighlightSlot
						&& hasValidRect[dstSlot];
					if (isValidDestination) {
						Rect srcRect = slotScreenRects[activeHighlightSlot];
						Rect dstRect = slotScreenRects[dstSlot];
						DrawRectBorder(srcRect, new Color(0.2f, 0.95f, 1f, 1f), 2.5f);
						DrawRectBorder(dstRect, new Color(0.2f, 1f, 0.4f, 1f), 2.5f);
						DrawConnectionLine(srcRect.center, dstRect.center, new Color(0.2f, 0.95f, 1f, 0.95f), 3f);
					}
				}
			}
		}

		private void DrawPriorityBadge(Rect slotRect, ArtifactPriorityTier tier) {
			Rect tagRect = new Rect(slotRect.xMax - 42f, slotRect.y + 18f, 40f, 15f);
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
					label = "희생(콤보유지)";
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
				tags.Add($"생존20(각인:{rules.TabletEngravingCount})");
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
				ArtifactPriorityTier.Core => "★최우선",
				ArtifactPriorityTier.High => "▲높음",
				ArtifactPriorityTier.Normal => "●보통",
				ArtifactPriorityTier.Low => "▼토템",
				ArtifactPriorityTier.Sacrifice => "✕희생",
				_ => "●보통"
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
				sbyte x = (sbyte)(s % width);
				sbyte y = (sbyte)(s / width);
				UI_NewInventoryIcon icon = statusPanel.GetItemIcon(new ItemPosition(x, y));
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

		private int ComputeInventoryStateHash(GridInventory inventory) {
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

			for (int s = 0; s < unlocked; s++) {
				sbyte x = (sbyte)(s % width);
				sbyte y = (sbyte)(s / width);
				NewItemOwnInstance item = inventory.FindItem(x, y);
				int instanceId = item != null ? item.InstanceID : -1;
				int rot = item != null && item.StoneTablet != null ? item.StoneTablet.rotation : 0;
				hash = (hash * 31) + instanceId;
				hash = (hash * 31) + rot;
			}

			hash = (hash * 31) + pinnedSlotIndices.Count;
			hash = (hash * 31) + userSettings.ComputeSettingsHash();
			return hash;
		}

		private void SetStatusBanner(string message) {
			statusBannerText = message;
		}

		private void EnsureGuiStyles() {
			if (panelBoxStyle != null) {
				return;
			}

			panelBoxStyle = new GUIStyle(GUI.skin.box);
			headerLabelStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 13,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(0.95f, 0.96f, 1f, 1f) }
			};
			subHeaderStyle = new GUIStyle(GUI.skin.label) {
				fontSize = 12,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(0.55f, 0.88f, 1f, 1f) }
			};
			buttonStyle = new GUIStyle(GUI.skin.button) {
				fontSize = 11,
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
