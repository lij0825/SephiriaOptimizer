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
		public const int WastedPositiveOutOfBoundsPenalty = 280;
		public const int WastedPositiveOnEmptySlotPenalty = 80;
		public const int WastedMultiplierPenalty = 90;
		public const int TabletOnBuffedSlotPenalty = 340;
		public const int IgnoreConstraintSynergyBonus = 95;
		public const int ConnectedCharmSynergyBonus = 85;
		public const int ComboBreakpointReachedBonus = 240;
		public const int TargetComboDynamicStackBonus = 320;
		public const int DefaultComboDynamicStackBonus = 90;
		public const int ChalcedonyTargetMatchBonus = 420;
		public const int ChalcedonyTargetMismatchPenalty = 280;
		public const int WhitePaperCopiedBonus = 140;
		public const int WhitePaperWastedPenalty = 160;
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
			Span<int> tabletDeltaBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> positiveBuffBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> negativePenaltyBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> effectiveLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isActiveBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				int baseLvl = snapshot.BaseSlotLevel[s];
				int baseDis = snapshot.BaseSlotDisable[s];
				int baseIgn = snapshot.BaseSlotIgnoreCriteria[s];
				int baseMul = snapshot.BaseSlotMultiplyLevel[s];

				levelBuf[s] = baseLvl;
				tabletDeltaBuf[s] = baseLvl;
				disableBuf[s] = baseDis;
				ignoreBuf[s] = baseIgn;
				mulBuf[s] = baseMul;
				positiveBuffBuf[s] = Math.Max(0, baseLvl) + baseIgn;
				negativePenaltyBuf[s] = Math.Max(0, -baseLvl) + (baseDis * 2);
				effectiveLevelBuf[s] = 0;
				isActiveBuf[s] = 0;
				categoryMaskBuf[s] = 0u;
			}

			SlotItemSnapshot[] items = snapshot.Items;
			TalentRuleSnapshot talentRules = snapshot.TalentRules;
			bool hasEngravingTalent = talentRules.TabletEngravingCount > 0;
			int tabletPlacementScore = 0;

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
						tabletPlacementScore -= 180;
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
									tabletDeltaBuf[targetSlot] += eff.LevelParam;
									if (eff.LevelParam > 0) {
										positiveBuffBuf[targetSlot] += eff.LevelParam;
									} else if (eff.LevelParam < 0) {
										negativePenaltyBuf[targetSlot] += -eff.LevelParam;
									}
								} else if (eff.LevelParam > 0) {
									tabletPlacementScore -= eff.LevelParam * WastedPositiveOutOfBoundsPenalty;
								} else if (eff.LevelParam < 0) {
									tabletPlacementScore += (-eff.LevelParam) * NeutralizedNegativeLevelBonus;
									outOfBoundsPenalties++;
								}
								break;
							}
							case TabletEffectType.Disable: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									disableBuf[targetSlot]++;
									negativePenaltyBuf[targetSlot] += 2;
								} else {
									tabletPlacementScore += NeutralizedDisableBonus;
									outOfBoundsPenalties++;
								}
								break;
							}
							case TabletEffectType.IgnoreCriteria: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									ignoreBuf[targetSlot]++;
									positiveBuffBuf[targetSlot]++;
								} else {
									tabletPlacementScore -= 180;
								}
								break;
							}
							case TabletEffectType.MultiplyConstLevel: {
								if (targetSlot >= 0 && targetSlot < unlockedSlots) {
									mulBuf[targetSlot] += eff.LevelParam;
								} else if (eff.LevelParam > 1) {
									tabletPlacementScore -= eff.LevelParam * 220;
								}
								break;
							}
						}
					}
					if (hasEngravingTalent && outOfBoundsPenalties > 0) {
						tabletPlacementScore += outOfBoundsPenalties * EngravingOutOfBoundsBonus;
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (mulBuf[s] != 0) {
					int extraDelta = tabletDeltaBuf[s] * (mulBuf[s] - 1);
					levelBuf[s] *= mulBuf[s];
					if (extraDelta > 0) {
						positiveBuffBuf[s] += extraDelta;
					} else if (extraDelta < 0) {
						negativePenaltyBuf[s] += -extraDelta;
					} else if (mulBuf[s] > 1) {
						tabletPlacementScore -= (mulBuf[s] - 1) * WastedMultiplierPenalty;
					}
				}
			}

			int totalScore = tabletPlacementScore;
			bool globalEnabled = snapshot.GlobalActiveValue > 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				int posBuff = positiveBuffBuf[s];
				int negPen = negativePenaltyBuf[s];

				if (itemIdx < 0) {
					if (posBuff > 0) {
						int wastedPositive = Math.Max(levelBuf[s], posBuff);
						totalScore -= wastedPositive * WastedPositiveOnEmptySlotPenalty;
					} else if (posBuff == 0 && negPen > 0) {
						totalScore += negPen * NeutralizedNegativeLevelBonus;
					}
					continue;
				}

				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind != SlotItemKind.Artifact) {
					if (posBuff > 0) {
						totalScore -= posBuff * TabletOnBuffedSlotPenalty;
						if (levelBuf[s] > 0) {
							totalScore -= levelBuf[s] * 200;
						}
					} else if (negPen > 0) {
						totalScore += negPen * NeutralizedNegativeLevelBonus;
					}
					continue;
				}

				bool absorbsPenalty = item.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| item.IsDisabledByWeaponOrDual;
				if (absorbsPenalty) {
					if (posBuff > 0) {
						int wastedPositive = Math.Max(levelBuf[s], posBuff);
						totalScore -= wastedPositive * WastedPositiveOnEmptySlotPenalty;
					} else if (negPen > 0) {
						totalScore += negPen * NeutralizedNegativeLevelBonus;
					}
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
					categoryMaskBuf[s] = 0u;
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

				isActiveBuf[s] = 1;
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

				if (displayedLevel > item.MaxLevel) {
					int overflow = Math.Min(4, displayedLevel - item.MaxLevel);
					artifactScore += overflow * 8;
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
				if (itemIdx < 0 || isActiveBuf[s] == 0) {
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

						if (snapshot.PreferredChalcedonyCategoryId >= 0) {
							if (assignedCatId == snapshot.PreferredChalcedonyCategoryId) {
								totalScore += ChalcedonyTargetMatchBonus;
							} else {
								totalScore -= ChalcedonyTargetMismatchPenalty;
							}
						}

						int elemBonus = item.GetRowElementalBonus(effectiveLevelBuf[s]);
						int statAffinity = GetElementalCategoryAffinity(snapshot, assignedCatId);
						totalScore += elemBonus * (25 + statAffinity);
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
					if (isActiveBuf[s] == 0) {
						totalScore -= WastedNeedlePenalty;
						continue;
					}
					int rootSlot = ResolveNeedleRootSlot(snapshot, slotToItemIndex, isActiveBuf, s);
					if (rootSlot >= 0) {
						int rootItemIdx = slotToItemIndex[rootSlot];
						SlotItemSnapshot rootItem = items[rootItemIdx];
						uint rootCategories = categoryMaskBuf[rootSlot];
						categoryMaskBuf[s] |= rootCategories;
						AccumulateMaskIntoComboCounts(rootCategories, dynamicComboBonusCounts);

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
					if (isActiveBuf[s] == 0) {
						totalScore -= WhitePaperWastedPenalty;
						continue;
					}
					SlotCoord coord = snapshot.Slots[s];
					int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
					int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
					uint leftMask = (leftSlot >= 0 && slotToItemIndex[leftSlot] >= 0 && isActiveBuf[leftSlot] != 0)
						? categoryMaskBuf[leftSlot]
						: 0u;
					uint rightMask = (rightSlot >= 0 && slotToItemIndex[rightSlot] >= 0 && isActiveBuf[rightSlot] != 0)
						? categoryMaskBuf[rightSlot]
						: 0u;

					uint copiedMask = item.WhitePaperMatchCount <= 1
						? (leftMask | rightMask)
						: (leftMask & rightMask);
					if (copiedMask != 0u) {
						categoryMaskBuf[s] |= copiedMask;
						AccumulateMaskIntoComboCounts(copiedMask, dynamicComboBonusCounts);
						totalScore += WhitePaperCopiedBonus;
					} else {
						totalScore -= WhitePaperWastedPenalty;
					}
				}
			}

			totalScore += EvaluateComboCategories(snapshot, dynamicComboBonusCounts);

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || isActiveBuf[s] == 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact) {
					int posSynergy = EvaluatePositionSynergy(
						snapshot,
						slotToItemIndex,
						isActiveBuf,
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
			Span<byte> isActiveBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];
			Span<int> connectedTargetBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> engravingReadyBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				levelBuf[s] = snapshot.BaseSlotLevel[s];
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
				isActiveBuf[s] = 0;
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

			bool globalEnabled = snapshot.GlobalActiveValue > 0;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				int displayedLevel = levelBuf[s];
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool negDeactivated = displayedLevel <= -1;
				bool disableDeactivated = disableBuf[s] > 0 || item.IsDisabledByWeaponOrDual || !globalEnabled;
				bool criteriaDeactivated = !criteriaIgnored && !naturalCriteriaMet;
				if (!negDeactivated && !disableDeactivated && !criteriaDeactivated) {
					isActiveBuf[s] = 1;
				} else {
					categoryMaskBuf[s] = 0u;
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx >= 0 && isActiveBuf[s] != 0 && items[itemIdx].Kind == SlotItemKind.Artifact) {
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
					&& isActiveBuf[s] != 0
					&& items[itemIdx].Kind == SlotItemKind.Artifact
					&& items[itemIdx].HasDependencyTarget;
				if (hasDep) {
					int rootSlot = ResolveNeedleRootSlot(snapshot, slotToItemIndex, isActiveBuf, s);
					connectedTargetBuf[s] = rootSlot;
					if (rootSlot >= 0) {
						categoryMaskBuf[s] |= categoryMaskBuf[rootSlot];
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				bool isActiveWhitePaper = itemIdx >= 0
					&& isActiveBuf[s] != 0
					&& items[itemIdx].Kind == SlotItemKind.Artifact
					&& items[itemIdx].IsWhitePaperCharm;
				if (isActiveWhitePaper) {
					SlotCoord coord = snapshot.Slots[s];
					int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
					int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
					uint leftMask = (leftSlot >= 0 && slotToItemIndex[leftSlot] >= 0 && isActiveBuf[leftSlot] != 0)
						? categoryMaskBuf[leftSlot]
						: 0u;
					uint rightMask = (rightSlot >= 0 && slotToItemIndex[rightSlot] >= 0 && isActiveBuf[rightSlot] != 0)
						? categoryMaskBuf[rightSlot]
						: 0u;
					uint copiedMask = items[itemIdx].WhitePaperMatchCount <= 1
						? (leftMask | rightMask)
						: (leftMask & rightMask);
					categoryMaskBuf[s] |= copiedMask;
				}
			}

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
				bool isEnabled = isActiveBuf[s] != 0;
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
			if ((uint)startNeedleSlot >= (uint)snapshot.UnlockedSlots) {
				return -1;
			}
			SlotItemSnapshot[] items = snapshot.Items;
			ulong visitedMask = 1UL << startNeedleSlot;
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
				if (nextSlot < 0 || nextSlot >= InventorySnapshot.MaxSupportedSlots) {
					return -1;
				}
				ulong nextBit = 1UL << nextSlot;
				if ((visitedMask & nextBit) != 0UL) {
					return -1;
				}
				visitedMask |= nextBit;
				cursorSlot = nextSlot;
			}

			return -1;
		}

		public static int ResolveNeedleRootSlot(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<byte> isActiveBuf,
			int startNeedleSlot
		) {
			if ((uint)startNeedleSlot >= (uint)snapshot.UnlockedSlots || isActiveBuf[startNeedleSlot] == 0) {
				return -1;
			}
			SlotItemSnapshot[] items = snapshot.Items;
			ulong visitedMask = 1UL << startNeedleSlot;
			int cursorSlot = startNeedleSlot;
			int hopCount = 0;

			while (hopCount < 8) {
				hopCount++;
				if (isActiveBuf[cursorSlot] == 0) {
					return -1;
				}
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
				if (nextSlot < 0 || nextSlot >= InventorySnapshot.MaxSupportedSlots) {
					return -1;
				}
				ulong nextBit = 1UL << nextSlot;
				if ((visitedMask & nextBit) != 0UL) {
					return -1;
				}
				visitedMask |= nextBit;
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
						if (cond.TargetSlotIndex == tabletSlotIndex
							&& origin.X == cond.TargetX
							&& origin.Y == cond.TargetY) {
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
					return slotIndex >= storage - width;
				}
				case ArtifactConstraintType.SideEnd: {
					return coord.X == 0 || coord.X == width - 1;
				}
				case ArtifactConstraintType.Outlined: {
					return coord.X <= 0
						|| coord.Y <= 0
						|| coord.X >= width - 1
						|| slotIndex >= storage - width;
				}
				case ArtifactConstraintType.Inside: {
					return coord.X > 0
						&& coord.Y > 0
						&& coord.X < width - 1
						&& (slotIndex + width + 1) <= (storage - 1);
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
			SlotCoord coord = snapshot.Slots[slotIndex];
			SlotItemSnapshot[] items = snapshot.Items;
			int synergy = 0;

			if (item.WantsRightSideMagicBook) {
				int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
				if (rightSlot >= 0) {
					int rightItem = slotToItemIndex[rightSlot];
					if (rightItem >= 0 && items[rightItem].IsMagicBook) {
						synergy += ConnectedCharmSynergyBonus;
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
							neighborLevelSum += effectiveLevelBuf[nSlot];
						}
					}
				}
				synergy += neighborLevelSum * 30;
			}

			if (item.IsCompanionChaosCharm) {
				int sameRowCompanions = 0;
				for (int x = 0; x < snapshot.Width; x++) {
					if (x == coord.X) {
						continue;
					}
					int rSlot = snapshot.CoordToSlotIndex(x, coord.Y);
					if (rSlot >= 0) {
						int rItem = slotToItemIndex[rSlot];
						if (rItem >= 0 && items[rItem].IsCompanionCharm) {
							sameRowCompanions++;
						}
					}
				}
				synergy += sameRowCompanions * 55;
			}

			if (item.IsPlanetModuleCharm) {
				int nearPlanets = 0;
				for (int dy = -1; dy <= 1; dy++) {
					for (int dx = -1; dx <= 1; dx++) {
						if (dx == 0 && dy == 0) {
							continue;
						}
						int nSlot = snapshot.CoordToSlotIndex(coord.X + dx, coord.Y + dy);
						if (nSlot >= 0) {
							int nItem = slotToItemIndex[nSlot];
							if (nItem >= 0 && items[nItem].IsPlanetCharm) {
								nearPlanets++;
							}
						}
					}
				}
				synergy += nearPlanets * 65;
			}

			if (item.IsNearMagicBulletCharm) {
				int sameColBooks = 0;
				for (int y = 0; y < snapshot.Height; y++) {
					if (y == coord.Y) {
						continue;
					}
					int cSlot = snapshot.CoordToSlotIndex(coord.X, y);
					if (cSlot >= 0) {
						int cItem = slotToItemIndex[cSlot];
						if (cItem >= 0 && items[cItem].IsMagicBook) {
							sameColBooks++;
						}
					}
				}
				synergy += sameColBooks * 50;
			}

			if (item.IsWoodenBoxCharm) {
				int topCount = Math.Min(6, snapshot.UnlockedSlots);
				int topCharms = 0;
				for (int topSlot = 0; topSlot < topCount; topSlot++) {
					int tIdx = slotToItemIndex[topSlot];
					if (tIdx >= 0 && items[tIdx].Kind == SlotItemKind.Artifact) {
						topCharms++;
					}
				}
				synergy += topCharms * 25;
			}

			if (item.IsFireIcePositionCharm) {
				int fireStat = snapshot.TalentRules.FireDamageStat;
				int iceStat = snapshot.TalentRules.IceDamageStat;
				bool isLeftHalf = coord.X < (snapshot.Width / 2);
				if (fireStat > iceStat && isLeftHalf) {
					synergy += 60;
				} else if (iceStat > fireStat && !isLeftHalf) {
					synergy += 60;
				}
			}

			return synergy;
		}

		private static int EvaluateArrangementPatterns(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex
		) {
			ArrangementPattern[] patterns = snapshot.TalentRules.ArrangementPatterns;
			SlotItemSnapshot[] items = snapshot.Items;
			int unlockedSlots = snapshot.UnlockedSlots;
			int matchedCount = 0;

			for (int p = 0; p < patterns.Length; p++) {
				ArrangementOffsetEntry[] entries = patterns[p].Entries;
				if (entries.Length == 0) {
					continue;
				}
				int firstEntityId = entries[0].RequiredEntityId;
				bool patternMatched = false;

				for (int s = 0; s < unlockedSlots; s++) {
					int itemIdx = slotToItemIndex[s];
					if (itemIdx < 0 || items[itemIdx].EntityId != firstEntityId) {
						continue;
					}
					SlotCoord anchor = snapshot.Slots[s];
					bool allEntriesMatch = true;

					for (int e = 1; e < entries.Length; e++) {
						ArrangementOffsetEntry entry = entries[e];
						int targetSlot = snapshot.CoordToSlotIndex(
							anchor.X + entry.OffsetX,
							anchor.Y + entry.OffsetY
						);
						if (targetSlot < 0) {
							allEntriesMatch = false;
							break;
						}
						int targetItemIdx = slotToItemIndex[targetSlot];
						if (targetItemIdx < 0 || items[targetItemIdx].EntityId != entry.RequiredEntityId) {
							allEntriesMatch = false;
							break;
						}
					}

					if (allEntriesMatch) {
						patternMatched = true;
						break;
					}
				}

				if (patternMatched) {
					matchedCount++;
				}
			}

			return matchedCount * ArrangementPatternMatchBonus;
		}
	}
}
