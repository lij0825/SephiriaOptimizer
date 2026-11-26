using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SephiriaOptimizer.Solver;

namespace SephiriaOptimizer.Models {
	public sealed class InventorySnapshot {
		public const int MaxSupportedSlots = 64;
		public const int MaxSupportedCategories = 32;

		private readonly byte[] categoryElementalKinds;

		public int Width { get; }
		public int Height { get; }
		public int UnlockedSlots { get; }
		public SlotCoord[] Slots { get; }
		public ulong[] MooreNeighborMasks { get; }
		public ulong[] OrthogonalNeighborMasks { get; }
		public ulong[] RowSlotMasks { get; }
		public ulong[] ColSlotMasks { get; }
		public sbyte[] HorizontalNeighborLeft { get; }
		public sbyte[] HorizontalNeighborRight { get; }
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
		public int MysticCategoryId { get; }
		public int StaticMysticTagCount { get; }
		public int MysticTier1Threshold { get; }
		public int MysticTier2Threshold { get; }
		public int MysticSlot0 { get; }
		public int MysticSlot1 { get; }
		public int[] ExtraMysticTier1Slots { get; }
		public int[] ExtraMysticTier2Slots { get; }
		public uint UserTargetCategoryMask { get; }
		public int DependencyArtifactCount { get; }
		public bool HasAnyUserPriority { get; }
		public bool HasAnyCompanionEmblem { get; }
		public bool HasUnplacedCandidates { get; }
		public PlayerCombatBaseline CombatBaseline { get; }
		public bool ConsiderCompanionEmblemArtifacts { get; }
		public DpsBreakdown CurrentBreakdown { get; }

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
			int preferredChalcedonyCategoryId = -1,
			ulong[]? mooreNeighborMasks = null,
			int mysticSlot0 = -1,
			int mysticSlot1 = -1,
			int mysticTier1Threshold = 2,
			int mysticTier2Threshold = 4,
			int[]? extraMysticTier2Slots = null,
			PlayerCombatBaseline? combatBaseline = null,
			int[]? extraMysticTier1Slots = null,
			bool considerCompanionEmblemArtifacts = true
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

			if (mooreNeighborMasks != null && mooreNeighborMasks.Length == UnlockedSlots) {
				MooreNeighborMasks = new ulong[UnlockedSlots];
				Array.Copy(mooreNeighborMasks, MooreNeighborMasks, UnlockedSlots);
			} else {
				MooreNeighborMasks = ComputeMooreNeighborMasks(Width, Height, UnlockedSlots);
			}

			OrthogonalNeighborMasks = new ulong[UnlockedSlots];
			HorizontalNeighborLeft = new sbyte[UnlockedSlots];
			HorizontalNeighborRight = new sbyte[UnlockedSlots];
			RowSlotMasks = new ulong[Height];
			ColSlotMasks = new ulong[Width];
			for (int s = 0; s < UnlockedSlots; s++) {
				int x = Slots[s].X;
				int y = Slots[s].Y;
				RowSlotMasks[y] |= 1UL << s;
				ColSlotMasks[x] |= 1UL << s;

				int left = CoordToSlotIndex(x - 1, y);
				int right = CoordToSlotIndex(x + 1, y);
				int up = CoordToSlotIndex(x, y - 1);
				int down = CoordToSlotIndex(x, y + 1);
				HorizontalNeighborLeft[s] = left >= 0 ? (sbyte)left : (sbyte)-1;
				HorizontalNeighborRight[s] = right >= 0 ? (sbyte)right : (sbyte)-1;

				ulong ortho = 0UL;
				if (left >= 0) {
					ortho |= 1UL << left;
				}
				if (right >= 0) {
					ortho |= 1UL << right;
				}
				if (up >= 0) {
					ortho |= 1UL << up;
				}
				if (down >= 0) {
					ortho |= 1UL << down;
				}
				OrthogonalNeighborMasks[s] = ortho;
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
			CombatBaseline = combatBaseline ?? PlayerCombatBaseline.Default;

			categoryElementalKinds = new byte[MaxSupportedCategories];
			for (int i = 0; i < ComboCategories.Length; i++) {
				int catId = ComboCategories[i].CategoryId;
				if ((uint)catId < MaxSupportedCategories) {
					categoryElementalKinds[catId] = ComboCategories[i].CategoryElementalKind;
				}
			}

			MysticCategoryId = FindCategoryId("MYSTIC");
			StaticMysticTagCount = ResolveStaticMysticCount(MysticCategoryId, ComboCategories, Items);
			MysticTier1Threshold = Math.Max(1, mysticTier1Threshold);
			MysticTier2Threshold = Math.Max(MysticTier1Threshold + 1, mysticTier2Threshold);
			MysticSlot0 = (mysticSlot0 >= 0 && mysticSlot0 < UnlockedSlots) ? mysticSlot0 : -1;
			MysticSlot1 = (mysticSlot1 >= 0 && mysticSlot1 < UnlockedSlots) ? mysticSlot1 : -1;
			ExtraMysticTier1Slots = SanitizeExtraMysticSlots(
				extraMysticTier1Slots,
				UnlockedSlots,
				MysticSlot0,
				MysticSlot1
			);
			ExtraMysticTier2Slots = SanitizeExtraMysticSlots(
				extraMysticTier2Slots,
				UnlockedSlots,
				MysticSlot0,
				MysticSlot1
			);

			uint targetMask = 0u;
			for (int i = 0; i < ComboCategories.Length; i++) {
				int catId = ComboCategories[i].CategoryId;
				if (ComboCategories[i].IsUserTarget && catId >= 0 && catId < MaxSupportedCategories) {
					targetMask |= 1u << catId;
				}
			}
			UserTargetCategoryMask = targetMask;

			InitialSlotToItemIndex = new int[UnlockedSlots];
			Array.Fill(InitialSlotToItemIndex, -1);
			InitialItemRotations = new int[Items.Length];
			int depCount = 0;
			bool anyUserPriority = preferredNeedleTargetInstanceId >= 0
				|| preferredChalcedonyCategoryId >= 0
				|| targetMask != 0u;
			bool anyCompanionEmblem = false;
			bool anyUnplaced = false;

			for (int i = 0; i < Items.Length; i++) {
				SlotItemSnapshot item = Items[i];
				InitialItemRotations[i] = item.InitialRotation;
				if (item.InitialSlotIndex >= 0
					&& item.InitialSlotIndex < UnlockedSlots
					&& InitialSlotToItemIndex[item.InitialSlotIndex] < 0) {
					InitialSlotToItemIndex[item.InitialSlotIndex] = i;
				} else {
					anyUnplaced = true;
				}
				if (item.IsUserPriorityOverridden) {
					anyUserPriority = true;
				}
				if (item.IsCompanionEmblemArtifact) {
					anyCompanionEmblem = true;
				}
				if (item.Kind == SlotItemKind.Artifact
					&& item.HasDependencyTarget
					&& !item.IsDisabledByWeaponOrDual
					&& item.PriorityTier != ArtifactPriorityTier.Sacrifice) {
					depCount++;
				}
			}
			DependencyArtifactCount = depCount;
			HasAnyUserPriority = anyUserPriority;
			HasAnyCompanionEmblem = anyCompanionEmblem;
			HasUnplacedCandidates = anyUnplaced;
			ConsiderCompanionEmblemArtifacts = considerCompanionEmblemArtifacts;
			CurrentBreakdown = DpsEstimator.CalculateCurrentBreakdown(this);
		}

		public InventorySnapshot WithUserSettings(
			OptimizerUserSettings settings,
			IReadOnlyCollection<int>? pinnedSlotIndices = null
		) {
			if (settings == null) {
				throw new ArgumentNullException(nameof(settings));
			}

			SlotItemSnapshot[] updatedItems = new SlotItemSnapshot[Items.Length];
			for (int i = 0; i < Items.Length; i++) {
				SlotItemSnapshot src = Items[i];
				bool hasExplicit = settings.HasExplicitPriority(src.InstanceId);
				ArtifactPriorityTier nextTier = hasExplicit
					? settings.GetPriority(src.InstanceId, src.PriorityTier)
					: src.PriorityTier;
				bool nextOverridden = hasExplicit;

				if (!hasExplicit
					&& settings.PreferredNeedleTargetInstanceId == src.InstanceId
					&& nextTier < ArtifactPriorityTier.Core) {
					nextTier = ArtifactPriorityTier.Core;
					nextOverridden = true;
				}

				updatedItems[i] = src.WithPriorityOverride(nextTier, nextOverridden);
			}

			ComboCategoryInfo[] updatedCombos = new ComboCategoryInfo[ComboCategories.Length];
			for (int i = 0; i < ComboCategories.Length; i++) {
				ComboCategoryInfo srcCombo = ComboCategories[i];
				bool isUserTarget = settings.TargetComboCategories.Contains(srcCombo.CategoryKey);
				updatedCombos[i] = new ComboCategoryInfo(
					srcCombo.CategoryId,
					srcCombo.CategoryKey,
					srcCombo.DisplayName,
					srcCombo.StaticCount,
					srcCombo.Breakpoints,
					isUserTarget,
					srcCombo.BreakpointStatBonuses,
					srcCombo.BreakpointStatDeltas
				);
			}

			int preferredChalcedonyId = -1;
			if (!string.IsNullOrEmpty(settings.PreferredChalcedonyCategory)) {
				preferredChalcedonyId = FindCategoryId(settings.PreferredChalcedonyCategory);
			}

			bool[] nextPinned = new bool[UnlockedSlots];
			if (pinnedSlotIndices != null) {
				foreach (int slotIdx in pinnedSlotIndices) {
					if (slotIdx >= 0 && slotIdx < UnlockedSlots) {
						nextPinned[slotIdx] = true;
					}
				}
			} else {
				Array.Copy(PinnedSlots, nextPinned, UnlockedSlots);
			}

			return new InventorySnapshot(
				Width,
				Height,
				UnlockedSlots,
				BaseSlotLevel,
				BaseSlotDisable,
				BaseSlotIgnoreCriteria,
				BaseSlotMultiplyLevel,
				nextPinned,
				IsFullHpSatisfied,
				GlobalActiveValue,
				updatedItems,
				updatedCombos,
				TalentRules,
				settings.PreferredNeedleTargetInstanceId,
				preferredChalcedonyId,
				MooreNeighborMasks,
				MysticSlot0,
				MysticSlot1,
				MysticTier1Threshold,
				MysticTier2Threshold,
				ExtraMysticTier2Slots,
				CombatBaseline,
				ExtraMysticTier1Slots,
				settings.ConsiderCompanionEmblemArtifacts
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsExcludedCompanionEmblem(SlotItemSnapshot item) {
			return !ConsiderCompanionEmblemArtifacts
				&& item.Kind == SlotItemKind.Artifact
				&& item.IsCompanionEmblemArtifact
				&& !item.IsUserPriorityOverridden;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte GetCategoryElementalKind(int categoryId) {
			if ((uint)categoryId < MaxSupportedCategories) {
				return categoryElementalKinds[categoryId];
			}
			return 0;
		}

		public static ulong[] ComputeMooreNeighborMasks(int width, int height, int unlockedSlots) {
			int safeWidth = Math.Max(1, width);
			int safeUnlocked = Math.Clamp(unlockedSlots, 0, MaxSupportedSlots);
			int minReqHeight = safeUnlocked > 0 ? ((safeUnlocked + safeWidth - 1) / safeWidth) : 1;
			int safeHeight = Math.Max(Math.Max(1, height), minReqHeight);

			ulong[] masks = new ulong[safeUnlocked];
			for (int s = 0; s < safeUnlocked; s++) {
				int x = s % safeWidth;
				int y = s / safeWidth;
				ulong mask = 0UL;
				for (int dy = -1; dy <= 1; dy++) {
					int ny = y + dy;
					if (ny < 0 || ny >= safeHeight) {
						continue;
					}
					for (int dx = -1; dx <= 1; dx++) {
						if (dx == 0 && dy == 0) {
							continue;
						}
						int nx = x + dx;
						if (nx < 0 || nx >= safeWidth) {
							continue;
						}
						int nSlot = (ny * safeWidth) + nx;
						if (nSlot >= 0 && nSlot < safeUnlocked) {
							mask |= 1UL << nSlot;
						}
					}
				}
				masks[s] = mask;
			}
			return masks;
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

		private static int ResolveStaticMysticCount(
			int mysticCategoryId,
			ComboCategoryInfo[] comboCategories,
			SlotItemSnapshot[] items
		) {
			if (mysticCategoryId >= 0) {
				for (int i = 0; i < comboCategories.Length; i++) {
					if (comboCategories[i].CategoryId == mysticCategoryId) {
						return comboCategories[i].StaticCount;
					}
				}
			}
			int count = 0;
			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot item = items[i];
				if (item.Kind != SlotItemKind.Artifact) {
					continue;
				}
				if (item.IsElementalByRowCharm || item.HasDependencyTarget || item.IsWhitePaperCharm) {
					continue;
				}
				string[] cats = item.Categories;
				for (int c = 0; c < cats.Length; c++) {
					if (string.Equals(cats[c], "MYSTIC", StringComparison.OrdinalIgnoreCase)) {
						count++;
						break;
					}
				}
			}
			return count;
		}

		private static int[] SanitizeExtraMysticSlots(
			int[]? rawSlots,
			int unlockedSlots,
			int mysticSlot0,
			int mysticSlot1
		) {
			if (rawSlots == null || rawSlots.Length == 0) {
				return Array.Empty<int>();
			}
			int validCount = 0;
			for (int i = 0; i < rawSlots.Length; i++) {
				int slot = rawSlots[i];
				if (slot >= 0 && slot < unlockedSlots && slot != mysticSlot0 && slot != mysticSlot1) {
					validCount++;
				}
			}
			if (validCount == 0) {
				return Array.Empty<int>();
			}
			int[] cleaned = new int[validCount];
			int writeIdx = 0;
			for (int i = 0; i < rawSlots.Length; i++) {
				int slot = rawSlots[i];
				if (slot < 0 || slot >= unlockedSlots || slot == mysticSlot0 || slot == mysticSlot1) {
					continue;
				}
				bool duplicate = false;
				for (int j = 0; j < writeIdx; j++) {
					if (cleaned[j] == slot) {
						duplicate = true;
						break;
					}
				}
				if (!duplicate) {
					cleaned[writeIdx++] = slot;
				}
			}
			if (writeIdx == cleaned.Length) {
				return cleaned;
			}
			int[] trimmed = new int[writeIdx];
			Array.Copy(cleaned, trimmed, writeIdx);
			return trimmed;
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
