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
		public const double DpsGainScoreMultiplier = 100000.0;
		public const double CompanionChaosRowDpsBonusPercent = 25.0;

		public const int DeactivationBasePenalty = 16000;
		public const int NegativeLevelExtraPenalty = 600;
		public const int TabletOnBuffedSlotPenalty = 9000;
		public const int TabletOnBuffedSlotTieBreakerPenalty = 240;
		public const int UnmetTabletConditionPenalty = 8000;
		public const int WastedPositiveOutOfBoundsPenalty = 1600;

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
		public const int CrystalNeighborSlotBonus = 15;
		public const int WhitePaperTargetCopiedBonus = 350;
		public const int WhitePaperGeneralCopiedBonus = 120;
		public const int WhitePaperCopiedBonus = WhitePaperGeneralCopiedBonus;
		public const int WhitePaperWastedPenalty = 300;
		public const int WastedPositiveOnEmptySlotPenalty = 360;
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
		public const int TabletPlusMinusCollisionPenalty = 950;
		public const int TabletMinusOnNetPositiveSlotPenalty = 650;
		public const int TabletCollisionEffectiveLevelLossPenalty = 320;
		public const int OvercappedPlusMinusTieBreakerPenalty = 40;

		public static int Evaluate(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			return EvaluateInternal(snapshot, slotToItemIndex, itemRotations, out _, out _, out _);
		}

		public static int CalculateEstimatedDummyDps(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			EvaluateInternal(snapshot, slotToItemIndex, itemRotations, out double exactDps, out _, out _);
			return Math.Max(0, (int)Math.Round(exactDps));
		}

		public static int EvaluateWithDps(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations,
			out int estimatedDps
		) {
			int score = EvaluateInternal(snapshot, slotToItemIndex, itemRotations, out double exactDps, out _, out _);
			estimatedDps = Math.Max(0, (int)Math.Round(exactDps));
			return score;
		}

		public static int EvaluateWithMetrics(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations,
			out double exactEstimatedDps,
			out int hardViolationCount,
			out int utilityEffectiveLevelSum
		) {
			return EvaluateInternal(
				snapshot,
				slotToItemIndex,
				itemRotations,
				out exactEstimatedDps,
				out hardViolationCount,
				out utilityEffectiveLevelSum
			);
		}

		private static int EvaluateInternal(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations,
			out double exactEstimatedDps,
			out int hardViolationCount,
			out int utilityEffectiveLevelSum
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			if (unlockedSlots <= 0) {
				exactEstimatedDps = 0.0;
				hardViolationCount = 0;
				utilityEffectiveLevelSum = 0;
				return 0;
			}

			Span<int> tabletDeltaBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mysticMulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> positiveBuffBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> otherPosBuffBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> negativePenaltyBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> posLevelSumBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> negLevelSumBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> displayedLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> effectiveLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> needleRootBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<ulong> needleRootsMaskBuf = stackalloc ulong[InventorySnapshot.MaxSupportedSlots];
			Span<ulong> needleBypassMaskBuf = stackalloc ulong[InventorySnapshot.MaxSupportedSlots];
			Span<int> chalcedonyCatBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isActiveBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> naturalCriteriaBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				int permLvl = snapshot.BaseSlotLevel[s];
				int baseDis = snapshot.BaseSlotDisable[s];
				int baseIgn = snapshot.BaseSlotIgnoreCriteria[s];
				int baseMul = snapshot.BaseSlotMultiplyLevel[s];

				tabletDeltaBuf[s] = permLvl;
				disableBuf[s] = baseDis;
				ignoreBuf[s] = baseIgn;
				mulBuf[s] = baseMul;
				mysticMulBuf[s] = 0;
				posLevelSumBuf[s] = Math.Max(0, permLvl);
				negLevelSumBuf[s] = Math.Max(0, -permLvl);
				int initPos = Math.Max(0, permLvl) + baseIgn;
				positiveBuffBuf[s] = initPos;
				otherPosBuffBuf[s] = initPos;
				negativePenaltyBuf[s] = Math.Max(0, -permLvl) + (baseDis * 2);
				displayedLevelBuf[s] = permLvl;
				effectiveLevelBuf[s] = 0;
				needleRootBuf[s] = -1;
				needleRootsMaskBuf[s] = 0UL;
				needleBypassMaskBuf[s] = 0UL;
				chalcedonyCatBuf[s] = -1;
				isActiveBuf[s] = 0;
				naturalCriteriaBuf[s] = 0;
				categoryMaskBuf[s] = 0u;
			}

			SlotItemSnapshot[] items = snapshot.Items;
			TalentRuleSnapshot talentRules = snapshot.TalentRules;
			bool hasEngravingTalent = talentRules.TabletEngravingCount > 0;
			int tabletPlacementScore = 0;
			hardViolationCount = 0;
			utilityEffectiveLevelSum = 0;

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
					hardViolationCount++;
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
									posLevelSumBuf[targetSlot] += eff.LevelParam;
									if (targetSlot != s) {
										otherPosBuffBuf[targetSlot] += eff.LevelParam;
									}
								} else if (eff.LevelParam < 0) {
									negativePenaltyBuf[targetSlot] += -eff.LevelParam;
									negLevelSumBuf[targetSlot] += -eff.LevelParam;
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
								if (targetSlot != s) {
									otherPosBuffBuf[targetSlot]++;
								}
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
				int preMulPrelim = item.BaseLevel + tabletDeltaBuf[s];
				int mulPrelim = mulBuf[s];
				int prelimDisplayedLevel = mulPrelim != 0 ? (preMulPrelim * mulPrelim) : preMulPrelim;
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
			Span<uint> whitePaperCopiedBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];

			int mysticCatId = snapshot.MysticCategoryId;
			ulong amplifiedMysticMask = 0UL;
			int mysticTagCount = 0;

			for (int convPass = 0; convPass < 2; convPass++) {
				for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
					dynamicComboBonusCounts[c] = 0;
				}
				for (int s = 0; s < unlockedSlots; s++) {
					chalcedonyCatBuf[s] = -1;
					needleRootBuf[s] = -1;
					needleRootsMaskBuf[s] = 0UL;
					needleBypassMaskBuf[s] = 0UL;
					whitePaperCopiedBuf[s] = 0u;
					if (isActiveBuf[s] != 0) {
						categoryMaskBuf[s] = items[slotToItemIndex[s]].StaticCategoryMask;
					} else {
						categoryMaskBuf[s] = 0u;
					}
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
						ResolveNeedleRootMasks(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							s,
							out ulong rootsMask,
							out ulong bypassMask,
							out int rootSlot
						);
						needleRootBuf[s] = rootSlot;
						needleRootsMaskBuf[s] = rootsMask;
						needleBypassMaskBuf[s] = bypassMask;
						if (rootsMask != 0UL) {
							uint rootCategories = 0u;
							ulong iterMask = rootsMask;
							while (iterMask != 0UL) {
								int rSlot = BitOperations.TrailingZeroCount(iterMask);
								iterMask &= iterMask - 1UL;
								rootCategories |= categoryMaskBuf[rSlot];
							}
							categoryMaskBuf[s] |= rootCategories;
							AccumulateMaskIntoComboCounts(rootCategories, dynamicComboBonusCounts);
						}
					}
				}

				for (int s = 0; s < unlockedSlots; s++) {
					if (isActiveBuf[s] == 0) {
						continue;
					}
					SlotItemSnapshot item = items[slotToItemIndex[s]];
					if (item.IsWhitePaperCharm) {
						SlotCoord coord = snapshot.Slots[s];
						int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
						int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
						uint leftMask = ResolveWhitePaperNeighborMask(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							categoryMaskBuf,
							leftSlot
						);
						uint rightMask = ResolveWhitePaperNeighborMask(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							categoryMaskBuf,
							rightSlot
						);
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

				int dynamicMysticTags = (mysticCatId >= 0 && mysticCatId < InventorySnapshot.MaxSupportedCategories)
					? dynamicComboBonusCounts[mysticCatId]
					: 0;
				mysticTagCount = snapshot.StaticMysticTagCount + dynamicMysticTags;
				for (int s = 0; s < unlockedSlots; s++) {
					mysticMulBuf[s] = 0;
				}
				amplifiedMysticMask = 0UL;

				if (mysticTagCount >= snapshot.MysticTier1Threshold) {
					ApplyMysticSlotMultiplier(
						snapshot.MysticSlot0,
						unlockedSlots,
						ref amplifiedMysticMask,
						mysticMulBuf
					);
					int[] extraTier1 = snapshot.ExtraMysticTier1Slots;
					for (int m = 0; m < extraTier1.Length; m++) {
						ApplyMysticSlotMultiplier(
							extraTier1[m],
							unlockedSlots,
							ref amplifiedMysticMask,
							mysticMulBuf
						);
					}
				}
				if (mysticTagCount >= snapshot.MysticTier2Threshold) {
					ApplyMysticSlotMultiplier(
						snapshot.MysticSlot1,
						unlockedSlots,
						ref amplifiedMysticMask,
						mysticMulBuf
					);
					int[] extraMysticSlots = snapshot.ExtraMysticTier2Slots;
					for (int m = 0; m < extraMysticSlots.Length; m++) {
						ApplyMysticSlotMultiplier(
							extraMysticSlots[m],
							unlockedSlots,
							ref amplifiedMysticMask,
							mysticMulBuf
						);
					}
				}

				bool activeStateFlipped = false;
				for (int s = 0; s < unlockedSlots; s++) {
					int itemIdx = slotToItemIndex[s];
					if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
						continue;
					}
					SlotItemSnapshot item = items[itemIdx];
					int totalMulPass = mulBuf[s] + mysticMulBuf[s];
					int preMulPass = item.BaseLevel + tabletDeltaBuf[s];
					int dispPass = totalMulPass != 0 ? (preMulPass * totalMulPass) : preMulPass;
					bool criteriaMetPass = ignoreBuf[s] > 0 || naturalCriteriaBuf[s] != 0;
					byte nextActive = (globalEnabled
						&& !item.IsDisabledByWeaponOrDual
						&& disableBuf[s] == 0
						&& dispPass >= 0
						&& criteriaMetPass) ? (byte)1 : (byte)0;
					if (nextActive != isActiveBuf[s]) {
						isActiveBuf[s] = nextActive;
						activeStateFlipped = true;
					}
				}
				if (!activeStateFlipped) {
					break;
				}
			}

			int totalScore = tabletPlacementScore;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				int baseMul = mulBuf[s];
				int mysticMul = mysticMulBuf[s];
				int totalMul = baseMul + mysticMul;
				int slotDelta = tabletDeltaBuf[s];

				if (itemIdx < 0) {
					int scaledSlotLevel = totalMul != 0 ? (slotDelta * totalMul) : slotDelta;
					displayedLevelBuf[s] = scaledSlotLevel;
					if (baseMul > 1 && slotDelta == 0) {
						totalScore -= (baseMul - 1) * WastedMultiplierPenalty;
					}
					if (totalMul != 0 && slotDelta != 0) {
						int extraScale = slotDelta * (totalMul - 1);
						if (extraScale > 0) {
							positiveBuffBuf[s] += extraScale;
							otherPosBuffBuf[s] += extraScale;
						} else if (extraScale < 0) {
							negativePenaltyBuf[s] += -extraScale;
						}
					}
					int posBuff = positiveBuffBuf[s];
					int negPen = negativePenaltyBuf[s];
					if (posBuff > 0) {
						int wastedPositive = Math.Max(scaledSlotLevel, posBuff);
						totalScore -= wastedPositive * WastedPositiveOnEmptySlotPenalty;
					} else if (posBuff == 0 && negPen > 0) {
						totalScore += negPen * NeutralizedNegativeLevelBonus;
					}
					continue;
				}

				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind != SlotItemKind.Artifact) {
					int scaledSlotLevel = totalMul != 0 ? (slotDelta * totalMul) : slotDelta;
					displayedLevelBuf[s] = scaledSlotLevel;
					if (baseMul > 1 && slotDelta == 0) {
						totalScore -= (baseMul - 1) * WastedMultiplierPenalty;
					}
					if (totalMul != 0 && slotDelta != 0) {
						int extraScale = slotDelta * (totalMul - 1);
						if (extraScale > 0) {
							positiveBuffBuf[s] += extraScale;
							otherPosBuffBuf[s] += extraScale;
						} else if (extraScale < 0) {
							negativePenaltyBuf[s] += -extraScale;
						}
					}
					int otherPos = otherPosBuffBuf[s];
					int negPen = negativePenaltyBuf[s];
					if (otherPos > 0) {
						totalScore -= otherPos * TabletOnBuffedSlotTieBreakerPenalty;
					} else if (positiveBuffBuf[s] > 0) {
						totalScore -= positiveBuffBuf[s] * WastedPositiveOnEmptySlotPenalty;
					} else if (negPen > 0) {
						totalScore += negPen * NeutralizedNegativeLevelBonus;
					}
					continue;
				}

				int preMul = item.BaseLevel + slotDelta;
				int displayedLevel = totalMul != 0 ? (preMul * totalMul) : preMul;
				displayedLevelBuf[s] = displayedLevel;
				int slotLevel = displayedLevel - item.BaseLevel;
				if (baseMul > 1 && preMul == 0) {
					totalScore -= (baseMul - 1) * WastedMultiplierPenalty;
				}
				if (totalMul != 0 && preMul != 0) {
					int extraScale = preMul * (totalMul - 1);
					if (extraScale > 0) {
						positiveBuffBuf[s] += extraScale;
					} else if (extraScale < 0) {
						negativePenaltyBuf[s] += -extraScale;
					}
				}

				bool isExcludedCompanion = snapshot.IsExcludedCompanionEmblem(item);
				bool isNonPriorityOrDisabled = item.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| item.IsDisabledByWeaponOrDual
					|| isExcludedCompanion;
				bool absorbsPenalty = isNonPriorityOrDisabled;
				if (absorbsPenalty) {
					int posBuff = positiveBuffBuf[s];
					int negPen = negativePenaltyBuf[s];
					if (posBuff > 0) {
						int wastedPositive = Math.Max(slotLevel, posBuff);
						totalScore -= wastedPositive * WastedPositiveOnEmptySlotPenalty;
					} else if (posBuff == 0 && negPen > 0) {
						totalScore += negPen * NeutralizedNegativeLevelBonus;
					}
				}

				bool naturalCriteriaMet = naturalCriteriaBuf[s] != 0;
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool criteriaSatisfied = criteriaIgnored || naturalCriteriaMet;
				bool isDisabled = !globalEnabled
					|| item.IsDisabledByWeaponOrDual
					|| disableBuf[s] > 0
					|| displayedLevel <= -1
					|| !criteriaSatisfied;

				if (!absorbsPenalty) {
					int rawPosLevels = posLevelSumBuf[s];
					int rawNegLevels = negLevelSumBuf[s];
					if (rawPosLevels > 0 && rawNegLevels > 0) {
						int effMul = totalMul != 0 ? Math.Abs(totalMul) : 1;
						int rawCanceled = Math.Min(rawPosLevels, rawNegLevels);
						int canceledPositiveBuff = rawCanceled * effMul;
						int positiveMul = totalMul > 0 ? totalMul : 1;
						int unpenalizedPreMul = item.BaseLevel + rawPosLevels;
						int unpenalizedEffective = Math.Clamp(
							unpenalizedPreMul * positiveMul,
							0,
							item.MaxLevel
						);
						int actualEffective = !isDisabled ? Math.Clamp(displayedLevel, 0, item.MaxLevel) : 0;
						int lostEffectiveLevels = item.MaxLevel > 0
							? Math.Max(0, unpenalizedEffective - actualEffective)
							: 0;

						if (!isDisabled && item.MaxLevel > 0 && lostEffectiveLevels == 0) {
							totalScore -= canceledPositiveBuff * OvercappedPlusMinusTieBreakerPenalty;
						} else {
							int collisionPenalty = canceledPositiveBuff * TabletPlusMinusCollisionPenalty;
							if (slotLevel > 0) {
								collisionPenalty += (rawNegLevels * effMul) * TabletMinusOnNetPositiveSlotPenalty;
							}
							if (lostEffectiveLevels > 0) {
								collisionPenalty += (int)Math.Round(
									lostEffectiveLevels
									* TabletCollisionEffectiveLevelLossPenalty
									* GetNormalizedWeight(item)
								);
							}
							totalScore -= collisionPenalty;
						}
					} else if (disableBuf[s] > 0 && positiveBuffBuf[s] > 0) {
						totalScore -= positiveBuffBuf[s] * TabletPlusMinusCollisionPenalty;
					}
				}

				if (isDisabled) {
					isActiveBuf[s] = 0;
					effectiveLevelBuf[s] = 0;
					categoryMaskBuf[s] = 0u;
					if (!absorbsPenalty) {
						hardViolationCount++;
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

				if (absorbsPenalty) {
					totalScore += SacrificeActiveTieBreakerScore;
					continue;
				}

				if (!item.IsAttackableCharm) {
					utilityEffectiveLevelSum += effectiveLevel;
				}

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
				if (slotLevel < 0) {
					artifactScore -= (-slotLevel) * 140;
				}
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

				bool isSacrificeOrExcluded = item.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| item.IsDisabledByWeaponOrDual
					|| snapshot.IsExcludedCompanionEmblem(item);

				if (item.HasDependencyTarget && !isSacrificeOrExcluded) {
					if (isActiveBuf[s] == 0) {
						totalScore -= UnlinkedGoldenNeedlePenalty;
					} else {
						ulong rootsMask = needleRootsMaskBuf[s];
						ulong bypassMask = needleBypassMaskBuf[s];
						int linkedRootCount = 0;
						while (rootsMask != 0UL) {
							int rootSlot = BitOperations.TrailingZeroCount(rootsMask);
							rootsMask &= rootsMask - 1UL;
							if (rootSlot < 0 || rootSlot >= unlockedSlots || isActiveBuf[rootSlot] == 0) {
								continue;
							}
							SlotItemSnapshot rootItem = items[slotToItemIndex[rootSlot]];
							double rootWeight = GetNeedleRootWeight(snapshot, rootItem);
							if (rootWeight <= 0.0) {
								continue;
							}
							linkedRootCount++;
							int needleLevel = effectiveLevelBuf[s];
							int rootLevel = effectiveLevelBuf[rootSlot];
							int fixedLinkBonus = (int)Math.Round(GoldenNeedleLinkBaseBonus * rootWeight);
							double ampFactor = item.IsCompassBreakthrough
								? 0.65
								: (1.0 + (0.25 * (needleLevel + 1)));
							double multiplicativeAmp = ampFactor
								* (GoldenNeedleRootLevelFactor * rootLevel)
								* rootWeight;
							bool bypassRarity = (bypassMask & (1UL << rootSlot)) != 0UL;
							if (item.HasDependencyCondition
								&& (bypassRarity || rootItem.RarityTier <= item.DependencyMaxRarity)) {
								multiplicativeAmp += 35.0 * (needleLevel + 1);
							}
							totalScore += fixedLinkBonus + (int)Math.Round(multiplicativeAmp);
						}
						if (linkedRootCount == 0) {
							totalScore -= UnlinkedGoldenNeedlePenalty;
						}
					}
				}

				if (item.IsWhitePaperCharm && !isSacrificeOrExcluded) {
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

				double weight = snapshot.IsExcludedCompanionEmblem(item) ? 0.0 : GetNormalizedWeight(item);
				if (item.IsCrystalOfHarmony && weight > 0.0) {
					ulong neighborMask = mooreNeighborMasks[s];
					int neighborSlotCount = BitOperations.PopCount(neighborMask);
					int sumNeighborLevels = 0;
					ulong iterMask = neighborMask;
					while (iterMask != 0UL) {
						int nSlot = BitOperations.TrailingZeroCount(iterMask);
						iterMask &= iterMask - 1UL;
						int nItemIdx = slotToItemIndex[nSlot];
						if (nItemIdx >= 0 && items[nItemIdx].Kind == SlotItemKind.Artifact) {
							sumNeighborLevels += Math.Min(displayedLevelBuf[nSlot], items[nItemIdx].MaxLevel);
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

			CombatStatDelta matchedArrangementDelta = CombatStatDelta.Zero;
			if (talentRules.ArrangementBonusStat > 0 && talentRules.ArrangementPatterns.Length > 0) {
				totalScore += EvaluateArrangementPatterns(
					snapshot,
					slotToItemIndex,
					out matchedArrangementDelta
				);
			}

			exactEstimatedDps = ComputeDpsBreakdownFromState(
				snapshot,
				slotToItemIndex,
				isActiveBuf,
				displayedLevelBuf,
				effectiveLevelBuf,
				needleRootsMaskBuf,
				needleBypassMaskBuf,
				chalcedonyCatBuf,
				dynamicComboBonusCounts,
				in matchedArrangementDelta,
				out double baseWeaponOnlyExactDps
			).TotalEstimatedDps;

			if (Math.Abs(exactEstimatedDps - baseWeaponOnlyExactDps) > 1e-6) {
				double dpsGainRatio = (exactEstimatedDps - baseWeaponOnlyExactDps)
					/ Math.Max(100.0, baseWeaponOnlyExactDps);
				double rawDpsScore = Math.Round(dpsGainRatio * DpsGainScoreMultiplier);
				totalScore += (int)Math.Clamp(rawDpsScore, -500_000_000.0, 500_000_000.0);
			}

			return totalScore;
		}

		internal static DpsBreakdown EvaluateDpsBreakdown(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			if (snapshot == null || snapshot.UnlockedSlots <= 0) {
				return DpsBreakdown.Empty;
			}

			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;

			Span<int> tabletDeltaBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mysticMulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> displayedLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> effectiveLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<ulong> needleRootsMaskBuf = stackalloc ulong[InventorySnapshot.MaxSupportedSlots];
			Span<ulong> needleBypassMaskBuf = stackalloc ulong[InventorySnapshot.MaxSupportedSlots];
			Span<int> chalcedonyCatBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isActiveBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> naturalCriteriaBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];
			Span<int> dynamicComboBonusCounts = stackalloc int[InventorySnapshot.MaxSupportedCategories];

			for (int s = 0; s < unlockedSlots; s++) {
				int permLvl = snapshot.BaseSlotLevel[s];
				tabletDeltaBuf[s] = permLvl;
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
				mysticMulBuf[s] = 0;
				displayedLevelBuf[s] = permLvl;
				effectiveLevelBuf[s] = 0;
				needleRootsMaskBuf[s] = 0UL;
				needleBypassMaskBuf[s] = 0UL;
				chalcedonyCatBuf[s] = -1;
				isActiveBuf[s] = 0;
				naturalCriteriaBuf[s] = 0;
				categoryMaskBuf[s] = 0u;
			}

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

			bool globalEnabled = snapshot.GlobalActiveValue > 0;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				naturalCriteriaBuf[s] = naturalCriteriaMet ? (byte)1 : (byte)0;
				bool criteriaSatisfied = ignoreBuf[s] > 0 || naturalCriteriaMet;
				int preMulPrelim = item.BaseLevel + tabletDeltaBuf[s];
				int mulPrelim = mulBuf[s];
				int prelimDisplayedLevel = mulPrelim != 0 ? (preMulPrelim * mulPrelim) : preMulPrelim;
				if (globalEnabled
					&& !item.IsDisabledByWeaponOrDual
					&& disableBuf[s] == 0
					&& prelimDisplayedLevel >= 0
					&& criteriaSatisfied) {
					isActiveBuf[s] = 1;
				}
			}

			int mysticCatId = snapshot.MysticCategoryId;
			ulong amplifiedMysticMask = 0UL;
			for (int convPass = 0; convPass < 2; convPass++) {
				for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
					dynamicComboBonusCounts[c] = 0;
				}
				for (int s = 0; s < unlockedSlots; s++) {
					chalcedonyCatBuf[s] = -1;
					needleRootsMaskBuf[s] = 0UL;
					needleBypassMaskBuf[s] = 0UL;
					categoryMaskBuf[s] = isActiveBuf[s] != 0
						? items[slotToItemIndex[s]].StaticCategoryMask
						: 0u;
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
						ResolveNeedleRootMasks(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							s,
							out ulong rootsMask,
							out ulong bypassMask,
							out _
						);
						needleRootsMaskBuf[s] = rootsMask;
						needleBypassMaskBuf[s] = bypassMask;
						if (rootsMask != 0UL) {
							uint rootCategories = 0u;
							ulong iterMask = rootsMask;
							while (iterMask != 0UL) {
								int rSlot = BitOperations.TrailingZeroCount(iterMask);
								iterMask &= iterMask - 1UL;
								rootCategories |= categoryMaskBuf[rSlot];
							}
							categoryMaskBuf[s] |= rootCategories;
							AccumulateMaskIntoComboCounts(rootCategories, dynamicComboBonusCounts);
						}
					}
				}

				for (int s = 0; s < unlockedSlots; s++) {
					if (isActiveBuf[s] == 0) {
						continue;
					}
					SlotItemSnapshot item = items[slotToItemIndex[s]];
					if (item.IsWhitePaperCharm) {
						SlotCoord coord = snapshot.Slots[s];
						int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
						int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
						uint leftMask = ResolveWhitePaperNeighborMask(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							categoryMaskBuf,
							leftSlot
						);
						uint rightMask = ResolveWhitePaperNeighborMask(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							categoryMaskBuf,
							rightSlot
						);
						uint copiedMask = item.WhitePaperMatchCount <= 1
							? (leftMask | rightMask)
							: (leftMask & rightMask);
						if (copiedMask != 0u) {
							categoryMaskBuf[s] |= copiedMask;
							AccumulateMaskIntoComboCounts(copiedMask, dynamicComboBonusCounts);
						}
					}
				}

				int dynamicMysticTags = (mysticCatId >= 0 && mysticCatId < InventorySnapshot.MaxSupportedCategories)
					? dynamicComboBonusCounts[mysticCatId]
					: 0;
				int mysticTagCount = snapshot.StaticMysticTagCount + dynamicMysticTags;
				for (int s = 0; s < unlockedSlots; s++) {
					mysticMulBuf[s] = 0;
				}
				amplifiedMysticMask = 0UL;

				if (mysticTagCount >= snapshot.MysticTier1Threshold) {
					ApplyMysticSlotMultiplier(
						snapshot.MysticSlot0,
						unlockedSlots,
						ref amplifiedMysticMask,
						mysticMulBuf
					);
					int[] extraTier1 = snapshot.ExtraMysticTier1Slots;
					for (int m = 0; m < extraTier1.Length; m++) {
						ApplyMysticSlotMultiplier(
							extraTier1[m],
							unlockedSlots,
							ref amplifiedMysticMask,
							mysticMulBuf
						);
					}
				}
				if (mysticTagCount >= snapshot.MysticTier2Threshold) {
					ApplyMysticSlotMultiplier(
						snapshot.MysticSlot1,
						unlockedSlots,
						ref amplifiedMysticMask,
						mysticMulBuf
					);
					int[] extraMysticSlots = snapshot.ExtraMysticTier2Slots;
					for (int m = 0; m < extraMysticSlots.Length; m++) {
						ApplyMysticSlotMultiplier(
							extraMysticSlots[m],
							unlockedSlots,
							ref amplifiedMysticMask,
							mysticMulBuf
						);
					}
				}

				bool activeStateFlipped = false;
				for (int s = 0; s < unlockedSlots; s++) {
					int itemIdx = slotToItemIndex[s];
					int totalMul = mulBuf[s] + mysticMulBuf[s];
					if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
						int preMulEmpty = tabletDeltaBuf[s];
						displayedLevelBuf[s] = totalMul != 0 ? (preMulEmpty * totalMul) : preMulEmpty;
						continue;
					}
					SlotItemSnapshot item = items[itemIdx];
					int preMul = item.BaseLevel + tabletDeltaBuf[s];
					int displayedLevel = totalMul != 0 ? (preMul * totalMul) : preMul;
					displayedLevelBuf[s] = displayedLevel;
					bool criteriaSatisfied = ignoreBuf[s] > 0 || naturalCriteriaBuf[s] != 0;
					byte nextActive = (globalEnabled
						&& !item.IsDisabledByWeaponOrDual
						&& disableBuf[s] == 0
						&& displayedLevel >= 0
						&& criteriaSatisfied) ? (byte)1 : (byte)0;
					if (nextActive != isActiveBuf[s]) {
						isActiveBuf[s] = nextActive;
						activeStateFlipped = true;
					}
					effectiveLevelBuf[s] = nextActive != 0 ? Math.Clamp(displayedLevel, 0, item.MaxLevel) : 0;
				}
				if (!activeStateFlipped) {
					break;
				}
			}

			CombatStatDelta matchedArrangementDelta = CombatStatDelta.Zero;
			if (snapshot.TalentRules.ArrangementBonusStat > 0
				&& snapshot.TalentRules.ArrangementPatterns.Length > 0) {
				EvaluateArrangementPatterns(snapshot, slotToItemIndex, out matchedArrangementDelta);
			}

			return ComputeDpsBreakdownFromState(
				snapshot,
				slotToItemIndex,
				isActiveBuf,
				displayedLevelBuf,
				effectiveLevelBuf,
				needleRootsMaskBuf,
				needleBypassMaskBuf,
				chalcedonyCatBuf,
				dynamicComboBonusCounts,
				in matchedArrangementDelta,
				out _
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint BuildCompanionChaosRowMask(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<byte> isActiveBuf
		) {
			uint mask = 0u;
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			for (int s = 0; s < unlockedSlots; s++) {
				if (isActiveBuf[s] == 0) {
					continue;
				}
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				if (item.Kind == SlotItemKind.Artifact
					&& item.IsCompanionChaosCharm
					&& item.PriorityTier != ArtifactPriorityTier.Sacrifice
					&& !snapshot.IsExcludedCompanionEmblem(item)) {
					int row = snapshot.Slots[s].Y;
					if ((uint)row < 32u) {
						mask |= 1u << row;
					}
				}
			}
			return mask;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static double ComputeFollowerAttackSpeedMultiplier(double followerAttackSpeedPercent) {
			return Math.Clamp(1.0 + (followerAttackSpeedPercent * 0.01), 0.25, 6.0);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static double ComputeFollowerCritMultiplier(double followerCritChancePercent) {
			double prob = Math.Clamp(followerCritChancePercent * 0.01, 0.0, 1.0);
			return 1.0 + (prob * 0.5);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool IsCompanionAttackItem(SlotItemSnapshot item) {
			return item.IsCompanionAttackCharm || (item.IsCompanionCharm && item.IsAttackableCharm);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static DpsBreakdown ComputeDpsBreakdownFromState(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<byte> isActiveBuf,
			ReadOnlySpan<int> displayedLevelBuf,
			ReadOnlySpan<int> effectiveLevelBuf,
			ReadOnlySpan<ulong> needleRootsMaskBuf,
			ReadOnlySpan<ulong> needleBypassMaskBuf,
			ReadOnlySpan<int> chalcedonyCatBuf,
			ReadOnlySpan<int> dynamicComboBonusCounts,
			in CombatStatDelta matchedArrangementDelta,
			out double baseWeaponOnlyExactDps
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;

			bool hasOwnedGoldenNeedle = false;
			bool hasOwnedHarmonyCrystal = false;
			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot cand = items[i];
				if (cand.Kind != SlotItemKind.Artifact
					|| cand.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| cand.IsDisabledByWeaponOrDual
					|| snapshot.IsExcludedCompanionEmblem(cand)) {
					continue;
				}
				if (cand.HasDependencyTarget) {
					hasOwnedGoldenNeedle = true;
				}
				if (cand.IsCrystalOfHarmony) {
					hasOwnedHarmonyCrystal = true;
				}
			}

			Span<int> needleBonusPctSumBySlot = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isLinkedNeedleRootSlot = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			for (int s = 0; s < unlockedSlots; s++) {
				needleBonusPctSumBySlot[s] = 0;
				isLinkedNeedleRootSlot[s] = 0;
			}

			bool hasActiveGoldenNeedle = false;
			double activeNeedlePercentSum = 0.0;
			int activeNeedleCount = 0;

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
					|| needleItem.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| snapshot.IsExcludedCompanionEmblem(needleItem)) {
					continue;
				}
				ulong rootsMask = needleRootsMaskBuf[s];
				ulong bypassMask = needleBypassMaskBuf[s];
				while (rootsMask != 0UL) {
					int rootSlot = BitOperations.TrailingZeroCount(rootsMask);
					rootsMask &= rootsMask - 1UL;
					if (rootSlot >= 0 && rootSlot < unlockedSlots && isActiveBuf[rootSlot] != 0) {
						int rootItemIdx = slotToItemIndex[rootSlot];
						if (rootItemIdx >= 0) {
							SlotItemSnapshot rootItem = items[rootItemIdx];
							if (rootItem.Kind == SlotItemKind.Artifact
								&& rootItem.IsAttackableCharm
								&& rootItem.PriorityTier != ArtifactPriorityTier.Sacrifice
								&& !snapshot.IsExcludedCompanionEmblem(rootItem)) {
								int needleLv = effectiveLevelBuf[s];
								bool bypassRarity = (bypassMask & (1UL << rootSlot)) != 0UL;
								int bonusPct = needleItem.GetNeedleBonusPercent(
									needleLv,
									rootItem.RarityTier,
									bypassRarity
								);
								needleBonusPctSumBySlot[rootSlot] += bonusPct;
								isLinkedNeedleRootSlot[rootSlot] = 1;
								hasActiveGoldenNeedle = true;
								activeNeedlePercentSum += bonusPct;
								activeNeedleCount++;
							}
						}
					}
				}
			}

			uint chaosRowMask = BuildCompanionChaosRowMask(snapshot, slotToItemIndex, isActiveBuf);
			ulong[] mooreNeighborMasks = snapshot.MooreNeighborMasks;
			ComboCategoryInfo[] categories = snapshot.ComboCategories;
			int unlimitedComboStat = snapshot.TalentRules.UnlimitedComboStat;
			bool hasActiveHarmonyCrystal = false;
			int harmonyNeighborLevelSum = 0;
			double totalHarmonyAmpPercent = 0.0;

			if (snapshot.CombatBaseline.HasDetailedWeaponProfile) {
				baseWeaponOnlyExactDps = snapshot.CombatBaseline.BaseWeaponDps;
				CombatStatDelta totalDelta = matchedArrangementDelta;
				double customFinalDamageMultiplier = 1.0;

				int top6CharmCount = 0;
				int topLimit = Math.Min(6, unlockedSlots);
				for (int topSlot = 0; topSlot < topLimit; topSlot++) {
					int tIdx = slotToItemIndex[topSlot];
					if (tIdx >= 0 && items[tIdx].Kind == SlotItemKind.Artifact) {
						top6CharmCount++;
					}
				}

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
						|| snapshot.IsExcludedCompanionEmblem(item)) {
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

					if (item.IsWoodenBoxCharm && top6CharmCount > 0) {
						int apPerCharm = item.GetWoodenBoxApPerCharm(effLv);
						if (apPerCharm != 0) {
							int totalAp = apPerCharm * top6CharmCount;
							totalDelta = totalDelta.Add(new CombatStatDelta(
								fireDamage: totalAp,
								iceDamage: totalAp,
								lightningDamage: totalAp
							));
						}
					}

					if (item.IsFireIcePositionCharm) {
						bool isLeftHalf = snapshot.Slots[s].X <= 2
							&& snapshot.Slots[s].X < Math.Max(1, snapshot.Width / 2);
						item.GetFireIceStats(
							effLv,
							isLeftHalf,
							out byte lElem,
							out int lDelta,
							out byte rElem,
							out int rDelta
						);
						if (lDelta != 0) {
							totalDelta = totalDelta.AddElementalByKind(lElem, lDelta);
						}
						if (rDelta != 0) {
							totalDelta = totalDelta.AddElementalByKind(rElem, rDelta);
						}
					}

					if (item.IsCrystalOfHarmony) {
						hasActiveHarmonyCrystal = true;
						ulong neighborMask = mooreNeighborMasks[s];
						int sumNeighborLevels = 0;
						while (neighborMask != 0UL) {
							int nSlot = BitOperations.TrailingZeroCount(neighborMask);
							neighborMask &= neighborMask - 1UL;
							int nItemIdx = slotToItemIndex[nSlot];
							if (nItemIdx >= 0 && items[nItemIdx].Kind == SlotItemKind.Artifact) {
								sumNeighborLevels += Math.Min(displayedLevelBuf[nSlot], items[nItemIdx].MaxLevel);
							}
						}
						harmonyNeighborLevelSum += sumNeighborLevels;
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

				WeaponCombatEvaluation weaponEvalBase = snapshot.CombatBaseline.WeaponProfile.Evaluate(
					in totalDelta,
					customFinalDamageMultiplier
				);
				WeaponCombatEvaluation weaponEvalWithHarmony = totalHarmonyAmpPercent != 0.0
					? snapshot.CombatBaseline.WeaponProfile.Evaluate(
						totalDelta.Add(new CombatStatDelta(allDamageBonus: (float)totalHarmonyAmpPercent)),
						customFinalDamageMultiplier
					)
					: weaponEvalBase;

				double followerAsMult = ComputeFollowerAttackSpeedMultiplier(
					snapshot.CombatBaseline.BaseFollowerAttackSpeed + totalDelta.FollowerAttackSpeed
				);
				double followerCritMult = ComputeFollowerCritMultiplier(
					snapshot.CombatBaseline.BaseFollowerCritChancePercent + totalDelta.FollowerCritChancePercent
				);
				double followerDmgBonus = snapshot.CombatBaseline.BaseFollowerDamageBonus
					+ totalDelta.FollowerDamageBonus;
				double compAllDmgBaseFactor = Math.Max(
					0.1,
					1.0 + ((snapshot.CombatBaseline.WeaponProfile.BaseAllDamageBonus + totalDelta.AllDamageBonus)
						* 0.01)
				);
				double compAllDmgHarmFactor = Math.Max(
					0.1,
					1.0 + ((snapshot.CombatBaseline.WeaponProfile.BaseAllDamageBonus
						+ totalDelta.AllDamageBonus
						+ totalHarmonyAmpPercent) * 0.01)
				);
				double compSharedPost = snapshot.CombatBaseline.WeaponProfile.GlobalPostMultiplier
					* customFinalDamageMultiplier;

				double unamplifiedCharmRawDps = 0.0;
				double unamplifiedCharmEffectiveBase = 0.0;
				double amplifiedCharmEffectiveBase = 0.0;
				double amplifiedCharmEffectiveHarmony = 0.0;
				double detailedLinkedRootUnampRawDps = 0.0;
				double detailedLinkedRootNeedleRawGain = 0.0;

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
						|| snapshot.IsExcludedCompanionEmblem(item)
						|| !item.IsAttackableCharm) {
						continue;
					}

					int effLv = effectiveLevelBuf[s];
					double elemStatBase = weaponEvalBase.ResolveElementalStat(item.CharmScalingElementKind);
					double rawCharmDpsBase = item.GetAttackRawDpsAtLevelWithElement(effLv, elemStatBase);
					int needlePct = needleBonusPctSumBySlot[s];

					if (IsCompanionAttackItem(item)) {
						int row = snapshot.Slots[s].Y;
						double chaosBonus = ((uint)row < 32u && (chaosRowMask & (1u << row)) != 0u)
							? CompanionChaosRowDpsBonusPercent
							: 0.0;
						double compTail = followerAsMult * followerCritMult * compSharedPost;
						double compBase = rawCharmDpsBase
							* Math.Max(0.1, 1.0 + ((followerDmgBonus + chaosBonus) * 0.01))
							* compAllDmgBaseFactor
							* compTail;
						double compNeedleBase = rawCharmDpsBase
							* Math.Max(0.1, 1.0 + ((followerDmgBonus + chaosBonus + needlePct) * 0.01))
							* compAllDmgBaseFactor
							* compTail;
						unamplifiedCharmRawDps += rawCharmDpsBase;
						unamplifiedCharmEffectiveBase += compBase;
						amplifiedCharmEffectiveBase += compNeedleBase;
						if (totalHarmonyAmpPercent != 0.0) {
							double elemHarm = weaponEvalWithHarmony.ResolveElementalStat(item.CharmScalingElementKind);
							double rawHarm = item.GetAttackRawDpsAtLevelWithElement(effLv, elemHarm);
							amplifiedCharmEffectiveHarmony += rawHarm
								* Math.Max(0.1, 1.0 + ((followerDmgBonus + chaosBonus + needlePct) * 0.01))
								* compAllDmgHarmFactor
								* compTail;
						} else {
							amplifiedCharmEffectiveHarmony += compNeedleBase;
						}
					} else {
						double unampFactor = Math.Max(0.1, 1.0 + (weaponEvalBase.LiveCharmDamageBonusPercent * 0.01));
						double ampFactor = Math.Max(
							0.1,
							1.0 + ((weaponEvalBase.LiveCharmDamageBonusPercent + needlePct) * 0.01)
						);
						double playerCharmBase = rawCharmDpsBase * unampFactor * weaponEvalBase.CharmPostMultiplier;
						double playerCharmNeedle = rawCharmDpsBase * ampFactor * weaponEvalBase.CharmPostMultiplier;
						unamplifiedCharmRawDps += rawCharmDpsBase;
						unamplifiedCharmEffectiveBase += playerCharmBase;
						amplifiedCharmEffectiveBase += playerCharmNeedle;
						if (totalHarmonyAmpPercent != 0.0) {
							double elemHarm = weaponEvalWithHarmony.ResolveElementalStat(item.CharmScalingElementKind);
							double rawHarm = item.GetAttackRawDpsAtLevelWithElement(effLv, elemHarm);
							double ampFactorHarm = Math.Max(
								0.1,
								1.0 + ((weaponEvalWithHarmony.LiveCharmDamageBonusPercent + needlePct) * 0.01)
							);
							amplifiedCharmEffectiveHarmony += rawHarm
								* ampFactorHarm
								* weaponEvalWithHarmony.CharmPostMultiplier;
						} else {
							amplifiedCharmEffectiveHarmony += playerCharmNeedle;
						}
					}

					if (isLinkedNeedleRootSlot[s] != 0) {
						detailedLinkedRootUnampRawDps += rawCharmDpsBase;
						detailedLinkedRootNeedleRawGain += rawCharmDpsBase * (needlePct * 0.01);
					}
				}

				double detailedBaseRawDps = weaponEvalBase.WeaponAndAddonRawDps + unamplifiedCharmRawDps;
				double detailedBaseEffectiveDps = weaponEvalBase.WeaponAndAddonEffectiveDps
					+ unamplifiedCharmEffectiveBase;
				double detailedGeneralMult = detailedBaseRawDps > 1e-6
					? (detailedBaseEffectiveDps / detailedBaseRawDps)
					: weaponEvalBase.CharmPostMultiplier;

				double detailedNeedleAmpPct = 0.0;
				if (hasActiveGoldenNeedle) {
					if (detailedLinkedRootUnampRawDps > 1e-6) {
						detailedNeedleAmpPct = (detailedLinkedRootNeedleRawGain * 100.0)
							/ detailedLinkedRootUnampRawDps;
					} else if (activeNeedleCount > 0) {
						detailedNeedleAmpPct = activeNeedlePercentSum / activeNeedleCount;
					}
				}

				double detailedNeedleAddedDps = Math.Max(
					0.0,
					amplifiedCharmEffectiveBase - unamplifiedCharmEffectiveBase
				);
				double detailedDpsWithNeedle = detailedBaseEffectiveDps + detailedNeedleAddedDps;
				double detailedTotalDps = Math.Max(
					0.0,
					totalHarmonyAmpPercent != 0.0
						? (weaponEvalWithHarmony.WeaponAndAddonEffectiveDps + amplifiedCharmEffectiveHarmony)
						: detailedDpsWithNeedle
				);
				double detailedHarmonyAddedDps = Math.Max(0.0, detailedTotalDps - detailedDpsWithNeedle);

				return new DpsBreakdown(
					detailedBaseRawDps,
					detailedGeneralMult,
					detailedBaseEffectiveDps,
					hasActiveGoldenNeedle,
					detailedNeedleAmpPct,
					detailedNeedleAddedDps,
					hasActiveHarmonyCrystal,
					harmonyNeighborLevelSum,
					totalHarmonyAmpPercent,
					detailedHarmonyAddedDps,
					detailedTotalDps,
					hasOwnedGoldenNeedle,
					hasOwnedHarmonyCrystal
				);
			}

			double staticComboStatPercent = 0.0;
			for (int i = 0; i < categories.Length; i++) {
				staticComboStatPercent += categories[i].EvaluateStatBonusPercent(
					categories[i].StaticCount,
					unlimitedComboStat
				);
			}
			double baseWeaponRawDps = snapshot.CombatBaseline.ComputeBaseWeaponRawDps();
			double bucketCCritMultiplier = snapshot.CombatBaseline.ComputeCritExpectedMultiplier();
			double baseStaticStatMultiplier = 1.0
				+ ((snapshot.CombatBaseline.ElementalDamageBonusPercent + staticComboStatPercent) * 0.01);
			baseWeaponOnlyExactDps = baseWeaponRawDps * baseStaticStatMultiplier * bucketCCritMultiplier;

			double unamplifiedPlayerCharmRawDps = 0.0;
			double amplifiedPlayerCharmRawDps = 0.0;
			double unamplifiedCompanionRawDps = 0.0;
			double amplifiedCompanionRawDps = 0.0;
			double linkedRootUnamplifiedRawDps = 0.0;
			double linkedRootAmplifiedRawDps = 0.0;
			double statBonusPercentSum = snapshot.CombatBaseline.ElementalDamageBonusPercent
				+ matchedArrangementDelta.AllDamageBonus
				+ matchedArrangementDelta.PhysicalDamage
				+ matchedArrangementDelta.FireDamage
				+ matchedArrangementDelta.IceDamage
				+ matchedArrangementDelta.LightningDamage;
			double followerDmgSum = snapshot.CombatBaseline.BaseFollowerDamageBonus
				+ matchedArrangementDelta.FollowerDamageBonus;
			double followerAsSum = snapshot.CombatBaseline.BaseFollowerAttackSpeed
				+ matchedArrangementDelta.FollowerAttackSpeed;
			double followerCritSum = snapshot.CombatBaseline.BaseFollowerCritChancePercent
				+ matchedArrangementDelta.FollowerCritChancePercent;
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
				if (item.Kind != SlotItemKind.Artifact
					|| item.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| snapshot.IsExcludedCompanionEmblem(item)) {
					continue;
				}

				int effLv = effectiveLevelBuf[s];
				CombatStatDelta itemDelta = item.GetStatDeltaAtLevel(effLv);
				followerDmgSum += itemDelta.FollowerDamageBonus;
				followerAsSum += itemDelta.FollowerAttackSpeed;
				followerCritSum += itemDelta.FollowerCritChancePercent;

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
						statBonusPercentSum += item.GetRowElementalBonus(effLv);
					}
				}

				if (item.IsCrystalOfHarmony) {
					hasActiveHarmonyCrystal = true;
					ulong neighborMask = mooreNeighborMasks[s];
					int sumNeighborLevels = 0;
					while (neighborMask != 0UL) {
						int nSlot = BitOperations.TrailingZeroCount(neighborMask);
						neighborMask &= neighborMask - 1UL;
						int nItemIdx = slotToItemIndex[nSlot];
						if (nItemIdx >= 0 && items[nItemIdx].Kind == SlotItemKind.Artifact) {
							sumNeighborLevels += Math.Min(displayedLevelBuf[nSlot], items[nItemIdx].MaxLevel);
						}
					}
					harmonyNeighborLevelSum += sumNeighborLevels;
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
				CombatStatDelta cDelta = info.EvaluateStatDelta(totalCount, unlimitedComboStat);
				followerDmgSum += cDelta.FollowerDamageBonus;
				followerAsSum += cDelta.FollowerAttackSpeed;
				followerCritSum += cDelta.FollowerCritChancePercent;
			}

			double legFollowerAsMult = ComputeFollowerAttackSpeedMultiplier(followerAsSum);
			double legFollowerCritMult = ComputeFollowerCritMultiplier(followerCritSum);

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
					|| snapshot.IsExcludedCompanionEmblem(item)
					|| !item.IsAttackableCharm) {
					continue;
				}

				int effLv = effectiveLevelBuf[s];
				double rawAttackDps = item.GetAttackRawDpsAtLevel(effLv);
				int needlePct = needleBonusPctSumBySlot[s];
				if (IsCompanionAttackItem(item)) {
					int row = snapshot.Slots[s].Y;
					double chaosBonus = ((uint)row < 32u && (chaosRowMask & (1u << row)) != 0u)
						? CompanionChaosRowDpsBonusPercent
						: 0.0;
					double compUnamp = rawAttackDps
						* legFollowerAsMult
						* legFollowerCritMult
						* Math.Max(0.1, 1.0 + ((followerDmgSum + chaosBonus) * 0.01));
					double compAmp = rawAttackDps
						* legFollowerAsMult
						* legFollowerCritMult
						* Math.Max(0.1, 1.0 + ((followerDmgSum + chaosBonus + needlePct) * 0.01));
					unamplifiedCompanionRawDps += compUnamp;
					amplifiedCompanionRawDps += compAmp;
					if (isLinkedNeedleRootSlot[s] != 0) {
						linkedRootUnamplifiedRawDps += rawAttackDps;
						linkedRootAmplifiedRawDps += rawAttackDps * (1.0 + (needlePct * 0.01));
					}
				} else {
					double needleMult = 1.0 + (needlePct * 0.01);
					double amplifiedAttackDps = rawAttackDps * needleMult;
					unamplifiedPlayerCharmRawDps += rawAttackDps;
					amplifiedPlayerCharmRawDps += amplifiedAttackDps;
					if (isLinkedNeedleRootSlot[s] != 0) {
						linkedRootUnamplifiedRawDps += rawAttackDps;
						linkedRootAmplifiedRawDps += amplifiedAttackDps;
					}
				}
			}

			double bucketBStatMultiplier = 1.0 + (statBonusPercentSum * 0.01);
			double generalStatMultiplier = bucketBStatMultiplier * bucketCCritMultiplier * finalDamageMultiplier;
			double companionStatMultiplier = bucketBStatMultiplier * finalDamageMultiplier;
			double weaponAndCharmBaseDps = baseWeaponRawDps
				+ unamplifiedPlayerCharmRawDps
				+ unamplifiedCompanionRawDps;
			double baseEffectiveDps = ((baseWeaponRawDps + unamplifiedPlayerCharmRawDps) * generalStatMultiplier)
				+ (unamplifiedCompanionRawDps * companionStatMultiplier);

			double goldenNeedleAmpPercent = 0.0;
			if (hasActiveGoldenNeedle) {
				if (linkedRootUnamplifiedRawDps > 1e-6) {
					goldenNeedleAmpPercent =
						((linkedRootAmplifiedRawDps - linkedRootUnamplifiedRawDps) * 100.0)
						/ linkedRootUnamplifiedRawDps;
				} else if (activeNeedleCount > 0) {
					goldenNeedleAmpPercent = activeNeedlePercentSum / activeNeedleCount;
				}
			}

			double goldenNeedleAddedDps = Math.Max(
				0.0,
				((amplifiedPlayerCharmRawDps - unamplifiedPlayerCharmRawDps) * generalStatMultiplier)
					+ ((amplifiedCompanionRawDps - unamplifiedCompanionRawDps) * companionStatMultiplier)
			);
			double dpsWithNeedle = baseEffectiveDps + goldenNeedleAddedDps;
			double harmonyAddedDps = Math.Max(0.0, dpsWithNeedle * (totalHarmonyAmpPercent * 0.01));
			double totalEstimatedDps = Math.Max(0.0, dpsWithNeedle + harmonyAddedDps);

			return new DpsBreakdown(
				weaponAndCharmBaseDps,
				weaponAndCharmBaseDps > 1e-6 ? (baseEffectiveDps / weaponAndCharmBaseDps) : generalStatMultiplier,
				baseEffectiveDps,
				hasActiveGoldenNeedle,
				goldenNeedleAmpPercent,
				goldenNeedleAddedDps,
				hasActiveHarmonyCrystal,
				harmonyNeighborLevelSum,
				totalHarmonyAmpPercent,
				harmonyAddedDps,
				totalEstimatedDps,
				hasOwnedGoldenNeedle,
				hasOwnedHarmonyCrystal
			);
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

			Span<int> tabletDeltaBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mysticMulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> finalSlotLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> naturalCriteriaBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isActiveBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];
			Span<int> connectedTargetBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> engravingReadyBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<int> dynamicComboBonusCounts = stackalloc int[InventorySnapshot.MaxSupportedCategories];

			for (int s = 0; s < unlockedSlots; s++) {
				int permLvl = snapshot.BaseSlotLevel[s];
				tabletDeltaBuf[s] = permLvl;
				finalSlotLevelBuf[s] = permLvl;
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
				mysticMulBuf[s] = 0;
				naturalCriteriaBuf[s] = 0;
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

			bool globalEnabled = snapshot.GlobalActiveValue > 0;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
					continue;
				}
				SlotItemSnapshot item = items[itemIdx];
				int preMulPrelim = item.BaseLevel + tabletDeltaBuf[s];
				int mulPrelim = mulBuf[s];
				int prelimDisplayed = mulPrelim != 0 ? (preMulPrelim * mulPrelim) : preMulPrelim;
				bool naturalCriteriaMet = IsArtifactConstraintMet(snapshot, slotToItemIndex, s, item.ConstraintType);
				naturalCriteriaBuf[s] = naturalCriteriaMet ? (byte)1 : (byte)0;
				bool criteriaIgnored = ignoreBuf[s] > 0;
				bool negDeactivated = prelimDisplayed <= -1;
				bool disableDeactivated = disableBuf[s] > 0 || item.IsDisabledByWeaponOrDual || !globalEnabled;
				bool criteriaDeactivated = !criteriaIgnored && !naturalCriteriaMet;
				if (!negDeactivated && !disableDeactivated && !criteriaDeactivated) {
					isActiveBuf[s] = 1;
					categoryMaskBuf[s] = item.StaticCategoryMask;
				}
			}

			int mysticCatId = snapshot.MysticCategoryId;
			ulong amplifiedMysticMask = 0UL;

			for (int convPass = 0; convPass < 2; convPass++) {
				for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
					dynamicComboBonusCounts[c] = 0;
				}
				for (int s = 0; s < unlockedSlots; s++) {
					connectedTargetBuf[s] = -1;
					if (isActiveBuf[s] != 0) {
						categoryMaskBuf[s] = items[slotToItemIndex[s]].StaticCategoryMask;
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
						ResolveNeedleRootMasks(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							s,
							out ulong rootsMask,
							out _,
							out int rootSlot
						);
						connectedTargetBuf[s] = rootSlot;
						if (rootsMask != 0UL) {
							uint rootCategories = 0u;
							ulong iterMask = rootsMask;
							while (iterMask != 0UL) {
								int rSlot = BitOperations.TrailingZeroCount(iterMask);
								iterMask &= iterMask - 1UL;
								rootCategories |= categoryMaskBuf[rSlot];
							}
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
						uint leftMask = ResolveWhitePaperNeighborMask(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							categoryMaskBuf,
							leftSlot
						);
						uint rightMask = ResolveWhitePaperNeighborMask(
							snapshot,
							slotToItemIndex,
							isActiveBuf,
							categoryMaskBuf,
							rightSlot
						);
						uint copiedMask = items[itemIdx].WhitePaperMatchCount <= 1
							? (leftMask | rightMask)
							: (leftMask & rightMask);
						if (copiedMask != 0u) {
							categoryMaskBuf[s] |= copiedMask;
							AccumulateMaskIntoComboCounts(copiedMask, dynamicComboBonusCounts);
						}
					}
				}

				int dynamicMysticTags = (mysticCatId >= 0 && mysticCatId < InventorySnapshot.MaxSupportedCategories)
					? dynamicComboBonusCounts[mysticCatId]
					: 0;
				int mysticTagCount = snapshot.StaticMysticTagCount + dynamicMysticTags;
				for (int s = 0; s < unlockedSlots; s++) {
					mysticMulBuf[s] = 0;
				}
				amplifiedMysticMask = 0UL;

				if (mysticTagCount >= snapshot.MysticTier1Threshold) {
					ApplyMysticSlotMultiplier(
						snapshot.MysticSlot0,
						unlockedSlots,
						ref amplifiedMysticMask,
						mysticMulBuf
					);
					int[] extraTier1 = snapshot.ExtraMysticTier1Slots;
					for (int m = 0; m < extraTier1.Length; m++) {
						ApplyMysticSlotMultiplier(
							extraTier1[m],
							unlockedSlots,
							ref amplifiedMysticMask,
							mysticMulBuf
						);
					}
				}
				if (mysticTagCount >= snapshot.MysticTier2Threshold) {
					ApplyMysticSlotMultiplier(
						snapshot.MysticSlot1,
						unlockedSlots,
						ref amplifiedMysticMask,
						mysticMulBuf
					);
					int[] extraMysticSlots = snapshot.ExtraMysticTier2Slots;
					for (int m = 0; m < extraMysticSlots.Length; m++) {
						ApplyMysticSlotMultiplier(
							extraMysticSlots[m],
							unlockedSlots,
							ref amplifiedMysticMask,
							mysticMulBuf
						);
					}
				}

				bool activeStateFlipped = false;
				for (int s = 0; s < unlockedSlots; s++) {
					int itemIdx = slotToItemIndex[s];
					if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
						continue;
					}
					SlotItemSnapshot item = items[itemIdx];
					int totalMulPass = mulBuf[s] + mysticMulBuf[s];
					int preMulPass = item.BaseLevel + tabletDeltaBuf[s];
					int dispPass = totalMulPass != 0 ? (preMulPass * totalMulPass) : preMulPass;
					bool criteriaMetPass = ignoreBuf[s] > 0 || naturalCriteriaBuf[s] != 0;
					byte nextActive = (globalEnabled
						&& !item.IsDisabledByWeaponOrDual
						&& disableBuf[s] == 0
						&& dispPass >= 0
						&& criteriaMetPass) ? (byte)1 : (byte)0;
					if (nextActive != isActiveBuf[s]) {
						isActiveBuf[s] = nextActive;
						activeStateFlipped = true;
					}
				}
				if (!activeStateFlipped) {
					break;
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItemIndex[s];
				int totalMul = mulBuf[s] + mysticMulBuf[s];
				if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.Artifact) {
					int preEmpty = tabletDeltaBuf[s];
					int dispEmpty = totalMul != 0 ? (preEmpty * totalMul) : preEmpty;
					details[s] = new SlotEvaluationDetail(
						s,
						itemIdx,
						dispEmpty,
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
				int preMul = item.BaseLevel + tabletDeltaBuf[s];
				int displayedLevel = totalMul != 0 ? (preMul * totalMul) : preMul;
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

			int requiredReachableCount = 0;
			bool hasNorthOffset = false;
			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot cand = items[i];
				if (cand.Kind == SlotItemKind.Artifact
					&& cand.HasDependencyTarget
					&& !cand.IsDisabledByWeaponOrDual
					&& cand.PriorityTier != ArtifactPriorityTier.Sacrifice) {
					requiredReachableCount++;
					(sbyte X, sbyte Y)[] offs = cand.DependencyParentOffsets;
					for (int p = 0; p < offs.Length; p++) {
						if (offs[p].X == 0 && offs[p].Y == -1) {
							hasNorthOffset = true;
							break;
						}
					}
				}
			}

			if (requiredReachableCount == 0) {
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
						bool isBlocked = hasNorthOffset
							? (snapshot.CoordToSlotIndex(snapshot.Slots[s].X, snapshot.Slots[s].Y + 1) < 0)
							: !CanAnyCompassReachRootSlot(snapshot, items, s);
						if (isBlocked) {
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

		private static bool CanAnyCompassReachRootSlot(
			InventorySnapshot snapshot,
			SlotItemSnapshot[] items,
			int rootSlot
		) {
			SlotCoord rootCoord = snapshot.Slots[rootSlot];
			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot item = items[i];
				if (item.Kind != SlotItemKind.Artifact
					|| !item.HasDependencyTarget
					|| item.IsDisabledByWeaponOrDual
					|| item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
					continue;
				}
				(sbyte X, sbyte Y)[] offs = item.DependencyParentOffsets;
				for (int p = 0; p < offs.Length; p++) {
					int childX = rootCoord.X - offs[p].X;
					int childY = rootCoord.Y - offs[p].Y;
					if (snapshot.CoordToSlotIndex(childX, childY) >= 0) {
						return true;
					}
				}
			}
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint ResolveWhitePaperNeighborMask(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<byte> isActiveBuf,
			ReadOnlySpan<uint> categoryMaskBuf,
			int neighborSlot
		) {
			if ((uint)neighborSlot >= (uint)snapshot.UnlockedSlots) {
				return 0u;
			}
			if (isActiveBuf[neighborSlot] != 0) {
				return categoryMaskBuf[neighborSlot];
			}
			int neighborIdx = slotToItemIndex[neighborSlot];
			if (neighborIdx < 0) {
				return 0u;
			}
			SlotItemSnapshot neighborItem = snapshot.Items[neighborIdx];
			if (neighborItem.Kind != SlotItemKind.Artifact || neighborItem.IsWhitePaperCharm) {
				return 0u;
			}
			return neighborItem.StaticCategoryMask;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void ApplyMysticSlotMultiplier(
			int mysticSlot,
			int unlockedSlots,
			ref ulong amplifiedMysticMask,
			Span<int> mysticMulBuf
		) {
			if ((uint)mysticSlot >= (uint)unlockedSlots) {
				return;
			}
			ulong slotBit = 1UL << mysticSlot;
			if ((amplifiedMysticMask & slotBit) != 0UL) {
				return;
			}
			amplifiedMysticMask |= slotBit;
			mysticMulBuf[mysticSlot] += 2;
		}

		public static int GetCanonicalRotation(SlotItemSnapshot tablet, int slotIndex, int rawRotation) {
			int rot = rawRotation & 3;
			if (rot == 0 || tablet.Kind != SlotItemKind.StoneTablet || !tablet.IsRotatable) {
				return rot;
			}
			TabletPlacementRule targetRule = tablet.GetPlacementRule(slotIndex, rot);
			for (int prevRot = 0; prevRot < rot; prevRot++) {
				if (ArePlacementRulesEqual(targetRule, tablet.GetPlacementRule(slotIndex, prevRot))) {
					return prevRot;
				}
			}
			return rot;
		}

		public static bool ArePlacementRulesEqual(TabletPlacementRule first, TabletPlacementRule second) {
			if (first.Conditions.Length != second.Conditions.Length
				|| first.Effects.Length != second.Effects.Length) {
				return false;
			}
			for (int i = 0; i < first.Conditions.Length; i++) {
				PrecomputedCondition condA = first.Conditions[i];
				PrecomputedCondition condB = second.Conditions[i];
				if (condA.TargetSlotIndex != condB.TargetSlotIndex
					|| condA.ConditionType != condB.ConditionType
					|| condA.TargetX != condB.TargetX
					|| condA.TargetY != condB.TargetY) {
					return false;
				}
			}
			for (int i = 0; i < first.Effects.Length; i++) {
				PrecomputedEffect effA = first.Effects[i];
				PrecomputedEffect effB = second.Effects[i];
				if (effA.TargetSlotIndex != effB.TargetSlotIndex
					|| effA.EffectType != effB.EffectType
					|| effA.LevelParam != effB.LevelParam) {
					return false;
				}
			}
			return true;
		}

		public static ulong ComputeCanonicalLayoutHash(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			const ulong fnvOffset = 14695981039346656037UL;
			const ulong fnvPrime = 1099511628211UL;
			ulong hash = fnvOffset;
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = s < slotToItemIndex.Length ? slotToItemIndex[s] : -1;
				ulong slotToken;
				if (itemIdx < 0 || itemIdx >= items.Length) {
					slotToken = 0xFFFFFFFFUL;
				} else {
					SlotItemSnapshot item = items[itemIdx];
					int rot = 0;
					if (item.Kind == SlotItemKind.StoneTablet && item.IsRotatable) {
						int rawRot = itemIdx < itemRotations.Length ? itemRotations[itemIdx] : 0;
						rot = GetCanonicalRotation(item, s, rawRot);
					}
					slotToken = ((ulong)(uint)item.SymmetryGroupId << 4) | (ulong)(uint)(rot & 3);
				}
				hash ^= (ulong)(s + 1) * 0x9E3779B97F4A7C15UL ^ slotToken;
				hash *= fnvPrime;
			}
			return hash;
		}

		public static bool AreLayoutsCanonicallyEqual(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotsA,
			ReadOnlySpan<int> rotsA,
			ReadOnlySpan<int> slotsB,
			ReadOnlySpan<int> rotsB
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			for (int s = 0; s < unlockedSlots; s++) {
				int itemA = s < slotsA.Length ? slotsA[s] : -1;
				int itemB = s < slotsB.Length ? slotsB[s] : -1;
				if (itemA < 0 && itemB < 0) {
					continue;
				}
				if (itemA < 0 || itemB < 0 || itemA >= items.Length || itemB >= items.Length) {
					return false;
				}
				SlotItemSnapshot snapA = items[itemA];
				SlotItemSnapshot snapB = items[itemB];
				if (snapA.SymmetryGroupId != snapB.SymmetryGroupId) {
					return false;
				}
				if (snapA.Kind == SlotItemKind.StoneTablet && snapA.IsRotatable) {
					int rawRotA = itemA < rotsA.Length ? rotsA[itemA] : 0;
					int rawRotB = itemB < rotsB.Length ? rotsB[itemB] : 0;
					if (GetCanonicalRotation(snapA, s, rawRotA) != GetCanonicalRotation(snapB, s, rawRotB)) {
						return false;
					}
				}
			}
			return true;
		}

		public static void ResolveNeedleRootMasks(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<byte> isActiveBuf,
			int startNeedleSlot,
			out ulong rootSlotsMask,
			out ulong bypassRootSlotsMask,
			out int primaryRootSlot
		) {
			rootSlotsMask = 0UL;
			bypassRootSlotsMask = 0UL;
			primaryRootSlot = -1;
			if ((uint)startNeedleSlot >= (uint)snapshot.UnlockedSlots || isActiveBuf[startNeedleSlot] == 0) {
				return;
			}
			int startItemIdx = slotToItemIndex[startNeedleSlot];
			if (startItemIdx < 0) {
				return;
			}
			SlotItemSnapshot[] items = snapshot.Items;
			SlotItemSnapshot startItem = items[startItemIdx];
			if (!startItem.HasDependencyTarget) {
				return;
			}

			Span<byte> queueSlot = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> queueBypass = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			int head = 0;
			int tail = 0;

			bool initBypass = startItem.IsCompassBreakthrough;
			ulong visitedNoBypass = initBypass ? 0UL : (1UL << startNeedleSlot);
			ulong visitedWithBypass = initBypass ? (1UL << startNeedleSlot) : 0UL;
			queueSlot[tail] = (byte)startNeedleSlot;
			queueBypass[tail] = initBypass ? (byte)1 : (byte)0;
			tail++;

			while (head < tail) {
				int curSlot = queueSlot[head];
				bool curBypass = queueBypass[head] != 0;
				head++;

				SlotItemSnapshot curItem = items[slotToItemIndex[curSlot]];
				(sbyte X, sbyte Y)[] offsets = curItem.DependencyParentOffsets;
				SlotCoord coord = snapshot.Slots[curSlot];

				for (int p = 0; p < offsets.Length; p++) {
					int nextSlot = snapshot.CoordToSlotIndex(
						coord.X + offsets[p].X,
						coord.Y + offsets[p].Y
					);
					if (nextSlot < 0
						|| nextSlot >= snapshot.UnlockedSlots
						|| isActiveBuf[nextSlot] == 0) {
						continue;
					}
					int nextItemIdx = slotToItemIndex[nextSlot];
					if (nextItemIdx < 0) {
						continue;
					}
					SlotItemSnapshot nextItem = items[nextItemIdx];
					if (nextItem.Kind != SlotItemKind.Artifact) {
						continue;
					}

					ulong nextBit = 1UL << nextSlot;
					if (!nextItem.HasDependencyTarget) {
						if (nextItem.IsAttackableCharm) {
							if (primaryRootSlot < 0) {
								primaryRootSlot = nextSlot;
							}
							rootSlotsMask |= nextBit;
							if (curBypass) {
								bypassRootSlotsMask |= nextBit;
							}
						}
						continue;
					}

					if (nextItem.IsAttackableCharm) {
						if (primaryRootSlot < 0) {
							primaryRootSlot = nextSlot;
						}
						rootSlotsMask |= nextBit;
						if (curBypass) {
							bypassRootSlotsMask |= nextBit;
						}
					}

					if (nextSlot == startNeedleSlot) {
						continue;
					}

					bool nextBypass = curBypass || nextItem.IsCompassBreakthrough;
					if (nextBypass) {
						if ((visitedWithBypass & nextBit) == 0UL
							&& tail < InventorySnapshot.MaxSupportedSlots) {
							visitedWithBypass |= nextBit;
							queueSlot[tail] = (byte)nextSlot;
							queueBypass[tail] = 1;
							tail++;
						}
					} else {
						if (((visitedNoBypass | visitedWithBypass) & nextBit) == 0UL
							&& tail < InventorySnapshot.MaxSupportedSlots) {
							visitedNoBypass |= nextBit;
							queueSlot[tail] = (byte)nextSlot;
							queueBypass[tail] = 0;
							tail++;
						}
					}
				}
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
			Span<byte> allActive = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			for (int s = 0; s < snapshot.UnlockedSlots; s++) {
				allActive[s] = 1;
			}
			ResolveNeedleRootMasks(
				snapshot,
				slotToItemIndex,
				allActive,
				startNeedleSlot,
				out _,
				out _,
				out int primaryRootSlot
			);
			return primaryRootSlot;
		}

		public static int ResolveNeedleRootSlot(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<byte> isActiveBuf,
			int startNeedleSlot
		) {
			ResolveNeedleRootMasks(
				snapshot,
				slotToItemIndex,
				isActiveBuf,
				startNeedleSlot,
				out _,
				out _,
				out int primaryRootSlot
			);
			return primaryRootSlot;
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
			if (snapshot.IsExcludedCompanionEmblem(rootItem)) {
				return 0.0;
			}
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
				if (snapshot.IsExcludedCompanionEmblem(item)) {
					return 0;
				}
				int sameRowCompanions = 0;
				for (int x = 0; x < snapshot.Width; x++) {
					if (x == coord.X) {
						continue;
					}
					int rSlot = snapshot.CoordToSlotIndex(x, coord.Y);
					if (rSlot >= 0) {
						int rItem = slotToItemIndex[rSlot];
						if (rItem >= 0
							&& items[rItem].IsCompanionCharm
							&& items[rItem].PriorityTier != ArtifactPriorityTier.Sacrifice
							&& !snapshot.IsExcludedCompanionEmblem(items[rItem])) {
							sameRowCompanions++;
						}
					}
				}
				synergy += sameRowCompanions * 120;
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

		public static int EvaluateArrangementPatterns(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			out CombatStatDelta matchedStatDelta
		) {
			matchedStatDelta = CombatStatDelta.Zero;
			ArrangementPattern[] patterns = snapshot.TalentRules.ArrangementPatterns;
			int matchedCount = 0;

			for (int p = 0; p < patterns.Length; p++) {
				ArrangementPattern pattern = patterns[p];
				if (IsArrangementPatternMatched(snapshot, slotToItemIndex, pattern)) {
					matchedCount++;
					CombatStatDelta patternDelta = pattern.StatDelta;
					if (patternDelta.HasAnyNonZero) {
						matchedStatDelta = matchedStatDelta.Add(in patternDelta);
					}
				}
			}

			return matchedCount * ArrangementPatternMatchBonus;
		}

		public static bool IsArrangementPatternMatched(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ArrangementPattern pattern
		) {
			int[] requiredIds = pattern.RequiredEntityIds;
			int reqCount = requiredIds.Length;
			if (reqCount <= 0) {
				return false;
			}

			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			if (reqCount == 1) {
				int targetId = requiredIds[0];
				for (int s = 0; s < unlockedSlots; s++) {
					int idx = slotToItemIndex[s];
					if (idx >= 0 && items[idx].EntityId == targetId) {
						return true;
					}
				}
				return false;
			}

			ulong candidateSlotsMask = 0UL;
			for (int s = 0; s < unlockedSlots; s++) {
				int idx = slotToItemIndex[s];
				if (idx < 0) {
					continue;
				}
				int entityId = items[idx].EntityId;
				for (int r = 0; r < reqCount; r++) {
					if (requiredIds[r] == entityId) {
						candidateSlotsMask |= 1UL << s;
						break;
					}
				}
			}

			if (BitOperations.PopCount(candidateSlotsMask) < reqCount) {
				return false;
			}

			ulong remainingCandidates = candidateSlotsMask;
			while (remainingCandidates != 0UL) {
				int seedSlot = BitOperations.TrailingZeroCount(remainingCandidates);
				ulong seedBit = 1UL << seedSlot;
				remainingCandidates &= ~seedBit;
				ulong compMask = seedBit;
				ulong frontier = seedBit;

				while (frontier != 0UL) {
					int cur = BitOperations.TrailingZeroCount(frontier);
					frontier &= frontier - 1UL;
					SlotCoord coord = snapshot.Slots[cur];

					int leftSlot = snapshot.CoordToSlotIndex(coord.X - 1, coord.Y);
					if (leftSlot >= 0) {
						ulong nBit = 1UL << leftSlot;
						if ((remainingCandidates & nBit) != 0UL) {
							remainingCandidates &= ~nBit;
							compMask |= nBit;
							frontier |= nBit;
						}
					}
					int rightSlot = snapshot.CoordToSlotIndex(coord.X + 1, coord.Y);
					if (rightSlot >= 0) {
						ulong nBit = 1UL << rightSlot;
						if ((remainingCandidates & nBit) != 0UL) {
							remainingCandidates &= ~nBit;
							compMask |= nBit;
							frontier |= nBit;
						}
					}
					int upSlot = snapshot.CoordToSlotIndex(coord.X, coord.Y - 1);
					if (upSlot >= 0) {
						ulong nBit = 1UL << upSlot;
						if ((remainingCandidates & nBit) != 0UL) {
							remainingCandidates &= ~nBit;
							compMask |= nBit;
							frontier |= nBit;
						}
					}
					int downSlot = snapshot.CoordToSlotIndex(coord.X, coord.Y + 1);
					if (downSlot >= 0) {
						ulong nBit = 1UL << downSlot;
						if ((remainingCandidates & nBit) != 0UL) {
							remainingCandidates &= ~nBit;
							compMask |= nBit;
							frontier |= nBit;
						}
					}
				}

				if (BitOperations.PopCount(compMask) < reqCount) {
					continue;
				}

				ulong usedSlotsMask = 0UL;
				bool allFound = true;
				for (int r = 0; r < reqCount; r++) {
					int reqId = requiredIds[r];
					ulong avail = compMask & ~usedSlotsMask;
					bool foundOne = false;
					while (avail != 0UL) {
						int slot = BitOperations.TrailingZeroCount(avail);
						avail &= avail - 1UL;
						int itemIdx = slotToItemIndex[slot];
						if (itemIdx >= 0 && items[itemIdx].EntityId == reqId) {
							usedSlotsMask |= 1UL << slot;
							foundOne = true;
							break;
						}
					}
					if (!foundOne) {
						allFound = false;
						break;
					}
				}

				if (allFound) {
					return true;
				}
			}

			return false;
		}
	}
}
