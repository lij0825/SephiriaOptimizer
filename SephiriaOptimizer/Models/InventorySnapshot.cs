using System;

namespace SephiriaOptimizer.Models {
	public sealed class InventorySnapshot {
		public const int MaxSupportedSlots = 64;
		public const int MaxSupportedCategories = 32;

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
		public ComboCategoryInfo[] ComboCategories { get; }
		public TalentRuleSnapshot TalentRules { get; }
		public int PreferredNeedleTargetInstanceId { get; }
		public int PreferredChalcedonyCategoryId { get; }

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
			SlotItemSnapshot[] items,
			ComboCategoryInfo[]? comboCategories = null,
			TalentRuleSnapshot? talentRules = null,
			int preferredNeedleTargetInstanceId = -1,
			int preferredChalcedonyCategoryId = -1
		) {
			Width = Math.Max(1, width);
			UnlockedSlots = Math.Clamp(unlockedSlots, 0, MaxSupportedSlots);
			int minRequiredHeight = UnlockedSlots > 0 ? ((UnlockedSlots + Width - 1) / Width) : 1;
			Height = Math.Max(Math.Max(1, height), minRequiredHeight);
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
			ComboCategories = comboCategories ?? Array.Empty<ComboCategoryInfo>();
			TalentRules = talentRules ?? TalentRuleSnapshot.Default;
			PreferredNeedleTargetInstanceId = preferredNeedleTargetInstanceId;
			PreferredChalcedonyCategoryId = preferredChalcedonyCategoryId;

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

		public int FindCategoryId(string categoryKey) {
			for (int i = 0; i < ComboCategories.Length; i++) {
				if (string.Equals(ComboCategories[i].CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase)) {
					return ComboCategories[i].CategoryId;
				}
			}
			return -1;
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
