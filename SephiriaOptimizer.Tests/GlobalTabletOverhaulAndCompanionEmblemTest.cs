using System;
using System.Collections.Generic;
using SephiriaOptimizer.Models;
using SephiriaOptimizer.Snapshot;
using SephiriaOptimizer.Solver;
using Xunit;

namespace SephiriaOptimizer.Tests {
	public sealed class GlobalTabletOverhaulAndCompanionEmblemTest {
		[Fact(DisplayName = "다중 석판 연쇄 버프가 석판 위치에 겹치더라도 기대 DPS 극대화 배치를 탐색하고 평가기와 일치시킨다")]
		public void Solve_ChainedMultiTabletBuffStackingYieldsMaxDpsAndMatchesDpsEstimator() {
			const int width = 4;
			const int height = 4;
			const int unlockedSlots = 16;

			int[] zeroArr = new int[unlockedSlots];
			bool[] unpinned = new bool[unlockedSlots];

			SlotItemSnapshot coreAttackCharm = new SlotItemSnapshot(
				itemIndex: 0,
				instanceId: 5001,
				entityId: 5001,
				displayName: "집속 뇌전 부적",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 15,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 8,
				rarityTier: 2,
				customWeight: 20,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: true,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: Array.Empty<string>(),
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 1,
				priorityTier: ArtifactPriorityTier.Core,
				attackDamageByLevel: new[] { 15f, 35f, 70f, 120f, 185f, 265f, 365f, 500f, 680f },
				statBonusPercentByLevel: new float[9]
			);

			SlotItemSnapshot statCharm = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 5002,
				entityId: 5002,
				displayName: "보조 마력석",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 14,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 4,
				rarityTier: 1,
				customWeight: 10,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: Array.Empty<string>(),
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 2,
				priorityTier: ArtifactPriorityTier.Normal,
				statBonusPercentByLevel: new[] { 2f, 5f, 8f, 12f, 16f }
			);

			TabletPlacementRule[] chainPlusRules = BuildDoubleRightTabletRules(
				width,
				height,
				unlockedSlots,
				firstDx: 1,
				firstDy: 0,
				firstType: TabletEffectType.IncreaseConstLevel,
				firstParam: 2,
				secondDx: 2,
				secondDy: 0,
				secondType: TabletEffectType.IncreaseConstLevel,
				secondParam: 3
			);
			SlotItemSnapshot chainPlusTablet = CreateTabletItem(
				2,
				5003,
				9,
				true,
				chainPlusRules,
				101
			);

			TabletPlacementRule[] multiplyRules = BuildSingleEffectTabletRules(
				width,
				height,
				unlockedSlots,
				baseDx: 1,
				baseDy: 0,
				effectType: TabletEffectType.MultiplyConstLevel,
				levelParam: 2
			);
			SlotItemSnapshot multiplyTablet = CreateTabletItem(
				3,
				5004,
				13,
				true,
				multiplyRules,
				102
			);

			PlayerCombatBaseline baseline = new PlayerCombatBaseline(120f, 2.0f, 15f, 1.5f, 0f);
			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				new[] { coreAttackCharm, statCharm, chainPlusTablet, multiplyTablet },
				combatBaseline: baseline
			);

			int[] chainedManualSlots = new int[unlockedSlots];
			Array.Fill(chainedManualSlots, -1);
			chainedManualSlots[4] = 2;
			chainedManualSlots[5] = 3;
			chainedManualSlots[6] = 0;
			chainedManualSlots[7] = 1;
			int[] chainedManualRots = new[] { 0, 0, 0, 0 };
			DpsBreakdown chainedManualDps = DpsEstimator.CalculateBreakdown(
				snapshot,
				chainedManualSlots,
				chainedManualRots
			);

			int[] nonOverlapSlots = new int[unlockedSlots];
			Array.Fill(nonOverlapSlots, -1);
			nonOverlapSlots[0] = 2;
			nonOverlapSlots[2] = 0;
			nonOverlapSlots[6] = 3;
			nonOverlapSlots[7] = 1;
			int[] nonOverlapRots = new[] { 0, 0, 0, 0 };
			DpsBreakdown nonOverlapDps = DpsEstimator.CalculateBreakdown(
				snapshot,
				nonOverlapSlots,
				nonOverlapRots
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot, 350);

			ObjectiveEvaluator.EvaluateWithMetrics(
				snapshot,
				result.BestSlotToItemIndex,
				result.BestItemRotations,
				out double evalDps,
				out int hardViolations,
				out _
			);
			DpsBreakdown estimatorDps = DpsEstimator.CalculateBreakdown(
				snapshot,
				result.BestSlotToItemIndex,
				result.BestItemRotations
			);

			int coreSlot = result.BestItemToSlotIndex[0];
			Assert.Equal(0, hardViolations);
			Assert.True(chainedManualDps.TotalEstimatedDps > nonOverlapDps.TotalEstimatedDps + 400.0);
			Assert.True(result.TopCandidates[0].Metrics.ExactEstimatedDps >= chainedManualDps.TotalEstimatedDps - 1e-3);
			Assert.Equal(8, result.BestSlotDetails[coreSlot].EffectiveLevel);
			Assert.InRange(Math.Abs(evalDps - estimatorDps.TotalEstimatedDps), 0.0, 1e-4);
		}

		[Fact(DisplayName = "복합 페널티(-3 및 비활성화)와 희생 아티팩트가 섞여 있어도 페널티 상쇄 배치로 핵심 DPS를 보존한다")]
		public void Solve_NeutralizesComplexMultiTabletPenaltiesViaOobAndSacrificeSink() {
			const int width = 5;
			const int height = 4;
			const int unlockedSlots = 20;

			int[] zeroArr = new int[unlockedSlots];
			bool[] unpinned = new bool[unlockedSlots];

			SlotItemSnapshot primaryAttack = CreateAttackArtifact(
				itemIndex: 0,
				instanceId: 6001,
				initialSlot: 6,
				displayName: "파괴의 검기",
				baseLevel: 1,
				maxLevel: 6,
				weight: 20,
				tier: ArtifactPriorityTier.Core,
				symmetryGroupId: 1,
				attackTable: new[] { 30f, 70f, 140f, 230f, 340f, 480f, 650f }
			);
			SlotItemSnapshot secondaryStat = CreateStatArtifact(
				itemIndex: 1,
				instanceId: 6002,
				initialSlot: 7,
				displayName: "증폭의 핵",
				baseLevel: 2,
				maxLevel: 6,
				weight: 16,
				tier: ArtifactPriorityTier.High,
				symmetryGroupId: 2,
				statTable: new[] { 4f, 8f, 14f, 20f, 28f, 36f, 45f }
			);
			SlotItemSnapshot sacrificeSink = new SlotItemSnapshot(
				itemIndex: 2,
				instanceId: 6003,
				entityId: 6003,
				displayName: "빈 껍데기",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 19,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 1,
				rarityTier: 1,
				customWeight: 1,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: Array.Empty<string>(),
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 3,
				priorityTier: ArtifactPriorityTier.Sacrifice,
				statBonusPercentByLevel: new float[2]
			);

			TabletPlacementRule[] plusMinusRules = BuildDoubleRightTabletRules(
				width,
				height,
				unlockedSlots,
				firstDx: 1,
				firstDy: 0,
				firstType: TabletEffectType.IncreaseConstLevel,
				firstParam: 3,
				secondDx: -1,
				secondDy: 0,
				secondType: TabletEffectType.IncreaseConstLevel,
				secondParam: -3
			);
			SlotItemSnapshot plusMinusTablet = CreateTabletItem(3, 6004, 1, true, plusMinusRules, 103);

			TabletPlacementRule[] mulDisableRules = BuildDoubleRightTabletRules(
				width,
				height,
				unlockedSlots,
				firstDx: 1,
				firstDy: 0,
				firstType: TabletEffectType.MultiplyConstLevel,
				firstParam: 2,
				secondDx: -1,
				secondDy: 0,
				secondType: TabletEffectType.Disable,
				secondParam: 1
			);
			SlotItemSnapshot mulDisableTablet = CreateTabletItem(4, 6005, 8, true, mulDisableRules, 104);

			PlayerCombatBaseline baseline = new PlayerCombatBaseline(150f, 2.0f, 15f, 1.5f, 0f);
			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				new[] { primaryAttack, secondaryStat, sacrificeSink, plusMinusTablet, mulDisableTablet },
				combatBaseline: baseline
			);

			int[] brokenInitSlots = new int[unlockedSlots];
			Array.Fill(brokenInitSlots, -1);
			brokenInitSlots[0] = 3;
			brokenInitSlots[1] = 2;
			brokenInitSlots[6] = 0;
			brokenInitSlots[7] = 4;
			brokenInitSlots[8] = 1;
			InventorySnapshot brokenSnapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				new[] {
					CloneWithInit(primaryAttack, 6),
					CloneWithInit(secondaryStat, 8),
					CloneWithInit(sacrificeSink, 1),
					CloneWithInit(plusMinusTablet, 0),
					CloneWithInit(mulDisableTablet, 7)
				},
				combatBaseline: baseline
			);

			int[] manualNeutralizedSlots = new int[unlockedSlots];
			Array.Fill(manualNeutralizedSlots, -1);
			manualNeutralizedSlots[5] = 3;
			manualNeutralizedSlots[6] = 0;
			manualNeutralizedSlots[10] = 4;
			manualNeutralizedSlots[11] = 1;
			manualNeutralizedSlots[19] = 2;
			DpsBreakdown manualNeutralizedDps = DpsEstimator.CalculateBreakdown(
				brokenSnapshot,
				manualNeutralizedSlots,
				new[] { 0, 0, 0, 0, 0 }
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(brokenSnapshot, 350);

			int solvedAttackSlot = result.BestItemToSlotIndex[0];
			int solvedStatSlot = result.BestItemToSlotIndex[1];
			Assert.Equal(0, result.TopCandidates[0].Metrics.HardViolationCount);
			Assert.True(result.BestSlotDetails[solvedAttackSlot].IsEnabled);
			Assert.True(result.BestSlotDetails[solvedStatSlot].IsEnabled);
			Assert.True(result.BestEstimatedDps > result.InitialEstimatedDps);
			Assert.True(
				result.TopCandidates[0].Metrics.ExactEstimatedDps >= manualNeutralizedDps.TotalEstimatedDps - 1e-3
			);
		}

		[Fact(DisplayName = "소규모 복합 시너지 보드에서 분기 한정 탐색이 완전 탐색 전역 최적해와 동일한 기대 DPS를 보장한다")]
		public void Solve_SmallScaleExactBranchAndBoundMatchesBruteForceGlobalOptimum() {
			const int width = 3;
			const int height = 3;
			const int unlockedSlots = 9;

			int[] zeroArr = new int[unlockedSlots];
			bool[] unpinned = new bool[unlockedSlots];
			ulong[] mooreMasks = GameInventoryReader.BuildMooreNeighborMasks(width, height, unlockedSlots);
			const uint mysticMask = 1u;

			SlotItemSnapshot attackCharm = new SlotItemSnapshot(
				itemIndex: 0,
				instanceId: 7001,
				entityId: 7001,
				displayName: "빛의 검기",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 0,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 5,
				rarityTier: 2,
				customWeight: 18,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: true,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: new[] { "MYSTIC" },
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 1,
				priorityTier: ArtifactPriorityTier.Core,
				staticCategoryMask: mysticMask,
				attackDamageByLevel: new[] { 20f, 55f, 110f, 185f, 280f, 410f },
				statBonusPercentByLevel: new float[6]
			);

			SlotItemSnapshot crystal = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 7002,
				entityId: 7002,
				displayName: "조화의 수정",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 1,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 2,
				customWeight: 14,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: Array.Empty<string>(),
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 2,
				priorityTier: ArtifactPriorityTier.High,
				isNearLevelDamageCharm: true,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 2f, 4f, 6f, 8f }
			);

			SlotItemSnapshot sideStatCharm = new SlotItemSnapshot(
				itemIndex: 2,
				instanceId: 7003,
				entityId: 7003,
				displayName: "가장자리 핵",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 4,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 2,
				maxLevel: 5,
				rarityTier: 1,
				customWeight: 12,
				constraintType: ArtifactConstraintType.SideEnd,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: new[] { "MYSTIC" },
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 3,
				priorityTier: ArtifactPriorityTier.Normal,
				staticCategoryMask: mysticMask,
				statBonusPercentByLevel: new[] { 3f, 7f, 12f, 18f, 26f, 35f }
			);

			TabletPlacementRule[] rightBoostRules = BuildSingleEffectTabletRules(
				width,
				height,
				unlockedSlots,
				baseDx: 1,
				baseDy: 0,
				effectType: TabletEffectType.IncreaseConstLevel,
				levelParam: 2
			);
			SlotItemSnapshot rotBoostTablet = CreateTabletItem(3, 7004, 6, true, rightBoostRules, 105);

			TabletPlacementRule[] downBoostNegUpRules = BuildDoubleRightTabletRules(
				width,
				height,
				unlockedSlots,
				firstDx: 0,
				firstDy: 1,
				firstType: TabletEffectType.IncreaseConstLevel,
				firstParam: 2,
				secondDx: 0,
				secondDy: -1,
				secondType: TabletEffectType.IncreaseConstLevel,
				secondParam: -2
			);
			SlotItemSnapshot fixedBipolarTablet = CreateTabletItem(4, 7005, 8, false, downBoostNegUpRules, 106);

			ComboCategoryInfo[] combos = new[] {
				new ComboCategoryInfo(
					0,
					"MYSTIC",
					"신비",
					0,
					new[] { 2, 4 },
					true,
					breakpointStatBonuses: new[] { 10f, 25f },
					breakpointStatDeltas: new[] {
						new CombatStatDelta(allDamageBonus: 10f),
						new CombatStatDelta(allDamageBonus: 25f)
					}
				)
			};

			PlayerCombatBaseline baseline = new PlayerCombatBaseline(140f, 2.0f, 15f, 1.5f, 0f);
			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				new[] { attackCharm, crystal, sideStatCharm, rotBoostTablet, fixedBipolarTablet },
				comboCategories: combos,
				mooreNeighborMasks: mooreMasks,
				mysticSlot0: 4,
				mysticSlot1: 8,
				mysticTier1Threshold: 2,
				mysticTier2Threshold: 4,
				combatBaseline: baseline
			);

			ComputeBruteForceSmallBoardOptimum(snapshot, out long bruteMaxCentiDps, out int bruteMaxScoreAtBestDps);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot, 500);

			long solvedCentiDps = (long)Math.Round(result.TopCandidates[0].Metrics.ExactEstimatedDps * 100.0);
			Assert.True(result.UsedExactBranchAndBound);
			Assert.Equal(0, result.TopCandidates[0].Metrics.HardViolationCount);
			Assert.Equal(bruteMaxCentiDps, solvedCentiDps);
			Assert.True(result.BestScore >= bruteMaxScoreAtBestDps);
		}

		[Fact(DisplayName = "30슬롯 대규모(신비·수정·금빛침·연쇄 석판) 조합에서도 수동 최적 배치 이상의 기대 DPS와 대안을 산출한다")]
		public void Solve_LargeSixByFiveSynergyBoardBeatsOrMatchesManualOptimum() {
			const int width = 6;
			const int height = 5;
			const int unlockedSlots = 30;

			int[] zeroArr = new int[unlockedSlots];
			bool[] unpinned = new bool[unlockedSlots];
			ulong[] mooreMasks = GameInventoryReader.BuildMooreNeighborMasks(width, height, unlockedSlots);
			const uint mysticMask = 1u;

			SlotItemSnapshot coreAttack = new SlotItemSnapshot(
				itemIndex: 0,
				instanceId: 8001,
				entityId: 8001,
				displayName: "성검 오러",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 24,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 6,
				rarityTier: 1,
				customWeight: 22,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: true,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: new[] { "MYSTIC" },
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 1,
				priorityTier: ArtifactPriorityTier.Core,
				staticCategoryMask: mysticMask,
				attackDamageByLevel: new[] { 35f, 80f, 150f, 245f, 360f, 510f, 690f },
				statBonusPercentByLevel: new float[7]
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 8002,
				entityId: 8002,
				displayName: "북향의 금빛침",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 0,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 2,
				maxLevel: 5,
				rarityTier: 2,
				customWeight: 14,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: true,
				dependencyOffsetX: 0,
				dependencyOffsetY: -1,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: Array.Empty<string>(),
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 2,
				priorityTier: ArtifactPriorityTier.High,
				needleDamageBonusByLevel: new[] { 15, 25, 40, 60, 85, 115 },
				needleDependencyBonusByLevel: new[] { 10, 20, 30, 45, 65, 90 }
			);

			SlotItemSnapshot crystal = new SlotItemSnapshot(
				itemIndex: 2,
				instanceId: 8003,
				entityId: 8003,
				displayName: "조화의 수정",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 29,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 2,
				maxLevel: 4,
				rarityTier: 2,
				customWeight: 16,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: Array.Empty<string>(),
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 3,
				priorityTier: ArtifactPriorityTier.High,
				isNearLevelDamageCharm: true,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1f, 2f, 3f, 4f, 5f }
			);

			SlotItemSnapshot mysticAmpA = CreateStatArtifact(
				3,
				8004,
				25,
				"신비 공명석",
				2,
				5,
				14,
				ArtifactPriorityTier.High,
				4,
				new[] { 3f, 7f, 12f, 18f, 25f, 34f },
				new[] { "MYSTIC" },
				mysticMask
			);
			SlotItemSnapshot mysticAmpB = CreateStatArtifact(
				4,
				8005,
				26,
				"고대 마석",
				2,
				5,
				12,
				ArtifactPriorityTier.Normal,
				5,
				new[] { 2f, 5f, 9f, 14f, 20f, 28f },
				new[] { "MYSTIC" },
				mysticMask
			);
			SlotItemSnapshot dupFillerA = CreateStatArtifact(
				5,
				8006,
				27,
				"전투 부적",
				1,
				4,
				8,
				ArtifactPriorityTier.Normal,
				6,
				new[] { 2f, 4f, 7f, 11f, 16f }
			);
			SlotItemSnapshot dupFillerB = CreateStatArtifact(
				6,
				8007,
				28,
				"전투 부적",
				1,
				4,
				8,
				ArtifactPriorityTier.Normal,
				6,
				new[] { 2f, 4f, 7f, 11f, 16f }
			);

			TabletPlacementRule[] plus2RightRules = BuildSingleEffectTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot plus2TabletA = CreateTabletItem(7, 8008, 1, true, plus2RightRules, 107);
			SlotItemSnapshot plus2TabletB = CreateTabletItem(8, 8009, 2, true, plus2RightRules, 107);

			TabletPlacementRule[] mulDisableRules = BuildDoubleRightTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.MultiplyConstLevel,
				2,
				-1,
				0,
				TabletEffectType.Disable,
				1
			);
			SlotItemSnapshot mulDisableTablet = CreateTabletItem(9, 8010, 12, true, mulDisableRules, 108);

			ComboCategoryInfo[] combos = new[] {
				new ComboCategoryInfo(
					0,
					"MYSTIC",
					"신비",
					1,
					new[] { 2, 4 },
					true,
					breakpointStatBonuses: new[] { 12f, 28f },
					breakpointStatDeltas: new[] {
						new CombatStatDelta(allDamageBonus: 12f),
						new CombatStatDelta(allDamageBonus: 28f)
					}
				)
			};

			PlayerCombatBaseline baseline = new PlayerCombatBaseline(180f, 2.2f, 15f, 1.5f, 0f);
			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				new[] {
					coreAttack,
					goldenNeedle,
					crystal,
					mysticAmpA,
					mysticAmpB,
					dupFillerA,
					dupFillerB,
					plus2TabletA,
					plus2TabletB,
					mulDisableTablet
				},
				comboCategories: combos,
				mooreNeighborMasks: mooreMasks,
				mysticSlot0: 8,
				mysticSlot1: 14,
				mysticTier1Threshold: 2,
				mysticTier2Threshold: 4,
				combatBaseline: baseline
			);

			int[] manualSlots = new int[unlockedSlots];
			Array.Fill(manualSlots, -1);
			manualSlots[7] = 7;
			manualSlots[8] = 0;
			manualSlots[14] = 1;
			manualSlots[13] = 2;
			manualSlots[12] = 9;
			manualSlots[19] = 3;
			manualSlots[20] = 4;
			manualSlots[21] = 5;
			manualSlots[22] = 6;
			manualSlots[6] = 8;
			DpsBreakdown manualDps = DpsEstimator.CalculateBreakdown(
				snapshot,
				manualSlots,
				new int[10]
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot, 420);

			int coreSlot = result.BestItemToSlotIndex[0];
			int needleSlot = result.BestItemToSlotIndex[1];
			Assert.Equal(0, result.TopCandidates[0].Metrics.HardViolationCount);
			Assert.Equal(coreSlot, result.BestSlotDetails[needleSlot].ConnectedTargetSlotIndex);
			Assert.True(result.BestEstimatedDps > result.InitialEstimatedDps);
			Assert.True(result.TopCandidates[0].Metrics.ExactEstimatedDps >= manualDps.TotalEstimatedDps - 1e-3);

			HashSet<ulong> seenHashes = new HashSet<ulong>();
			for (int i = 0; i < result.TopCandidates.Count; i++) {
				Assert.True(seenHashes.Add(result.TopCandidates[i].Metrics.CanonicalHash));
			}
		}

		[Fact(DisplayName = "동료 휘장 고려 옵션 ON일 때 헌신의 휘장과 동료 부적을 같은 행에 정렬해 DPS를 높이고 OFF일 때는 핵심 아티팩트 위주로 배치한다")]
		public void Solve_CompanionEmblemToggle_BranchesOnAndOffCorrectly() {
			OptimizerUserSettings settingsOn = new OptimizerUserSettings {
				ConsiderCompanionEmblemArtifacts = true
			};
			OptimizerUserSettings settingsOff = new OptimizerUserSettings {
				ConsiderCompanionEmblemArtifacts = false
			};
			Assert.NotEqual(settingsOn.ComputeSettingsHash(), settingsOff.ComputeSettingsHash());

			const int width = 5;
			const int height = 4;
			const int unlockedSlots = 20;
			int[] zeroArr = new int[unlockedSlots];
			bool[] unpinned = new bool[unlockedSlots];

			CombatStatDelta[] chaosDeltas = new[] {
				new CombatStatDelta(followerDamageBonus: 8f),
				new CombatStatDelta(followerDamageBonus: 18f, followerAttackSpeed: 10f),
				new CombatStatDelta(followerDamageBonus: 30f, followerAttackSpeed: 18f),
				new CombatStatDelta(followerDamageBonus: 45f, followerAttackSpeed: 28f),
				new CombatStatDelta(followerDamageBonus: 62f, followerAttackSpeed: 38f)
			};
			SlotItemSnapshot companionChaos = new SlotItemSnapshot(
				itemIndex: 0,
				instanceId: 9001,
				entityId: 9001,
				displayName: "헌신의 휘장",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 0,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 4,
				rarityTier: 2,
				customWeight: 14,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: new[] { "COMPANION" },
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 1,
				priorityTier: ArtifactPriorityTier.High,
				isCompanionChaosCharm: true,
				statDeltaByLevel: chaosDeltas,
				isCompanionEmblemArtifact: true
			);

			CombatStatDelta[] companionAtkDeltas = new[] {
				new CombatStatDelta(followerCritChancePercent: 5f),
				new CombatStatDelta(followerDamageBonus: 12f, followerCritChancePercent: 10f),
				new CombatStatDelta(followerDamageBonus: 24f, followerCritChancePercent: 15f),
				new CombatStatDelta(followerDamageBonus: 40f, followerCritChancePercent: 22f),
				new CombatStatDelta(followerDamageBonus: 60f, followerCritChancePercent: 30f)
			};
			SlotItemSnapshot companionBallista = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 9002,
				entityId: 9002,
				displayName: "미니 발리스타",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 18,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 4,
				rarityTier: 2,
				customWeight: 18,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: true,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: new[] { "COMPANION" },
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: 2,
				priorityTier: ArtifactPriorityTier.High,
				isCompanionCharm: true,
				attackDamageByLevel: new[] { 90f, 180f, 320f, 520f, 780f },
				statDeltaByLevel: companionAtkDeltas,
				isCompanionEmblemArtifact: true,
				isCompanionAttackCharm: true
			);

			SlotItemSnapshot playerCoreCharm = CreateAttackArtifact(
				itemIndex: 2,
				instanceId: 9003,
				initialSlot: 19,
				displayName: "화염 검기",
				baseLevel: 1,
				maxLevel: 4,
				weight: 16,
				tier: ArtifactPriorityTier.Core,
				symmetryGroupId: 3,
				attackTable: new[] { 40f, 80f, 145f, 235f, 360f }
			);

			TabletPlacementRule[] bipolarRules = BuildDoubleRightTabletRules(
				width,
				height,
				unlockedSlots,
				firstDx: 1,
				firstDy: 0,
				firstType: TabletEffectType.IncreaseConstLevel,
				firstParam: 3,
				secondDx: -1,
				secondDy: 0,
				secondType: TabletEffectType.IncreaseConstLevel,
				secondParam: -3
			);
			SlotItemSnapshot bipolarTablet = CreateTabletItem(3, 9004, 6, true, bipolarRules, 109);

			SlotItemSnapshot[] itemsForOn = new[] { companionChaos, companionBallista, playerCoreCharm, bipolarTablet };
			SlotItemSnapshot[] itemsForOff = new[] {
				CloneWithInit(companionChaos, 0),
				CloneWithInit(companionBallista, 18),
				CloneWithInit(playerCoreCharm, 19),
				CloneWithInit(bipolarTablet, 6)
			};

			PlayerCombatBaseline baseline = new PlayerCombatBaseline(120f, 2.0f, 15f, 1.5f, 0f)
				.WithFollowerBaseline(10f, 10f, 10f);

			InventorySnapshot snapshotOn = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				itemsForOn,
				combatBaseline: baseline,
				considerCompanionEmblemArtifacts: true
			);
			InventorySnapshot snapshotOff = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				itemsForOff,
				combatBaseline: baseline,
				considerCompanionEmblemArtifacts: false
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult resultOn = solver.Solve(snapshotOn, 350);
			OptimizationResult resultOff = solver.Solve(snapshotOff, 350);

			int chaosSlotOn = resultOn.BestItemToSlotIndex[0];
			int ballistaSlotOn = resultOn.BestItemToSlotIndex[1];
			int coreSlotOn = resultOn.BestItemToSlotIndex[2];
			int coreSlotOff = resultOff.BestItemToSlotIndex[2];
			int chaosRowOn = snapshotOn.Slots[chaosSlotOn].Y;
			int ballistaRowOn = snapshotOn.Slots[ballistaSlotOn].Y;

			Assert.False(snapshotOn.IsExcludedCompanionEmblem(companionChaos));
			Assert.True(snapshotOff.IsExcludedCompanionEmblem(companionChaos));
			Assert.True(snapshotOff.IsExcludedCompanionEmblem(companionBallista));
			Assert.False(snapshotOff.IsExcludedCompanionEmblem(playerCoreCharm));

			Assert.Equal(chaosRowOn, ballistaRowOn);
			Assert.True(resultOn.BestSlotDetails[ballistaSlotOn].EffectiveLevel >= 3);
			Assert.True(resultOn.BestEstimatedDps > resultOff.BestEstimatedDps);
			Assert.True(resultOn.BestSlotDetails[coreSlotOn].IsEnabled);

			Assert.True(resultOff.BestSlotDetails[coreSlotOff].IsEnabled);
			Assert.Equal(4, resultOff.BestSlotDetails[coreSlotOff].EffectiveLevel);
		}

		[Fact(DisplayName = "동료 휘장 및 2패스 신비·콤보 수렴 평가 핫패스는 스레드 힙 메모리 할당을 0 바이트로 유지한다")]
		public void HotPath_WithCompanionEmblemsAndConvergence_AllocatesZeroHeapBytes() {
			const int width = 5;
			const int height = 4;
			const int unlockedSlots = 20;
			int[] zeroArr = new int[unlockedSlots];
			bool[] unpinned = new bool[unlockedSlots];

			SlotItemSnapshot chaos = new SlotItemSnapshot(
				0,
				9501,
				9501,
				"헌신의 휘장",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				1,
				4,
				2,
				14,
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
				new[] { "COMPANION" },
				Array.Empty<TabletPlacementRule>(),
				1,
				isCompanionChaosCharm: true,
				isCompanionEmblemArtifact: true
			);
			SlotItemSnapshot companionAtk = new SlotItemSnapshot(
				1,
				9502,
				9502,
				"늑대 소환",
				SlotItemKind.Artifact,
				1,
				0,
				false,
				1,
				4,
				2,
				16,
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
				new[] { "COMPANION" },
				Array.Empty<TabletPlacementRule>(),
				2,
				isCompanionCharm: true,
				attackDamageByLevel: new[] { 40f, 80f, 130f, 190f, 270f },
				isCompanionEmblemArtifact: true,
				isCompanionAttackCharm: true
			);
			TabletPlacementRule[] rightRules = BuildSingleEffectTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot tablet = CreateTabletItem(2, 9503, 5, true, rightRules, 110);

			InventorySnapshot snapshotOn = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				new[] { chaos, companionAtk, tablet },
				considerCompanionEmblemArtifacts: true
			);
			InventorySnapshot snapshotOff = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				zeroArr,
				zeroArr,
				zeroArr,
				zeroArr,
				unpinned,
				isFullHpSatisfied: true,
				globalActiveValue: 1,
				new[] { CloneWithInit(chaos, 0), CloneWithInit(companionAtk, 1), CloneWithInit(tablet, 5) },
				considerCompanionEmblemArtifacts: false
			);

			int[] slots = new int[unlockedSlots];
			Array.Fill(slots, -1);
			slots[0] = 0;
			slots[6] = 1;
			slots[5] = 2;
			int[] rots = new[] { 0, 0, 0 };

			ObjectiveEvaluator.EvaluateWithMetrics(snapshotOn, slots, rots, out _, out _, out _);
			ObjectiveEvaluator.EvaluateWithMetrics(snapshotOff, slots, rots, out _, out _, out _);
			ObjectiveEvaluator.ComputeCanonicalLayoutHash(snapshotOn, slots, rots);

			long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
			for (int i = 0; i < 2000; i++) {
				InventorySnapshot target = (i & 1) == 0 ? snapshotOn : snapshotOff;
				ObjectiveEvaluator.Evaluate(target, slots, rots);
				ObjectiveEvaluator.EvaluateWithMetrics(target, slots, rots, out _, out _, out _);
				ObjectiveEvaluator.ComputeCanonicalLayoutHash(target, slots, rots);
			}
			long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

			Assert.Equal(0L, allocatedBytes);
		}

		private static void ComputeBruteForceSmallBoardOptimum(
			InventorySnapshot snapshot,
			out long maxCentiDps,
			out int maxScoreAtMaxDps
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			int[] slots = new int[unlockedSlots];
			Array.Fill(slots, -1);
			int[] rots = new int[itemCount];

			long bestCenti = long.MinValue;
			int bestScore = int.MinValue;

			void Recurse(int depth) {
				if (depth == itemCount) {
					int score = ObjectiveEvaluator.EvaluateWithMetrics(
						snapshot,
						slots,
						rots,
						out double dps,
						out int viol,
						out _
					);
					if (viol > 0) {
						return;
					}
					long centi = (long)Math.Round(dps * 100.0);
					if (centi > bestCenti || (centi == bestCenti && score > bestScore)) {
						bestCenti = centi;
						bestScore = score;
					}
					return;
				}
				SlotItemSnapshot item = items[depth];
				int maxRot = (item.Kind == SlotItemKind.StoneTablet && item.IsRotatable) ? 4 : 1;
				for (int s = 0; s < unlockedSlots; s++) {
					if (slots[s] >= 0) {
						continue;
					}
					slots[s] = depth;
					for (int r = 0; r < maxRot; r++) {
						rots[depth] = r;
						Recurse(depth + 1);
					}
					slots[s] = -1;
				}
				rots[depth] = 0;
			}

			Recurse(0);
			maxCentiDps = bestCenti;
			maxScoreAtMaxDps = bestScore;
		}

		private static SlotItemSnapshot CreateAttackArtifact(
			int itemIndex,
			int instanceId,
			int initialSlot,
			string displayName,
			int baseLevel,
			int maxLevel,
			int weight,
			ArtifactPriorityTier tier,
			int symmetryGroupId,
			float[] attackTable
		) {
			return new SlotItemSnapshot(
				itemIndex: itemIndex,
				instanceId: instanceId,
				entityId: instanceId,
				displayName: displayName,
				kind: SlotItemKind.Artifact,
				initialSlotIndex: initialSlot,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: baseLevel,
				maxLevel: maxLevel,
				rarityTier: 2,
				customWeight: weight,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: true,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: Array.Empty<string>(),
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: symmetryGroupId,
				priorityTier: tier,
				attackDamageByLevel: attackTable,
				statBonusPercentByLevel: new float[maxLevel + 1]
			);
		}

		private static SlotItemSnapshot CreateStatArtifact(
			int itemIndex,
			int instanceId,
			int initialSlot,
			string displayName,
			int baseLevel,
			int maxLevel,
			int weight,
			ArtifactPriorityTier tier,
			int symmetryGroupId,
			float[] statTable,
			string[]? categories = null,
			uint staticCategoryMask = 0u
		) {
			return new SlotItemSnapshot(
				itemIndex: itemIndex,
				instanceId: instanceId,
				entityId: instanceId,
				displayName: displayName,
				kind: SlotItemKind.Artifact,
				initialSlotIndex: initialSlot,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: baseLevel,
				maxLevel: maxLevel,
				rarityTier: 1,
				customWeight: weight,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: categories ?? Array.Empty<string>(),
				placementRules: Array.Empty<TabletPlacementRule>(),
				symmetryGroupId: symmetryGroupId,
				priorityTier: tier,
				staticCategoryMask: staticCategoryMask,
				statBonusPercentByLevel: statTable
			);
		}

		private static SlotItemSnapshot CreateTabletItem(
			int itemIndex,
			int instanceId,
			int initialSlot,
			bool isRotatable,
			TabletPlacementRule[] rules,
			int symmetryGroupId
		) {
			return new SlotItemSnapshot(
				itemIndex: itemIndex,
				instanceId: instanceId,
				entityId: instanceId,
				displayName: $"석판 {instanceId}",
				kind: SlotItemKind.StoneTablet,
				initialSlotIndex: initialSlot,
				initialRotation: 0,
				isRotatable: isRotatable,
				baseLevel: 0,
				maxLevel: 0,
				rarityTier: 1,
				customWeight: 1,
				constraintType: ArtifactConstraintType.None,
				isDisabledByWeaponOrDual: false,
				isMagicBook: false,
				wantsRightSideMagicBook: false,
				isAttackableCharm: false,
				hasDependencyTarget: false,
				dependencyOffsetX: 0,
				dependencyOffsetY: 0,
				isElementalByRowCharm: false,
				isWhitePaperCharm: false,
				categories: Array.Empty<string>(),
				placementRules: rules,
				symmetryGroupId: symmetryGroupId
			);
		}

		private static SlotItemSnapshot CloneWithInit(SlotItemSnapshot src, int initSlot) {
			return new SlotItemSnapshot(
				itemIndex: src.ItemIndex,
				instanceId: src.InstanceId,
				entityId: src.EntityId,
				displayName: src.DisplayName,
				kind: src.Kind,
				initialSlotIndex: initSlot,
				initialRotation: src.InitialRotation,
				isRotatable: src.IsRotatable,
				baseLevel: src.BaseLevel,
				maxLevel: src.MaxLevel,
				rarityTier: src.RarityTier,
				customWeight: src.CustomWeight,
				constraintType: src.ConstraintType,
				isDisabledByWeaponOrDual: src.IsDisabledByWeaponOrDual,
				isMagicBook: src.IsMagicBook,
				wantsRightSideMagicBook: src.WantsRightSideMagicBook,
				isAttackableCharm: src.IsAttackableCharm,
				hasDependencyTarget: src.HasDependencyTarget,
				dependencyOffsetX: src.DependencyOffsetX,
				dependencyOffsetY: src.DependencyOffsetY,
				isElementalByRowCharm: src.IsElementalByRowCharm,
				isWhitePaperCharm: src.IsWhitePaperCharm,
				categories: src.Categories,
				placementRules: src.PlacementRules,
				symmetryGroupId: src.SymmetryGroupId,
				priorityTier: src.PriorityTier,
				staticCategoryMask: src.StaticCategoryMask,
				isNearLevelDamageCharm: src.IsNearLevelDamageCharm,
				isCompanionChaosCharm: src.IsCompanionChaosCharm,
				isCompanionCharm: src.IsCompanionCharm,
				isCrystalOfHarmony: src.IsCrystalOfHarmony,
				crystalDamageBonusByLevel: src.CrystalDamageBonusByLevel,
				attackDamageByLevel: src.AttackDamageByLevel,
				statBonusPercentByLevel: src.StatBonusPercentByLevel,
				statDeltaByLevel: src.HasExplicitStatDeltas ? src.StatDeltaByLevel : null,
				isCompanionEmblemArtifact: src.IsCompanionEmblemArtifact,
				isCompanionAttackCharm: src.IsCompanionAttackCharm
			);
		}

		private static TabletPlacementRule[] BuildSingleEffectTabletRules(
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
					int tgt = ResolveSlot(x + dx, y + dy, width, height, unlockedSlots);
					rules[(s * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						new[] {
							new PrecomputedEffect((sbyte)(x + dx), (sbyte)(y + dy), tgt, effectType, levelParam)
						}
					);
				}
			}
			return rules;
		}

		private static TabletPlacementRule[] BuildDoubleRightTabletRules(
			int width,
			int height,
			int unlockedSlots,
			int firstDx,
			int firstDy,
			TabletEffectType firstType,
			int firstParam,
			int secondDx,
			int secondDy,
			TabletEffectType secondType,
			int secondParam
		) {
			TabletPlacementRule[] rules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				int x = s % width;
				int y = s / width;
				for (int rot = 0; rot < 4; rot++) {
					(int dx1, int dy1) = RotateOffset(firstDx, firstDy, rot);
					(int dx2, int dy2) = RotateOffset(secondDx, secondDy, rot);
					int tgt1 = ResolveSlot(x + dx1, y + dy1, width, height, unlockedSlots);
					int tgt2 = ResolveSlot(x + dx2, y + dy2, width, height, unlockedSlots);
					rules[(s * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						new[] {
							new PrecomputedEffect((sbyte)(x + dx1), (sbyte)(y + dy1), tgt1, firstType, firstParam),
							new PrecomputedEffect((sbyte)(x + dx2), (sbyte)(y + dy2), tgt2, secondType, secondParam)
						}
					);
				}
			}
			return rules;
		}

		private static (int Dx, int Dy) RotateOffset(int dx, int dy, int rot) {
			return (rot & 3) switch {
				0 => (dx, dy),
				1 => (-dy, dx),
				2 => (-dx, -dy),
				_ => (dy, -dx)
			};
		}

		private static int ResolveSlot(int x, int y, int width, int height, int unlockedSlots) {
			if (x < 0 || y < 0 || x >= width || y >= height) {
				return -1;
			}
			int idx = (y * width) + x;
			return idx < unlockedSlots ? idx : -1;
		}
	}
}
