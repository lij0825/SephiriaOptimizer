using System;
using SephiriaOptimizer.Execution;
using SephiriaOptimizer.Models;
using SephiriaOptimizer.Snapshot;
using SephiriaOptimizer.Solver;
using Xunit;

namespace SephiriaOptimizer.Tests {
	public sealed class OptimizationSolverTest {
		[Fact(DisplayName = "아티팩트의 유효 레벨이 최대 별 개수로 클램핑되고 최종 레벨이 -1 이하일 때 비활성화 페널티가 부과된다")]
		public void Evaluate_ClampsEffectiveLevelAndPenalizesNegativeLevel() {
			int width = 3;
			int height = 1;
			int unlockedSlots = 3;

			SlotItemSnapshot artifactMax2 = CreateArtifact(
				0,
				101,
				0,
				0,
				2,
				3,
				ArtifactConstraintType.None,
				1
			);

			TabletPlacementRule[] buffRules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				5
			);
			SlotItemSnapshot buffTablet = CreateTablet(1, 201, 1, 0, false, buffRules, 2);

			TabletPlacementRule[] debuffRules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				-2
			);
			SlotItemSnapshot debuffTablet = CreateTablet(2, 202, 2, 0, false, debuffRules, 3);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { artifactMax2, buffTablet, debuffTablet }
			);

			int[] buffedLayout = new[] { 1, 0, -1 };
			int[] rotations = new[] { 0, 0, 0 };
			SlotEvaluationDetail[] buffedDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				buffedLayout,
				rotations
			);
			int buffedScore = ObjectiveEvaluator.Evaluate(snapshot, buffedLayout, rotations);

			int[] deactivatedLayout = new[] { 2, 0, -1 };
			SlotEvaluationDetail[] deactivatedDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				deactivatedLayout,
				rotations
			);
			int deactivatedScore = ObjectiveEvaluator.Evaluate(snapshot, deactivatedLayout, rotations);

			Assert.True(buffedDetails[1].IsEnabled);
			Assert.Equal(5, buffedDetails[1].DisplayedLevel);
			Assert.Equal(2, buffedDetails[1].EffectiveLevel);

			Assert.False(deactivatedDetails[1].IsEnabled);
			Assert.True(deactivatedDetails[1].IsDeactivatedByNegativeLevel);
			Assert.Equal(-2, deactivatedDetails[1].DisplayedLevel);
			Assert.True(buffedScore > 0);
			Assert.True(deactivatedScore < 0);
		}

		[Fact(DisplayName = "석판의 레벨 감소 효과가 그리드 바깥이나 빈 슬롯을 향하도록 배치하여 페널티를 증발시킨다")]
		public void Solve_NeutralizesNegativeLevelPenaltyByPointingOutsideOrEmpty() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			SlotItemSnapshot artifact = CreateArtifact(
				0,
				101,
				1,
				0,
				3,
				5,
				ArtifactConstraintType.None,
				1
			);

			TabletPlacementRule[] mixedTabletRules = new TabletPlacementRule[unlockedSlots * 4];
			for (int slotIdx = 0; slotIdx < unlockedSlots; slotIdx++) {
				int x = slotIdx % width;
				int y = slotIdx / width;
				for (int rot = 0; rot < 4; rot++) {
					(int buffDx, int buffDy) = RotateOffset(-1, 0, rot);
					(int debuffDx, int debuffDy) = RotateOffset(1, 0, rot);

					int buffTarget = CoordToSlot(x + buffDx, y + buffDy, width, height, unlockedSlots);
					int debuffTarget = CoordToSlot(x + debuffDx, y + debuffDy, width, height, unlockedSlots);

					PrecomputedEffect[] effects = new[] {
						new PrecomputedEffect(
							(sbyte)(x + buffDx),
							(sbyte)(y + buffDy),
							buffTarget,
							TabletEffectType.IncreaseConstLevel,
							2
						),
						new PrecomputedEffect(
							(sbyte)(x + debuffDx),
							(sbyte)(y + debuffDy),
							debuffTarget,
							TabletEffectType.IncreaseConstLevel,
							-2
						)
					};
					mixedTabletRules[(slotIdx * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						effects
					);
				}
			}

			SlotItemSnapshot tablet = CreateTablet(1, 201, 0, 0, true, mixedTabletRules, 2);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { artifact, tablet }
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int artifactTargetSlot = result.BestItemToSlotIndex[0];
			SlotEvaluationDetail artifactDetail = result.BestSlotDetails[artifactTargetSlot];

			Assert.True(result.UsedExactBranchAndBound);
			Assert.True(result.BestScore > result.InitialScore);
			Assert.True(artifactDetail.IsEnabled);
			Assert.Equal(2, artifactDetail.EffectiveLevel);
		}

		[Fact(DisplayName = "제약 조건이 있는 아티팩트를 제약 조건 무시 석판의 타겟 슬롯에 배치하여 활성화 시너지를 획득한다")]
		public void Solve_PlacesConstrainedArtifactOnIgnoreCriteriaSlot() {
			int width = 6;
			int height = 2;
			int unlockedSlots = 12;

			SlotItemSnapshot insideArtifact = CreateArtifact(
				0,
				105,
				0,
				1,
				3,
				6,
				ArtifactConstraintType.Inside,
				1
			);

			TabletPlacementRule[] ignoreTabletRules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IgnoreCriteria,
				1
			);
			SlotItemSnapshot ignoreTablet = CreateTablet(1, 205, 5, 0, false, ignoreTabletRules, 2);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { insideArtifact, ignoreTablet }
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int artifactSlot = result.BestItemToSlotIndex[0];
			int tabletSlot = result.BestItemToSlotIndex[1];
			SlotEvaluationDetail detail = result.BestSlotDetails[artifactSlot];

			Assert.True(detail.IsEnabled);
			Assert.True(detail.IsCriteriaIgnored);
			Assert.Equal(tabletSlot + 1, artifactSlot);
			Assert.True(result.BestScore > result.InitialScore);
		}

		[Fact(DisplayName = "캘세더니 열쇠와 하얀 종이를 목표 콤보 행 및 동일 태그 아티팩트 사이에 배치하여 세트 단계를 달성한다")]
		public void Solve_PlacesChalcedonyKeyAndWhitePaperToCompleteTargetCombo() {
			int width = 3;
			int height = 3;
			int unlockedSlots = 9;

			uint emberMask = 1u << 0;
			uint glacierMask = 1u << 1;

			SlotItemSnapshot chalcedonyKey = new SlotItemSnapshot(
				0,
				101,
				101,
				"캘세더니 열쇠",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				0,
				0,
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
				true,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Normal,
				0u,
				false,
				1,
				null,
				null,
				new[] { 0, 1, 2 },
				new[] { 2, 4, 6 }
			);

			SlotItemSnapshot whitePaper = new SlotItemSnapshot(
				1,
				102,
				102,
				"하얀 종이",
				SlotItemKind.Artifact,
				8,
				0,
				false,
				0,
				0,
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
				2,
				ArtifactPriorityTier.Normal,
				0u,
				false,
				1,
				null,
				null,
				null,
				null,
				2
			);

			SlotItemSnapshot glacierCharm = new SlotItemSnapshot(
				2,
				103,
				103,
				"서리 반지",
				SlotItemKind.Artifact,
				2,
				0,
				false,
				0,
				2,
				1,
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
				new[] { "GLACIER" },
				Array.Empty<TabletPlacementRule>(),
				3,
				ArtifactPriorityTier.Normal,
				glacierMask
			);

			ComboCategoryInfo[] comboInfos = new[] {
				new ComboCategoryInfo(0, "EMBER", "잉걸불", 0, new[] { 3, 6 }, false),
				new ComboCategoryInfo(1, "GLACIER", "빙하", 1, new[] { 3, 6 }, true),
				new ComboCategoryInfo(2, "MAGITECH", "마도공학", 0, new[] { 3, 6 }, false)
			};

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
				new[] { chalcedonyKey, whitePaper, glacierCharm },
				comboInfos,
				TalentRuleSnapshot.Default,
				-1
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int keySlot = result.BestItemToSlotIndex[0];
			int paperSlot = result.BestItemToSlotIndex[1];
			int glacierSlot = result.BestItemToSlotIndex[2];

			int keyRow = keySlot / width;
			int paperRow = paperSlot / width;
			int glacierRow = glacierSlot / width;
			int paperCol = paperSlot % width;

			Assert.Equal(1, keyRow);
			Assert.Equal(1, paperRow);
			Assert.Equal(1, glacierRow);
			Assert.Equal(1, paperCol);
			Assert.Equal(glacierMask, result.BestSlotDetails[keySlot].DynamicCategoryMask);
			Assert.Equal(glacierMask, result.BestSlotDetails[paperSlot].DynamicCategoryMask);
			Assert.True((emberMask & result.BestSlotDetails[keySlot].DynamicCategoryMask) == 0u);
		}

		[Fact(DisplayName = "북향의 금빛침을 지정된 공격형 아티팩트 아래에 배치하여 등급 조건부 2배 피해 증폭과 카테고리 복제를 달성한다")]
		public void Solve_PlacesGoldenNeedleBelowTargetAttackableArtifactAndCopiesCategories() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			uint arcaneMask = 1u << 0;

			SlotItemSnapshot uncommonDagger = new SlotItemSnapshot(
				0,
				301,
				301,
				"마력 단검",
				SlotItemKind.Artifact,
				5,
				0,
				false,
				1,
				2,
				1,
				5,
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
				new[] { "ARCANE" },
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Core,
				arcaneMask
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				1,
				302,
				302,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				0,
				2,
				2,
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
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				2,
				ArtifactPriorityTier.Normal,
				0u,
				true,
				1,
				new[] { 30, 50, 80 },
				new[] { 30, 50, 80 }
			);

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
				new[] { uncommonDagger, goldenNeedle },
				new[] { new ComboCategoryInfo(0, "ARCANE", "비전", 1, new[] { 2 }, true) },
				TalentRuleSnapshot.Default,
				301
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int daggerSlot = result.BestItemToSlotIndex[0];
			int needleSlot = result.BestItemToSlotIndex[1];

			int daggerX = daggerSlot % width;
			int daggerY = daggerSlot / width;
			int needleX = needleSlot % width;
			int needleY = needleSlot / width;

			Assert.Equal(daggerX, needleX);
			Assert.Equal(daggerY + 1, needleY);
			Assert.Equal(arcaneMask, result.BestSlotDetails[needleSlot].DynamicCategoryMask);
			Assert.True(result.BestScore > result.InitialScore);
		}

		[Fact(DisplayName = "강화 우선순위가 최우선인 아티팩트에 버프를 집중하고 희생 등급 아티팩트로 석판의 레벨 감소 페널티를 흡수한다")]
		public void Solve_PrioritizesCoreArtifactsAndUsesSacrificeArtifactsToAbsorbNegativeTabletEffects() {
			int width = 3;
			int height = 1;
			int unlockedSlots = 3;

			SlotItemSnapshot coreArtifact = new SlotItemSnapshot(
				0,
				401,
				401,
				"핵심 딜링 아티팩트",
				SlotItemKind.Artifact,
				2,
				0,
				false,
				0,
				3,
				2,
				15,
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
				new[] { "EMBER" },
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Core,
				1u
			);

			SlotItemSnapshot sacrificeTotem = new SlotItemSnapshot(
				1,
				402,
				402,
				"희생용 콤보 토템",
				SlotItemKind.Artifact,
				0,
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
				new[] { "EMBER" },
				Array.Empty<TabletPlacementRule>(),
				2,
				ArtifactPriorityTier.Sacrifice,
				1u
			);

			TabletPlacementRule[] centerTabletRules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				for (int r = 0; r < 4; r++) {
					int leftTarget = s - 1 >= 0 ? s - 1 : -1;
					int rightTarget = s + 1 < unlockedSlots ? s + 1 : -1;
					PrecomputedEffect[] effects = new[] {
						new PrecomputedEffect((sbyte)(s - 1), 0, leftTarget, TabletEffectType.IncreaseConstLevel, 3),
						new PrecomputedEffect((sbyte)(s + 1), 0, rightTarget, TabletEffectType.IncreaseConstLevel, -2)
					};
					centerTabletRules[(s * 4) + r] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						effects
					);
				}
			}

			bool[] pinned = new[] { false, true, false };
			SlotItemSnapshot centerTablet = CreateTablet(2, 403, 1, 0, false, centerTabletRules, 3);

			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				pinned,
				true,
				1,
				new[] { coreArtifact, sacrificeTotem, centerTablet },
				new[] { new ComboCategoryInfo(0, "EMBER", "잉걸불", 2, new[] { 2 }, true) },
				TalentRuleSnapshot.Default,
				-1
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int coreSlot = result.BestItemToSlotIndex[0];
			int sacrificeSlot = result.BestItemToSlotIndex[1];

			Assert.Equal(0, coreSlot);
			Assert.Equal(2, sacrificeSlot);
			Assert.Equal(3, result.BestSlotDetails[coreSlot].EffectiveLevel);
			Assert.False(result.BestSlotDetails[sacrificeSlot].IsEnabled);
			Assert.True(result.BestScore > result.InitialScore);
		}

		[Fact(DisplayName = "생존 20 재능 활성화 시 줄어든 인벤토리 경계 밖으로 석판의 페널티를 유도하고 영구 각인 슬롯 효과를 반영한다")]
		public void Solve_AppliesSurvival20TabletEngravingAndPermanentSlotBonuses() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			int[] baseConstLevel = new[] { 2, 0, 0, 0, 0, 0 };

			SlotItemSnapshot artifact = CreateArtifact(
				0,
				501,
				5,
				0,
				3,
				8,
				ArtifactConstraintType.None,
				1
			);

			TabletPlacementRule[] engravingTabletRules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				0,
				1,
				TabletEffectType.IncreaseConstLevel,
				-3
			);
			SlotItemSnapshot tablet = CreateTablet(1, 502, 1, 0, false, engravingTabletRules, 2);

			TalentRuleSnapshot survival20Rules = new TalentRuleSnapshot(
				2,
				0,
				0,
				0,
				0,
				false,
				false,
				0,
				0,
				0,
				0,
				Array.Empty<ArrangementPattern>()
			);

			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				baseConstLevel,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { artifact, tablet },
				Array.Empty<ComboCategoryInfo>(),
				survival20Rules,
				-1
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int artifactSlot = result.BestItemToSlotIndex[0];
			int tabletSlot = result.BestItemToSlotIndex[1];
			int tabletRow = tabletSlot / width;

			Assert.Equal(0, artifactSlot);
			Assert.Equal(2, result.BestSlotDetails[artifactSlot].EffectiveLevel);
			Assert.Equal(1, tabletRow);
			Assert.True(result.BestSlotDetails[tabletSlot].IsEngravingReady);
			Assert.True(result.BestScore > result.InitialScore);
		}

		[Fact(DisplayName = "대규모 인벤토리에서 담금질 기법과 재가열이 400ms 이내에 최적화된 배치를 도출한다")]
		public void Solve_LargeScaleSimulatedAnnealingImprovesScoreWithinBudget() {
			int width = 6;
			int height = 4;
			int unlockedSlots = 24;

			SlotItemSnapshot[] items = new SlotItemSnapshot[12];
			for (int i = 0; i < 8; i++) {
				ArtifactConstraintType constraint = (i % 2 == 0)
					? ArtifactConstraintType.TopInInventory
					: ArtifactConstraintType.None;
				items[i] = CreateArtifact(
					i,
					100 + i,
					unlockedSlots - 1 - i,
					0,
					3,
					4 + (i % 3),
					constraint,
					i + 1
				);
			}

			for (int t = 0; t < 4; t++) {
				int itemIdx = 8 + t;
				TabletPlacementRule[] rules = CreateDirectionalTabletRules(
					width,
					height,
					unlockedSlots,
					1,
					0,
					TabletEffectType.IncreaseConstLevel,
					2
				);
				items[itemIdx] = CreateTablet(itemIdx, 300 + t, t, 2, true, rules, 50);
			}

			InventorySnapshot snapshot = CreateSimpleSnapshot(width, height, unlockedSlots, items);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot, 250);

			Assert.False(result.UsedExactBranchAndBound);
			Assert.True(result.BestScore > result.InitialScore);
			Assert.True(result.ElapsedMilliseconds < 400.0);
		}

		[Fact(DisplayName = "순열 사이클 분해가 최소 횟수의 2-슬롯 교환으로 현재 배치를 최적 배치로 정확히 변환한다")]
		public void BuildExecutionPlan_DecomposesPermutationIntoMinimalSwaps() {
			int width = 4;
			int height = 2;
			int unlockedSlots = 8;

			SlotItemSnapshot[] items = new SlotItemSnapshot[5];
			for (int i = 0; i < 5; i++) {
				items[i] = CreateArtifact(i, 100 + i, i, 0, 2, 2, ArtifactConstraintType.None, i + 1);
			}

			InventorySnapshot snapshot = CreateSimpleSnapshot(width, height, unlockedSlots, items);

			int[] currentSlots = new[] { 0, 1, 2, 3, 4, -1, -1, -1 };
			int[] targetSlots = new[] { 1, 2, 0, -1, 4, 3, -1, -1 };
			int[] currentRots = new int[5];
			int[] targetRots = new int[5];

			ExecutionPlan plan = CycleDecomposition.BuildExecutionPlan(
				snapshot,
				currentSlots,
				targetSlots,
				currentRots,
				targetRots
			);

			int[] simulatedSlots = (int[])currentSlots.Clone();
			for (int i = 0; i < plan.SwapSteps.Count; i++) {
				SlotSwapStep step = plan.SwapSteps[i];
				int temp = simulatedSlots[step.FirstSlotIndex];
				simulatedSlots[step.FirstSlotIndex] = simulatedSlots[step.SecondSlotIndex];
				simulatedSlots[step.SecondSlotIndex] = temp;
			}

			Assert.Equal(3, plan.SwapSteps.Count);
			Assert.Equal(targetSlots, simulatedSlots);
		}

		[Fact(DisplayName = "석판의 +강화 효과가 인벤토리 밖으로 빠져 낭비되거나 다른 석판이 강화한 칸 위에 석판이 올라가는 배치를 방지한다")]
		public void Solve_PreventsTabletPositiveEffectFromWastingOutOfBoundsAndPreventsTabletSittingOnBuffedSlot() {
			int width = 3;
			int height = 1;
			int unlockedSlots = 3;

			SlotItemSnapshot maxedArtifact = CreateArtifact(
				0,
				601,
				0,
				1,
				1,
				5,
				ArtifactConstraintType.None,
				1
			);

			TabletPlacementRule[] rightBuffRules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);

			SlotItemSnapshot tabletA = CreateTablet(1, 602, 1, 0, true, rightBuffRules, 2);
			SlotItemSnapshot tabletB = CreateTablet(2, 603, 2, 0, true, rightBuffRules, 2);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { maxedArtifact, tabletA, tabletB }
			);

			int[] badOutAndOverlapLayout = new[] { 0, 1, 2 };
			int[] badRotations = new[] { 0, 0, 0 };
			int[] goodConvergingLayout = new[] { 1, 0, 2 };
			int[] goodRotations = new[] { 0, 0, 2 };

			int badScore = ObjectiveEvaluator.Evaluate(snapshot, badOutAndOverlapLayout, badRotations);
			int goodScore = ObjectiveEvaluator.Evaluate(snapshot, goodConvergingLayout, goodRotations);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			Assert.True(goodScore > badScore);
			Assert.Equal(1, result.BestItemToSlotIndex[0]);
			Assert.Equal(3, result.BestSlotDetails[1].DisplayedLevel);
		}

		[Fact(DisplayName = "캘세더니 열쇠의 고유 목표 속성과 하얀 종이의 양쪽 아티팩트 콤보 복제 목표를 독립된 영역으로 달성한다")]
		public void Solve_SeparatesChalcedonyKeyTargetFromWhitePaperComboTarget() {
			int width = 3;
			int height = 4;
			int unlockedSlots = 12;

			uint frostMask = 1u << 4;

			SlotItemSnapshot chalcedonyKey = new SlotItemSnapshot(
				0,
				701,
				701,
				"캘세더니 열쇠",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				0,
				0,
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
				true,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Normal,
				0u,
				false,
				1,
				null,
				null,
				new[] { 0, 1, 2, 3 },
				new[] { 2, 4, 6, 8 }
			);

			SlotItemSnapshot whitePaper = new SlotItemSnapshot(
				1,
				702,
				702,
				"하얀 종이",
				SlotItemKind.Artifact,
				11,
				0,
				false,
				0,
				0,
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
				2,
				ArtifactPriorityTier.Normal,
				0u,
				false,
				1,
				null,
				null,
				null,
				null,
				2
			);

			SlotItemSnapshot frostA = new SlotItemSnapshot(
				2,
				703,
				703,
				"얼음무구 A",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				0,
				1,
				1,
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
				new[] { "FROST" },
				Array.Empty<TabletPlacementRule>(),
				3,
				ArtifactPriorityTier.Normal,
				frostMask
			);

			SlotItemSnapshot frostB = new SlotItemSnapshot(
				3,
				704,
				704,
				"얼음무구 B",
				SlotItemKind.Artifact,
				8,
				0,
				false,
				0,
				1,
				1,
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
				new[] { "FROST" },
				Array.Empty<TabletPlacementRule>(),
				3,
				ArtifactPriorityTier.Normal,
				frostMask
			);

			ComboCategoryInfo[] comboInfos = new[] {
				new ComboCategoryInfo(0, "EMBER", "잉걸불", 0, new[] { 2, 4 }, false),
				new ComboCategoryInfo(1, "GLACIER", "빙하", 0, new[] { 2, 4 }, false),
				new ComboCategoryInfo(2, "MAGITECH", "마법공학", 0, new[] { 2, 4 }, false),
				new ComboCategoryInfo(3, "STURDY", "견고", 1, new[] { 2, 4 }, false),
				new ComboCategoryInfo(4, "FROST", "얼음무구", 2, new[] { 3, 6 }, true)
			};

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
				new[] { chalcedonyKey, whitePaper, frostA, frostB },
				comboInfos,
				TalentRuleSnapshot.Default,
				-1,
				3
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int keySlot = result.BestItemToSlotIndex[0];
			int paperSlot = result.BestItemToSlotIndex[1];
			int frostASlot = result.BestItemToSlotIndex[2];
			int frostBSlot = result.BestItemToSlotIndex[3];

			int keyRow = keySlot / width;
			int paperRow = paperSlot / width;
			int paperCol = paperSlot % width;

			Assert.Equal(3, keyRow);
			Assert.Equal(1, paperCol);
			Assert.Equal(paperRow, frostASlot / width);
			Assert.Equal(paperRow, frostBSlot / width);
			Assert.Equal(frostMask, result.BestSlotDetails[paperSlot].DynamicCategoryMask);
		}

		[Fact(DisplayName = "빈 슬롯이나 최하 등급 아티팩트 슬롯에 양수 버프와 음수 디버프가 중첩되어 최종 레벨이 0 이하일 때도 양수 버프 낭비 페널티가 부과된다")]
		public void Evaluate_PenalizesWastedPositiveBuffEvenWhenCancelledByNegativeDebuffOnEmptyOrSacrificeSlot() {
			int width = 3;
			int height = 1;
			int unlockedSlots = 3;

			TabletPlacementRule[] rightPlus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			TabletPlacementRule[] leftMinus3Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.IncreaseConstLevel,
				-3
			);

			SlotItemSnapshot tabletPlus2 = CreateTablet(0, 801, 0, 0, false, rightPlus2Rules, 1);
			SlotItemSnapshot tabletMinus3 = CreateTablet(1, 802, 2, 0, false, leftMinus3Rules, 2);
			SlotItemSnapshot sacrificeArtifact = new SlotItemSnapshot(
				2,
				803,
				803,
				"최하 콤보 토템",
				SlotItemKind.Artifact,
				1,
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
				3,
				ArtifactPriorityTier.Sacrifice
			);

			InventorySnapshot emptyOverlapSnapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { tabletPlus2, tabletMinus3 }
			);
			int[] emptyOverlapLayout = new[] { 0, -1, 1 };
			int[] twoRots = new[] { 0, 0 };
			int emptyOverlapScore = ObjectiveEvaluator.Evaluate(
				emptyOverlapSnapshot,
				emptyOverlapLayout,
				twoRots
			);

			InventorySnapshot sacrificeOverlapSnapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { tabletPlus2, tabletMinus3, sacrificeArtifact }
			);
			int[] sacrificeOverlapLayout = new[] { 0, 2, 1 };
			int[] threeRots = new[] { 0, 0, 0 };
			int sacrificeOverlapScore = ObjectiveEvaluator.Evaluate(
				sacrificeOverlapSnapshot,
				sacrificeOverlapLayout,
				threeRots
			);

			Assert.Equal(-160, emptyOverlapScore);
			Assert.True(sacrificeOverlapScore < 0);
		}

		[Fact(DisplayName = "배율 석판이 음수 디버프 슬롯이나 0레벨 슬롯에 적용될 때 양수 버프 버퍼를 오염시키지 않고 배율 낭비 페널티를 정확히 계산한다")]
		public void Evaluate_HandlesMultiplyTabletOnNegativeAndZeroSlotsWithoutPositiveBufferContamination() {
			int width = 3;
			int height = 1;
			int unlockedSlots = 3;

			TabletPlacementRule[] rightMinus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				-2
			);
			TabletPlacementRule[] leftMul2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.MultiplyConstLevel,
				2
			);

			SlotItemSnapshot debuffTablet = CreateTablet(0, 811, 0, 0, false, rightMinus2Rules, 1);
			SlotItemSnapshot mulTablet = CreateTablet(1, 812, 2, 0, false, leftMul2Rules, 2);

			InventorySnapshot wastedMulSnapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { mulTablet }
			);
			int wastedMulScore = ObjectiveEvaluator.Evaluate(
				wastedMulSnapshot,
				new[] { -1, -1, 0 },
				new[] { 0 }
			);

			InventorySnapshot negativeMulSnapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { debuffTablet, mulTablet }
			);
			int negativeMulScore = ObjectiveEvaluator.Evaluate(
				negativeMulSnapshot,
				new[] { 0, -1, 1 },
				new[] { 0, 0 }
			);

			Assert.Equal(-90, wastedMulScore);
			Assert.Equal(140, negativeMulScore);
		}

		[Fact(DisplayName = "비활성화된 동적 태그 아티팩트의 시너지를 차단하고 북향의 금빛침 상호 순환 참조를 비트마스크로 즉시 종료한다")]
		public void Evaluate_BlocksDisabledDynamicTagArtifactsAndDetectsGoldenNeedleMutualCycleInConstantTime() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			uint emberMask = 1u << 0;

			SlotItemSnapshot needleTop = new SlotItemSnapshot(
				0,
				821,
				821,
				"상단 금빛침",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				0,
				2,
				2,
				4,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				true,
				true,
				0,
				1,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Normal,
				0u,
				true,
				1,
				new[] { 30, 50, 80 },
				new[] { 30, 50, 80 }
			);

			SlotItemSnapshot needleBottom = new SlotItemSnapshot(
				1,
				822,
				822,
				"하단 금빛침",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				0,
				2,
				2,
				4,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				true,
				true,
				0,
				-1,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				2,
				ArtifactPriorityTier.Normal,
				0u,
				true,
				1,
				new[] { 30, 50, 80 },
				new[] { 30, 50, 80 }
			);

			SlotItemSnapshot disabledWhitePaper = new SlotItemSnapshot(
				2,
				823,
				823,
				"비활성 하얀 종이",
				SlotItemKind.Artifact,
				1,
				0,
				false,
				-1,
				0,
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
				3,
				ArtifactPriorityTier.Normal,
				0u,
				false,
				1,
				null,
				null,
				null,
				null,
				2
			);

			SlotItemSnapshot emberRight = new SlotItemSnapshot(
				3,
				824,
				824,
				"잉걸불 반지",
				SlotItemKind.Artifact,
				2,
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
				false,
				new[] { "EMBER" },
				Array.Empty<TabletPlacementRule>(),
				4,
				ArtifactPriorityTier.Normal,
				emberMask
			);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { needleTop, needleBottom, disabledWhitePaper, emberRight }
			);

			int[] layout = new[] { 0, 2, 3, 1, -1, -1 };
			int[] rots = new[] { 0, 0, 0, 0 };
			SlotEvaluationDetail[] details = ObjectiveEvaluator.EvaluateDetailed(snapshot, layout, rots);

			Assert.Equal(0u, details[0].DynamicCategoryMask);
			Assert.Equal(0u, details[3].DynamicCategoryMask);
			Assert.False(details[1].IsEnabled);
			Assert.Equal(0u, details[1].DynamicCategoryMask);
		}

		[Fact(DisplayName = "비직사각형 해금 슬롯에서 석판의 Placed 위치 조건이 인게임 유효 높이와 정확히 일치한다")]
		public void BuildTabletPlacementRules_MatchesInGameEffectiveHeightForNonRectangularUnlockedSlots() {
			int width = 6;
			int height = 2;
			int unlockedSlots = 15;

			TabletPlacementRule[] bottomRules = GameInventoryReader.BuildTabletPlacementRules(
				"Placed(BOTTOM)",
				"0, -1: 2",
				width,
				height,
				unlockedSlots
			);

			TabletPlacementRule[] innerRules = GameInventoryReader.BuildTabletPlacementRules(
				"Placed(INNER)",
				"1, 0: 1",
				width,
				height,
				unlockedSlots
			);

			Assert.Empty(bottomRules[6 * 4].Effects);
			Assert.Single(bottomRules[12 * 4].Effects);
			Assert.Equal(6, bottomRules[12 * 4].Effects[0].TargetSlotIndex);

			Assert.Empty(innerRules[6 * 4].Effects);
			Assert.Single(innerRules[7 * 4].Effects);
			Assert.Empty(innerRules[8 * 4].Effects);
			Assert.Empty(innerRules[13 * 4].Effects);
		}

		[Fact(DisplayName = "동일한 SymmetryGroupId를 가진 아이템이나 빈 슬롯 간의 불필요한 스왑을 Identity Matching으로 제거한다")]
		public void BuildExecutionPlan_EliminatesRedundantSwapsBetweenIdenticalSymmetryGroupItems() {
			int width = 4;
			int height = 1;
			int unlockedSlots = 4;

			SlotItemSnapshot artA1 = CreateArtifact(0, 901, 0, 0, 2, 3, ArtifactConstraintType.None, 10);
			SlotItemSnapshot artA2 = CreateArtifact(1, 902, 1, 0, 2, 3, ArtifactConstraintType.None, 10);

			TabletPlacementRule[] dummyRules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);
			SlotItemSnapshot tabB1 = CreateTablet(2, 903, 2, 1, true, dummyRules, 20);
			SlotItemSnapshot tabB2 = CreateTablet(3, 904, 3, 1, true, dummyRules, 20);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { artA1, artA2, tabB1, tabB2 }
			);

			int[] currentSlots = new[] { 0, 1, 2, 3 };
			int[] targetSlots = new[] { 1, 0, 3, 2 };
			int[] currentRots = new[] { 0, 0, 1, 1 };
			int[] targetRots = new[] { 0, 0, 1, 1 };

			ExecutionPlan plan = CycleDecomposition.BuildExecutionPlan(
				snapshot,
				currentSlots,
				targetSlots,
				currentRots,
				targetRots
			);

			Assert.Empty(plan.SwapSteps);
			Assert.Empty(plan.RotateSteps);
			Assert.Equal(0, plan.TotalOperations);
		}

		private static InventorySnapshot CreateSimpleSnapshot(
			int width,
			int height,
			int unlockedSlots,
			SlotItemSnapshot[] items
		) {
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
				items
			);
		}

		private static SlotItemSnapshot CreateArtifact(
			int itemIndex,
			int instanceId,
			int initialSlot,
			int baseLevel,
			int maxLevel,
			int weight,
			ArtifactConstraintType constraintType,
			int symmetryGroupId
		) {
			return new SlotItemSnapshot(
				itemIndex,
				instanceId,
				instanceId,
				$"Artifact_{instanceId}",
				SlotItemKind.Artifact,
				initialSlot,
				0,
				false,
				baseLevel,
				maxLevel,
				1,
				weight,
				constraintType,
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
				symmetryGroupId
			);
		}

		private static SlotItemSnapshot CreateTablet(
			int itemIndex,
			int instanceId,
			int initialSlot,
			int initialRotation,
			bool isRotatable,
			TabletPlacementRule[] rules,
			int symmetryGroupId
		) {
			return new SlotItemSnapshot(
				itemIndex,
				instanceId,
				instanceId,
				$"Tablet_{instanceId}",
				SlotItemKind.StoneTablet,
				initialSlot,
				initialRotation,
				isRotatable,
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
				rules,
				symmetryGroupId
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
			for (int slotIdx = 0; slotIdx < unlockedSlots; slotIdx++) {
				int x = slotIdx % width;
				int y = slotIdx / width;
				for (int rot = 0; rot < 4; rot++) {
					(int dx, int dy) = RotateOffset(baseDx, baseDy, rot);
					int targetSlot = CoordToSlot(x + dx, y + dy, width, height, unlockedSlots);
					PrecomputedEffect[] effects = new[] {
						new PrecomputedEffect(
							(sbyte)(x + dx),
							(sbyte)(y + dy),
							targetSlot,
							effectType,
							levelParam
						)
					};
					rules[(slotIdx * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						effects
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
