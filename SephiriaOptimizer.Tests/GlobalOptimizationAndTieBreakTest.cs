using System;
using System.Collections.Generic;
using SephiriaOptimizer.Execution;
using SephiriaOptimizer.Models;
using SephiriaOptimizer.Solver;
using Xunit;

namespace SephiriaOptimizer.Tests {
	public sealed class GlobalOptimizationAndTieBreakTest {
		[Fact(DisplayName = "회전 가능한 석판의 90·180·270도 규칙이 서로 다른 대상을 가리킬 때 가지치기로 제거되지 않고 최적 각도로 회전한다")]
		public void Audit_RotatableTabletDistinctTargetsAreNotPrunedAndSolverRotatesToOptimal() {
			int width = 3;
			int height = 3;
			int unlockedSlots = 9;

			SlotItemSnapshot attackCore = CreateWeaponScaledAttackCharm(
				0,
				9101,
				1,
				0,
				3,
				180f,
				ArtifactPriorityTier.Core
			);
			TabletPlacementRule[] rotatingPlus3Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				3
			);
			SlotItemSnapshot rotatableTablet = CreateSimpleTablet(
				1,
				9201,
				4,
				0,
				true,
				rotatingPlus3Rules,
				10
			);

			TabletPlacementRule r0 = rotatableTablet.GetPlacementRule(4, 0);
			TabletPlacementRule r1 = rotatableTablet.GetPlacementRule(4, 1);
			TabletPlacementRule r2 = rotatableTablet.GetPlacementRule(4, 2);
			TabletPlacementRule r3 = rotatableTablet.GetPlacementRule(4, 3);

			Assert.False(ObjectiveEvaluator.ArePlacementRulesEqual(r0, r1));
			Assert.False(ObjectiveEvaluator.ArePlacementRulesEqual(r0, r2));
			Assert.False(ObjectiveEvaluator.ArePlacementRulesEqual(r0, r3));

			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new[] { false, false, false, false, true, false, false, false, false },
				true,
				1,
				new[] { attackCore, rotatableTablet },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				CreateDetailedWeaponBaseline()
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int placedAttackSlot = result.BestItemToSlotIndex[0];
			SlotEvaluationDetail detail = result.BestSlotDetails[placedAttackSlot];

			Assert.True(result.UsedExactBranchAndBound);
			Assert.True(detail.IsEnabled);
			Assert.Equal(3, detail.EffectiveLevel);
		}

		[Fact(DisplayName = "Y_PLUS 자기 자신 슬롯 버프가 중첩 위반으로 오판되지 않으며 비정형 해금 슬롯의 경계 외 페널티가 강제 비활성화를 유발하지 않는다")]
		public void Audit_SelfHitYPlusDoesNotTriggerOverlapAndIrregularUnlockedSlotsRespectActivation() {
			int width = 6;
			int height = 3;
			int unlockedSlots = 15;

			SlotItemSnapshot charm = CreateWeaponScaledAttackCharm(0, 9102, 1, 0, 2, 120f, ArtifactPriorityTier.Core);

			TabletPlacementRule[] selfAndDownRules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				int x = s % width;
				int y = s / width;
				for (int rot = 0; rot < 4; rot++) {
					int downTarget = CoordToSlot(x, y + 1, width, height, unlockedSlots);
					int down2Target = CoordToSlot(x, y + 2, width, height, unlockedSlots);
					PrecomputedEffect[] effects = new[] {
						new PrecomputedEffect((sbyte)x, (sbyte)y, s, TabletEffectType.IncreaseConstLevel, 1),
						new PrecomputedEffect(
							(sbyte)x,
							(sbyte)(y + 1),
							downTarget,
							TabletEffectType.IncreaseConstLevel,
							2
						),
						new PrecomputedEffect(
							(sbyte)x,
							(sbyte)(y + 2),
							down2Target,
							TabletEffectType.IncreaseConstLevel,
							1
						)
					};
					selfAndDownRules[(s * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						effects
					);
				}
			}

			SlotItemSnapshot tablet = CreateSimpleTablet(1, 9202, 2, 0, false, selfAndDownRules, 11);
			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { charm, tablet }
			);

			int[] activeLayout = new int[unlockedSlots];
			Array.Fill(activeLayout, -1);
			activeLayout[2] = 1;
			activeLayout[8] = 0;

			int activeScore = ObjectiveEvaluator.EvaluateWithMetrics(
				snapshot,
				activeLayout,
				new[] { 0, 0 },
				out _,
				out int violations,
				out _
			);

			TabletPlacementRule[] edgeOnlyRules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);
			TabletPlacementRule[] placedCondRules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				int x = s % width;
				int y = s / width;
				for (int rot = 0; rot < 4; rot++) {
					PrecomputedCondition[] conds = new[] {
						new PrecomputedCondition(
							0,
							2,
							x == 0 && y == 2 ? s : -1,
							TabletConditionType.Placed
						)
					};
					placedCondRules[(s * 4) + rot] = new TabletPlacementRule(
						conds,
						edgeOnlyRules[(s * 4) + rot].Effects
					);
				}
			}
			SlotItemSnapshot condTablet = CreateSimpleTablet(0, 9203, 0, 0, false, placedCondRules, 12);
			InventorySnapshot condSnap = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { condTablet }
			);

			int[] metOnCorner = new int[unlockedSlots];
			Array.Fill(metOnCorner, -1);
			metOnCorner[12] = 0;
			int[] unmetLayout = new int[unlockedSlots];
			Array.Fill(unmetLayout, -1);
			unmetLayout[13] = 0;

			int metScore = ObjectiveEvaluator.Evaluate(condSnap, metOnCorner, new[] { 0 });
			int unmetScore = ObjectiveEvaluator.Evaluate(condSnap, unmetLayout, new[] { 0 });

			Assert.Equal(0, violations);
			Assert.True(activeScore > 0);
			Assert.True(metScore > unmetScore);
		}

		[Fact(DisplayName = "신비 2콤보 가산 배율(+2)과 X배율 석판이 아티팩트 각인 레벨에 합산 적용된 뒤 최대 레벨 상한으로 클램핑된다")]
		public void Audit_EnchantPlusMysticAdditiveMultiplierAndMaxLevelClamp() {
			int width = 4;
			int height = 1;
			int unlockedSlots = 4;

			SlotItemSnapshot enchantedUtility = new SlotItemSnapshot(
				0,
				9103,
				9103,
				"각인 유틸 아티팩트",
				SlotItemKind.Artifact,
				2,
				0,
				false,
				1,
				5,
				2,
				2,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				20,
				ArtifactPriorityTier.Normal,
				1u,
				statBonusPercentByLevel: new[] { 0f, 5f, 10f, 15f, 20f, 25f }
			);
			SlotItemSnapshot mysticTagMate = new SlotItemSnapshot(
				1,
				9104,
				9104,
				"신비 태그 동료",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				0,
				2,
				1,
				1,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				21,
				ArtifactPriorityTier.Normal,
				1u
			);

			TabletPlacementRule[] plus1Right = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);
			TabletPlacementRule[] mulPlus2Left = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.MultiplyConstLevel,
				2
			);
			SlotItemSnapshot plus1Tablet = CreateSimpleTablet(2, 9204, 1, 0, false, plus1Right, 22);
			SlotItemSnapshot mulTablet = CreateSimpleTablet(3, 9205, 3, 0, false, mulPlus2Left, 23);

			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { enchantedUtility, mysticTagMate, plus1Tablet, mulTablet },
				new[] { new ComboCategoryInfo(0, "MYSTIC", "신비", 2, new[] { 2, 4 }, true) },
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				2,
				-1,
				2,
				4,
				null,
				CreateDetailedWeaponBaseline()
			);

			int[] layout = new[] { 1, 2, 0, 3 };
			int[] rots = new[] { 0, 0, 0, 0 };
			SlotEvaluationDetail[] details = ObjectiveEvaluator.EvaluateDetailed(snapshot, layout, rots);

			Assert.Equal(8, details[2].DisplayedLevel);
			Assert.Equal(5, details[2].EffectiveLevel);
		}

		[Fact(DisplayName = "만렙 초과분으로 인해 +/- 석판 중첩 시 유효 레벨 손실이 0이면 과도한 중첩 페널티가 부과되지 않는다")]
		public void Audit_OvercappedPlusMinusOverlapDoesNotIncurDestructivePenalty() {
			int width = 4;
			int height = 1;
			int unlockedSlots = 4;

			SlotItemSnapshot cap2Artifact = CreateWeaponScaledAttackCharm(
				0,
				9105,
				2,
				0,
				2,
				100f,
				ArtifactPriorityTier.Core
			);
			TabletPlacementRule[] plus3Left = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.IncreaseConstLevel,
				3
			);
			TabletPlacementRule[] minus1Right = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				-1
			);
			TabletPlacementRule[] plus1Right = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);

			SlotItemSnapshot tabPlus3 = CreateSimpleTablet(1, 9206, 3, 0, false, plus3Left, 30);
			SlotItemSnapshot tabMinus1 = CreateSimpleTablet(2, 9207, 1, 0, false, minus1Right, 31);
			SlotItemSnapshot tabPlus1 = CreateSimpleTablet(3, 9208, 0, 0, false, plus1Right, 32);

			InventorySnapshot snapOvercapped = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { cap2Artifact, tabPlus3, tabMinus1 },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				CreateDetailedWeaponBaseline()
			);
			int overcappedScore = ObjectiveEvaluator.Evaluate(
				snapOvercapped,
				new[] { 2, 0, 1, -1 },
				new[] { 0, 0, 0 }
			);

			InventorySnapshot snapPlus1Only = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { cap2Artifact, tabPlus1, tabMinus1 },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				CreateDetailedWeaponBaseline()
			);
			int level1Score = ObjectiveEvaluator.Evaluate(
				snapPlus1Only,
				new[] { 2, 1, 0, -1 },
				new[] { 0, 0, 0 }
			);

			Assert.True(overcappedScore > level1Score);
		}

		[Fact(DisplayName = "조화의 수정이 음수 인접 레벨을 차감하고 상세 무기 프로필에서 합연산 모든 피해로 반영하며 8칸 내 버프 석판을 불필요하게 쫓아내지 않는다")]
		public void Audit_CrystalHarmonyHandlesNegativesUsesAdditiveBonusAndRetainsBuffTablet() {
			int width = 3;
			int height = 3;
			int unlockedSlots = 9;

			SlotItemSnapshot crystal = new SlotItemSnapshot(
				0,
				9106,
				9106,
				"조화의 수정",
				SlotItemKind.Artifact,
				4,
				0,
				false,
				3,
				3,
				2,
				4,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				40,
				ArtifactPriorityTier.Core,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 0f, 2f, 3f, 4f }
			);
			SlotItemSnapshot cap4Artifact = CreateWeaponScaledAttackCharm(
				1,
				9107,
				1,
				0,
				4,
				10f,
				ArtifactPriorityTier.Normal
			);
			SlotItemSnapshot bareZeroCap = new SlotItemSnapshot(
				2,
				9108,
				9108,
				"0레벨 잡템",
				SlotItemKind.Artifact,
				7,
				0,
				false,
				0,
				0,
				1,
				1,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				41,
				ArtifactPriorityTier.Low
			);
			TabletPlacementRule[] rightPlus4 = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				4
			);
			SlotItemSnapshot strongBuffTablet = CreateSimpleTablet(3, 9209, 0, 0, false, rightPlus4, 42);

			PlayerCombatBaseline detailed = CreateDetailedWeaponBaseline();
			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new[] { 0, 0, 0, -2, 0, 0, 0, 0, 0 },
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { crystal, cap4Artifact, bareZeroCap, strongBuffTablet },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				detailed
			);

			int[] keepTabletInsideRing = new[] { 3, 1, -1, -1, 0, -1, -1, 2, -1 };
			int[] evictTabletOutOfRing = new[] { -1, 1, -1, 3, 0, -1, -1, 2, -1 };
			int[] rots = new[] { 0, 0, 0, 0 };

			int keepScore = ObjectiveEvaluator.EvaluateWithDps(snapshot, keepTabletInsideRing, rots, out int keepDps);
			int evictScore = ObjectiveEvaluator.EvaluateWithDps(snapshot, evictTabletOutOfRing, rots, out int evictDps);
			DpsBreakdown breakdown = DpsEstimator.CalculateBreakdown(snapshot, keepTabletInsideRing, rots);

			Assert.True(keepScore > evictScore);
			Assert.True(keepDps > evictDps);
			Assert.True(breakdown.HarmonyAddedDps > 0.0);

			int[] withNegativeNeighbor = new[] { -1, 1, -1, 2, 0, -1, -1, -1, -1 };
			SlotItemSnapshot negCharm = new SlotItemSnapshot(
				2,
				9109,
				9109,
				"음수 이웃",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				-2,
				2,
				1,
				1,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				43,
				ArtifactPriorityTier.Sacrifice
			);
			SlotItemSnapshot positiveCap4Artifact = CreateWeaponScaledAttackCharm(
				1,
				9112,
				1,
				2,
				4,
				10f,
				ArtifactPriorityTier.Normal
			);
			InventorySnapshot negSnap = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { crystal, positiveCap4Artifact, negCharm },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				detailed
			);
			int[] onlyCrystalAndCap4 = new[] { -1, 1, -1, -1, 0, -1, -1, -1, -1 };
			DpsBreakdown noNegBd = DpsEstimator.CalculateBreakdown(negSnap, onlyCrystalAndCap4, new[] { 0, 0, 0 });
			DpsBreakdown withNegBd = DpsEstimator.CalculateBreakdown(negSnap, withNegativeNeighbor, new[] { 0, 0, 0 });

			Assert.True(withNegBd.HarmonyAmpPercent < noNegBd.HarmonyAmpPercent);
		}

		[Fact(DisplayName = "무제한 콤보 재능이 상한 초과분만큼 모든 피해(%)를 가산하며 하얀 종이가 비활성화된 제물 아티팩트의 정적 태그도 복사한다")]
		public void Audit_UnlimitedComboAddsSurplusAllDamageAndWhitePaperCopiesDisabledSacrificeTag() {
			ComboCategoryInfo emberCombo = new ComboCategoryInfo(
				0,
				"EMBER",
				"잉걸불",
				4,
				new[] { 2, 4 },
				true,
				new[] { 10f, 20f }
			);
			CombatStatDelta baseDelta = emberCombo.EvaluateStatDelta(4, unlimitedComboStat: 6);
			CombatStatDelta surplusDelta = emberCombo.EvaluateStatDelta(6, unlimitedComboStat: 6);

			Assert.Equal(12f, surplusDelta.AllDamageBonus - baseDelta.AllDamageBonus);

			int width = 3;
			int height = 1;
			int unlockedSlots = 3;
			SlotItemSnapshot whitePaper = new SlotItemSnapshot(
				0,
				9110,
				9110,
				"하얀 종이",
				SlotItemKind.Artifact,
				1,
				0,
				false,
				0,
				1,
				1,
				2,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				true,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				50,
				ArtifactPriorityTier.Normal,
				whitePaperMatchCount: 1
			);
			SlotItemSnapshot disabledSacrifice = new SlotItemSnapshot(
				1,
				9111,
				9111,
				"비활성 제물 태그",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				0,
				1,
				0,
				1,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				new[] { "EMBER" },
				Array.Empty<TabletPlacementRule>(),
				51,
				ArtifactPriorityTier.Sacrifice,
				staticCategoryMask: 1u
			);

			InventorySnapshot snap = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new[] { 1, 0, 0 },
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { whitePaper, disabledSacrifice },
				new[] { emberCombo }
			);

			SlotEvaluationDetail[] details = ObjectiveEvaluator.EvaluateDetailed(
				snap,
				new[] { 1, 0, -1 },
				new[] { 0, 0 }
			);

			Assert.False(details[0].IsEnabled);
			Assert.Equal(1u, details[1].DynamicCategoryMask);
		}

		[Fact(DisplayName = "초반(6x4)·중반(27칸)·후반(8x8 64칸) 복합 시너지 시나리오에서 수동 최적 배치를 상회/동등 달성하고 지역 최적 없이 목표 시간 내에 수렴한다")]
		public void Solve_EarlyMidLateFullSynergyScenariosReachGlobalOptimumWithinBudget() {
			VerifyScenarioReachesOptimumAndLocalConvergence(width: 6, height: 4, unlockedSlots: 24, extraArtifactCount: 6);
			VerifyScenarioReachesOptimumAndLocalConvergence(width: 6, height: 5, unlockedSlots: 27, extraArtifactCount: 8);
			OptimizationResult late64 = VerifyScenarioReachesOptimumAndLocalConvergence(
				width: 8,
				height: 8,
				unlockedSlots: 64,
				extraArtifactCount: 16
			);

			Assert.True(late64.ElapsedMilliseconds < 1500.0);
			Assert.Equal(0, late64.TopCandidates[0].Metrics.HardViolationCount);
		}

		[Fact(DisplayName = "동점/다중 최상 배치 발생 시 SymmetryGroupId 중복을 제거하고 4단계 사전식 우선순위 및 조건부 후보 플래그를 정확히 생성한다")]
		public void Solve_DeduplicatesSymmetryPermutationsAndRanksMultiOptimalTiesLexicographically() {
			int width = 4;
			int height = 1;
			int unlockedSlots = 4;

			SlotItemSnapshot cloneA = CreateWeaponScaledAttackCharm(0, 9301, 0, 0, 1, 50f, ArtifactPriorityTier.Core, 77);
			SlotItemSnapshot cloneB = CreateWeaponScaledAttackCharm(1, 9302, 2, 0, 1, 50f, ArtifactPriorityTier.Core, 77);
			InventorySnapshot dedupSnap = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new[] { 1, 0, 1, 0 },
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { cloneA, cloneB },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				CreateDetailedWeaponBaseline()
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult dedupResult = solver.Solve(dedupSnap);

			Assert.Single(dedupResult.TopCandidates);
			Assert.False(dedupResult.HasAlternativeCandidates);
			Assert.Equal("기본 추천", dedupResult.TopCandidates[0].DiffSummary);
			Assert.Equal(0, dedupResult.TopCandidates[0].Metrics.SwapCount);

			int tWidth = 4;
			int tHeight = 2;
			int tSlots = 8;
			SlotItemSnapshot attackCore = CreateWeaponScaledAttackCharm(
				0,
				9303,
				1,
				0,
				2,
				120f,
				ArtifactPriorityTier.Core,
				81
			);
			SlotItemSnapshot utilityHigher = new SlotItemSnapshot(
				1,
				9304,
				9304,
				"보조 유틸리티",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				0,
				2,
				1,
				1,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				82,
				ArtifactPriorityTier.Normal
			);
			TabletPlacementRule[] plus2Right = CreateDirectionalTabletRules(
				tWidth,
				tHeight,
				tSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot tabletA = CreateSimpleTablet(2, 9305, 0, 0, true, plus2Right, 83);

			InventorySnapshot tieSnap = new InventorySnapshot(
				tWidth,
				tHeight,
				tSlots,
				new[] { 0, 0, 0, 0, 0, 0, 2, 0 },
				new int[tSlots],
				new int[tSlots],
				new int[tSlots],
				new[] { false, false, false, false, true, true, true, true },
				true,
				1,
				new[] { attackCore, utilityHigher, tabletA },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				CreateDetailedWeaponBaseline()
			);

			OptimizationResult tieResult = solver.Solve(tieSnap);

			Assert.True(tieResult.TopCandidates.Count >= 2);
			Assert.True(tieResult.HasAlternativeCandidates);
			Assert.Equal(1, tieResult.TopCandidates[0].Rank);
			Assert.Equal("기본 추천", tieResult.TopCandidates[0].DiffSummary);
			Assert.Equal(2, tieResult.BestSlotDetails[tieResult.BestItemToSlotIndex[0]].EffectiveLevel);
			for (int i = 0; i + 1 < tieResult.TopCandidates.Count; i++) {
				CandidateMetrics left = tieResult.TopCandidates[i].Metrics;
				CandidateMetrics right = tieResult.TopCandidates[i + 1].Metrics;
				Assert.True(CandidateMetrics.CompareLexicographical(in left, in right) <= 0);
				Assert.False(string.IsNullOrWhiteSpace(tieResult.TopCandidates[i + 1].DiffSummary));
			}
		}

		[Fact(DisplayName = "핵심 평가·해시·실행비용 핫패스 메서드가 JIT 워밍업 이후 5,000회 반복 호출에서도 힙 할당 0 Bytes를 엄격히 유지한다")]
		public void HotPathMethods_MaintainStrictZeroByteHeapAllocation() {
			int width = 6;
			int height = 4;
			int unlockedSlots = 24;
			InventorySnapshot snapshot = BuildSynergySnapshot(
				width,
				height,
				unlockedSlots,
				extraArtifactCount: 6,
				out int[] manualLayout,
				out int[] manualRotations
			);

			int[] initialLayout = snapshot.InitialSlotToItemIndex;
			int[] initialRots = snapshot.InitialItemRotations;

			int warm = 0;
			warm ^= ObjectiveEvaluator.Evaluate(snapshot, manualLayout, manualRotations);
			warm ^= ObjectiveEvaluator.EvaluateWithDps(snapshot, manualLayout, manualRotations, out int _);
			warm ^= ObjectiveEvaluator.EvaluateWithMetrics(
				snapshot,
				manualLayout,
				manualRotations,
				out double _,
				out int _,
				out int _
			);
			ulong warmHash = ObjectiveEvaluator.ComputeCanonicalLayoutHash(snapshot, manualLayout, manualRotations);
			CycleDecomposition.CountExecutionCost(
				snapshot,
				initialLayout,
				manualLayout,
				initialRots,
				manualRotations,
				out int _,
				out int _
			);

			long evalAlloc = MeasureLoopAllocations(
				snapshot,
				initialLayout,
				initialRots,
				manualLayout,
				manualRotations,
				mode: 0
			);
			long evalDpsAlloc = MeasureLoopAllocations(
				snapshot,
				initialLayout,
				initialRots,
				manualLayout,
				manualRotations,
				mode: 1
			);
			long evalMetricsAlloc = MeasureLoopAllocations(
				snapshot,
				initialLayout,
				initialRots,
				manualLayout,
				manualRotations,
				mode: 2
			);
			long hashAlloc = MeasureLoopAllocations(
				snapshot,
				initialLayout,
				initialRots,
				manualLayout,
				manualRotations,
				mode: 3
			);
			long costAlloc = MeasureLoopAllocations(
				snapshot,
				initialLayout,
				initialRots,
				manualLayout,
				manualRotations,
				mode: 4
			);

			Assert.True(warmHash != 0UL || warm != int.MinValue);
			Assert.Equal(0L, evalAlloc);
			Assert.Equal(0L, evalDpsAlloc);
			Assert.Equal(0L, evalMetricsAlloc);
			Assert.Equal(0L, hashAlloc);
			Assert.Equal(0L, costAlloc);
		}

		private static long MeasureLoopAllocations(
			InventorySnapshot snapshot,
			int[] initialLayout,
			int[] initialRots,
			int[] targetLayout,
			int[] targetRots,
			int mode
		) {
			long before = GC.GetAllocatedBytesForCurrentThread();
			int sink = 0;
			ulong hSink = 0UL;
			for (int i = 0; i < 5000; i++) {
				switch (mode) {
					case 0:
						sink ^= ObjectiveEvaluator.Evaluate(snapshot, targetLayout, targetRots);
						break;
					case 1:
						sink ^= ObjectiveEvaluator.EvaluateWithDps(snapshot, targetLayout, targetRots, out int _);
						break;
					case 2:
						sink ^= ObjectiveEvaluator.EvaluateWithMetrics(
							snapshot,
							targetLayout,
							targetRots,
							out double _,
							out int _,
							out int _
						);
						break;
					case 3:
						hSink ^= ObjectiveEvaluator.ComputeCanonicalLayoutHash(snapshot, targetLayout, targetRots);
						break;
					case 4:
						CycleDecomposition.CountExecutionCost(
							snapshot,
							initialLayout,
							targetLayout,
							initialRots,
							targetRots,
							out int s,
							out int r
						);
						sink ^= s + r;
						break;
				}
			}
			long after = GC.GetAllocatedBytesForCurrentThread();
			if (sink == int.MinValue && hSink == ulong.MaxValue) {
				return -1L;
			}
			return after - before;
		}

		private static OptimizationResult VerifyScenarioReachesOptimumAndLocalConvergence(
			int width,
			int height,
			int unlockedSlots,
			int extraArtifactCount
		) {
			InventorySnapshot snapshot = BuildSynergySnapshot(
				width,
				height,
				unlockedSlots,
				extraArtifactCount,
				out int[] manualLayout,
				out int[] manualRotations
			);

			int manualScore = ObjectiveEvaluator.EvaluateWithDps(
				snapshot,
				manualLayout,
				manualRotations,
				out int manualDps
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			Assert.True(result.BestScore >= manualScore);
			Assert.True(result.BestEstimatedDps >= manualDps);

			AssertNoStrictOneRotOrTwoSwapImprovement(snapshot, result.BestSlotToItemIndex, result.BestItemRotations);
			return result;
		}

		private static void AssertNoStrictOneRotOrTwoSwapImprovement(
			InventorySnapshot snapshot,
			int[] bestSlots,
			int[] bestRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int bestScore = ObjectiveEvaluator.Evaluate(snapshot, bestSlots, bestRotations);
			int[] probeSlots = new int[unlockedSlots];
			Array.Copy(bestSlots, probeSlots, unlockedSlots);
			int[] probeRots = new int[bestRotations.Length];
			Array.Copy(bestRotations, probeRots, bestRotations.Length);

			for (int i = 0; i < snapshot.Items.Length; i++) {
				if (snapshot.Items[i].Kind != SlotItemKind.StoneTablet || !snapshot.Items[i].IsRotatable) {
					continue;
				}
				int saved = probeRots[i];
				for (int r = 0; r < 4; r++) {
					probeRots[i] = r;
					int sc = ObjectiveEvaluator.Evaluate(snapshot, probeSlots, probeRots);
					Assert.True(sc <= bestScore);
				}
				probeRots[i] = saved;
			}

			for (int a = 0; a < unlockedSlots; a++) {
				if (snapshot.PinnedSlots[a]) {
					continue;
				}
				for (int b = a + 1; b < unlockedSlots; b++) {
					if (snapshot.PinnedSlots[b]) {
						continue;
					}
					int itemA = probeSlots[a];
					int itemB = probeSlots[b];
					if (itemA == itemB) {
						continue;
					}
					probeSlots[a] = itemB;
					probeSlots[b] = itemA;
					int sc = ObjectiveEvaluator.Evaluate(snapshot, probeSlots, probeRots);
					Assert.True(sc <= bestScore);
					probeSlots[a] = itemA;
					probeSlots[b] = itemB;
				}
			}
		}

		private static InventorySnapshot BuildSynergySnapshot(
			int width,
			int height,
			int unlockedSlots,
			int extraArtifactCount,
			out int[] manualLayout,
			out int[] manualRotations
		) {
			List<SlotItemSnapshot> items = new List<SlotItemSnapshot>();
			manualLayout = new int[unlockedSlots];
			Array.Fill(manualLayout, -1);

			int crystalManualSlot = (1 * width) + 2;
			int coreManualSlot = (1 * width) + 1;
			int needleManualSlot = (2 * width) + 1;
			int wpLeftSlot = (0 * width) + 0;
			int wpCenterSlot = (0 * width) + 1;
			int wpRightSlot = (0 * width) + 2;
			int chalcedonyManualSlot = (1 * width) + 3;
			int mysticCharmSlot = (2 * width) + 3;
			int tabRightPlus3ManualSlot = (1 * width) + 0;
			int tabLeftPlus2ManualSlot = (2 * width) + 4;
			int sacrificeManualSlot = (0 * width) + 5;
			int tabDebuffManualSlot = (0 * width) + 4;

			int nextInitSlot = unlockedSlots - 1;
			int TakeScrambledInit(int forbidSlot) {
				while (nextInitSlot == forbidSlot && nextInitSlot > 0) {
					nextInitSlot--;
				}
				return nextInitSlot--;
			}

			SlotItemSnapshot coreAttack = CreateWeaponScaledAttackCharm(
				items.Count,
				9401,
				TakeScrambledInit(coreManualSlot),
				0,
				4,
				200f,
				ArtifactPriorityTier.Core,
				501,
				category: "EMBER",
				categoryMask: 1u
			);
			manualLayout[coreManualSlot] = items.Count;
			items.Add(coreAttack);

			SlotItemSnapshot needle = new SlotItemSnapshot(
				items.Count,
				9402,
				9402,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				TakeScrambledInit(needleManualSlot),
				0,
				false,
				0,
				3,
				1,
				4,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				true,
				0,
				-1,
				false,
				false,
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				502,
				ArtifactPriorityTier.Core,
				staticCategoryMask: 2u,
				hasDependencyCondition: true,
				dependencyMaxRarity: 2,
				needleDamageBonusByLevel: new[] { 15, 30, 45, 65 },
				needleDependencyBonusByLevel: new[] { 20, 35, 50, 75 }
			);
			manualLayout[needleManualSlot] = items.Count;
			items.Add(needle);

			SlotItemSnapshot crystal = new SlotItemSnapshot(
				items.Count,
				9403,
				9403,
				"조화의 수정",
				SlotItemKind.Artifact,
				TakeScrambledInit(crystalManualSlot),
				0,
				false,
				2,
				3,
				2,
				4,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				503,
				ArtifactPriorityTier.Core,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 0f, 1.5f, 2.5f, 3.5f }
			);
			manualLayout[crystalManualSlot] = items.Count;
			items.Add(crystal);

			SlotItemSnapshot chalcedony = new SlotItemSnapshot(
				items.Count,
				9404,
				9404,
				"캘세더니 열쇠",
				SlotItemKind.Artifact,
				TakeScrambledInit(chalcedonyManualSlot),
				0,
				false,
				0,
				3,
				2,
				3,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				true,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				504,
				ArtifactPriorityTier.Core,
				rowCategoryIds: new[] { 0, 1, 0 },
				rowElementalBonusByLevel: new[] { 0, 20, 35, 50 }
			);
			manualLayout[chalcedonyManualSlot] = items.Count;
			items.Add(chalcedony);

			SlotItemSnapshot whitePaper = new SlotItemSnapshot(
				items.Count,
				9405,
				9405,
				"하얀 종이",
				SlotItemKind.Artifact,
				TakeScrambledInit(wpCenterSlot),
				0,
				false,
				0,
				1,
				1,
				3,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				true,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				505,
				ArtifactPriorityTier.Core,
				whitePaperMatchCount: 1
			);
			manualLayout[wpCenterSlot] = items.Count;
			items.Add(whitePaper);

			SlotItemSnapshot mysticMate = new SlotItemSnapshot(
				items.Count,
				9406,
				9406,
				"신비 태그 부적",
				SlotItemKind.Artifact,
				TakeScrambledInit(wpRightSlot),
				0,
				false,
				1,
				2,
				1,
				2,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				506,
				ArtifactPriorityTier.Normal,
				staticCategoryMask: 2u,
				statBonusPercentByLevel: new[] { 5f, 12f, 20f }
			);
			manualLayout[wpRightSlot] = items.Count;
			items.Add(mysticMate);

			SlotItemSnapshot emberMate = new SlotItemSnapshot(
				items.Count,
				9407,
				9407,
				"잉걸불 보조 부적",
				SlotItemKind.Artifact,
				TakeScrambledInit(wpLeftSlot),
				0,
				false,
				1,
				3,
				1,
				2,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				new[] { "EMBER" },
				Array.Empty<TabletPlacementRule>(),
				507,
				ArtifactPriorityTier.Normal,
				staticCategoryMask: 1u,
				statBonusPercentByLevel: new[] { 5f, 10f, 15f, 25f }
			);
			manualLayout[wpLeftSlot] = items.Count;
			items.Add(emberMate);

			SlotItemSnapshot mysticFiller = new SlotItemSnapshot(
				items.Count,
				9408,
				9408,
				"신비 공명 부적",
				SlotItemKind.Artifact,
				TakeScrambledInit(mysticCharmSlot),
				0,
				false,
				1,
				2,
				1,
				2,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				508,
				ArtifactPriorityTier.Normal,
				staticCategoryMask: 2u,
				statBonusPercentByLevel: new[] { 4f, 8f, 16f }
			);
			manualLayout[mysticCharmSlot] = items.Count;
			items.Add(mysticFiller);

			SlotItemSnapshot sacCharm = new SlotItemSnapshot(
				items.Count,
				9409,
				9409,
				"희생 오브",
				SlotItemKind.Artifact,
				TakeScrambledInit(sacrificeManualSlot),
				0,
				false,
				0,
				1,
				0,
				1,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				509,
				ArtifactPriorityTier.Sacrifice
			);
			manualLayout[sacrificeManualSlot] = items.Count;
			items.Add(sacCharm);

			TabletPlacementRule[] rightPlus3Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				3
			);
			SlotItemSnapshot buffTabA = CreateSimpleTablet(
				items.Count,
				9501,
				TakeScrambledInit(tabRightPlus3ManualSlot),
				1,
				true,
				rightPlus3Rules,
				510
			);
			manualLayout[tabRightPlus3ManualSlot] = items.Count;
			items.Add(buffTabA);

			TabletPlacementRule[] leftPlus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot buffTabB = CreateSimpleTablet(
				items.Count,
				9502,
				TakeScrambledInit(tabLeftPlus2ManualSlot),
				2,
				true,
				leftPlus2Rules,
				511
			);
			manualLayout[tabLeftPlus2ManualSlot] = items.Count;
			items.Add(buffTabB);

			TabletPlacementRule[] bipolarRules = CreateBipolarTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				2,
				1,
				0,
				-2
			);
			SlotItemSnapshot bipolarTab = CreateSimpleTablet(
				items.Count,
				9503,
				TakeScrambledInit(tabDebuffManualSlot),
				0,
				true,
				bipolarRules,
				512
			);
			manualLayout[tabDebuffManualSlot] = items.Count;
			items.Add(bipolarTab);

			int fillCursor = 0;
			for (int e = 0; e < extraArtifactCount; e++) {
				while (fillCursor < unlockedSlots && manualLayout[fillCursor] >= 0) {
					fillCursor++;
				}
				if (fillCursor >= unlockedSlots) {
					break;
				}
				SlotItemSnapshot filler = new SlotItemSnapshot(
					items.Count,
					9600 + e,
					9600 + e,
					$"추가 유물 {e + 1}",
					SlotItemKind.Artifact,
					fillCursor,
					0,
					false,
					1,
					2,
					1,
					1,
					ArtifactConstraintType.None,
					false,
					false,
					false,
					false,
					false,
					0,
					0,
					false,
					false,
					Array.Empty<string>(),
					Array.Empty<TabletPlacementRule>(),
					600 + e,
					ArtifactPriorityTier.Normal,
					statBonusPercentByLevel: new[] { 2f, 5f, 9f }
				);
				manualLayout[fillCursor] = items.Count;
				items.Add(filler);
			}

			manualRotations = new int[items.Count];
			manualRotations[buffTabA.ItemIndex] = 0;
			manualRotations[buffTabB.ItemIndex] = 0;
			manualRotations[bipolarTab.ItemIndex] = 0;

			int mysticSlot = (1 * width) + 1;
			return new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				items.ToArray(),
				new[] {
					new ComboCategoryInfo(0, "EMBER", "잉걸불", 0, new[] { 2, 4 }, true),
					new ComboCategoryInfo(1, "MYSTIC", "신비", 0, new[] { 2, 4 }, true)
				},
				TalentRuleSnapshot.Default,
				coreAttack.InstanceId,
				1,
				null,
				mysticSlot,
				needleManualSlot,
				2,
				4,
				null,
				CreateDetailedWeaponBaseline()
			);
		}

		private static PlayerCombatBaseline CreateDetailedWeaponBaseline() {
			WeaponCombatProfile profile = new WeaponCombatProfile(
				WeaponElementalFormulaMode.AverageMask,
				averageElementMask: 0x0F,
				basePhysicalStat: 30f,
				baseFireStat: 30f,
				baseIceStat: 30f,
				baseLightningStat: 30f,
				baseAllDamageBonus: 20f,
				baseCritChancePercent: 25f,
				baseCritDamageRate: 80f,
				comboSwingMultiplier: 1.2f,
				avgSwingRatioForAddons: 1.0f,
				baseSwingRate: 2.2f
			);
			return new PlayerCombatBaseline(in profile);
		}

		private static SlotItemSnapshot CreateWeaponScaledAttackCharm(
			int itemIndex,
			int instanceId,
			int initSlot,
			int baseLevel,
			int maxLevel,
			float dmgPerLevel,
			ArtifactPriorityTier tier,
			int symGroupId = -1,
			string category = "",
			uint categoryMask = 0u
		) {
			float[] dmg = new float[maxLevel + 1];
			for (int i = 0; i <= maxLevel; i++) {
				dmg[i] = dmgPerLevel * (i + 1);
			}
			return new SlotItemSnapshot(
				itemIndex,
				instanceId,
				instanceId,
				$"공격 아티팩트 {instanceId}",
				SlotItemKind.Artifact,
				initSlot,
				0,
				false,
				baseLevel,
				maxLevel,
				1,
				tier == ArtifactPriorityTier.Core ? 4 : 2,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				true,
				false,
				0,
				0,
				false,
				false,
				string.IsNullOrEmpty(category) ? Array.Empty<string>() : new[] { category },
				Array.Empty<TabletPlacementRule>(),
				symGroupId >= 0 ? symGroupId : instanceId,
				tier,
				categoryMask,
				attackDamageByLevel: dmg,
				baseProcsPerSecond: 1.5f
			);
		}

		private static SlotItemSnapshot CreateSimpleTablet(
			int itemIndex,
			int instanceId,
			int initSlot,
			int initRot,
			bool isRotatable,
			TabletPlacementRule[] rules,
			int symGroupId
		) {
			return new SlotItemSnapshot(
				itemIndex,
				instanceId,
				instanceId,
				$"석판 {instanceId}",
				SlotItemKind.StoneTablet,
				initSlot,
				initRot,
				isRotatable,
				0,
				0,
				0,
				1,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				false,
				0,
				0,
				false,
				false,
				Array.Empty<string>(),
				rules,
				symGroupId
			);
		}

		private static TabletPlacementRule[] CreateDirectionalTabletRules(
			int width,
			int height,
			int unlockedSlots,
			int baseDx,
			int baseDy,
			TabletEffectType effectType,
			int levelParam
		) {
			TabletPlacementRule[] rules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				int x = s % width;
				int y = s / width;
				for (int rot = 0; rot < 4; rot++) {
					(int dx, int dy) = RotateOffset(baseDx, baseDy, rot);
					int targetSlot = CoordToSlot(x + dx, y + dy, width, height, unlockedSlots);
					rules[(s * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						new[] {
							new PrecomputedEffect(
								(sbyte)(x + dx),
								(sbyte)(y + dy),
								targetSlot,
								effectType,
								levelParam
							)
						}
					);
				}
			}
			return rules;
		}

		private static TabletPlacementRule[] CreateBipolarTabletRules(
			int width,
			int height,
			int unlockedSlots,
			int posDx,
			int posDy,
			int posLevel,
			int negDx,
			int negDy,
			int negLevel
		) {
			TabletPlacementRule[] rules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				int x = s % width;
				int y = s / width;
				for (int rot = 0; rot < 4; rot++) {
					(int pDx, int pDy) = RotateOffset(posDx, posDy, rot);
					(int nDx, int nDy) = RotateOffset(negDx, negDy, rot);
					int posTarget = CoordToSlot(x + pDx, y + pDy, width, height, unlockedSlots);
					int negTarget = CoordToSlot(x + nDx, y + nDy, width, height, unlockedSlots);
					rules[(s * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						new[] {
							new PrecomputedEffect(
								(sbyte)(x + pDx),
								(sbyte)(y + pDy),
								posTarget,
								TabletEffectType.IncreaseConstLevel,
								posLevel
							),
							new PrecomputedEffect(
								(sbyte)(x + nDx),
								(sbyte)(y + nDy),
								negTarget,
								TabletEffectType.IncreaseConstLevel,
								negLevel
							)
						}
					);
				}
			}
			return rules;
		}

		private static (int Dx, int Dy) RotateOffset(int dx, int dy, int rotation) {
			return (rotation & 3) switch {
				0 => (dx, dy),
				1 => (dy, -dx),
				2 => (-dx, -dy),
				_ => (-dy, dx)
			};
		}

		private static int CoordToSlot(int x, int y, int width, int height, int unlockedSlots) {
			if (x < 0 || x >= width || y < 0 || y >= height) {
				return -1;
			}
			int idx = (y * width) + x;
			return idx < unlockedSlots ? idx : -1;
		}
	}
}
