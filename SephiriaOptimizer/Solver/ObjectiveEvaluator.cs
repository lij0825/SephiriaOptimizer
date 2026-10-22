using System;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Solver {
	public static class ObjectiveEvaluator {
		public const int BaseActiveArtifactScore = 120;
		public const int EffectiveLevelScoreMultiplier = 100;
		public const int MaxLevelReachedBonus = 45;
		public const int DeactivationBasePenalty = 450;
		public const int NegativeLevelExtraPenalty = 80;
		public const int NeutralizedNegativeLevelBonus = 35;
		public const int NeutralizedDisableBonus = 60;
		public const int IgnoreConstraintSynergyBonus = 95;
		public const int ConnectedCharmSynergyBonus = 85;

		public static int Evaluate(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			if (unlockedSlots <= 0) {
				return 0;
			}

			Span<int> levelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				levelBuf[s] = snapshot.BaseSlotLevel[s];
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
			}

			SlotItemSnapshot[] items = snapshot.Items;
			int neutralizationScore = 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact) {
					levelBuf[s] += item.BaseLevel;
				} else if (item.Kind == SlotItemKind.StoneTablet) {
					int rotation = itemRotations[itemIdx] & 3;
					TabletPlacementRule rule = item.GetPlacementRule(s, rotation);
					if (!IsTabletConditionMet(snapshot, slotToItemIndex, s, rule)) {
						continue;
					}
					PrecomputedEffect[] effects = rule.Effects;
					for (int e = 0; e < effects.Length; e++) {
						PrecomputedEffect eff = effects[e];
						int targetSlot = eff.TargetSlotIndex;
						switch (eff.EffectType) {
							case TabletEffectType.IncreaseConstLevel: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									levelBuf[targetSlot] += eff.LevelParam;
									if (eff.LevelParam < 0) {
										int targetItemIdx = slotToItemIndex[targetSlot];
										if (targetItemIdx < 0 || items[targetItemIdx].Kind != SlotItemKind.Artifact) {
											neutralizationScore += (-eff.LevelParam) * NeutralizedNegativeLevelBonus;
										}
									}
								} else if (eff.LevelParam < 0) {
									neutralizationScore += (-eff.LevelParam) * NeutralizedNegativeLevelBonus;
								}
								break;
							}
							case TabletEffectType.Disable: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									disableBuf[targetSlot]++;
									int targetItemIdx = slotToItemIndex[targetSlot];
									if (targetItemIdx < 0 || items[targetItemIdx].Kind != SlotItemKind.Artifact) {
										neutralizationScore += NeutralizedDisableBonus;
									}
								} else {
									neutralizationScore += NeutralizedDisableBonus;
								}
								break;
							}
							case TabletEffectType.IgnoreCriteria: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									ignoreBuf[targetSlot]++;
								}
								break;
							}
							case TabletEffectType.MultiplyConstLevel: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									mulBuf[targetSlot] += eff.LevelParam;
								}
								break;
							}
						}
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (mulBuf[s] != 0) {
					levelBuf[s] *= mulBuf[s];
				}
			}

			int totalScore = neutralizationScore;
			bool globalEnabled = snapshot.GlobalActiveValue > 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind != SlotItemKind.Artifact) {
					continue;
				}

				int displayedLevel = levelBuf[s];
				int weight = item.CustomWeight;
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool criteriaSatisfied = criteriaIgnored || naturalCriteriaMet;
				bool isDisabled = !globalEnabled
					|| item.IsDisabledByWeaponOrDual
					|| disableBuf[s] > 0
					|| displayedLevel <= -1
					|| !criteriaSatisfied;

				if (isDisabled) {
					int penalty = DeactivationBasePenalty * weight;
					if (displayedLevel <= -1) {
						penalty += (-displayedLevel) * NegativeLevelExtraPenalty * weight;
					}
					totalScore -= penalty;
					continue;
				}

				int effectiveLevel = Math.Min(item.MaxLevel, Math.Max(0, displayedLevel));
				int baseAndLevel = BaseActiveArtifactScore + (effectiveLevel * EffectiveLevelScoreMultiplier);
				int artifactScore = baseAndLevel * weight;
				if (item.MaxLevel > 0 && effectiveLevel == item.MaxLevel) {
					artifactScore += MaxLevelReachedBonus * weight;
				}

				if (item.ConstraintType != ArtifactConstraintType.None && criteriaIgnored) {
					int ignoreBonus = naturalCriteriaMet
						? (IgnoreConstraintSynergyBonus / 2)
						: IgnoreConstraintSynergyBonus;
					artifactScore += ignoreBonus * weight;
				}

				artifactScore += EvaluatePositionSynergy(snapshot, slotToItemIndex, s, item) * weight;
				totalScore += artifactScore;
			}

			return totalScore;
		}

		public static SlotEvaluationDetail[] EvaluateDetailed(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotEvaluationDetail[] details = new SlotEvaluationDetail[unlockedSlots];
			if (unlockedSlots <= 0) {
				return details;
			}

			Span<int> levelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				levelBuf[s] = snapshot.BaseSlotLevel[s];
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
			}

			SlotItemSnapshot[] items = snapshot.Items;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact) {
					levelBuf[s] += item.BaseLevel;
				} else if (item.Kind == SlotItemKind.StoneTablet) {
					int rotation = itemRotations[itemIdx] & 3;
					TabletPlacementRule rule = item.GetPlacementRule(s, rotation);
					if (!IsTabletConditionMet(snapshot, slotToItemIndex, s, rule)) {
						continue;
					}
					PrecomputedEffect[] effects = rule.Effects;
					for (int e = 0; e < effects.Length; e++) {
						PrecomputedEffect eff = effects[e];
						int targetSlot = eff.TargetSlotIndex;
						if (targetSlot < 0 || targetSlot >= unlockedSlots) {
							continue;
						}
						switch (eff.EffectType) {
							case TabletEffectType.IncreaseConstLevel: {
								levelBuf[targetSlot] += eff.LevelParam;
								break;
							}
							case TabletEffectType.Disable: {
								disableBuf[targetSlot]++;
								break;
							}
							case TabletEffectType.IgnoreCriteria: {
								ignoreBuf[targetSlot]++;
								break;
							}
							case TabletEffectType.MultiplyConstLevel: {
								mulBuf[targetSlot] += eff.LevelParam;
								break;
							}
						}
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (mulBuf[s] != 0) {
					levelBuf[s] *= mulBuf[s];
				}
			}

			bool globalEnabled = snapshot.GlobalActiveValue > 0;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
					details[s] = new SlotEvaluationDetail(
						s,
						itemIdx,
						levelBuf[s],
						0,
						0,
						true,
						false,
						false,
						false,
						ignoreBuf[s] > 0
					);
					continue;
				}

				SlotItemSnapshot item = items[itemIdx];
				int displayedLevel = levelBuf[s];
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool negDeactivated = displayedLevel <= -1;
				bool disableDeactivated = disableBuf[s] > 0 || item.IsDisabledByWeaponOrDual || !globalEnabled;
				bool criteriaDeactivated = !criteriaIgnored && !naturalCriteriaMet;
				bool isEnabled = !negDeactivated && !disableDeactivated && !criteriaDeactivated;
				int effectiveLevel = isEnabled ? Math.Min(item.MaxLevel, Math.Max(0, displayedLevel)) : 0;

				details[s] = new SlotEvaluationDetail(
					s,
					itemIdx,
					displayedLevel,
					effectiveLevel,
					item.MaxLevel,
					isEnabled,
					negDeactivated,
					disableDeactivated,
					criteriaDeactivated,
					criteriaIgnored
				);
			}

			return details;
		}

		public static bool IsTabletConditionMet(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			int tabletSlotIndex,
			TabletPlacementRule rule
		) {
			PrecomputedCondition[] conditions = rule.Conditions;
			if (conditions.Length == 0) {
				return true;
			}

			SlotCoord origin = snapshot.Slots[tabletSlotIndex];
			SlotItemSnapshot[] items = snapshot.Items;
			bool allNonPlacedHold = true;
			bool anyPlacedMatched = false;

			for (int i = 0; i < conditions.Length; i++) {
				PrecomputedCondition cond = conditions[i];
				switch (cond.ConditionType) {
					case TabletConditionType.AnyItem: {
						if (cond.TargetSlotIndex < 0 || slotToItemIndex[cond.TargetSlotIndex] < 0) {
							allNonPlacedHold = false;
						}
						break;
					}
					case TabletConditionType.OnlyCharm: {
						if (cond.TargetSlotIndex < 0) {
							allNonPlacedHold = false;
						} else {
							int targetItemIdx = slotToItemIndex[cond.TargetSlotIndex];
							if (targetItemIdx < 0 || items[targetItemIdx].Kind != SlotItemKind.Artifact) {
								allNonPlacedHold = false;
							}
						}
						break;
					}
					case TabletConditionType.Placed: {
						if (origin.X == cond.TargetX && origin.Y == cond.TargetY) {
							anyPlacedMatched = true;
						}
						break;
					}
				}
				if (!allNonPlacedHold) {
					return false;
				}
			}

			if (rule.HasPlacedCondition && !anyPlacedMatched) {
				return false;
			}
			return true;
		}

		public static bool IsArtifactConstraintMet(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			int slotIndex,
			ArtifactConstraintType constraintType
		) {
			if (constraintType == ArtifactConstraintType.None) {
				return true;
			}

			SlotCoord coord = snapshot.Slots[slotIndex];
			int width = snapshot.Width;
			int height = snapshot.Height;
			int storage = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;

			switch (constraintType) {
				case ArtifactConstraintType.TopInInventory: {
					return coord.Y == 0;
				}
				case ArtifactConstraintType.BottomInInventory: {
					return slotIndex >= storage - 6;
				}
				case ArtifactConstraintType.SideEnd: {
					return coord.X == 0 || coord.X == 5;
				}
				case ArtifactConstraintType.Outlined: {
					return coord.X <= 0
						|| coord.Y <= 0
						|| coord.X >= width - 1
						|| slotIndex >= storage - 6;
				}
				case ArtifactConstraintType.Inside: {
					return coord.X > 0
						&& coord.Y > 0
						&& coord.X < width - 1
						&& (slotIndex + 7) <= (storage - 1);
				}
				case ArtifactConstraintType.BothSideCharm: {
					if (coord.X <= 0 || coord.X >= width - 1) {
						return false;
					}
					int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
					int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
					if (leftSlot < 0 || rightSlot < 0) {
						return false;
					}
					int leftItem = slotToItemIndex[leftSlot];
					int rightItem = slotToItemIndex[rightSlot];
					return leftItem >= 0
						&& rightItem >= 0
						&& items[leftItem].Kind == SlotItemKind.Artifact
						&& items[rightItem].Kind == SlotItemKind.Artifact;
				}
				case ArtifactConstraintType.BothSidesAreEmpty: {
					int rem = storage % width;
					if (coord.X <= 0 || coord.X >= width - 1) {
						return false;
					}
					if (rem != 0 && coord.Y >= height - 1 && coord.X >= rem - 1) {
						return false;
					}
					int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
					int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
					bool leftEmpty = leftSlot < 0 || slotToItemIndex[leftSlot] < 0;
					bool rightEmpty = rightSlot < 0 || slotToItemIndex[rightSlot] < 0;
					return leftEmpty && rightEmpty;
				}
				case ArtifactConstraintType.Near8MagicBook: {
					for (int dy = -1; dy <= 1; dy++) {
						for (int dx = -1; dx <= 1; dx++) {
							if (dx == 0 && dy == 0) {
								continue;
							}
							int neighborSlot = snapshot.CoordToSlotIndex(coord.X + dx, coord.Y + dy);
							if (neighborSlot >= 0) {
								int neighborItem = slotToItemIndex[neighborSlot];
								if (neighborItem >= 0 && items[neighborItem].IsMagicBook) {
									return true;
								}
							}
						}
					}
					return false;
				}
				case ArtifactConstraintType.NeighborsAreFull: {
					for (int dy = -1; dy <= 1; dy++) {
						for (int dx = -1; dx <= 1; dx++) {
							if (dx == 0 && dy == 0) {
								continue;
							}
							int neighborSlot = snapshot.CoordToSlotIndex(coord.X + dx, coord.Y + dy);
							if (neighborSlot < 0 || slotToItemIndex[neighborSlot] < 0) {
								return false;
							}
						}
					}
					return true;
				}
				case ArtifactConstraintType.FullHp: {
					return snapshot.IsFullHpSatisfied;
				}
				default: {
					return true;
				}
			}
		}

		private static int EvaluatePositionSynergy(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			int slotIndex,
			SlotItemSnapshot item
		) {
			int bonus = 0;
			SlotCoord coord = snapshot.Slots[slotIndex];
			SlotItemSnapshot[] items = snapshot.Items;

			if (item.WantsRightSideMagicBook) {
				int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
				if (rightSlot >= 0) {
					int rightItem = slotToItemIndex[rightSlot];
					if (rightItem >= 0 && items[rightItem].IsMagicBook) {
						bonus += ConnectedCharmSynergyBonus;
					}
				}
			}

			if (item.HasDependencyTarget) {
				int targetSlot = snapshot.CoordToSlotIndex(
					coord.X + item.DependencyOffsetX,
					coord.Y + item.DependencyOffsetY
				);
				if (targetSlot >= 0) {
					int targetItemIdx = slotToItemIndex[targetSlot];
					if (targetItemIdx >= 0) {
						SlotItemSnapshot targetItem = items[targetItemIdx];
						if (targetItem.IsAttackableCharm || targetItem.HasDependencyTarget) {
							bonus += ConnectedCharmSynergyBonus;
						}
					}
				}
			}

			if (item.IsWhitePaperCharm) {
				int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
				int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
				if (leftSlot >= 0 && rightSlot >= 0) {
					int leftItemIdx = slotToItemIndex[leftSlot];
					int rightItemIdx = slotToItemIndex[rightSlot];
					if (leftItemIdx >= 0 && rightItemIdx >= 0) {
						string[] leftCats = items[leftItemIdx].Categories;
						string[] rightCats = items[rightItemIdx].Categories;
						if (HasSharedCategory(leftCats, rightCats)) {
							bonus += ConnectedCharmSynergyBonus;
						}
					}
				}
			}

			return bonus;
		}

		private static bool HasSharedCategory(string[] firstCategories, string[] secondCategories) {
			for (int i = 0; i < firstCategories.Length; i++) {
				string first = firstCategories[i];
				for (int j = 0; j < secondCategories.Length; j++) {
					if (string.Equals(first, secondCategories[j], StringComparison.Ordinal)) {
						return true;
					}
				}
			}
			return false;
		}
	}
}
