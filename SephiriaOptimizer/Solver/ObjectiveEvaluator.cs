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
		public const int ComboBreakpointReachedBonus = 240;
		public const int TargetComboDynamicStackBonus = 320;
		public const int DefaultComboDynamicStackBonus = 90;
		public const int WastedNeedlePenalty = 350;
		public const int EngravingOutOfBoundsBonus = 55;
		public const int ArrangementPatternMatchBonus = 280;

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
			Span<int> effectiveLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> enabledBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				levelBuf[s] = snapshot.BaseSlotLevel[s];
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
				effectiveLevelBuf[s] = 0;
				enabledBuf[s] = 0;
				categoryMaskBuf[s] = 0u;
			}

			SlotItemSnapshot[] items = snapshot.Items;
			TalentRuleSnapshot talentRules = snapshot.TalentRules;
			bool hasEngravingTalent = talentRules.TabletEngravingCount > 0;
			int neutralizationScore = 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact) {
					levelBuf[s] += item.BaseLevel;
					categoryMaskBuf[s] = item.StaticCategoryMask;
				} else if (item.Kind == SlotItemKind.StoneTablet) {
					int rotation = itemRotations[itemIdx] & 3;
					TabletPlacementRule rule = item.GetPlacementRule(s, rotation);
					if (!IsTabletConditionMet(snapshot, slotToItemIndex, s, rule)) {
						continue;
					}
					PrecomputedEffect[] effects = rule.Effects;
					int outOfBoundsPenalties = 0;
					for (int e = 0; e < effects.Length; e++) {
						PrecomputedEffect eff = effects[e];
						int targetSlot = eff.TargetSlotIndex;
						switch (eff.EffectType) {
							case TabletEffectType.IncreaseConstLevel: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									levelBuf[targetSlot] += eff.LevelParam;
									bool absorbsPenalty = eff.LevelParam < 0
										&& IsPenaltyAbsorbingSlot(items, slotToItemIndex[targetSlot]);
									if (absorbsPenalty) {
										neutralizationScore += (-eff.LevelParam) * NeutralizedNegativeLevelBonus;
									}
								} else if (eff.LevelParam < 0) {
									neutralizationScore += (-eff.LevelParam) * NeutralizedNegativeLevelBonus;
									outOfBoundsPenalties++;
								}
								break;
							}
							case TabletEffectType.Disable: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									disableBuf[targetSlot]++;
									if (IsPenaltyAbsorbingSlot(items, slotToItemIndex[targetSlot])) {
										neutralizationScore += NeutralizedDisableBonus;
									}
								} else {
									neutralizationScore += NeutralizedDisableBonus;
									outOfBoundsPenalties++;
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
					if (hasEngravingTalent && outOfBoundsPenalties > 0) {
						neutralizationScore += outOfBoundsPenalties * EngravingOutOfBoundsBonus;
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
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool criteriaSatisfied = criteriaIgnored || naturalCriteriaMet;
				bool isDisabled = !globalEnabled
					|| item.IsDisabledByWeaponOrDual
					|| disableBuf[s] > 0
					|| displayedLevel <= -1
					|| !criteriaSatisfied;

				if (isDisabled) {
					if (item.PriorityTier != ArtifactPriorityTier.Sacrifice && !item.IsDisabledByWeaponOrDual) {
						int priorityWeight = GetPriorityScaledWeight(item);
						int penalty = DeactivationBasePenalty * priorityWeight;
						if (displayedLevel <= -1) {
							penalty += (-displayedLevel) * NegativeLevelExtraPenalty * priorityWeight;
						}
						totalScore -= penalty;
					}
					continue;
				}

				enabledBuf[s] = 1;
				int effectiveLevel = Math.Min(item.MaxLevel, Math.Max(0, displayedLevel));
				effectiveLevelBuf[s] = effectiveLevel;

				if (item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
					totalScore += BaseActiveArtifactScore / 2;
					continue;
				}

				int scaledWeight = GetPriorityScaledWeight(item);
				int levelScore = item.MaxLevel > 0 ? (effectiveLevel * EffectiveLevelScoreMultiplier) : 0;
				int artifactScore = (BaseActiveArtifactScore + levelScore) * scaledWeight;

				if (item.MaxLevel > 0 && effectiveLevel == item.MaxLevel) {
					int maxBonus = item.PriorityTier == ArtifactPriorityTier.Core
						? (MaxLevelReachedBonus * 2)
						: MaxLevelReachedBonus;
					artifactScore += maxBonus * scaledWeight;
				}

				if (item.ConstraintType != ArtifactConstraintType.None && criteriaIgnored) {
					int ignoreBonus = naturalCriteriaMet
						? (IgnoreConstraintSynergyBonus / 2)
						: IgnoreConstraintSynergyBonus;
					artifactScore += ignoreBonus * scaledWeight;
				}

				totalScore += artifactScore;
			}

			Span<int> dynamicComboBonusCounts = stackalloc int[InventorySnapshot.MaxSupportedCategories];
			for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
				dynamicComboBonusCounts[c] = 0;
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				bool isElemRow = item.Kind == SlotItemKind.Artifact
					&& item.IsElementalByRowCharm
					&& item.RowCategoryIds.Length > 0;
				if (isElemRow) {
					int rowIdx = snapshot.Slots[s].Y % item.RowCategoryIds.Length;
					int assignedCatId = item.RowCategoryIds[rowIdx];
					if (assignedCatId >= 0 && assignedCatId < InventorySnapshot.MaxSupportedCategories) {
						categoryMaskBuf[s] |= 1u << assignedCatId;
						dynamicComboBonusCounts[assignedCatId]++;
						if (enabledBuf[s] != 0) {
							int elemBonus = item.GetRowElementalBonus(effectiveLevelBuf[s]);
							int statAffinity = GetElementalCategoryAffinity(snapshot, assignedCatId);
							totalScore += elemBonus * (25 + statAffinity);
						}
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact && item.HasDependencyTarget) {
					int rootSlot = ResolveNeedleRootSlot(snapshot, slotToItemIndex, s);
					if (rootSlot >= 0) {
						int rootItemIdx = slotToItemIndex[rootSlot];
						SlotItemSnapshot rootItem = items[rootItemIdx];
						uint rootCategories = categoryMaskBuf[rootSlot];
						categoryMaskBuf[s] |= rootCategories;
						AccumulateMaskIntoComboCounts(rootCategories, dynamicComboBonusCounts);

						if (enabledBuf[s] != 0 && enabledBuf[rootSlot] != 0) {
							int needleLevel = effectiveLevelBuf[s];
							int rootLevel = effectiveLevelBuf[rootSlot];
							int ampPercent = item.GetNeedleBonusPercent(needleLevel, rootItem.RarityTier);
							int rootWeight = GetPriorityScaledWeight(rootItem);
							int rootPower = rootWeight * (2 + rootLevel);
							if (snapshot.PreferredNeedleTargetInstanceId == rootItem.InstanceId) {
								rootPower = (rootPower * 5) / 2;
							} else if (rootItem.PriorityTier == ArtifactPriorityTier.Core) {
								rootPower = (rootPower * 9) / 5;
							}
							totalScore += ampPercent * rootPower;
						}
					} else {
						totalScore -= WastedNeedlePenalty;
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact && item.IsWhitePaperCharm) {
					SlotCoord coord = snapshot.Slots[s];
					int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
					int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
					uint leftMask = (leftSlot >= 0 && slotToItemIndex[leftSlot] >= 0)
						? categoryMaskBuf[leftSlot]
						: 0u;
					uint rightMask = (rightSlot >= 0 && slotToItemIndex[rightSlot] >= 0)
						? categoryMaskBuf[rightSlot]
						: 0u;

					uint copiedMask = item.WhitePaperMatchCount <= 1
						? (leftMask | rightMask)
						: (leftMask & rightMask);
					if (copiedMask != 0u) {
						categoryMaskBuf[s] |= copiedMask;
						AccumulateMaskIntoComboCounts(copiedMask, dynamicComboBonusCounts);
					}
				}
			}

			totalScore += EvaluateComboCategories(snapshot, dynamicComboBonusCounts);

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || enabledBuf[s] == 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact) {
					int posSynergy = EvaluatePositionSynergy(
						snapshot,
						slotToItemIndex,
						enabledBuf,
						effectiveLevelBuf,
						s,
						item
					);
					totalScore += posSynergy * GetPriorityScaledWeight(item);
				}
			}

			if (talentRules.ArrangementBonusStat > 0 && talentRules.ArrangementPatterns.Length > 0) {
				totalScore += EvaluateArrangementPatterns(snapshot, slotToItemIndex);
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
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];
			Span<int> connectedTargetBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> engravingReadyBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				levelBuf[s] = snapshot.BaseSlotLevel[s];
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
				categoryMaskBuf[s] = 0u;
				connectedTargetBuf[s] = -1;
				engravingReadyBuf[s] = 0;
			}

			SlotItemSnapshot[] items = snapshot.Items;
			bool hasEngravingTalent = snapshot.TalentRules.TabletEngravingCount > 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact) {
					levelBuf[s] += item.BaseLevel;
					categoryMaskBuf[s] = item.StaticCategoryMask;
				} else if (item.Kind == SlotItemKind.StoneTablet) {
					int rotation = itemRotations[itemIdx] & 3;
					TabletPlacementRule rule = item.GetPlacementRule(s, rotation);
					if (!IsTabletConditionMet(snapshot, slotToItemIndex, s, rule)) {
						continue;
					}
					if (hasEngravingTalent) {
						engravingReadyBuf[s] = 1;
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

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx >= 0 && items[itemIdx].Kind == SlotItemKind.Artifact) {
					SlotItemSnapshot item = items[itemIdx];
					if (item.IsElementalByRowCharm && item.RowCategoryIds.Length > 0) {
						int rowIdx = snapshot.Slots[s].Y % item.RowCategoryIds.Length;
						int catId = item.RowCategoryIds[rowIdx];
						if (catId >= 0 && catId < InventorySnapshot.MaxSupportedCategories) {
							categoryMaskBuf[s] |= 1u << catId;
						}
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				bool hasDep = itemIdx >= 0
					&& items[itemIdx].Kind == SlotItemKind.Artifact
					&& items[itemIdx].HasDependencyTarget;
				if (hasDep) {
					int rootSlot = ResolveNeedleRootSlot(snapshot, slotToItemIndex, s);
					connectedTargetBuf[s] = rootSlot;
					if (rootSlot >= 0) {
						categoryMaskBuf[s] |= categoryMaskBuf[rootSlot];
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx >= 0 && items[itemIdx].Kind == SlotItemKind.Artifact && items[itemIdx].IsWhitePaperCharm) {
					SlotCoord coord = snapshot.Slots[s];
					int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
					int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
					uint leftMask = (leftSlot >= 0 && slotToItemIndex[leftSlot] >= 0)
						? categoryMaskBuf[leftSlot]
						: 0u;
					uint rightMask = (rightSlot >= 0 && slotToItemIndex[rightSlot] >= 0)
						? categoryMaskBuf[rightSlot]
						: 0u;
					uint copiedMask = items[itemIdx].WhitePaperMatchCount <= 1
						? (leftMask | rightMask)
						: (leftMask & rightMask);
					categoryMaskBuf[s] |= copiedMask;
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
						ignoreBuf[s] > 0,
						0u,
						-1,
						engravingReadyBuf[s] != 0
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
					criteriaIgnored,
					categoryMaskBuf[s],
					connectedTargetBuf[s],
					false
				);
			}

			return details;
		}

		public static int ResolveNeedleRootSlot(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			int startNeedleSlot
		) {
			SlotItemSnapshot[] items = snapshot.Items;
			int cursorSlot = startNeedleSlot;
			int hopCount = 0;

			while (hopCount < 8) {
				hopCount++;
				int cursorItemIdx = slotToItemIndex[cursorSlot];
				if (cursorItemIdx < 0) {
					return -1;
				}
				SlotItemSnapshot cursorItem = items[cursorItemIdx];
				if (!cursorItem.HasDependencyTarget) {
					return cursorItem.IsAttackableCharm ? cursorSlot : -1;
				}

				SlotCoord coord = snapshot.Slots[cursorSlot];
				int nextSlot = snapshot.CoordToSlotIndex(
					coord.X + cursorItem.DependencyOffsetX,
					coord.Y + cursorItem.DependencyOffsetY
				);
				if (nextSlot < 0 || nextSlot == startNeedleSlot || nextSlot == cursorSlot) {
					return -1;
				}
				cursorSlot = nextSlot;
			}

			return -1;
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

		public static int GetPriorityScaledWeight(SlotItemSnapshot item) {
			return item.PriorityTier switch {
				ArtifactPriorityTier.Core => item.CustomWeight * 3,
				ArtifactPriorityTier.High => (item.CustomWeight * 9) / 5,
				ArtifactPriorityTier.Low => Math.Max(1, item.CustomWeight / 3),
				ArtifactPriorityTier.Sacrifice => 0,
				_ => item.CustomWeight
			};
		}

		private static bool IsPenaltyAbsorbingSlot(SlotItemSnapshot[] items, int targetItemIdx) {
			if (targetItemIdx < 0) {
				return true;
			}
			SlotItemSnapshot targetItem = items[targetItemIdx];
			if (targetItem.Kind != SlotItemKind.Artifact) {
				return true;
			}
			return targetItem.PriorityTier == ArtifactPriorityTier.Sacrifice
				|| targetItem.IsDisabledByWeaponOrDual;
		}

		private static void AccumulateMaskIntoComboCounts(uint mask, Span<int> dynamicComboBonusCounts) {
			uint remaining = mask;
			int bitIndex = 0;
			while (remaining != 0u && bitIndex < InventorySnapshot.MaxSupportedCategories) {
				if ((remaining & 1u) != 0u) {
					dynamicComboBonusCounts[bitIndex]++;
				}
				remaining >>= 1;
				bitIndex++;
			}
		}

		private static int EvaluateComboCategories(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> dynamicComboBonusCounts
		) {
			ComboCategoryInfo[] categories = snapshot.ComboCategories;
			if (categories.Length == 0) {
				int fallbackScore = 0;
				for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
					fallbackScore += dynamicComboBonusCounts[c] * DefaultComboDynamicStackBonus;
				}
				return fallbackScore;
			}

			TalentRuleSnapshot talent = snapshot.TalentRules;
			int comboScore = 0;
			int activeComboTypes = 0;

			for (int i = 0; i < categories.Length; i++) {
				ComboCategoryInfo info = categories[i];
				int catId = info.CategoryId;
				int dynAdded = (catId >= 0 && catId < InventorySnapshot.MaxSupportedCategories)
					? dynamicComboBonusCounts[catId]
					: 0;
				int totalCount = info.StaticCount + dynAdded;

				if (dynAdded > 0) {
					int perStack = info.IsUserTarget
						? TargetComboDynamicStackBonus
						: DefaultComboDynamicStackBonus;
					comboScore += dynAdded * perStack;
				}

				int[] breakpoints = info.Breakpoints;
				bool anyTierReached = false;
				for (int b = 0; b < breakpoints.Length; b++) {
					if (totalCount >= breakpoints[b]) {
						anyTierReached = true;
						int tierBonus = info.IsUserTarget
							? ((ComboBreakpointReachedBonus * 5) / 2)
							: ComboBreakpointReachedBonus;
						comboScore += tierBonus;
					}
				}

				if (anyTierReached) {
					activeComboTypes++;
				}

				if (talent.UnlimitedComboStat > 0
					&& info.HighestBreakpoint > 0
					&& totalCount > info.HighestBreakpoint) {
					int surplus = totalCount - info.HighestBreakpoint;
					comboScore += surplus * talent.UnlimitedComboStat * 45;
				}
			}

			if (talent.ComboBonusDamageStat > 0 && activeComboTypes > 0) {
				comboScore += activeComboTypes * talent.ComboBonusDamageStat * 35;
			}

			return comboScore;
		}

		private static int GetElementalCategoryAffinity(InventorySnapshot snapshot, int categoryId) {
			ComboCategoryInfo[] categories = snapshot.ComboCategories;
			if (categoryId < 0 || categoryId >= categories.Length) {
				return 0;
			}
			ComboCategoryInfo info = categories[categoryId];
			int affinity = info.IsUserTarget ? 45 : 0;
			TalentRuleSnapshot talent = snapshot.TalentRules;

			if (string.Equals(info.CategoryKey, "EMBER", StringComparison.OrdinalIgnoreCase)) {
				affinity += Math.Clamp(talent.FireDamageStat / 4, 0, 35);
			} else if (string.Equals(info.CategoryKey, "GLACIER", StringComparison.OrdinalIgnoreCase)) {
				affinity += Math.Clamp(talent.IceDamageStat / 4, 0, 35);
			} else if (string.Equals(info.CategoryKey, "MAGITECH", StringComparison.OrdinalIgnoreCase)) {
				affinity += Math.Clamp(talent.LightningDamageStat / 4, 0, 35);
			} else if (string.Equals(info.CategoryKey, "STURDY", StringComparison.OrdinalIgnoreCase)) {
				affinity += Math.Clamp(talent.PhysicalDamageStat / 4, 0, 35);
			}
			return affinity;
		}

		private static int EvaluatePositionSynergy(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<byte> enabledBuf,
			ReadOnlySpan<int> effectiveLevelBuf,
			int slotIndex,
			SlotItemSnapshot item
		) {
			int bonus = 0;
			SlotCoord coord = snapshot.Slots[slotIndex];
			SlotItemSnapshot[] items = snapshot.Items;
			int selfLevel = effectiveLevelBuf[slotIndex];

			if (item.WantsRightSideMagicBook) {
				int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
				if (rightSlot >= 0 && enabledBuf[rightSlot] != 0) {
					int rightItem = slotToItemIndex[rightSlot];
					if (rightItem >= 0 && items[rightItem].IsMagicBook) {
						bonus += ConnectedCharmSynergyBonus;
					}
				}
			}

			if (item.IsNearLevelDamageCharm) {
				int neighborLevelSum = 0;
				for (int dy = -1; dy <= 1; dy++) {
					for (int dx = -1; dx <= 1; dx++) {
						if (dx == 0 && dy == 0) {
							continue;
						}
						int nSlot = snapshot.CoordToSlotIndex(coord.X + dx, coord.Y + dy);
						if (nSlot >= 0 && enabledBuf[nSlot] != 0) {
							int nItem = slotToItemIndex[nSlot];
							if (nItem >= 0 && items[nItem].Kind == SlotItemKind.Artifact) {
								neighborLevelSum += effectiveLevelBuf[nSlot];
							}
						}
					}
				}
				bonus += neighborLevelSum * (12 + (selfLevel * 8));
			}

			if (item.IsCompanionChaosCharm) {
				for (int x = 0; x < snapshot.Width; x++) {
					if (x == coord.X) {
						continue;
					}
					int rowSlot = snapshot.CoordToSlotIndex(x, coord.Y);
					if (rowSlot >= 0 && enabledBuf[rowSlot] != 0) {
						int rItem = slotToItemIndex[rowSlot];
						if (rItem >= 0 && items[rItem].IsCompanionCharm) {
							bonus += 65;
						}
					}
				}
			}

			if (item.IsPlanetModuleCharm) {
				for (int dy = -1; dy <= 1; dy++) {
					for (int dx = -1; dx <= 1; dx++) {
						if (dx == 0 && dy == 0) {
							continue;
						}
						int nSlot = snapshot.CoordToSlotIndex(coord.X + dx, coord.Y + dy);
						if (nSlot >= 0 && enabledBuf[nSlot] != 0) {
							int nItem = slotToItemIndex[nSlot];
							if (nItem >= 0 && items[nItem].IsPlanetCharm) {
								bonus += 70;
							}
						}
					}
				}
			}

			if (item.IsNearMagicBulletCharm) {
				for (int y = 0; y < snapshot.Height; y++) {
					if (y == coord.Y) {
						continue;
					}
					int colSlot = snapshot.CoordToSlotIndex(coord.X, y);
					if (colSlot >= 0 && enabledBuf[colSlot] != 0) {
						int cItem = slotToItemIndex[colSlot];
						if (cItem >= 0 && items[cItem].IsMagicBook) {
							bonus += 55;
						}
					}
				}
			}

			if (item.IsWoodenBoxCharm) {
				int topRowLimit = Math.Min(6, snapshot.UnlockedSlots);
				int topCharmCount = 0;
				for (int topSlot = 0; topSlot < topRowLimit; topSlot++) {
					int topItem = slotToItemIndex[topSlot];
					if (topItem >= 0 && items[topItem].Kind == SlotItemKind.Artifact) {
						topCharmCount++;
					}
				}
				bonus += topCharmCount * (15 + (selfLevel * 10));
			}

			if (item.IsFireIcePositionCharm) {
				bool isLeftFireSide = coord.X <= 2;
				int firePref = snapshot.TalentRules.FireDamageStat;
				int icePref = snapshot.TalentRules.IceDamageStat;
				int emberId = snapshot.FindCategoryId("EMBER");
				int glacierId = snapshot.FindCategoryId("GLACIER");
				if (emberId >= 0 && snapshot.ComboCategories[emberId].IsUserTarget) {
					firePref += 100;
				}
				if (glacierId >= 0 && snapshot.ComboCategories[glacierId].IsUserTarget) {
					icePref += 100;
				}
				if (isLeftFireSide && firePref >= icePref) {
					bonus += 45;
				} else if (!isLeftFireSide && icePref >= firePref) {
					bonus += 45;
				}
			}

			return bonus;
		}

		private static int EvaluateArrangementPatterns(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex
		) {
			ArrangementPattern[] patterns = snapshot.TalentRules.ArrangementPatterns;
			SlotItemSnapshot[] items = snapshot.Items;
			int unlockedSlots = snapshot.UnlockedSlots;
			int patternBonus = 0;

			for (int p = 0; p < patterns.Length; p++) {
				ArrangementOffsetEntry[] entries = patterns[p].Entries;
				if (entries.Length == 0) {
					continue;
				}
				ArrangementOffsetEntry anchorEntry = entries[0];
				for (int s = 0; s < unlockedSlots; s++) {
					int itemIdx = slotToItemIndex[s];
					if (itemIdx < 0 || items[itemIdx].EntityId != anchorEntry.RequiredEntityId) {
						continue;
					}
					SlotCoord anchorCoord = snapshot.Slots[s];
					int originX = anchorCoord.X - anchorEntry.OffsetX;
					int originY = anchorCoord.Y - anchorEntry.OffsetY;
					bool allMatched = true;

					for (int k = 1; k < entries.Length; k++) {
						ArrangementOffsetEntry req = entries[k];
						int targetSlot = snapshot.CoordToSlotIndex(originX + req.OffsetX, originY + req.OffsetY);
						if (targetSlot < 0) {
							allMatched = false;
							break;
						}
						int targetItemIdx = slotToItemIndex[targetSlot];
						if (targetItemIdx < 0 || items[targetItemIdx].EntityId != req.RequiredEntityId) {
							allMatched = false;
							break;
						}
					}

					if (allMatched) {
						patternBonus += ArrangementPatternMatchBonus;
						break;
					}
				}
			}

			return patternBonus;
		}
	}
}
