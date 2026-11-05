using System;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Solver {
	public readonly struct DpsBreakdown {
		public double WeaponAndCharmBaseDps { get; }
		public double GeneralStatMultiplier { get; }
		public double BaseEffectiveDps { get; }
		public bool HasActiveGoldenNeedle { get; }
		public double GoldenNeedleAmpPercent { get; }
		public double GoldenNeedleAddedDps { get; }
		public bool HasActiveHarmonyCrystal { get; }
		public int HarmonyNeighborLevelSum { get; }
		public double HarmonyAmpPercent { get; }
		public double HarmonyAddedDps { get; }
		public double TotalEstimatedDps { get; }
		public bool HasOwnedGoldenNeedle { get; }
		public bool HasOwnedHarmonyCrystal { get; }
		public double BaseSharePercent { get; }
		public double GoldenNeedleSharePercent { get; }
		public double HarmonySharePercent { get; }

		public bool HasUnlinkedGoldenNeedle => HasOwnedGoldenNeedle && !HasActiveGoldenNeedle;

		public DpsBreakdown(
			double weaponAndCharmBaseDps,
			double generalStatMultiplier,
			double baseEffectiveDps,
			bool hasActiveGoldenNeedle,
			double goldenNeedleAmpPercent,
			double goldenNeedleAddedDps,
			bool hasActiveHarmonyCrystal,
			int harmonyNeighborLevelSum,
			double harmonyAmpPercent,
			double harmonyAddedDps,
			double totalEstimatedDps,
			bool hasOwnedGoldenNeedle = false,
			bool hasOwnedHarmonyCrystal = false
		) {
			WeaponAndCharmBaseDps = Math.Max(0.0, weaponAndCharmBaseDps);
			GeneralStatMultiplier = Math.Max(0.0, generalStatMultiplier);
			BaseEffectiveDps = Math.Max(0.0, baseEffectiveDps);
			HasActiveGoldenNeedle = hasActiveGoldenNeedle;
			GoldenNeedleAmpPercent = Math.Max(0.0, goldenNeedleAmpPercent);
			GoldenNeedleAddedDps = Math.Max(0.0, goldenNeedleAddedDps);
			HasActiveHarmonyCrystal = hasActiveHarmonyCrystal;
			HarmonyNeighborLevelSum = Math.Max(0, harmonyNeighborLevelSum);
			HarmonyAmpPercent = Math.Max(0.0, harmonyAmpPercent);
			HarmonyAddedDps = Math.Max(0.0, harmonyAddedDps);
			TotalEstimatedDps = Math.Max(0.0, totalEstimatedDps);
			HasOwnedGoldenNeedle = hasOwnedGoldenNeedle || hasActiveGoldenNeedle;
			HasOwnedHarmonyCrystal = hasOwnedHarmonyCrystal || hasActiveHarmonyCrystal;

			if (TotalEstimatedDps > 1e-6) {
				BaseSharePercent = (BaseEffectiveDps * 100.0) / TotalEstimatedDps;
				GoldenNeedleSharePercent = (GoldenNeedleAddedDps * 100.0) / TotalEstimatedDps;
				HarmonySharePercent = (HarmonyAddedDps * 100.0) / TotalEstimatedDps;
			} else {
				BaseSharePercent = 0.0;
				GoldenNeedleSharePercent = 0.0;
				HarmonySharePercent = 0.0;
			}
		}

		public static DpsBreakdown Empty => default;
	}

	public readonly struct DpsTooltipCache {
		public bool IsPopulated { get; }
		public bool HasOptimizedComparison { get; }
		public bool HasGoldenNeedleWarning { get; }
		public string PanelMainDpsLine { get; }
		public string PanelSubDpsLine { get; }
		public string TooltipTitle { get; }
		public string BaseDpsHeader { get; }
		public string BaseDpsValueLine { get; }
		public string BaseDpsDescLine { get; }
		public string GoldenNeedleHeader { get; }
		public string GoldenNeedleValueLine { get; }
		public string GoldenNeedleStatusLine { get; }
		public string HarmonyHeader { get; }
		public string HarmonyValueLine { get; }
		public string HarmonyAddedDpsLine { get; }
		public string TotalSummaryLine { get; }
		public string ShareSummaryLine { get; }

		public DpsTooltipCache(
			bool isPopulated,
			bool hasOptimizedComparison,
			bool hasGoldenNeedleWarning,
			string panelMainDpsLine,
			string panelSubDpsLine,
			string tooltipTitle,
			string baseDpsHeader,
			string baseDpsValueLine,
			string baseDpsDescLine,
			string goldenNeedleHeader,
			string goldenNeedleValueLine,
			string goldenNeedleStatusLine,
			string harmonyHeader,
			string harmonyValueLine,
			string harmonyAddedDpsLine,
			string totalSummaryLine,
			string shareSummaryLine
		) {
			IsPopulated = isPopulated;
			HasOptimizedComparison = hasOptimizedComparison;
			HasGoldenNeedleWarning = hasGoldenNeedleWarning;
			PanelMainDpsLine = panelMainDpsLine ?? string.Empty;
			PanelSubDpsLine = panelSubDpsLine ?? string.Empty;
			TooltipTitle = tooltipTitle ?? string.Empty;
			BaseDpsHeader = baseDpsHeader ?? string.Empty;
			BaseDpsValueLine = baseDpsValueLine ?? string.Empty;
			BaseDpsDescLine = baseDpsDescLine ?? string.Empty;
			GoldenNeedleHeader = goldenNeedleHeader ?? string.Empty;
			GoldenNeedleValueLine = goldenNeedleValueLine ?? string.Empty;
			GoldenNeedleStatusLine = goldenNeedleStatusLine ?? string.Empty;
			HarmonyHeader = harmonyHeader ?? string.Empty;
			HarmonyValueLine = harmonyValueLine ?? string.Empty;
			HarmonyAddedDpsLine = harmonyAddedDpsLine ?? string.Empty;
			TotalSummaryLine = totalSummaryLine ?? string.Empty;
			ShareSummaryLine = shareSummaryLine ?? string.Empty;
		}

		public static DpsTooltipCache CreateIdle(string talentBadge) {
			string suffix = string.IsNullOrEmpty(talentBadge) ? string.Empty : talentBadge;
			return new DpsTooltipCache(
				true,
				false,
				false,
				"0 DPS -> 0 DPS (+0.0%)",
				$"예상 DPS +0 | 스캔 대기 (-){suffix}",
				"허수아비 예상 DPS 상세 분석 (스캔 대기 중)",
				"1. 기본 딜 (무기 + 아티팩트 기본 피해)",
				"인벤토리 스냅샷 대기 중",
				"금빛침·조화의 수정 특수 증폭을 제외한 기본 무기·공격 아티팩트·일반 스탯 합산 DPS",
				"2. 북향의 금빛침 증폭",
				"미적용 (0%)",
				"공격형 아티팩트 바로 아래 칸에 배치 시 루트 아티팩트 피해 증폭",
				"3. 조화의 수정 증폭",
				"미적용 (0%)",
				"실질 추가 딜: +0 DPS",
				"총합 0 DPS",
				"비중 요약: 기본 딜 0.0% | 금빛침 0.0% | 조화의 수정 0.0%"
			);
		}
	}

	public static class DpsEstimator {
		public static DpsBreakdown CalculateCurrentBreakdown(InventorySnapshot snapshot) {
			if (snapshot == null) {
				return DpsBreakdown.Empty;
			}
			return CalculateBreakdown(
				snapshot,
				snapshot.InitialSlotToItemIndex,
				snapshot.InitialItemRotations
			);
		}

		public static DpsBreakdown CalculateBreakdown(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItemIndex,
			ReadOnlySpan<int> itemRotations
		) {
			if (snapshot == null || snapshot.UnlockedSlots <= 0) {
				return DpsBreakdown.Empty;
			}

			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;

			bool hasOwnedGoldenNeedle = false;
			bool hasOwnedHarmonyCrystal = false;
			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot candidate = items[i];
				if (candidate.Kind != SlotItemKind.Artifact
					|| candidate.PriorityTier == ArtifactPriorityTier.Sacrifice
					|| candidate.IsDisabledByWeaponOrDual) {
					continue;
				}
				if (candidate.HasDependencyTarget) {
					hasOwnedGoldenNeedle = true;
				}
				if (candidate.IsCrystalOfHarmony) {
					hasOwnedHarmonyCrystal = true;
				}
			}

			Span<int> rawSlotLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> finalSlotLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> tabletDeltaBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disableBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignoreBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> effectiveLevelBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> needleRootBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> chalcedonyCatBuf = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isActiveBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> naturalCriteriaBuf = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<uint> categoryMaskBuf = stackalloc uint[InventorySnapshot.MaxSupportedSlots];
			Span<int> dynamicComboBonusCounts = stackalloc int[InventorySnapshot.MaxSupportedCategories];

			for (int s = 0; s < unlockedSlots; s++) {
				int permLvl = snapshot.BaseSlotLevel[s];
				rawSlotLevelBuf[s] = permLvl;
				finalSlotLevelBuf[s] = permLvl;
				tabletDeltaBuf[s] = permLvl;
				disableBuf[s] = snapshot.BaseSlotDisable[s];
				ignoreBuf[s] = snapshot.BaseSlotIgnoreCriteria[s];
				mulBuf[s] = snapshot.BaseSlotMultiplyLevel[s];
				effectiveLevelBuf[s] = 0;
				needleRootBuf[s] = -1;
				chalcedonyCatBuf[s] = -1;
				isActiveBuf[s] = 0;
				naturalCriteriaBuf[s] = 0;
				categoryMaskBuf[s] = 0u;
			}
			for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
				dynamicComboBonusCounts[c] = 0;
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
				if (!ObjectiveEvaluator.IsTabletConditionMet(snapshot, slotToItemIndex, s, rule)) {
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

			for (int s = 0; s < unlockedSlots; s++) {
				int delta = tabletDeltaBuf[s];
				int mul = mulBuf[s];
				rawSlotLevelBuf[s] = mul != 0 ? (delta * mul) : delta;
				finalSlotLevelBuf[s] = rawSlotLevelBuf[s];
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
				bool naturalCriteriaMet = ObjectiveEvaluator.IsArtifactConstraintMet(
					snapshot,
					slotToItemIndex,
					s,
					item.ConstraintType
				);
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
					int rootSlot = ObjectiveEvaluator.ResolveNeedleRootSlot(
						snapshot,
						slotToItemIndex,
						isActiveBuf,
						s
					);
					needleRootBuf[s] = rootSlot;
					if (rootSlot >= 0) {
						uint rootCategories = categoryMaskBuf[rootSlot];
						categoryMaskBuf[s] |= rootCategories;
						AccumulateCategoryMask(rootCategories, dynamicComboBonusCounts);
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
						categoryMaskBuf[s] |= copiedMask;
						AccumulateCategoryMask(copiedMask, dynamicComboBonusCounts);
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
				DoubleMysticSlotLevel(
					snapshot.MysticSlot0,
					unlockedSlots,
					ref amplifiedMysticMask,
					rawSlotLevelBuf,
					finalSlotLevelBuf
				);
			}
			if (mysticTagCount >= snapshot.MysticTier2Threshold) {
				DoubleMysticSlotLevel(
					snapshot.MysticSlot1,
					unlockedSlots,
					ref amplifiedMysticMask,
					rawSlotLevelBuf,
					finalSlotLevelBuf
				);
				int[] extraMysticSlots = snapshot.ExtraMysticTier2Slots;
				for (int m = 0; m < extraMysticSlots.Length; m++) {
					DoubleMysticSlotLevel(
						extraMysticSlots[m],
						unlockedSlots,
						ref amplifiedMysticMask,
						rawSlotLevelBuf,
						finalSlotLevelBuf
					);
				}
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
				int displayedLevel = item.BaseLevel + finalSlotLevelBuf[s];
				bool naturalCriteriaMet = naturalCriteriaBuf[s] != 0;
				bool criteriaSatisfied = ignoreBuf[s] > 0 || naturalCriteriaMet;
				bool isDisabled = !globalEnabled
					|| item.IsDisabledByWeaponOrDual
					|| disableBuf[s] > 0
					|| displayedLevel <= -1
					|| !criteriaSatisfied;
				if (isDisabled) {
					isActiveBuf[s] = 0;
					effectiveLevelBuf[s] = 0;
				} else {
					isActiveBuf[s] = 1;
					effectiveLevelBuf[s] = Math.Clamp(displayedLevel, 0, item.MaxLevel);
				}
			}

			Span<double> needleMultiplierBySlot = stackalloc double[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isLinkedNeedleRootSlot = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			for (int s = 0; s < unlockedSlots; s++) {
				needleMultiplierBySlot[s] = 1.0;
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
							double prefScale = snapshot.PreferredNeedleTargetInstanceId == rootItem.InstanceId
								? 1.08
								: 1.0;
							double slotFactor = (1.0 + (bonusPct * 0.01)) * prefScale;
							needleMultiplierBySlot[rootSlot] *= slotFactor;
							isLinkedNeedleRootSlot[rootSlot] = 1;
							hasActiveGoldenNeedle = true;
							activeNeedlePercentSum += (slotFactor - 1.0) * 100.0;
							activeNeedleCount++;
						}
					}
				}
			}

			double baseWeaponRawDps = snapshot.CombatBaseline.ComputeBaseWeaponRawDps();
			double unamplifiedCharmAttackRawDps = 0.0;
			double amplifiedCharmAttackRawDps = 0.0;
			double linkedRootUnamplifiedRawDps = 0.0;
			double linkedRootAmplifiedRawDps = 0.0;
			double statBonusPercentSum = 0.0;
			double finalDamageMultiplier = 1.0;
			bool hasActiveHarmonyCrystal = false;
			int harmonyNeighborLevelSum = 0;
			double totalHarmonyAmpPercent = 0.0;
			ulong[] mooreNeighborMasks = snapshot.MooreNeighborMasks;

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
					double amplifiedAttackDps = rawAttackDps * needleMultiplierBySlot[s];
					unamplifiedCharmAttackRawDps += rawAttackDps;
					amplifiedCharmAttackRawDps += amplifiedAttackDps;
					if (isLinkedNeedleRootSlot[s] != 0) {
						linkedRootUnamplifiedRawDps += rawAttackDps;
						linkedRootAmplifiedRawDps += amplifiedAttackDps;
					}
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
						bool matchesPref = snapshot.PreferredChalcedonyCategoryId < 0
							|| assignedCatId == snapshot.PreferredChalcedonyCategoryId;
						statBonusPercentSum += matchesPref ? (elemBonus * 2.5) : (elemBonus * 0.5);
					}
				}

				if (item.IsCrystalOfHarmony) {
					hasActiveHarmonyCrystal = true;
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
					harmonyNeighborLevelSum += sumNeighborLevels;
					float crystalRate = item.GetCrystalRatePerLevel(effLv);
					totalHarmonyAmpPercent += sumNeighborLevels * crystalRate;
				}
			}

			ComboCategoryInfo[] categories = snapshot.ComboCategories;
			if (categories.Length > 0) {
				for (int i = 0; i < categories.Length; i++) {
					ComboCategoryInfo info = categories[i];
					int catId = info.CategoryId;
					int dynAdded = (catId >= 0 && catId < InventorySnapshot.MaxSupportedCategories)
						? dynamicComboBonusCounts[catId]
						: 0;
					int totalCount = info.StaticCount + dynAdded;
					double breakpointPct = info.EvaluateStatBonusPercent(totalCount);
					if (info.IsUserTarget) {
						breakpointPct *= 1.35;
					}
					statBonusPercentSum += breakpointPct;
					if (dynAdded > 0) {
						statBonusPercentSum += dynAdded * (info.IsUserTarget ? 2.5 : 1.0);
					}
				}
			} else {
				for (int c = 0; c < InventorySnapshot.MaxSupportedCategories; c++) {
					if (dynamicComboBonusCounts[c] > 0) {
						statBonusPercentSum += dynamicComboBonusCounts[c] * 1.5;
					}
				}
			}

			double weaponAndCharmBaseDps = baseWeaponRawDps + unamplifiedCharmAttackRawDps;
			double generalStatMultiplier = (1.0 + (statBonusPercentSum * 0.01)) * finalDamageMultiplier;
			double baseEffectiveDps = weaponAndCharmBaseDps * generalStatMultiplier;

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
				(amplifiedCharmAttackRawDps - unamplifiedCharmAttackRawDps) * generalStatMultiplier
			);
			double dpsWithNeedle = baseEffectiveDps + goldenNeedleAddedDps;
			double harmonyAddedDps = dpsWithNeedle * (totalHarmonyAmpPercent * 0.01);
			double totalEstimatedDps = dpsWithNeedle + harmonyAddedDps;

			return new DpsBreakdown(
				weaponAndCharmBaseDps,
				generalStatMultiplier,
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

		public static (float X, float Y) ComputeClampedTooltipPosition(
			float dpsBoxXMin,
			float dpsBoxYMin,
			float tooltipWidth,
			float tooltipHeight,
			float screenWidth,
			float screenHeight,
			float edgeMargin = 12f
		) {
			float desiredX = dpsBoxXMin - tooltipWidth - 10f;
			float desiredY = dpsBoxYMin;
			float maxAllowedX = Math.Max(edgeMargin, screenWidth - tooltipWidth - edgeMargin);
			float maxAllowedY = Math.Max(edgeMargin, screenHeight - tooltipHeight - edgeMargin);
			float clampedX = Math.Clamp(desiredX, edgeMargin, maxAllowedX);
			float clampedY = Math.Clamp(desiredY, edgeMargin, maxAllowedY);
			return (clampedX, clampedY);
		}

		public static DpsTooltipCache BuildTooltipCache(
			DpsBreakdown current,
			DpsBreakdown? optimized,
			bool usedExactBranchAndBound = false,
			double elapsedMilliseconds = 0.0,
			string talentBadge = ""
		) {
			bool hasOpt = optimized.HasValue;
			DpsBreakdown opt = optimized ?? current;

			int curTotalInt = (int)Math.Round(current.TotalEstimatedDps);
			int optTotalInt = (int)Math.Round(opt.TotalEstimatedDps);
			int totalDeltaInt = optTotalInt - curTotalInt;
			double totalGainPct = curTotalInt > 0
				? ((double)totalDeltaInt * 100.0) / curTotalInt
				: 0.0;

			string deltaSign = totalDeltaInt >= 0 ? "+" : string.Empty;
			string pctSign = totalGainPct >= 0.0 ? "+" : string.Empty;
			string solverLabel = hasOpt
				? (usedExactBranchAndBound ? "Exact B&B" : "LAHC+SA")
				: "현재 배치";
			string timeLabel = hasOpt ? $"{elapsedMilliseconds:F1}ms" : "-";

			string panelMainLine = hasOpt
				? $"{curTotalInt:N0} DPS -> {optTotalInt:N0} DPS ({pctSign}{totalGainPct:F1}%)"
				: $"{curTotalInt:N0} DPS (최적화 전)";
			string panelSubLine =
				$"예상 DPS {deltaSign}{totalDeltaInt:N0} | {solverLabel} ({timeLabel}){talentBadge}";

			int curBaseInt = (int)Math.Round(current.BaseEffectiveDps);
			int optBaseInt = (int)Math.Round(opt.BaseEffectiveDps);
			int baseDeltaInt = optBaseInt - curBaseInt;
			double baseGainPct = curBaseInt > 0
				? ((double)baseDeltaInt * 100.0) / curBaseInt
				: 0.0;
			string baseDeltaSign = baseDeltaInt >= 0 ? "+" : string.Empty;
			string basePctSign = baseGainPct >= 0.0 ? "+" : string.Empty;

			string baseValueLine = hasOpt
				? $"{curBaseInt:N0} DPS → {optBaseInt:N0} DPS ({baseDeltaSign}{baseDeltaInt:N0} / {basePctSign}{baseGainPct:F1}%)"
				: $"{curBaseInt:N0} DPS (일반 배율 ×{current.GeneralStatMultiplier:F2})";
			string baseDescLine =
				$"무기+공격 아티팩트 원시 딜 {Math.Round(current.WeaponAndCharmBaseDps):N0} → "
				+ $"{Math.Round(opt.WeaponAndCharmBaseDps):N0} | 일반·속성·콤보 배율 ×{current.GeneralStatMultiplier:F2} → "
				+ $"×{opt.GeneralStatMultiplier:F2}";

			bool hasNeedleWarning = current.HasUnlinkedGoldenNeedle || (hasOpt && opt.HasUnlinkedGoldenNeedle);
			string curNeedleStr = FormatGoldenNeedlePart(current);
			string needleValueLine = hasOpt
				? $"{curNeedleStr} → {FormatGoldenNeedlePart(opt)}"
				: curNeedleStr;

			string needleStatusLine;
			if (hasNeedleWarning) {
				if (current.HasUnlinkedGoldenNeedle && hasOpt && opt.HasActiveGoldenNeedle) {
					needleStatusLine = "주의: 현재 금빛침이 공격 아티팩트와 연결되지 않음 → 최적 배치 시 연결 활성화";
				} else {
					needleStatusLine = "주의: 북향의 금빛침이 공격형 아티팩트 바로 아래에 연결되지 않아 증폭 0% 상태";
				}
			} else if (current.HasActiveGoldenNeedle || (hasOpt && opt.HasActiveGoldenNeedle)) {
				needleStatusLine = "정상 적용: 바로 위 공격형 아티팩트의 피해량 및 카테고리 태그 증폭 활성";
			} else {
				needleStatusLine = "인벤토리 내 북향의 금빛침 미보유 (보유 시 공격 아티팩트 하단 배치 권장)";
			}

			string curHarmonyStr = FormatHarmonyPart(current);
			string harmonyValueLine = hasOpt
				? $"{curHarmonyStr} → {FormatHarmonyPart(opt)}"
				: curHarmonyStr;

			int curHarmonyDpsInt = (int)Math.Round(current.HarmonyAddedDps);
			int optHarmonyDpsInt = (int)Math.Round(opt.HarmonyAddedDps);
			int harmonyDeltaInt = optHarmonyDpsInt - curHarmonyDpsInt;
			string harmonyDeltaSign = harmonyDeltaInt >= 0 ? "+" : string.Empty;
			string harmonyAddedLine = hasOpt
				? $"실질 추가 딜: +{curHarmonyDpsInt:N0} DPS → +{optHarmonyDpsInt:N0} DPS ({harmonyDeltaSign}{harmonyDeltaInt:N0} DPS)"
				: $"실질 추가 딜: +{curHarmonyDpsInt:N0} DPS";

			string totalSummaryLine = hasOpt
				? $"총합 {curTotalInt:N0} DPS → {optTotalInt:N0} DPS ({deltaSign}{totalDeltaInt:N0} DPS / {pctSign}{totalGainPct:F1}% 상승)"
				: $"총합 {curTotalInt:N0} DPS (현재 배치 기준)";

			DpsBreakdown refBreakdown = hasOpt ? opt : current;
			string refLabel = hasOpt ? "최적 배치 비중" : "현재 배치 비중";
			string shareSummaryLine =
				$"{refLabel}: 기본 딜 {refBreakdown.BaseSharePercent:F1}% | "
				+ $"금빛침 {refBreakdown.GoldenNeedleSharePercent:F1}% | "
				+ $"조화의 수정 {refBreakdown.HarmonySharePercent:F1}%";

			return new DpsTooltipCache(
				true,
				hasOpt,
				hasNeedleWarning,
				panelMainLine,
				panelSubLine,
				hasOpt
					? "허수아비 예상 DPS 상세 분석 [현재 배치 → 최적 배치]"
					: "허수아비 예상 DPS 상세 분석 [현재 배치]",
				"1. 기본 딜 (무기 + 아티팩트 기본 피해)",
				baseValueLine,
				baseDescLine,
				"2. 북향의 금빛침 증폭",
				needleValueLine,
				needleStatusLine,
				"3. 조화의 수정 증폭",
				harmonyValueLine,
				harmonyAddedLine,
				totalSummaryLine,
				shareSummaryLine
			);
		}

		private static string FormatGoldenNeedlePart(DpsBreakdown breakdown) {
			if (!breakdown.HasActiveGoldenNeedle) {
				return breakdown.HasUnlinkedGoldenNeedle ? "미적용 (0% - 미연결)" : "미적용 (0%)";
			}
			int addedInt = (int)Math.Round(breakdown.GoldenNeedleAddedDps);
			return $"증폭 +{breakdown.GoldenNeedleAmpPercent:F1}% (추가 딜 +{addedInt:N0})";
		}

		private static string FormatHarmonyPart(DpsBreakdown breakdown) {
			if (!breakdown.HasActiveHarmonyCrystal) {
				return "미적용 (0%)";
			}
			return $"모든 피해 +{breakdown.HarmonyAmpPercent:0.#}% (인접 8칸 레벨 합: {breakdown.HarmonyNeighborLevelSum})";
		}

		private static void DoubleMysticSlotLevel(
			int mysticSlot,
			int unlockedSlots,
			ref ulong amplifiedMysticMask,
			ReadOnlySpan<int> rawSlotLevelBuf,
			Span<int> finalSlotLevelBuf
		) {
			if ((uint)mysticSlot >= (uint)unlockedSlots) {
				return;
			}
			ulong slotBit = 1UL << mysticSlot;
			if ((amplifiedMysticMask & slotBit) != 0UL) {
				return;
			}
			amplifiedMysticMask |= slotBit;
			finalSlotLevelBuf[mysticSlot] = rawSlotLevelBuf[mysticSlot] * 2;
		}

		private static void AccumulateCategoryMask(uint mask, Span<int> dynamicComboBonusCounts) {
			uint remaining = mask;
			while (remaining != 0u) {
				int bitIndex = BitOperations.TrailingZeroCount(remaining);
				if (bitIndex < InventorySnapshot.MaxSupportedCategories) {
					dynamicComboBonusCounts[bitIndex]++;
				}
				remaining &= remaining - 1u;
			}
		}
	}
}
