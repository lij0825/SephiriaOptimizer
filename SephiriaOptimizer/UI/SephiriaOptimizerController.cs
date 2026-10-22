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
		private readonly HashSet<int> pinnedSlotIndices = new HashSet<int>();
		private readonly Dictionary<int, int> customEntityWeights = new Dictionary<int, int>();
		private readonly Vector3[] worldCornersBuffer = new Vector3[4];
		private readonly OptimizationSolver solver = new OptimizationSolver();

		private InventorySnapshot? latestSnapshot;
		private OptimizationResult? latestResult;
		private ExecutionPlan? latestPlan;

		private Task<OptimizationResult>? activeSolveTask;
		private InventorySnapshot? pendingTaskSnapshot;

		private bool isOverlayEnabled = true;
		private bool isPinModeEnabled;
		private bool isAutoSwapRunning;
		private int lastObservedStateHash;
		private int selectedSlotIndex = -1;
		private string statusBannerText = "준비 완료 — [F5] 자동 배치 / [F6] 가이드 토글";

		private GUIStyle? panelBoxStyle;
		private GUIStyle? headerLabelStyle;
		private GUIStyle? buttonStyle;
		private GUIStyle? activePinButtonStyle;
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
					customEntityWeights
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
				customEntityWeights
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
			float panelWidth = 680f;
			float panelHeight = 88f;
			float panelX = Mathf.Max(16f, (Screen.width - panelWidth) * 0.5f);
			float panelY = 14f;

			if (TryGetInventoryGridBounds(statusPanel, inventory, out Rect gridBounds)) {
				panelX = Mathf.Clamp(
					gridBounds.center.x - (panelWidth * 0.5f),
					12f,
					Mathf.Max(12f, Screen.width - panelWidth - 12f)
				);
				panelY = Mathf.Max(10f, gridBounds.yMin - panelHeight - 12f);
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

			string scoreHeader = $"[Sephiria Optimizer] 현재 배치 점수: {curScore}  vs  "
				+ $"최적 배치 점수: {optScore} ({deltaStr})  |  {solverMode} ({timeStr})";

			GUI.Label(new Rect(panelX + 12f, panelY + 8f, panelWidth - 24f, 22f), scoreHeader, headerLabelStyle!);

			float btnY = panelY + 34f;
			float btnH = 26f;

			if (GUI.Button(new Rect(panelX + 12f, btnY, 155f, btnH), "[F5] 자동 정렬 실행", buttonStyle!)) {
				TriggerAutoSwapOrFallback(statusPanel, avatar, inventory);
			}

			string f6Label = isOverlayEnabled
				? "[F6] 가이드 오버레이: 켜짐"
				: "[F6] 가이드 오버레이: 꺼짐";
			if (GUI.Button(new Rect(panelX + 175f, btnY, 175f, btnH), f6Label, buttonStyle!)) {
				isOverlayEnabled = !isOverlayEnabled;
			}

			string pinLabel = isPinModeEnabled
				? $"[Pin 모드: ON] (우클릭 고정: {pinnedSlotIndices.Count}개)"
				: $"[Pin 모드: OFF] (고정됨: {pinnedSlotIndices.Count}개)";
			GUIStyle currentPinStyle = isPinModeEnabled ? activePinButtonStyle! : buttonStyle!;
			if (GUI.Button(new Rect(panelX + 358f, btnY, 210f, btnH), pinLabel, currentPinStyle)) {
				isPinModeEnabled = !isPinModeEnabled;
				SetStatusBanner(
					isPinModeEnabled
						? "슬롯 고정(Pin) 모드 켜짐: 슬롯 위에서 우클릭하면 해당 위치가 고정됩니다."
						: "슬롯 고정(Pin) 모드 꺼짐."
				);
			}

			if (GUI.Button(new Rect(panelX + 576f, btnY, 92f, btnH), "고정 초기화", buttonStyle!)) {
				pinnedSlotIndices.Clear();
				lastObservedStateHash = 0;
				SetStatusBanner("모든 슬롯 고정(Pin)이 해제되었습니다.");
			}

			GUI.Label(
				new Rect(panelX + 12f, panelY + 64f, panelWidth - 24f, 20f),
				$"상태: {statusBannerText}",
				badgeStyle!
			);
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

			if (Event.current != null && Event.current.type == EventType.MouseDown) {
				if (Event.current.button == 1 && isPinModeEnabled && hoveredSlot >= 0) {
					if (!pinnedSlotIndices.Add(hoveredSlot)) {
						pinnedSlotIndices.Remove(hoveredSlot);
					}
					lastObservedStateHash = 0;
					Event.current.Use();
				} else if (Event.current.button == 0 && hoveredSlot >= 0) {
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
					if (detail.ItemIndex >= 0 && snapshot.Items[detail.ItemIndex].Kind == SlotItemKind.Artifact) {
						DrawExpectedLevelBadge(rect, detail);
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

		private void DrawExpectedLevelBadge(Rect slotRect, SlotEvaluationDetail detail) {
			Rect badgeRect = new Rect(
				slotRect.x + 2f,
				slotRect.yMax - 20f,
				slotRect.width - 4f,
				18f
			);

			string label;
			Color bgColor;

			if (!detail.IsEnabled) {
				if (detail.IsDeactivatedByNegativeLevel) {
					label = $"{detail.DisplayedLevel} (비활성 경고)";
				} else if (detail.IsDeactivatedByCriteria) {
					label = "제약 미충족";
				} else {
					label = "비활성 (X)";
				}
				bgColor = new Color(0.72f, 0.12f, 0.12f, 0.92f);
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
			buttonStyle = new GUIStyle(GUI.skin.button) {
				fontSize = 12,
				fontStyle = FontStyle.Bold
			};
			activePinButtonStyle = new GUIStyle(GUI.skin.button) {
				fontSize = 12,
				fontStyle = FontStyle.Bold,
				normal = { textColor = new Color(1f, 0.88f, 0.2f, 1f) }
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
