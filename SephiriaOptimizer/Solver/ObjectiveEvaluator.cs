using System;
using System.Runtime.CompilerServices;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Solver {
	internal static class BitOperations {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int PopCount(ulong value) {
			value -= (value >> 1) & 0x5555555555555555UL;
			value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
			value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
			return (int)((value * 0x0101010101010101UL) >> 56);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int TrailingZeroCount(ulong value) {
			if (value == 0UL) {
				return 64;
			}
			ulong lowestBit = value & (0UL - value);
			return PopCount(lowestBit - 1UL);
		}
	}

	public static class ObjectiveEvaluator {
		public const int DpsScaleFactor = 20;

		public const int DeactivationBasePenalty = 16000;
		public const int NegativeLevelExtraPenalty = 600;
		public const int TabletOnBuffedSlotPenalty = 9000;
		public const int UnmetTabletConditionPenalty = 8000;
		public const int WastedPositiveOutOfBoundsPenalty = 5000;

		public const int GoldenNeedleLinkBaseBonus = 2000;
		public const int UnlinkedGoldenNeedlePenalty = 4500;
		public const int WastedNeedlePenalty = UnlinkedGoldenNeedlePenalty;
		public const int BottomRowCoreAttackBlockedPenalty = 2500;
		public const int ChalcedonyTargetMatchBonus = 1500;
		public const int ChalcedonyTargetMismatchPenalty = 1500;

		public const int BaseActiveArtifactScore = 100;
		public const int EffectiveLevelScoreMultiplier = 150;
		public const int MaxLevelReachedBonus = 100;
		public const int GoldenNeedleRootLevelFactor = 120;
		public const int CrystalNeighborLevelFactor = 120;
		public const int CrystalNeighborSlotBonus = 60;
		public const int WhitePaperTargetCopiedBonus = 350;
		public const int WhitePaperGeneralCopiedBonus = 120;
		public const int WhitePaperCopiedBonus = WhitePaperGeneralCopiedBonus;
		public const int WhitePaperWastedPenalty = 300;
		public const int WastedPositiveOnEmptySlotPenalty = 140;
		public const int WastedMultiplierPenalty = 90;
		public const int IgnoreConstraintSynergyBonus = 95;
		public const int ConnectedCharmSynergyBonus = 60;
		public const int ComboBreakpointReachedBonus = 200;
		public const int TargetComboBreakpointBonus = 420;
		public const int TargetComboDynamicStackBonus = 220;
		public const int DefaultComboDynamicStackBonus = 80;
		public const int ArrangementPatternMatchBonus = 240;

		public const int NeutralizedNegativeLevelBonus = 30;
		public const int NeutralizedDisableBonus = 60;
		public const int EngravingOutOfBoundsBonus = 45;
		public const int SacrificeActiveTieBreakerScore = 15;
		public const int MaxLevelOverflowTieBreakerBonus = 5;

		public static int Evaluate(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			return EvaluateWithDps(snapshot, slotToItemIndex, itemRotations, out _);
		}

		public static int CalculateEstimatedDummyDps(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			EvaluateWithDps(snapshot, slotToItemIndex, itemRotations, out int estimatedDps);
			return estimatedDps;
		}

		public static int EvaluateWithDps(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations,
			out int estimatedDps
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			if (unlockedSlots <= 0) {
				estimatedDps = 0;
				return 0;
			}

			Span<int> rawSlotLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> finalSlotLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> tabletDeltaBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> positiveBuffBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> negativePenaltyBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> effectiveLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> needleRootBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> chalcedonyCatBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isActiveBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> naturalCriteriaBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				int permLvl = snapshot.BaseSlotLevel[s];
				int baseDis = snapshot.BaseSlotDisable[s];
				int baseIgn = snapshot.BaseSlotIgnoreCriteria[s];
				int baseMul = snapshot.BaseSlotMultiplyLevel[s];

				rawSlotLevelBuf[s] = permLvl;
				finalSlotLevelBuf[s] = permLvl;
				tabletDeltaBuf[s] = permLvl;
				disableBuf[s] = baseDis;
				ignoreBuf[s] = baseIgn;
				mulBuf[s] = baseMul;
				positiveBuffBuf[s] = Math.Max(0, permLvl) + baseIgn;
				negativePenaltyBuf[s] = Math.Max(0, -permLvl) + (baseDis * 2);
				effectiveLevelBuf[s] = 0;
				needleRootBuf[s] = -1;
				chalcedonyCatBuf[s] = -1;
				isActiveBuf[s] = 0;
				naturalCriteriaBuf[s] = 0;
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
				if (item.Kind != SlotItemKind.StoneTablet) {
					continue;
				}

				int rotation = itemRotations[itemIdx] & 3;
				TabletPlacementRule rule = item.GetPlacementRule(s, rotation);
				if (!IsTabletConditionMet(snapshot, slotToItemIndex, s, rule)) {
					tabletPlacementScore -= UnmetTabletConditionPenalty;
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
								tabletPlacementScore -= WastedPositiveOutOfBoundsPenalty;
							}
							break;
						}
						case TabletEffectType.MultiplyConstLevel: {
							if (targetSlot >= 0 && targetSlot < unlockedSlots) {
								mulBuf[targetSlot] += eff.LevelParam;
							} else if (eff.LevelParam > 1) {
								tabletPlacementScore -= (eff.LevelParam - 1) * WastedPositiveOutOfBoundsPenalty;
							}
							break;
						}
					}
				}
				if (hasEngravingTalent && outOfBoundsPenalties > 0) {
					tabletPlacementScore += outOfBoundsPenalties * EngravingOutOfBoundsBonus;
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int delta = tabletDeltaBuf[s];
				int mul = mulBuf[s];
				if (mul != 0) {
					int extraDelta = delta * (mul - 1);
					rawSlotLevelBuf[s] = delta * mul;
					if (extraDelta > 0) {
						positiveBuffBuf[s] += extraDelta;
					} else if (extraDelta < 0) {
						negativePenaltyBuf[s] += -extraDelta;
					} else if (mul > 1) {
						tabletPlacementScore -= (mul - 1) * WastedMultiplierPenalty;
					}
				} else {
					rawSlotLevelBuf[s] = delta;
				}
			}

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
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				naturalCriteriaBuf[s] = naturalCriteriaMet ? (byte)1 : (byte)0;
				bool criteriaSatisfied = ignoreBuf[s] > 0 || naturalCriteriaMet;
				int prelimDisplayedLevel = item.BaseLevel + rawSlotLevelBuf[s];
				bool isPrelimActive = globalEnabled
					&& !item.IsDisabledByWeaponOrDual
					&& disableBuf[s] == 0
					&& prelimDisplayedLevel >= 0
					&& criteriaSatisfied;
				if (isPrelimActive) {
					isActiveBuf[s] = 1;
					categoryMaskBuf[s] = item.StaticCategoryMask;
				}
			}

			Span<int> dynamicComboBonusCounts = stackalloc int[InventorySnapshot.MaxSupportedCategories];
			for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
				dynamicComboBonusCounts[c] = 0;
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (isActiveBuf[s] == 0) {
					continue;
				}
				SlotItemSnapshot item = items[slotToItemIndex[s]];
				if (item.IsElementalByRowCharm && item.RowCategoryIds.Length > 0) {
					int rowIdx = snapshot.Slots[s].Y % item.RowCategoryIds.Length;
					int assignedCatId = item.RowCategoryIds[rowIdx];
					if (assignedCatId >= 0 && assignedCatId < InventorySnapshot.MaxSupportedCategories) {
						chalcedonyCatBuf[s] = assignedCatId;
						categoryMaskBuf[s] |= 1u << assignedCatId;
						dynamicComboBonusCounts[assignedCatId]++;
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (isActiveBuf[s] == 0) {
					continue;
				}
				SlotItemSnapshot item = items[slotToItemIndex[s]];
				if (item.HasDependencyTarget) {
					int rootSlot = ResolveNeedleRootSlot(snapshot, slotToItemIndex, isActiveBuf, s);
					needleRootBuf[s] = rootSlot;
					if (rootSlot >= 0) {
						uint rootCategories = categoryMaskBuf[rootSlot];
						categoryMaskBuf[s] |= rootCategories;
						AccumulateMaskIntoComboCounts(rootCategories, dynamicComboBonusCounts);
					}
				}
			}

			Span<uint> whitePaperCopiedBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];
			for (int s = 0; s < unlockedSlots; s++) {
				whitePaperCopiedBuf[s] = 0u;
				if (isActiveBuf[s] == 0) {
					continue;
				}
				SlotItemSnapshot item = items[slotToItemIndex[s]];
				if (item.IsWhitePaperCharm) {
					SlotCoord coord = snapshot.Slots[s];
					int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
					int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
					uint leftMask = (leftSlot >= 0 && isActiveBuf[leftSlot] != 0)
						? categoryMaskBuf[leftSlot]
						: 0u;
					uint rightMask = (rightSlot >= 0 && isActiveBuf[rightSlot] != 0)
						? categoryMaskBuf[rightSlot]
						: 0u;
					uint copiedMask = item.WhitePaperMatchCount <= 1
						? (leftMask | rightMask)
						: (leftMask & rightMask);
					if (copiedMask != 0u) {
						whitePaperCopiedBuf[s] = copiedMask;
						categoryMaskBuf[s] |= copiedMask;
						AccumulateMaskIntoComboCounts(copiedMask, dynamicComboBonusCounts);
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				finalSlotLevelBuf[s] = rawSlotLevelBuf[s];
			}

			int mysticCatId = snapshot.MysticCategoryId;
			int dynamicMysticTags = (mysticCatId >= 0 && mysticCatId < InventorySnapshot.MaxSupportedCategories)
				? dynamicComboBonusCounts[mysticCatId]
				: 0;
			int mysticTagCount = snapshot.StaticMysticTagCount + dynamicMysticTags;
			ulong amplifiedMysticMask = 0UL;

			if (mysticTagCount >= snapshot.MysticTier1Threshold) {
				ApplyMysticSlotAmplification(
					snapshot.MysticSlot0,
					unlockedSlots,
					ref amplifiedMysticMask,
					rawSlotLevelBuf,
					finalSlotLevelBuf,
					positiveBuffBuf,
					negativePenaltyBuf
				);
			}
			if (mysticTagCount >= snapshot.MysticTier2Threshold) {
				ApplyMysticSlotAmplification(
					snapshot.MysticSlot1,
					unlockedSlots,
					ref amplifiedMysticMask,
					rawSlotLevelBuf,
					finalSlotLevelBuf,
					positiveBuffBuf,
					negativePenaltyBuf
				);
				int[] extraMysticSlots = snapshot.ExtraMysticTier2Slots;
				for (int m = 0; m < extraMysticSlots.Length; m++) {
					ApplyMysticSlotAmplification(
						extraMysticSlots[m],
						unlockedSlots,
						ref amplifiedMysticMask,
						rawSlotLevelBuf,
						finalSlotLevelBuf,
						positiveBuffBuf,
						negativePenaltyBuf
					);
				}
			}

			int totalScore = tabletPlacementScore;
			int activeNonSacrificeArtifactCount = 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				int posBuff = positiveBuffBuf[s];
				int negPen = negativePenaltyBuf[s];
				int slotLevel = finalSlotLevelBuf[s];

				if (itemIdx < 0) {
					if (posBuff > 0) {
						int wastedPositive = Math.Max(slotLevel, posBuff);
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
					} else if (posBuff == 0 && negPen > 0) {
						totalScore += negPen * NeutralizedNegativeLevelBonus;
					}
					continue;
				}

				bool absorbsPenalty = item.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| item.IsDisabledByWeaponOrDual;
				if (absorbsPenalty) {
					if (posBuff > 0) {
						int wastedPositive = Math.Max(slotLevel, posBuff);
						totalScore -= wastedPositive * WastedPositiveOnEmptySlotPenalty;
					} else if (posBuff == 0 && negPen > 0) {
						totalScore += negPen * NeutralizedNegativeLevelBonus;
					}
				}

				int displayedLevel = item.BaseLevel + slotLevel;
				bool naturalCriteriaMet = naturalCriteriaBuf[s] != 0;
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool criteriaSatisfied = criteriaIgnored || naturalCriteriaMet;
				bool isDisabled = !globalEnabled
					|| item.IsDisabledByWeaponOrDual
					|| disableBuf[s] > 0
					|| displayedLevel <= -1
					|| !criteriaSatisfied;

				if (isDisabled) {
					isActiveBuf[s] = 0;
					effectiveLevelBuf[s] = 0;
					categoryMaskBuf[s] = 0u;
					if (item.PriorityTier != ArtifactPriorityTier.Sacrifice && !item.IsDisabledByWeaponOrDual) {
						double weight = GetNormalizedWeight(item);
						int absDisplayed = Math.Abs(displayedLevel);
						int rawPenalty = DeactivationBasePenalty + (absDisplayed * NegativeLevelExtraPenalty);
						totalScore -= (int)Math.Round(rawPenalty * weight);
					}
					continue;
				}

				isActiveBuf[s] = 1;
				int effectiveLevel = Math.Clamp(displayedLevel, 0, item.MaxLevel);
				effectiveLevelBuf[s] = effectiveLevel;

				if (item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
					totalScore += SacrificeActiveTieBreakerScore;
					continue;
				}

				activeNonSacrificeArtifactCount++;
				double artifactWeight = GetNormalizedWeight(item);
				int levelScore = item.MaxLevel > 0 ? (effectiveLevel * EffectiveLevelScoreMultiplier) : 0;
				int maxBonus = (item.MaxLevel > 0 && effectiveLevel == item.MaxLevel) ? MaxLevelReachedBonus : 0;
				int ignoreBonus = 0;
				if (item.ConstraintType != ArtifactConstraintType.None && criteriaIgnored) {
					ignoreBonus = naturalCriteriaMet
						? (IgnoreConstraintSynergyBonus / 2)
						: IgnoreConstraintSynergyBonus;
				}

				int artifactScore = (int)Math.Round(
					(BaseActiveArtifactScore + levelScore + maxBonus + ignoreBonus) * artifactWeight
				);
				if (displayedLevel > item.MaxLevel && slotLevel > 0) {
					int overflowLevels = Math.Min(displayedLevel - item.MaxLevel, slotLevel);
					artifactScore += overflowLevels * MaxLevelOverflowTieBreakerBonus;
				}

				totalScore += artifactScore;
			}

			if (snapshot.DependencyArtifactCount > 0 && unlockedSlots > snapshot.Width) {
				totalScore -= EvaluateBottomRowBlockedAttackablePenalty(snapshot, slotToItemIndex);
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind != SlotItemKind.Artifact) {
					continue;
				}

				if (item.IsElementalByRowCharm && isActiveBuf[s] != 0) {
					int assignedCatId = chalcedonyCatBuf[s];
					if (assignedCatId >= 0) {
						if (snapshot.PreferredChalcedonyCategoryId >= 0) {
							if (assignedCatId == snapshot.PreferredChalcedonyCategoryId) {
								totalScore += ChalcedonyTargetMatchBonus;
							} else {
								totalScore -= ChalcedonyTargetMismatchPenalty;
							}
						}
						int elemBonus = item.GetRowElementalBonus(effectiveLevelBuf[s]);
						int statAffinity = GetElementalCategoryAffinity(snapshot, assignedCatId);
						totalScore += elemBonus * (15 + statAffinity);
					}
				}

				bool isSacrificeOrWeaponDisabled = item.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| item.IsDisabledByWeaponOrDual;

				if (item.HasDependencyTarget && !isSacrificeOrWeaponDisabled) {
					if (isActiveBuf[s] == 0) {
						totalScore -= UnlinkedGoldenNeedlePenalty;
					} else {
						int rootSlot = needleRootBuf[s];
						if (rootSlot >= 0 && isActiveBuf[rootSlot] != 0) {
							SlotItemSnapshot rootItem = items[slotToItemIndex[rootSlot]];
							double rootWeight = GetNeedleRootWeight(snapshot, rootItem);
							if (rootWeight <= 0.0) {
								totalScore -= UnlinkedGoldenNeedlePenalty;
							} else {
								int needleLevel = effectiveLevelBuf[s];
								int rootLevel = effectiveLevelBuf[rootSlot];
								int fixedLinkBonus = (int)Math.Round(GoldenNeedleLinkBaseBonus * rootWeight);
								double ampFactor = 1.0 + (0.25 * (needleLevel + 1));
								double multiplicativeAmp = ampFactor
									* (GoldenNeedleRootLevelFactor * rootLevel)
									* rootWeight;
								if (item.HasDependencyCondition && rootItem.RarityTier <= item.DependencyMaxRarity) {
									multiplicativeAmp += 35.0 * (needleLevel + 1);
								}
								totalScore += fixedLinkBonus + (int)Math.Round(multiplicativeAmp);
							}
						} else {
							totalScore -= UnlinkedGoldenNeedlePenalty;
						}
					}
				}

				if (item.IsWhitePaperCharm && !isSacrificeOrWeaponDisabled) {
					uint copiedMask = whitePaperCopiedBuf[s];
					if (isActiveBuf[s] != 0 && copiedMask != 0u) {
						bool copiedTargetTag = (copiedMask & snapshot.UserTargetCategoryMask) != 0u;
						totalScore += copiedTargetTag
							? WhitePaperTargetCopiedBonus
							: WhitePaperGeneralCopiedBonus;
					} else {
						totalScore -= WhitePaperWastedPenalty;
					}
				}
			}

			totalScore += EvaluateComboCategories(snapshot, dynamicComboBonusCounts);

			ulong[] mooreNeighborMasks = snapshot.MooreNeighborMasks;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || isActiveBuf[s] == 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind != SlotItemKind.Artifact) {
					continue;
				}

				double weight = GetNormalizedWeight(item);
				if (item.IsCrystalOfHarmony && weight > 0.0) {
					ulong neighborMask = mooreNeighborMasks[s];
					int neighborSlotCount = BitOperations.PopCount(neighborMask);
					int sumNeighborLevels = 0;
					ulong iterMask = neighborMask;
					while (iterMask != 0UL) {
						int nSlot = BitOperations.TrailingZeroCount(iterMask);
						iterMask &= iterMask - 1UL;
						if (isActiveBuf[nSlot] != 0) {
							int nItemIdx = slotToItemIndex[nSlot];
							if (nItemIdx >= 0 && items[nItemIdx].Kind == SlotItemKind.Artifact) {
								sumNeighborLevels += effectiveLevelBuf[nSlot];
							}
						}
					}
					int crystalLv = effectiveLevelBuf[s];
					float ratePerLevel = item.GetCrystalRatePerLevel(crystalLv);
					int harmonyAmpScore = (int)Math.Round(
						sumNeighborLevels * ratePerLevel * CrystalNeighborLevelFactor * weight
					);
					int centerPlacementBonus = neighborSlotCount * CrystalNeighborSlotBonus;
					totalScore += harmonyAmpScore + centerPlacementBonus;
				}

				if (weight > 0.0) {
					int posSynergy = EvaluatePositionSynergy(snapshot, slotToItemIndex, s, item);
					if (posSynergy > 0) {
						totalScore += (int)Math.Round(posSynergy * weight);
					}
				}
			}

			if (talentRules.ArrangementBonusStat > 0 && talentRules.ArrangementPatterns.Length > 0) {
				totalScore += EvaluateArrangementPatterns(snapshot, slotToItemIndex);
			}

			estimatedDps = ComputeEstimatedDummyDpsFromState(
				snapshot,
				slotToItemIndex,
				isActiveBuf,
				effectiveLevelBuf,
				needleRootBuf,
				chalcedonyCatBuf,
				dynamicComboBonusCounts,
				out int baseWeaponOnlyDps
			);

			if (activeNonSacrificeArtifactCount > 0) {
				int artifactInducedDps = Math.Max(0, estimatedDps - baseWeaponOnlyDps);
				totalScore += artifactInducedDps * DpsScaleFactor;
			}

			return totalScore;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int ComputeEstimatedDummyDpsFromState(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<byte> isActiveBuf,
			ReadOnlySpan<int> effectiveLevelBuf,
			ReadOnlySpan<int> needleRootBuf,
			ReadOnlySpan<int> chalcedonyCatBuf,
			ReadOnlySpan<int> dynamicComboBonusCounts,
			out int baseWeaponOnlyDps
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;

			Span<int> needleBonusPctSumBySlot = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			for (int s = 0; s < unlockedSlots; s++) {
				needleBonusPctSumBySlot[s] = 0;
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (isActiveBuf[s] == 0) {
					continue;
				}
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot needleItem = items[itemIdx];
				if (needleItem.Kind != SlotItemKind.Artifact
					|| !needleItem.HasDependencyTarget
					|| needleItem.PriorityTier == ArtifactPriorityTier.Sacrifice) {
					continue;
				}
				int rootSlot = needleRootBuf[s];
				if (rootSlot >= 0 && rootSlot < unlockedSlots && isActiveBuf[rootSlot] != 0) {
					int rootItemIdx = slotToItemIndex[rootSlot];
					if (rootItemIdx >= 0) {
						SlotItemSnapshot rootItem = items[rootItemIdx];
						if (rootItem.Kind == SlotItemKind.Artifact
							&& rootItem.IsAttackableCharm
							&& rootItem.PriorityTier != ArtifactPriorityTier.Sacrifice) {
							int needleLv = effectiveLevelBuf[s];
							int bonusPct = needleItem.GetNeedleBonusPercent(needleLv, rootItem.RarityTier);
							needleBonusPctSumBySlot[rootSlot] += bonusPct;
						}
					}
				}
			}

			ulong[] mooreNeighborMasks = snapshot.MooreNeighborMasks;
			ComboCategoryInfo[] categories = snapshot.ComboCategories;
			int unlimitedComboStat = snapshot.TalentRules.UnlimitedComboStat;
			double totalHarmonyAmpPercent = 0.0;

			if (snapshot.CombatBaseline.HasDetailedWeaponProfile) {
				baseWeaponOnlyDps = (int)Math.Round(snapshot.CombatBaseline.BaseWeaponDps);
				CombatStatDelta totalDelta = CombatStatDelta.Zero;
				double customFinalDamageMultiplier = 1.0;

				for (int s = 0; s < unlockedSlots; s++) {
					if (isActiveBuf[s] == 0) {
						continue;
					}
					int itemIdx = slotToItemIndex[s];
					if (itemIdx < 0) {
						continue;
					}
					SlotItemSnapshot item = items[itemIdx];
					if (item.Kind != SlotItemKind.Artifact || item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
						continue;
					}

					int effLv = effectiveLevelBuf[s];
					CombatStatDelta itemDelta = item.GetStatDeltaAtLevel(effLv);
					if (itemDelta.HasAnyNonZero) {
						totalDelta = totalDelta.Add(in itemDelta);
					}
					if (!item.HasExplicitStatDeltas) {
						float finalPct = item.GetFinalDamagePercentAtLevel(effLv);
						if (finalPct > 0f) {
							customFinalDamageMultiplier *= 1.0 + (finalPct * 0.01);
						}
					}

					if (item.IsElementalByRowCharm) {
						int assignedCatId = chalcedonyCatBuf[s];
						if (assignedCatId >= 0) {
							byte elemKind = snapshot.GetCategoryElementalKind(assignedCatId);
							totalDelta = totalDelta.AddElementalByKind(elemKind, item.GetRowElementalBonus(effLv));
						}
					}

					if (item.IsCrystalOfHarmony) {
						ulong neighborMask = mooreNeighborMasks[s];
						int sumNeighborLevels = 0;
						while (neighborMask != 0UL) {
							int nSlot = BitOperations.TrailingZeroCount(neighborMask);
							neighborMask &= neighborMask - 1UL;
							if (isActiveBuf[nSlot] != 0) {
								int nItemIdx = slotToItemIndex[nSlot];
								if (nItemIdx >= 0 && items[nItemIdx].Kind == SlotItemKind.Artifact) {
									sumNeighborLevels += effectiveLevelBuf[nSlot];
								}
							}
						}
						float crystalRate = item.GetCrystalRatePerLevel(effLv);
						totalHarmonyAmpPercent += Math.Floor(sumNeighborLevels * (double)crystalRate);
					}
				}

				int activeComboTypes = 0;
				for (int i = 0; i < categories.Length; i++) {
					ComboCategoryInfo info = categories[i];
					int catId = info.CategoryId;
					int dynAdded = (catId >= 0 && catId < InventorySnapshot.MaxSupportedCategories)
						? dynamicComboBonusCounts[catId]
						: 0;
					int totalCount = info.StaticCount + dynAdded;
					CombatStatDelta comboDelta = info.EvaluateStatDelta(totalCount, unlimitedComboStat);
					if (comboDelta.HasAnyNonZero) {
						totalDelta = totalDelta.Add(in comboDelta);
					}
					if (info.Breakpoints.Length > 0 && totalCount >= info.Breakpoints[0]) {
						activeComboTypes++;
					}
				}

				int comboBonusDmgStat = snapshot.TalentRules.ComboBonusDamageStat;
				if (comboBonusDmgStat > 0 && activeComboTypes > 0) {
					totalDelta = totalDelta.Add(
						new CombatStatDelta(allDamageBonus: comboBonusDmgStat * activeComboTypes)
					);
				}

				WeaponCombatEvaluation weaponEval = snapshot.CombatBaseline.WeaponProfile.Evaluate(
					in totalDelta,
					customFinalDamageMultiplier
				);

				double unamplifiedCharmAfterBonusDps = 0.0;
				double amplifiedCharmAfterBonusDps = 0.0;
				for (int s = 0; s < unlockedSlots; s++) {
					if (isActiveBuf[s] == 0) {
						continue;
					}
					int itemIdx = slotToItemIndex[s];
					if (itemIdx < 0) {
						continue;
					}
					SlotItemSnapshot item = items[itemIdx];
					if (item.Kind != SlotItemKind.Artifact
						|| item.PriorityTier == ArtifactPriorityTier.Sacrifice
						|| !item.IsAttackableCharm) {
						continue;
					}

					int effLv = effectiveLevelBuf[s];
					double elemStat = weaponEval.ResolveElementalStat(item.CharmScalingElementKind);
					double rawCharmDps = item.GetAttackRawDpsAtLevelWithElement(effLv, elemStat);
					int needlePct = needleBonusPctSumBySlot[s];
					double unampFactor = Math.Max(0.1, 1.0 + (weaponEval.LiveCharmDamageBonusPercent * 0.01));
					double ampFactor = Math.Max(
						0.1,
						1.0 + ((weaponEval.LiveCharmDamageBonusPercent + needlePct) * 0.01)
					);

					unamplifiedCharmAfterBonusDps += rawCharmDps * unampFactor;
					amplifiedCharmAfterBonusDps += rawCharmDps * ampFactor;
				}

				double detailedBaseEffectiveDps = weaponEval.WeaponAndAddonEffectiveDps
					+ (unamplifiedCharmAfterBonusDps * weaponEval.CharmPostMultiplier);
				double detailedNeedleAddedDps = Math.Max(
					0.0,
					(amplifiedCharmAfterBonusDps - unamplifiedCharmAfterBonusDps) * weaponEval.CharmPostMultiplier
				);
				double detailedDpsWithNeedle = detailedBaseEffectiveDps + detailedNeedleAddedDps;
				double detailedHarmonyAddedDps = detailedDpsWithNeedle * (totalHarmonyAmpPercent * 0.01);
				double detailedTotalDps = detailedDpsWithNeedle + detailedHarmonyAddedDps;
				return Math.Max(0, (int)Math.Round(detailedTotalDps));
			}

			double baseWeaponRawDps = snapshot.CombatBaseline.ComputeBaseWeaponRawDps();
			double bucketCCritMultiplier = snapshot.CombatBaseline.ComputeCritExpectedMultiplier();
			double baseElemMultiplier = 1.0 + (snapshot.CombatBaseline.ElementalDamageBonusPercent * 0.01);
			baseWeaponOnlyDps = (int)Math.Round(baseWeaponRawDps * baseElemMultiplier * bucketCCritMultiplier);

			double charmAttackRawDps = 0.0;
			double statBonusPercentSum = snapshot.CombatBaseline.ElementalDamageBonusPercent;
			double finalDamageMultiplier = 1.0;

			for (int s = 0; s < unlockedSlots; s++) {
				if (isActiveBuf[s] == 0) {
					continue;
				}
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind != SlotItemKind.Artifact || item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
					continue;
				}

				int effLv = effectiveLevelBuf[s];
				if (item.IsAttackableCharm) {
					double rawAttackDps = item.GetAttackRawDpsAtLevel(effLv);
					double needleMult = 1.0 + (needleBonusPctSumBySlot[s] * 0.01);
					charmAttackRawDps += rawAttackDps * needleMult;
				}

				float statPct = item.GetStatBonusPercentAtLevel(effLv);
				if (statPct > 0f) {
					statBonusPercentSum += statPct;
				}

				float finalPct = item.GetFinalDamagePercentAtLevel(effLv);
				if (finalPct > 0f) {
					finalDamageMultiplier *= 1.0 + (finalPct * 0.01);
				}

				if (item.IsElementalByRowCharm) {
					int assignedCatId = chalcedonyCatBuf[s];
					if (assignedCatId >= 0) {
						int elemBonus = item.GetRowElementalBonus(effLv);
						statBonusPercentSum += elemBonus;
					}
				}

				if (item.IsCrystalOfHarmony) {
					ulong neighborMask = mooreNeighborMasks[s];
					int sumNeighborLevels = 0;
					while (neighborMask != 0UL) {
						int nSlot = BitOperations.TrailingZeroCount(neighborMask);
						neighborMask &= neighborMask - 1UL;
						if (isActiveBuf[nSlot] != 0) {
							int nItemIdx = slotToItemIndex[nSlot];
							if (nItemIdx >= 0 && items[nItemIdx].Kind == SlotItemKind.Artifact) {
								sumNeighborLevels += effectiveLevelBuf[nSlot];
							}
						}
					}
					float crystalRate = item.GetCrystalRatePerLevel(effLv);
					totalHarmonyAmpPercent += Math.Floor(sumNeighborLevels * (double)crystalRate);
				}
			}

			for (int i = 0; i < categories.Length; i++) {
				ComboCategoryInfo info = categories[i];
				int catId = info.CategoryId;
				int dynAdded = (catId >= 0 && catId < InventorySnapshot.MaxSupportedCategories)
					? dynamicComboBonusCounts[catId]
					: 0;
				int totalCount = info.StaticCount + dynAdded;
				statBonusPercentSum += info.EvaluateStatBonusPercent(totalCount, unlimitedComboStat);
			}

			double bucketBStatMultiplier = 1.0 + (statBonusPercentSum * 0.01);
			double generalStatMultiplier = bucketBStatMultiplier * bucketCCritMultiplier * finalDamageMultiplier;
			double dpsWithNeedle = (baseWeaponRawDps + charmAttackRawDps) * generalStatMultiplier;
			double harmonyAddedDps = dpsWithNeedle * (totalHarmonyAmpPercent * 0.01);
			double totalEstimatedDps = dpsWithNeedle + harmonyAddedDps;

			return Math.Max(0, (int)Math.Round(totalEstimatedDps));
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

			Span<int> rawSlotLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> finalSlotLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> tabletDeltaBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> dummyPosBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> dummyNegBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isActiveBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];
			Span<int> connectedTargetBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> engravingReadyBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<int> dynamicComboBonusCounts = stackalloc int[InventorySnapshot.MaxSupportedCategories];

			for (int s = 0; s < unlockedSlots; s++) {
				int permLvl = snapshot.BaseSlotLevel[s];
				rawSlotLevelBuf[s] = permLvl;
				finalSlotLevelBuf[s] = permLvl;
				tabletDeltaBuf[s] = permLvl;
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
				dummyPosBuf[s] = 0;
				dummyNegBuf[s] = 0;
				isActiveBuf[s] = 0;
				categoryMaskBuf[s] = 0u;
				connectedTargetBuf[s] = -1;
				engravingReadyBuf[s] = 0;
			}
			for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
				dynamicComboBonusCounts[c] = 0;
			}

			SlotItemSnapshot[] items = snapshot.Items;
			bool hasEngravingTalent = snapshot.TalentRules.TabletEngravingCount > 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind != SlotItemKind.StoneTablet) {
					continue;
				}
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
							tabletDeltaBuf[targetSlot] += eff.LevelParam;
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

			for (int s = 0; s < unlockedSlots; s++) {
				rawSlotLevelBuf[s] = mulBuf[s] != 0 ? (tabletDeltaBuf[s] * mulBuf[s]) : tabletDeltaBuf[s];
				finalSlotLevelBuf[s] = rawSlotLevelBuf[s];
			}

			bool globalEnabled = snapshot.GlobalActiveValue > 0;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				int prelimDisplayed = item.BaseLevel + rawSlotLevelBuf[s];
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool negDeactivated = prelimDisplayed <= -1;
				bool disableDeactivated = disableBuf[s] > 0 || item.IsDisabledByWeaponOrDual || !globalEnabled;
				bool criteriaDeactivated = !criteriaIgnored && !naturalCriteriaMet;
				if (!negDeactivated && !disableDeactivated && !criteriaDeactivated) {
					isActiveBuf[s] = 1;
					categoryMaskBuf[s] = item.StaticCategoryMask;
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
							dynamicComboBonusCounts[catId]++;
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
						uint rootCategories = categoryMaskBuf[rootSlot];
						categoryMaskBuf[s] |= rootCategories;
						AccumulateMaskIntoComboCounts(rootCategories, dynamicComboBonusCounts);
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
					uint leftMask = (leftSlot >= 0 && isActiveBuf[leftSlot] != 0)
						? categoryMaskBuf[leftSlot]
						: 0u;
					uint rightMask = (rightSlot >= 0 && isActiveBuf[rightSlot] != 0)
						? categoryMaskBuf[rightSlot]
						: 0u;
					uint copiedMask = items[itemIdx].WhitePaperMatchCount <= 1
						? (leftMask | rightMask)
						: (leftMask & rightMask);
					if (copiedMask != 0u) {
						categoryMaskBuf[s] |= copiedMask;
						AccumulateMaskIntoComboCounts(copiedMask, dynamicComboBonusCounts);
					}
				}
			}

			int mysticCatId = snapshot.MysticCategoryId;
			int dynamicMysticTags = (mysticCatId >= 0 && mysticCatId < InventorySnapshot.MaxSupportedCategories)
				? dynamicComboBonusCounts[mysticCatId]
				: 0;
			int mysticTagCount = snapshot.StaticMysticTagCount + dynamicMysticTags;
			ulong amplifiedMysticMask = 0UL;

			if (mysticTagCount >= snapshot.MysticTier1Threshold) {
				ApplyMysticSlotAmplification(
					snapshot.MysticSlot0,
					unlockedSlots,
					ref amplifiedMysticMask,
					rawSlotLevelBuf,
					finalSlotLevelBuf,
					dummyPosBuf,
					dummyNegBuf
				);
			}
			if (mysticTagCount >= snapshot.MysticTier2Threshold) {
				ApplyMysticSlotAmplification(
					snapshot.MysticSlot1,
					unlockedSlots,
					ref amplifiedMysticMask,
					rawSlotLevelBuf,
					finalSlotLevelBuf,
					dummyPosBuf,
					dummyNegBuf
				);
				int[] extraMysticSlots = snapshot.ExtraMysticTier2Slots;
				for (int m = 0; m < extraMysticSlots.Length; m++) {
					ApplyMysticSlotAmplification(
						extraMysticSlots[m],
						unlockedSlots,
						ref amplifiedMysticMask,
						rawSlotLevelBuf,
						finalSlotLevelBuf,
						dummyPosBuf,
						dummyNegBuf
					);
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
					details[s] = new SlotEvaluationDetail(
						s,
						itemIdx,
						finalSlotLevelBuf[s],
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
				int displayedLevel = item.BaseLevel + finalSlotLevelBuf[s];
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool negDeactivated = displayedLevel <= -1;
				bool disableDeactivated = disableBuf[s] > 0 || item.IsDisabledByWeaponOrDual || !globalEnabled;
				bool criteriaDeactivated = !criteriaIgnored && !naturalCriteriaMet;
				bool isEnabled = !negDeactivated && !disableDeactivated && !criteriaDeactivated;
				int effectiveLevel = isEnabled ? Math.Clamp(displayedLevel, 0, item.MaxLevel) : 0;
				uint finalCategoryMask = isEnabled ? categoryMaskBuf[s] : 0u;
				int finalConnectedTarget = isEnabled ? connectedTargetBuf[s] : -1;

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
					finalCategoryMask,
					finalConnectedTarget,
					false
				);
			}

			return details;
		}

		private static int EvaluateBottomRowBlockedAttackablePenalty(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex
		) {
			SlotItemSnapshot[] items = snapshot.Items;
			int unlockedSlots = snapshot.UnlockedSlots;
			int width = snapshot.Width;
			double maxAttackWeight = 0.0;

			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot candidate = items[i];
				if (candidate.Kind == SlotItemKind.Artifact
					&& candidate.IsAttackableCharm
					&& !candidate.IsDisabledByWeaponOrDual
					&& candidate.PriorityTier != ArtifactPriorityTier.Sacrifice) {
					double candWeight = GetNeedleRootWeight(snapshot, candidate);
					if (candWeight > maxAttackWeight) {
						maxAttackWeight = candWeight;
					}
				}
			}

			if (maxAttackWeight <= 0.0) {
				return 0;
			}

			int topAttackTotal = 0;
			int topAttackBlocked = 0;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot placed = items[itemIdx];
				if (placed.Kind == SlotItemKind.Artifact
					&& placed.IsAttackableCharm
					&& !placed.IsDisabledByWeaponOrDual
					&& placed.PriorityTier != ArtifactPriorityTier.Sacrifice) {
					double placedWeight = GetNeedleRootWeight(snapshot, placed);
					if (placedWeight >= maxAttackWeight - 1e-6) {
						topAttackTotal++;
						if (s + width >= unlockedSlots) {
							topAttackBlocked++;
						}
					}
				}
			}

			if (topAttackTotal <= 0) {
				return 0;
			}

			int requiredUnblocked = Math.Min(snapshot.DependencyArtifactCount, topAttackTotal);
			int availableUnblocked = topAttackTotal - topAttackBlocked;
			if (availableUnblocked < requiredUnblocked) {
				return (requiredUnblocked - availableUnblocked) * BottomRowCoreAttackBlockedPenalty;
			}
			return 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void ApplyMysticSlotAmplification(
			int mysticSlot,
			int unlockedSlots,
			ref ulong amplifiedMysticMask,
			ReadOnlySpan<int> rawSlotLevelBuf,
			Span<int> finalSlotLevelBuf,
			Span<int> positiveBuffBuf,
			Span<int> negativePenaltyBuf
		) {
			if ((uint)mysticSlot >= (uint)unlockedSlots) {
				return;
			}
			ulong slotBit = 1UL << mysticSlot;
			if ((amplifiedMysticMask & slotBit) != 0UL) {
				return;
			}
			amplifiedMysticMask |= slotBit;
			int rawSlotLevel = rawSlotLevelBuf[mysticSlot];
			finalSlotLevelBuf[mysticSlot] = rawSlotLevel * 2;
			if (rawSlotLevel > 0) {
				positiveBuffBuf[mysticSlot] += rawSlotLevel;
			} else if (rawSlotLevel < 0) {
				negativePenaltyBuf[mysticSlot] += -rawSlotLevel;
			}
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

			while (hopCount <= 8) {
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

			while (hopCount <= 8) {
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
					ulong neighborMask = snapshot.MooreNeighborMasks[slotIndex];
					while (neighborMask != 0UL) {
						int nSlot = BitOperations.TrailingZeroCount(neighborMask);
						neighborMask &= neighborMask - 1UL;
						int neighborItem = slotToItemIndex[nSlot];
						if (neighborItem >= 0 && items[neighborItem].IsMagicBook) {
							return true;
						}
					}
					return false;
				}
				case ArtifactConstraintType.NeighborsAreFull: {
					ulong neighborMask = snapshot.MooreNeighborMasks[slotIndex];
					if (BitOperations.PopCount(neighborMask) < 8) {
						return false;
					}
					while (neighborMask != 0UL) {
						int nSlot = BitOperations.TrailingZeroCount(neighborMask);
						neighborMask &= neighborMask - 1UL;
						if (slotToItemIndex[nSlot] < 0) {
							return false;
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double GetNormalizedWeight(SlotItemSnapshot item) {
			if (item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
				return 0.0;
			}
			int clampedCustom = Math.Clamp(item.CustomWeight, 1, 20);
			return item.PriorityTier switch {
				ArtifactPriorityTier.Core => 1.66 + (0.0070 * clampedCustom),
				ArtifactPriorityTier.High => 1.52 + (0.0070 * clampedCustom),
				ArtifactPriorityTier.Low => 1.24 + (0.0060 * clampedCustom),
				_ => 1.38 + (0.0065 * clampedCustom)
			};
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double GetNeedleRootWeight(InventorySnapshot snapshot, SlotItemSnapshot rootItem) {
			double weight = GetNormalizedWeight(rootItem);
			if (weight <= 0.0) {
				return 0.0;
			}
			if (snapshot.PreferredNeedleTargetInstanceId == rootItem.InstanceId) {
				weight += 0.07;
			}
			return weight;
		}

		public static int GetPriorityScaledWeight(SlotItemSnapshot item) {
			return (int)Math.Round(GetNormalizedWeight(item) * 100.0);
		}

		private static void AccumulateMaskIntoComboCounts(uint mask, Span<int> dynamicComboBonusCounts) {
			uint remaining = mask;
			while (remaining != 0u) {
				int bitIndex = BitOperations.TrailingZeroCount(remaining);
				if (bitIndex < InventorySnapshot.MaxSupportedCategories) {
					dynamicComboBonusCounts[bitIndex]++;
				}
				remaining &= remaining - 1u;
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
							? TargetComboBreakpointBonus
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
					comboScore += surplus * Math.Clamp(talent.UnlimitedComboStat, 1, 4) * 35;
				}
			}

			if (talent.ComboBonusDamageStat > 0 && activeComboTypes > 0) {
				comboScore += activeComboTypes * Math.Clamp(talent.ComboBonusDamageStat, 1, 4) * 25;
			}

			return comboScore;
		}

		private static int GetElementalCategoryAffinity(InventorySnapshot snapshot, int categoryId) {
			ComboCategoryInfo[] categories = snapshot.ComboCategories;
			if (categoryId < 0 || categoryId >= categories.Length) {
				return 0;
			}
			ComboCategoryInfo info = categories[categoryId];
			int affinity = info.IsUserTarget ? 30 : 0;
			TalentRuleSnapshot talent = snapshot.TalentRules;

			if (string.Equals(info.CategoryKey, "EMBER", StringComparison.OrdinalIgnoreCase)) {
				affinity += Math.Clamp(talent.FireDamageStat / 5, 0, 25);
			} else if (string.Equals(info.CategoryKey, "GLACIER", StringComparison.OrdinalIgnoreCase)) {
				affinity += Math.Clamp(talent.IceDamageStat / 5, 0, 25);
			} else if (string.Equals(info.CategoryKey, "MAGITECH", StringComparison.OrdinalIgnoreCase)) {
				affinity += Math.Clamp(talent.LightningDamageStat / 5, 0, 25);
			} else if (string.Equals(info.CategoryKey, "STURDY", StringComparison.OrdinalIgnoreCase)) {
				affinity += Math.Clamp(talent.PhysicalDamageStat / 5, 0, 25);
			}

			if (snapshot.CombatBaseline.HasDetailedWeaponProfile && info.CategoryElementalKind != 0) {
				WeaponCombatProfile wp = snapshot.CombatBaseline.WeaponProfile;
				affinity += GetWeaponElementAffinityBonus(in wp, info.CategoryElementalKind);
			}

			return affinity;
		}

		private static int GetWeaponElementAffinityBonus(in WeaponCombatProfile profile, byte elemKind) {
			int bonus = 0;
			float maxElem = Math.Max(
				Math.Max(profile.BasePhysicalStat, profile.BaseFireStat),
				Math.Max(profile.BaseIceStat, profile.BaseLightningStat)
			);
			float minElem = Math.Min(
				Math.Min(profile.BasePhysicalStat, profile.BaseFireStat),
				Math.Min(profile.BaseIceStat, profile.BaseLightningStat)
			);

			float targetBaseStat = elemKind switch {
				1 => profile.BasePhysicalStat,
				2 => profile.BaseFireStat,
				3 => profile.BaseIceStat,
				4 => profile.BaseLightningStat,
				_ => 20f
			};

			switch (profile.FormulaMode) {
				case WeaponElementalFormulaMode.SinglePhysical: {
					if (elemKind == 1) {
						bonus += 35;
					}
					break;
				}
				case WeaponElementalFormulaMode.SingleFire: {
					if (elemKind == 2) {
						bonus += 35;
					}
					break;
				}
				case WeaponElementalFormulaMode.SingleIce: {
					if (elemKind == 3) {
						bonus += 35;
					}
					break;
				}
				case WeaponElementalFormulaMode.SingleLightning: {
					if (elemKind == 4) {
						bonus += 35;
					}
					break;
				}
				case WeaponElementalFormulaMode.Highest: {
					if (Math.Abs(targetBaseStat - maxElem) <= 0.01f) {
						bonus += 25;
					}
					break;
				}
				case WeaponElementalFormulaMode.Lowest: {
					if (Math.Abs(targetBaseStat - minElem) <= 0.01f) {
						bonus += 25;
					}
					break;
				}
				case WeaponElementalFormulaMode.AverageMask: {
					int bit = 1 << (elemKind - 1);
					if ((profile.AverageElementMask & bit) != 0) {
						bonus += 20;
					}
					break;
				}
			}

			if (profile.HasElementalScaledSpread) {
				bonus += 10;
			}

			if (elemKind == 1
				&& (profile.AddOnHitPhysicalPct > 0f
					|| (profile.CritNeedleBaseCount > 0 && profile.CritNeedlePhysicalRatio > 0f))) {
				bonus += 18;
			} else if (elemKind == 2
				&& (profile.AddOnHitFirePct > 0f || profile.BurnRingFirePercentPerSec > 0f)) {
				bonus += 18;
			} else if (elemKind == 3
				&& (profile.AddOnHitIcePct > 0f
					|| (profile.NastrondProcsPerSec > 0f && profile.NastrondIcePercent > 0f))) {
				bonus += 18;
			} else if (elemKind == 4 && profile.AddOnHitLightningPct > 0f) {
				bonus += 18;
			}

			bonus += Math.Clamp((int)((targetBaseStat - 20f) * 0.25f), 0, 20);
			return bonus;
		}

		private static int EvaluatePositionSynergy(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
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
				synergy += sameRowCompanions * 45;
			}

			if (item.IsPlanetModuleCharm) {
				int nearPlanets = 0;
				ulong neighborMask = snapshot.MooreNeighborMasks[slotIndex];
				while (neighborMask != 0UL) {
					int nSlot = BitOperations.TrailingZeroCount(neighborMask);
					neighborMask &= neighborMask - 1UL;
					int nItem = slotToItemIndex[nSlot];
					if (nItem >= 0 && items[nItem].IsPlanetCharm) {
						nearPlanets++;
					}
				}
				synergy += nearPlanets * 55;
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
				synergy += sameColBooks * 40;
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
				synergy += topCharms * 20;
			}

			if (item.IsFireIcePositionCharm) {
				float fireStat = snapshot.TalentRules.FireDamageStat;
				float iceStat = snapshot.TalentRules.IceDamageStat;
				if (snapshot.CombatBaseline.HasDetailedWeaponProfile) {
					WeaponCombatProfile wp = snapshot.CombatBaseline.WeaponProfile;
					fireStat += wp.BaseFireStat
						+ (wp.FormulaMode == WeaponElementalFormulaMode.SingleFire ? 20f : 0f)
						+ wp.AddOnHitFirePct;
					iceStat += wp.BaseIceStat
						+ (wp.FormulaMode == WeaponElementalFormulaMode.SingleIce ? 20f : 0f)
						+ wp.AddOnHitIcePct;
				}
				bool isLeftHalf = coord.X < (snapshot.Width / 2);
				if (fireStat > iceStat && isLeftHalf) {
					synergy += 50;
				} else if (iceStat > fireStat && !isLeftHalf) {
					synergy += 50;
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
