using System;

namespace SephiriaOptimizer.Models {
	public sealed class InventorySnapshot {
		public const int MaxSupportedSlots = 64;

		public int Width { get; }
		public int Height { get; }
		public int UnlockedSlots { get; }
		public SlotCoord[] Slots { get; }
		public int[] BaseSlotLevel { get; }
		public int[] BaseSlotDisable { get; }
		public int[] BaseSlotIgnoreCriteria { get; }
		public int[] BaseSlotMultiplyLevel { get; }
		public bool[] PinnedSlots { get; }
		public bool IsFullHpSatisfied { get; }
		public int GlobalActiveValue { get; }
		public SlotItemSnapshot[] Items { get; }
		public int[] InitialSlotToItemIndex { get; }
		public int[] InitialItemRotations { get; }

		public InventorySnapshot(
			int width,
			int height,
			int unlockedSlots,
			int[] baseSlotLevel,
			int[] baseSlotDisable,
			int[] baseSlotIgnoreCriteria,
			int[] baseSlotMultiplyLevel,
			bool[] pinnedSlots,
			bool isFullHpSatisfied,
			int globalActiveValue,
			SlotItemSnapshot[] items
		) {
			Width = Math.Max(1, width);
			Height = Math.Max(1, height);
			UnlockedSlots = Math.Clamp(unlockedSlots, 0, MaxSupportedSlots);
			Slots = new SlotCoord[UnlockedSlots];
			for (int i = 0; i < UnlockedSlots; i++) {
				sbyte xCoord = (sbyte)(i % Width);
				sbyte yCoord = (sbyte)(i / Width);
				Slots[i] = new SlotCoord(xCoord, yCoord, i);
			}

			BaseSlotLevel = CopyOrAllocate(baseSlotLevel, UnlockedSlots);
			BaseSlotDisable = CopyOrAllocate(baseSlotDisable, UnlockedSlots);
			BaseSlotIgnoreCriteria = CopyOrAllocate(baseSlotIgnoreCriteria, UnlockedSlots);
			BaseSlotMultiplyLevel = CopyOrAllocate(baseSlotMultiplyLevel, UnlockedSlots);
			PinnedSlots = new bool[UnlockedSlots];
			if (pinnedSlots != null) {
				int copyLen = Math.Min(pinnedSlots.Length, UnlockedSlots);
				Array.Copy(pinnedSlots, PinnedSlots, copyLen);
			}

			IsFullHpSatisfied = isFullHpSatisfied;
			GlobalActiveValue = globalActiveValue;
			Items = items ?? Array.Empty<SlotItemSnapshot>();

			InitialSlotToItemIndex = new int[UnlockedSlots];
			Array.Fill(InitialSlotToItemIndex, -1);
			InitialItemRotations = new int[Items.Length];

			for (int i = 0; i < Items.Length; i++) {
				SlotItemSnapshot item = Items[i];
				InitialItemRotations[i] = item.InitialRotation;
				if (item.InitialSlotIndex >= 0 && item.InitialSlotIndex < UnlockedSlots) {
					InitialSlotToItemIndex[item.InitialSlotIndex] = i;
				}
			}
		}

		public int CoordToSlotIndex(int xCoord, int yCoord) {
			if (xCoord < 0 || xCoord >= Width || yCoord < 0 || yCoord >= Height) {
				return -1;
			}
			int linearIndex = (yCoord * Width) + xCoord;
			if (linearIndex < 0 || linearIndex >= UnlockedSlots) {
				return -1;
			}
			return linearIndex;
		}

		private static int[] CopyOrAllocate(int[]? source, int length) {
			int[] result = new int[length];
			if (source != null) {
				int copyLen = Math.Min(source.Length, length);
				Array.Copy(source, result, copyLen);
			}
			return result;
		}
	}
}
