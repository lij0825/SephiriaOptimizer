using System;
using System.Collections.Generic;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Execution {
	public readonly struct SlotSwapStep {
		public int FirstSlotIndex { get; }
		public SlotCoord FirstCoord { get; }
		public int SecondSlotIndex { get; }
		public SlotCoord SecondCoord { get; }

		public SlotSwapStep(
			int firstSlotIndex,
			SlotCoord firstCoord,
			int secondSlotIndex,
			SlotCoord secondCoord
		) {
			FirstSlotIndex = firstSlotIndex;
			FirstCoord = firstCoord;
			SecondSlotIndex = secondSlotIndex;
			SecondCoord = secondCoord;
		}
	}

	public readonly struct TabletRotateStep {
		public int ItemIndex { get; }
		public int TargetSlotIndex { get; }
		public SlotCoord TargetCoord { get; }
		public int ClickCount { get; }

		public TabletRotateStep(
			int itemIndex,
			int targetSlotIndex,
			SlotCoord targetCoord,
			int clickCount
		) {
			ItemIndex = itemIndex;
			TargetSlotIndex = targetSlotIndex;
			TargetCoord = targetCoord;
			ClickCount = clickCount;
		}
	}

	public sealed class ExecutionPlan {
		public IReadOnlyList<SlotSwapStep> SwapSteps { get; }
		public IReadOnlyList<TabletRotateStep> RotateSteps { get; }
		public int TotalOperations => SwapSteps.Count + RotateSteps.Count;

		public ExecutionPlan(
			IReadOnlyList<SlotSwapStep> swapSteps,
			IReadOnlyList<TabletRotateStep> rotateSteps
		) {
			SwapSteps = swapSteps ?? Array.Empty<SlotSwapStep>();
			RotateSteps = rotateSteps ?? Array.Empty<TabletRotateStep>();
		}
	}

	public static class CycleDecomposition {
		public static ExecutionPlan BuildExecutionPlan(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> currentSlotToItem,
			ReadOnlySpan<int> targetSlotToItem,
			ReadOnlySpan<int> currentItemRotations,
			ReadOnlySpan<int> targetItemRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;

			int[] workingSlots = new int[unlockedSlots];
			int[] resolvedTargetSlots = new int[unlockedSlots];
			for (int s = 0; s < unlockedSlots; s++) {
				workingSlots[s] = currentSlotToItem[s];
				resolvedTargetSlots[s] = targetSlotToItem[s];
			}

			int[] resolvedTargetRotations = new int[itemCount];
			int[] targetSlotForItem = new int[itemCount];
			for (int i = 0; i < itemCount; i++) {
				resolvedTargetRotations[i] = i < targetItemRotations.Length ? (targetItemRotations[i] & 3) : 0;
				targetSlotForItem[i] = -1;
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = resolvedTargetSlots[s];
				if (itemIdx >= 0 && itemIdx < itemCount) {
					targetSlotForItem[itemIdx] = s;
				}
			}

			bool[] visited = new bool[unlockedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				int curItem = workingSlots[s];
				int dstItem = resolvedTargetSlots[s];
				if (curItem < 0 && dstItem < 0) {
					visited[s] = true;
					continue;
				}
				if (curItem < 0 || dstItem < 0) {
					continue;
				}
				if (curItem == dstItem) {
					visited[s] = true;
					continue;
				}

				SlotItemSnapshot curSnap = snapshot.Items[curItem];
				SlotItemSnapshot dstSnap = snapshot.Items[dstItem];
				if (curSnap.SymmetryGroupId != dstSnap.SymmetryGroupId) {
					continue;
				}

				bool sameRotation = !curSnap.IsRotatable
					|| ((currentItemRotations[curItem] & 3) == (resolvedTargetRotations[dstItem] & 3));
				if (sameRotation) {
					SwapIdentityTargets(
						s,
						curItem,
						dstItem,
						workingSlots,
						resolvedTargetSlots,
						targetSlotForItem,
						resolvedTargetRotations,
						visited
					);
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (visited[s]) {
					continue;
				}
				int curItem = workingSlots[s];
				int dstItem = resolvedTargetSlots[s];
				if (curItem < 0 || dstItem < 0) {
					continue;
				}
				if (curItem == dstItem) {
					visited[s] = true;
					continue;
				}

				if (snapshot.Items[curItem].SymmetryGroupId == snapshot.Items[dstItem].SymmetryGroupId) {
					SwapIdentityTargets(
						s,
						curItem,
						dstItem,
						workingSlots,
						resolvedTargetSlots,
						targetSlotForItem,
						resolvedTargetRotations,
						visited
					);
				}
			}

			int[] nextSlotInPermutation = new int[unlockedSlots];
			Array.Fill(nextSlotInPermutation, -1);
			bool[] hasIncomingEdge = new bool[unlockedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				if (visited[s]) {
					nextSlotInPermutation[s] = s;
					hasIncomingEdge[s] = true;
					continue;
				}
				int itemIdx = workingSlots[s];
				if (itemIdx >= 0) {
					int dstSlot = targetSlotForItem[itemIdx];
					if (dstSlot >= 0) {
						nextSlotInPermutation[s] = dstSlot;
						hasIncomingEdge[dstSlot] = true;
					}
				}
			}

			for (int startSlot = 0; startSlot < unlockedSlots; startSlot++) {
				if (!visited[startSlot] && workingSlots[startSlot] >= 0 && !hasIncomingEdge[startSlot]) {
					int cursor = startSlot;
					while (nextSlotInPermutation[cursor] >= 0 && nextSlotInPermutation[cursor] != startSlot) {
						cursor = nextSlotInPermutation[cursor];
					}
					if (nextSlotInPermutation[cursor] < 0) {
						nextSlotInPermutation[cursor] = startSlot;
						hasIncomingEdge[startSlot] = true;
					}
				}
			}

			List<SlotSwapStep> swapSteps = new List<SlotSwapStep>();
			int[] cycleBuffer = new int[unlockedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				if (visited[s]) {
					continue;
				}
				int next = nextSlotInPermutation[s];
				if (next < 0 || next == s) {
					visited[s] = true;
					continue;
				}

				int cycleLength = 0;
				int cursor = s;
				while (cursor >= 0 && !visited[cursor]) {
					visited[cursor] = true;
					cycleBuffer[cycleLength++] = cursor;
					cursor = nextSlotInPermutation[cursor];
				}

				if (cycleLength <= 1) {
					continue;
				}

				int anchorSlot = cycleBuffer[0];
				SlotCoord anchorCoord = snapshot.Slots[anchorSlot];
				for (int k = 1; k < cycleLength; k++) {
					int targetSlot = cycleBuffer[k];
					SlotCoord targetCoord = snapshot.Slots[targetSlot];
					swapSteps.Add(new SlotSwapStep(anchorSlot, anchorCoord, targetSlot, targetCoord));

					int tempItem = workingSlots[anchorSlot];
					workingSlots[anchorSlot] = workingSlots[targetSlot];
					workingSlots[targetSlot] = tempItem;
				}
			}

			List<TabletRotateStep> rotateSteps = new List<TabletRotateStep>();
			for (int i = 0; i < itemCount; i++) {
				SlotItemSnapshot item = snapshot.Items[i];
				if (item.Kind != SlotItemKind.StoneTablet || !item.IsRotatable) {
					continue;
				}
				int curRot = currentItemRotations[i] & 3;
				int dstRot = resolvedTargetRotations[i] & 3;
				int clicks = (dstRot - curRot + 4) & 3;
				if (clicks > 0) {
					int finalSlot = targetSlotForItem[i];
					if (finalSlot >= 0 && finalSlot < unlockedSlots) {
						rotateSteps.Add(new TabletRotateStep(
							i,
							finalSlot,
							snapshot.Slots[finalSlot],
							clicks
						));
					}
				}
			}

			return new ExecutionPlan(swapSteps, rotateSteps);
		}

		private static void SwapIdentityTargets(
			int slotIndex,
			int curItem,
			int dstItem,
			int[] workingSlots,
			int[] resolvedTargetSlots,
			int[] targetSlotForItem,
			int[] resolvedTargetRotations,
			bool[] visited
		) {
			int prevTargetOfCur = targetSlotForItem[curItem];
			int prevRotOfCur = resolvedTargetRotations[curItem];
			int prevRotOfDst = resolvedTargetRotations[dstItem];

			resolvedTargetSlots[slotIndex] = curItem;
			targetSlotForItem[curItem] = slotIndex;
			resolvedTargetRotations[curItem] = prevRotOfDst;

			if (prevTargetOfCur >= 0) {
				resolvedTargetSlots[prevTargetOfCur] = dstItem;
			}
			targetSlotForItem[dstItem] = prevTargetOfCur;
			resolvedTargetRotations[dstItem] = prevRotOfCur;

			visited[slotIndex] = true;
			if (prevTargetOfCur >= 0 && workingSlots[prevTargetOfCur] == dstItem) {
				visited[prevTargetOfCur] = true;
			}
		}
	}
}
