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
			for (int s = 0; s < unlockedSlots; s++) {
				workingSlots[s] = currentSlotToItem[s];
			}

			int[] targetSlotForItem = new int[itemCount];
			Array.Fill(targetSlotForItem, -1);
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = targetSlotToItem[s];
				if (itemIdx >= 0 && itemIdx < itemCount) {
					targetSlotForItem[itemIdx] = s;
				}
			}

			int[] nextSlotInPermutation = new int[unlockedSlots];
			Array.Fill(nextSlotInPermutation, -1);
			bool[] hasIncomingEdge = new bool[unlockedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = workingSlots[s];
				if (itemIdx >= 0) {
					int dstSlot = targetSlotForItem[itemIdx];
					if (dstSlot >= 0) {
						nextSlotInPermutation[s] = dstSlot;
						hasIncomingEdge[dstSlot] = true;
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (workingSlots[s] < 0 && targetSlotToItem[s] < 0) {
					nextSlotInPermutation[s] = s;
					hasIncomingEdge[s] = true;
				}
			}

			for (int startSlot = 0; startSlot < unlockedSlots; startSlot++) {
				if (workingSlots[startSlot] >= 0 && !hasIncomingEdge[startSlot]) {
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
			bool[] visited = new bool[unlockedSlots];
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
				int dstRot = targetItemRotations[i] & 3;
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
	}
}
