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

			Assert.Equal(-2 * ObjectiveEvaluator.WastedPositiveOnEmptySlotPenalty, emptyOverlapScore);
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

			Assert.Equal(-ObjectiveEvaluator.WastedMultiplierPenalty, wastedMulScore);
			Assert.Equal(4 * ObjectiveEvaluator.NeutralizedNegativeLevelBonus, negativeMulScore);
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

		[Fact(DisplayName = "동적 태그 확정 후 활성화된 신비 슬롯의 석판 및 각인 레벨을 2배로 증폭하고 최대 레벨 초과분 증발과 버프 낭비 2배 페널티를 반영한다")]
		public void Evaluate_AmplifiesMysticSlotsAfterDynamicTagsAndClampsOverflowAndDoublesWastedPenalty() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;
			uint mysticMask = 1u << 0;

			SlotItemSnapshot mysticCharmA = new SlotItemSnapshot(
				0,
				1001,
				1001,
				"신비 성물 A",
				SlotItemKind.Artifact,
				0,
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
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Normal,
				mysticMask
			);

			SlotItemSnapshot whitePaper = new SlotItemSnapshot(
				1,
				1002,
				1002,
				"하얀 종이",
				SlotItemKind.Artifact,
				1,
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
				1
			);

			SlotItemSnapshot highMaxCoreArtifact = new SlotItemSnapshot(
				2,
				1003,
				1003,
				"고레벨 주력 아티팩트",
				SlotItemKind.Artifact,
				4,
				0,
				false,
				0,
				8,
				3,
				10,
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
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				3,
				ArtifactPriorityTier.Core
			);

			TabletPlacementRule[] rightPlus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot plus2Tablet = CreateTablet(3, 1004, 3, 0, false, rightPlus2Rules, 4);

			SlotItemSnapshot lowMaxCoreArtifact = new SlotItemSnapshot(
				4,
				1005,
				1005,
				"저상한 주력 아티팩트",
				SlotItemKind.Artifact,
				2,
				0,
				false,
				0,
				1,
				3,
				1,
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
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				5,
				ArtifactPriorityTier.Core
			);

			ComboCategoryInfo[] comboInfos = new[] {
				new ComboCategoryInfo(0, "MYSTIC", "신비", 1, new[] { 2, 4 }, true)
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
				new[] { mysticCharmA, whitePaper, highMaxCoreArtifact, plus2Tablet, lowMaxCoreArtifact },
				comboInfos,
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				4,
				5
			);

			int[] activeMysticHighMaxLayout = new[] { 0, 1, 4, 3, 2, -1 };
			int[] inactiveMysticHighMaxLayout = new[] { 0, 4, 1, 3, 2, -1 };
			int[] activeMysticLowMaxOverflowLayout = new[] { 0, 1, 2, 3, 4, -1 };
			int[] rotations = new[] { 0, 0, 0, 0, 0 };

			SlotEvaluationDetail[] activeDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				activeMysticHighMaxLayout,
				rotations
			);
			SlotEvaluationDetail[] inactiveDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				inactiveMysticHighMaxLayout,
				rotations
			);
			SlotEvaluationDetail[] overflowDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				activeMysticLowMaxOverflowLayout,
				rotations
			);

			int activeHighMaxScore = ObjectiveEvaluator.Evaluate(snapshot, activeMysticHighMaxLayout, rotations);
			int overflowLowMaxScore = ObjectiveEvaluator.Evaluate(
				snapshot,
				activeMysticLowMaxOverflowLayout,
				rotations
			);

			int[] tier2EngravedBaseLevels = new int[unlockedSlots];
			tier2EngravedBaseLevels[5] = 3;
			InventorySnapshot tier2MysticSnapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				tier2EngravedBaseLevels,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { highMaxCoreArtifact },
				new[] { new ComboCategoryInfo(0, "MYSTIC", "신비", 4, new[] { 2, 4 }, false) },
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				4,
				5
			);
			SlotEvaluationDetail[] tier2Details = ObjectiveEvaluator.EvaluateDetailed(
				tier2MysticSnapshot,
				new[] { -1, -1, -1, -1, -1, 0 },
				new[] { 0 }
			);

			InventorySnapshot emptyMysticSnapshot = new InventorySnapshot(
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
				new[] { plus2Tablet },
				new[] { new ComboCategoryInfo(0, "MYSTIC", "신비", 2, new[] { 2, 4 }, false) },
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				4,
				5
			);
			int emptyMysticWastedScore = ObjectiveEvaluator.Evaluate(
				emptyMysticSnapshot,
				new[] { -1, -1, -1, 0, -1, -1 },
				new[] { 0 }
			);

			Assert.Equal(4, activeDetails[4].DisplayedLevel);
			Assert.Equal(4, activeDetails[4].EffectiveLevel);
			Assert.Equal(2, inactiveDetails[4].DisplayedLevel);
			Assert.Equal(2, inactiveDetails[4].EffectiveLevel);
			Assert.Equal(4, overflowDetails[4].DisplayedLevel);
			Assert.Equal(1, overflowDetails[4].EffectiveLevel);
			Assert.Equal(6, tier2Details[5].DisplayedLevel);
			Assert.Equal(6, tier2Details[5].EffectiveLevel);
			Assert.True(activeHighMaxScore > overflowLowMaxScore);
			Assert.Equal(
				ObjectiveEvaluator.ComboBreakpointReachedBonus
					- (4 * ObjectiveEvaluator.WastedPositiveOnEmptySlotPenalty),
				emptyMysticWastedScore
			);
		}

		[Fact(DisplayName = "대규모 그리드에서 북향의 금빛침을 신비 슬롯 대신 주력 공격형 아티팩트 바로 아래 칸에 스냅 및 수직 쌍 동반 이동으로 배치한다")]
		public void Solve_SnapsAndCouplesGoldenNeedleDirectlyBelowCoreAttackableArtifactInsteadOfMysticSlot() {
			int width = 6;
			int height = 4;
			int unlockedSlots = 24;
			uint mysticMask = 1u << 0;

			int[] baseSlotLevels = new int[unlockedSlots];
			baseSlotLevels[7] = 2;

			SlotItemSnapshot coreAttackCharm = new SlotItemSnapshot(
				0,
				1101,
				1101,
				"주력 공격 아티팩트",
				SlotItemKind.Artifact,
				23,
				0,
				false,
				0,
				4,
				3,
				14,
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
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Core,
				mysticMask
			);

			SlotItemSnapshot weakAttackCharm = new SlotItemSnapshot(
				1,
				1102,
				1102,
				"보조 소형 단검",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				0,
				1,
				0,
				3,
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
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				2,
				ArtifactPriorityTier.Low,
				mysticMask
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				2,
				1103,
				1103,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				6,
				0,
				false,
				0,
				3,
				2,
				9,
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
				3,
				ArtifactPriorityTier.Normal,
				0u,
				false,
				1,
				new[] { 15, 25, 35, 50 },
				new[] { 15, 25, 35, 50 }
			);

			TabletPlacementRule[] rightPlus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot tablet = CreateTablet(3, 1104, 18, 0, true, rightPlus2Rules, 4);

			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				baseSlotLevels,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { coreAttackCharm, weakAttackCharm, goldenNeedle, tablet },
				new[] { new ComboCategoryInfo(0, "MYSTIC", "신비", 2, new[] { 2, 4 }, true) },
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				7,
				-1
			);

			int[] unlinkedTopRowSlots = new int[unlockedSlots];
			Array.Fill(unlinkedTopRowSlots, -1);
			unlinkedTopRowSlots[0] = 2;
			unlinkedTopRowSlots[7] = 0;
			unlinkedTopRowSlots[14] = 1;
			int unlinkedScore = ObjectiveEvaluator.Evaluate(snapshot, unlinkedTopRowSlots, new int[4]);

			int[] linkedBelowCoreSlots = new int[unlockedSlots];
			Array.Fill(linkedBelowCoreSlots, -1);
			linkedBelowCoreSlots[7] = 0;
			linkedBelowCoreSlots[13] = 2;
			linkedBelowCoreSlots[14] = 1;
			int linkedScore = ObjectiveEvaluator.Evaluate(snapshot, linkedBelowCoreSlots, new int[4]);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot, 220);

			int coreSlot = result.BestItemToSlotIndex[0];
			int needleSlot = result.BestItemToSlotIndex[2];

			Assert.True(linkedScore - unlinkedScore >= ObjectiveEvaluator.UnlinkedGoldenNeedlePenalty);
			Assert.Equal(7, coreSlot);
			Assert.Equal(coreSlot + width, needleSlot);
			Assert.Equal(4, result.BestSlotDetails[coreSlot].EffectiveLevel);
		}

		[Fact(DisplayName = "조화의 수정이 사전 계산된 8방향 비트마스크를 통해 중앙 8칸 슬롯에서 주변 활성 아티팩트 유효 레벨 총합을 극대화한다")]
		public void Solve_MaximizesCrystalOfHarmonyInInteriorMooreNeighborhoodUsingBitmasks() {
			int width = 4;
			int height = 4;
			int unlockedSlots = 16;

			ulong[] masks = GameInventoryReader.BuildMooreNeighborMasks(width, height, unlockedSlots);

			SlotItemSnapshot[] items = new SlotItemSnapshot[10];
			items[0] = new SlotItemSnapshot(
				0,
				1200,
				1200,
				"조화의 수정",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				0,
				1,
				2,
				9,
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
				1,
				ArtifactPriorityTier.High,
				0u,
				false,
				1,
				null,
				null,
				null,
				null,
				2,
				true,
				false,
				false,
				false,
				false,
				false,
				false,
				false,
				true,
				new[] { 1f, 2f }
			);

			for (int i = 1; i <= 8; i++) {
				items[i] = CreateArtifact(
					i,
					1200 + i,
					i,
					2,
					3,
					4,
					ArtifactConstraintType.None,
					2
				);
			}

			TabletPlacementRule[] rightPlus1Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);
			items[9] = CreateTablet(9, 1299, 15, 0, true, rightPlus1Rules, 3);

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
				items,
				null,
				null,
				-1,
				-1,
				masks
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot, 220);

			int crystalSlot = result.BestItemToSlotIndex[0];
			int crystalX = crystalSlot % width;
			int crystalY = crystalSlot / width;

			int cornerNeighborCount = CountBits(masks[0]);
			int edgeNeighborCount = CountBits(masks[1]);
			int crystalNeighborCount = CountBits(masks[crystalSlot]);

			Assert.Equal(3, cornerNeighborCount);
			Assert.Equal(5, edgeNeighborCount);
			Assert.Equal(8, crystalNeighborCount);
			Assert.True(crystalX >= 1 && crystalX <= 2);
			Assert.True(crystalY >= 1 && crystalY <= 2);
			Assert.True(result.BestScore > result.InitialScore);
		}

		[Fact(DisplayName = "신비 슬롯 증폭과 금빛침 연쇄 및 조화의 수정 평가 루프가 단일 평가당 힙 메모리 할당 0바이트를 보장한다")]
		public void Evaluate_GuaranteesZeroHeapAllocationAcrossThousandEvaluations() {
			int width = 4;
			int height = 3;
			int unlockedSlots = 12;
			uint mysticMask = 1u << 0;

			SlotItemSnapshot attackCore = new SlotItemSnapshot(
				0,
				1301,
				1301,
				"주력 공격 아티팩트",
				SlotItemKind.Artifact,
				1,
				0,
				false,
				1,
				6,
				3,
				12,
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
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Core,
				mysticMask
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				1,
				1302,
				1302,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				5,
				0,
				false,
				0,
				3,
				2,
				6,
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
				false,
				1,
				new[] { 15, 25, 35, 50 },
				new[] { 15, 25, 35, 50 },
				null,
				null,
				0
			);

			SlotItemSnapshot crystal = new SlotItemSnapshot(
				2,
				1303,
				1303,
				"조화의 수정",
				SlotItemKind.Artifact,
				6,
				0,
				false,
				1,
				2,
				3,
				10,
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
				3,
				ArtifactPriorityTier.Core,
				mysticMask,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1f, 2f, 2f }
			);

			TabletPlacementRule[] rightPlus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot tablet = CreateTablet(3, 1304, 0, 0, false, rightPlus2Rules, 4);

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
				new[] { attackCore, goldenNeedle, crystal, tablet },
				new[] { new ComboCategoryInfo(0, "MYSTIC", "신비", 2, new[] { 2, 4 }, true) },
				TalentRuleSnapshot.Default,
				-1,
				-1,
				InventorySnapshot.ComputeMooreNeighborMasks(width, height, unlockedSlots),
				1,
				6
			);

			int[] layout = new[] { 3, 0, -1, -1, -1, 1, 2, -1, -1, -1, -1, -1 };
			int[] rotations = new[] { 0, 0, 0, 0 };

			int warmupScore = ObjectiveEvaluator.Evaluate(snapshot, layout, rotations);
			long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
			int checksum = 0;
			for (int iter = 0; iter < 1000; iter++) {
				checksum ^= ObjectiveEvaluator.Evaluate(snapshot, layout, rotations);
			}
			long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

			Assert.True(warmupScore > 0);
			Assert.Equal(0, checksum);
			Assert.Equal(0L, allocatedAfter - allocatedBefore);
		}

		[Fact(DisplayName = "4계층 절대 우선순위가 석판 버프 중첩과 일반 아티팩트 비활성화 역전을 차단하고 조화의 수정 인접 석판 진입을 허용한다")]
		public void EvaluateAndSolve_EnforcesFourTierHierarchyWithoutInversionAndAllowsCrystalTabletEntry() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			SlotItemSnapshot coreAttack = new SlotItemSnapshot(
				0,
				1401,
				1401,
				"최상 공격 아티팩트",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				1,
				6,
				4,
				20,
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
				1u,
				isCrystalOfHarmony: false
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				1,
				1402,
				1402,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				0,
				3,
				2,
				8,
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
				ArtifactPriorityTier.Normal
			);

			SlotItemSnapshot coreCrystal = new SlotItemSnapshot(
				2,
				1403,
				1403,
				"강화 조화의 수정",
				SlotItemKind.Artifact,
				4,
				0,
				false,
				1,
				2,
				4,
				20,
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
				ArtifactPriorityTier.Core,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1f, 2f, 2f }
			);

			SlotItemSnapshot lowArtifact = new SlotItemSnapshot(
				3,
				1404,
				1404,
				"낮음 등급 아티팩트",
				SlotItemKind.Artifact,
				5,
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
				4,
				ArtifactPriorityTier.Low
			);

			TabletPlacementRule[] leftPlus1Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);
			TabletPlacementRule[] leftPlus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);

			SlotItemSnapshot tabletA = CreateTablet(4, 1405, 1, 0, false, leftPlus1Rules, 5);
			SlotItemSnapshot tabletB = CreateTablet(5, 1406, 2, 0, false, leftPlus2Rules, 6);

			InventorySnapshot overlapCheckSnapshot = new InventorySnapshot(
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
				new[] { coreAttack, goldenNeedle, coreCrystal, lowArtifact, tabletA, tabletB },
				new[] { new ComboCategoryInfo(0, "EMBER", "잉걸불", 1, new[] { 2 }, true) },
				TalentRuleSnapshot.Default,
				1401
			);

			int[] overlapLayout = new[] { 0, 5, 4, 1, 2, 3 };
			int[] nonOverlapLayout = new[] { 0, 5, 3, 1, 2, 4 };
			int[] sixRots = new[] { 0, 0, 0, 0, 0, 0 };
			int overlapScore = ObjectiveEvaluator.Evaluate(overlapCheckSnapshot, overlapLayout, sixRots);
			int nonOverlapScore = ObjectiveEvaluator.Evaluate(overlapCheckSnapshot, nonOverlapLayout, sixRots);

			int[] blockedBottomRowLayout = new[] { 3, -1, -1, 0, 1, 2 };
			int[] unblockedTopRowLayout = new[] { 0, -1, -1, 3, 1, 2 };
			int blockedBottomScore = ObjectiveEvaluator.Evaluate(
				overlapCheckSnapshot,
				blockedBottomRowLayout,
				sixRots
			);
			int unblockedTopScore = ObjectiveEvaluator.Evaluate(
				overlapCheckSnapshot,
				unblockedTopRowLayout,
				sixRots
			);

			int[] baseDisable = new[] { 0, 0, 1, 0, 0, 0 };
			InventorySnapshot deactivationCheckSnapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				baseDisable,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { coreAttack, goldenNeedle, lowArtifact },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default,
				1401
			);
			int[] sacrificedLowForNeedleLayout = new[] { 0, -1, 2, 1, -1, -1 };
			int[] safeLowWithLinkedNeedleLayout = new[] { 0, 2, -1, 1, -1, -1 };
			int[] safeLowEvenWithoutNeedleLayout = new[] { 0, 2, -1, -1, 1, -1 };
			int[] threeRots = new[] { 0, 0, 0 };
			int deactivatedLowScore = ObjectiveEvaluator.Evaluate(
				deactivationCheckSnapshot,
				sacrificedLowForNeedleLayout,
				threeRots
			);
			int safeLowUnlinkedScore = ObjectiveEvaluator.Evaluate(
				deactivationCheckSnapshot,
				safeLowEvenWithoutNeedleLayout,
				threeRots
			);
			int safeLowLinkedScore = ObjectiveEvaluator.Evaluate(
				deactivationCheckSnapshot,
				safeLowWithLinkedNeedleLayout,
				threeRots
			);

			int cWidth = 4;
			int cHeight = 3;
			int cSlots = 12;
			TabletPlacementRule[] dualNeighborBuffRules = new TabletPlacementRule[cSlots * 4];
			for (int s = 0; s < cSlots; s++) {
				int sx = s % cWidth;
				int sy = s / cWidth;
				for (int r = 0; r < 4; r++) {
					int leftTarget = CoordToSlot(sx - 1, sy, cWidth, cHeight, cSlots);
					int downTarget = CoordToSlot(sx, sy + 1, cWidth, cHeight, cSlots);
					PrecomputedEffect[] effs = new[] {
						new PrecomputedEffect(
							(sbyte)(sx - 1),
							(sbyte)sy,
							leftTarget,
							TabletEffectType.IncreaseConstLevel,
							2
						),
						new PrecomputedEffect(
							(sbyte)sx,
							(sbyte)(sy + 1),
							downTarget,
							TabletEffectType.IncreaseConstLevel,
							2
						)
					};
					dualNeighborBuffRules[(s * 4) + r] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						effs
					);
				}
			}

			SlotItemSnapshot[] crystalGridItems = new SlotItemSnapshot[10];
			crystalGridItems[0] = new SlotItemSnapshot(
				0,
				1500,
				1500,
				"조화의 수정",
				SlotItemKind.Artifact,
				5,
				0,
				false,
				1,
				2,
				3,
				12,
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
				1,
				ArtifactPriorityTier.Core,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1f, 2f, 2f }
			);
			for (int i = 1; i <= 8; i++) {
				crystalGridItems[i] = CreateArtifact(
					i,
					1500 + i,
					i - 1,
					1,
					4,
					5,
					ArtifactConstraintType.None,
					2
				);
			}
			crystalGridItems[9] = CreateTablet(9, 1599, 11, 0, false, dualNeighborBuffRules, 3);

			InventorySnapshot crystalTabletSnapshot = CreateSimpleSnapshot(
				cWidth,
				cHeight,
				cSlots,
				crystalGridItems
			);
			int[] eightNeighborsNoTabletInside = new[] { 1, 2, 3, -1, 4, 0, 5, -1, 6, 7, 8, -1 };
			int[] sevenNeighborsPlusTabletAtSlot2 = new[] { 1, 2, 9, 3, 4, 0, 5, -1, 6, 7, 8, -1 };
			int[] tenRots = new int[10];
			int excludedTabletScore = ObjectiveEvaluator.Evaluate(
				crystalTabletSnapshot,
				eightNeighborsNoTabletInside,
				tenRots
			);
			int enteredTabletScore = ObjectiveEvaluator.Evaluate(
				crystalTabletSnapshot,
				sevenNeighborsPlusTabletAtSlot2,
				tenRots
			);

			OptimizationSolver solver = new OptimizationSolver();
			int lWidth = 6;
			int lHeight = 4;
			int lSlots = 24;
			int[] largeDisable = new int[lSlots];
			largeDisable[23] = 1;

			TabletPlacementRule[] largeBuffRules = CreateDirectionalTabletRules(
				lWidth,
				lHeight,
				lSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot[] largeItems = new SlotItemSnapshot[13];
			largeItems[0] = new SlotItemSnapshot(
				0,
				1600,
				1600,
				"최상 공격 아티팩트",
				SlotItemKind.Artifact,
				18,
				0,
				false,
				1,
				6,
				4,
				20,
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
			largeItems[1] = new SlotItemSnapshot(
				1,
				1601,
				1601,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				0,
				3,
				2,
				8,
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
				ArtifactPriorityTier.Normal
			);
			largeItems[2] = new SlotItemSnapshot(
				2,
				1602,
				1602,
				"낮음 아티팩트",
				SlotItemKind.Artifact,
				23,
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
				3,
				ArtifactPriorityTier.Low
			);
			for (int i = 3; i < 10; i++) {
				largeItems[i] = CreateArtifact(
					i,
					1600 + i,
					i,
					1,
					4,
					5,
					ArtifactConstraintType.None,
					4
				);
			}
			for (int i = 10; i < 13; i++) {
				largeItems[i] = CreateTablet(
					i,
					1600 + i,
					i,
					0,
					true,
					largeBuffRules,
					5
				);
			}

			InventorySnapshot largeSnapshot = new InventorySnapshot(
				lWidth,
				lHeight,
				lSlots,
				new int[lSlots],
				largeDisable,
				new int[lSlots],
				new int[lSlots],
				new bool[lSlots],
				true,
				1,
				largeItems,
				new[] { new ComboCategoryInfo(0, "EMBER", "잉걸불", 1, new[] { 2 }, true) },
				TalentRuleSnapshot.Default,
				1600
			);
			OptimizationResult largeResult = solver.Solve(largeSnapshot, 220);

			Assert.True(nonOverlapScore > overlapScore);
			Assert.True(unblockedTopScore - blockedBottomScore >= 2400);
			Assert.True(safeLowUnlinkedScore > deactivatedLowScore);
			Assert.True(safeLowLinkedScore > safeLowUnlinkedScore);
			Assert.True(enteredTabletScore > excludedTabletScore);
			Assert.False(largeResult.UsedExactBranchAndBound);
			Assert.Equal(largeResult.BestItemToSlotIndex[0] + lWidth, largeResult.BestItemToSlotIndex[1]);
			for (int i = 0; i < 10; i++) {
				int placedSlot = largeResult.BestItemToSlotIndex[i];
				Assert.True(largeResult.BestSlotDetails[placedSlot].IsEnabled);
			}
			for (int i = 10; i < 13; i++) {
				int tabletSlot = largeResult.BestItemToSlotIndex[i];
				Assert.True(largeResult.BestSlotDetails[tabletSlot].DisplayedLevel <= 0);
			}
		}

		[Fact(DisplayName = "허수아비 예상 DPS 계산 공식이 금빛침 증폭과 신비 2배 증폭 및 조화의 수정 최종 피해 배율을 힙 할당 없이 정확히 산출한다")]
		public void CalculateEstimatedDummyDps_ComputesExactDummyDpsFormulaWithZeroHeapAllocation() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;
			uint mysticMask = 1u << 0;

			SlotItemSnapshot attackArtifact = new SlotItemSnapshot(
				0,
				1701,
				1701,
				"화염 투창",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				1,
				4,
				2,
				10,
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
				new[] { "MYSTIC" },
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Core,
				mysticMask,
				attackDamageByLevel: new[] { 100f, 150f, 220f, 300f, 400f },
				baseProcsPerSecond: 2f
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				1,
				1702,
				1702,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				1,
				3,
				2,
				8,
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
				needleDamageBonusByLevel: new[] { 10, 20, 30, 50 }
			);

			SlotItemSnapshot crystalOfHarmony = new SlotItemSnapshot(
				2,
				1703,
				1703,
				"조화의 수정",
				SlotItemKind.Artifact,
				4,
				0,
				false,
				1,
				2,
				3,
				10,
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
				3,
				ArtifactPriorityTier.High,
				mysticMask,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1f, 2f, 2f }
			);

			TabletPlacementRule[] leftPlus1Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);
			SlotItemSnapshot tabletPlus1 = CreateTablet(3, 1704, 1, 0, false, leftPlus1Rules, 4);

			PlayerCombatBaseline baseline = new PlayerCombatBaseline(50f, 2.0f, 0f, 1.5f, 0f);
			ComboCategoryInfo[] comboCategories = new[] {
				new ComboCategoryInfo(0, "MYSTIC", "신비", 2, new[] { 2, 4 }, true, new[] { 10f, 25f })
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
				new[] { attackArtifact, goldenNeedle, crystalOfHarmony, tabletPlus1 },
				comboCategories,
				TalentRuleSnapshot.Default,
				1701,
				-1,
				null,
				0,
				-1,
				2,
				4,
				null,
				baseline
			);

			int[] unlinkedNoBuffLayout = new[] { 1, 0, -1, 3, -1, 2 };
			int[] optimalSynergyLayout = new[] { 0, 3, -1, 1, 2, -1 };
			int[] rotations = new[] { 0, 0, 0, 0 };

			int unlinkedDps = ObjectiveEvaluator.CalculateEstimatedDummyDps(
				snapshot,
				unlinkedNoBuffLayout,
				rotations
			);
			int optimalDps = ObjectiveEvaluator.CalculateEstimatedDummyDps(
				snapshot,
				optimalSynergyLayout,
				rotations
			);

			long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
			int accum = 0;
			for (int iter = 0; iter < 1000; iter++) {
				accum ^= ObjectiveEvaluator.CalculateEstimatedDummyDps(snapshot, optimalSynergyLayout, rotations);
			}
			long afterBytes = GC.GetAllocatedBytesForCurrentThread();

			Assert.True(optimalDps > unlinkedDps);
			Assert.Equal(0L, afterBytes - beforeBytes);
			Assert.NotEqual(-1, accum);
		}

		[Fact(DisplayName = "최적화 솔버가 유효 배치 중에서 예상 DPS가 가장 높은 해를 최우선 선택하고 상승량 지표를 반환한다")]
		public void Solve_MaximizesEstimatedDummyDpsAndPopulatesOptimizationResultDpsMetrics() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			SlotItemSnapshot highScalingAttack = new SlotItemSnapshot(
				0,
				1801,
				1801,
				"고계수 번개 창",
				SlotItemKind.Artifact,
				5,
				0,
				false,
				0,
				3,
				3,
				10,
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
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Core,
				attackDamageByLevel: new[] { 60f, 180f, 340f, 560f },
				baseProcsPerSecond: 2.0f
			);

			SlotItemSnapshot lowScalingUtility = new SlotItemSnapshot(
				1,
				1802,
				1802,
				"저계수 보조 부적",
				SlotItemKind.Artifact,
				1,
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
				false,
				0,
				0,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				2,
				ArtifactPriorityTier.Low,
				statBonusPercentByLevel: new[] { 2f, 4f, 6f, 8f }
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				2,
				1803,
				1803,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				1,
				3,
				2,
				8,
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
				3,
				ArtifactPriorityTier.Normal,
				needleDamageBonusByLevel: new[] { 15, 25, 40, 60 }
			);

			TabletPlacementRule[] rightPlus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot plus2Tablet = CreateTablet(3, 1804, 2, 0, true, rightPlus2Rules, 4);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { highScalingAttack, lowScalingUtility, goldenNeedle, plus2Tablet }
			);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot, 200);

			int attackSlot = result.BestItemToSlotIndex[0];
			int needleSlot = result.BestItemToSlotIndex[2];

			Assert.True(result.BestEstimatedDps > result.InitialEstimatedDps);
			Assert.Equal(result.BestEstimatedDps - result.InitialEstimatedDps, result.EstimatedDpsDelta);
			Assert.True(result.EstimatedDpsGainPercent > 0.0);
			Assert.Equal(2, result.BestSlotDetails[attackSlot].EffectiveLevel);
			Assert.Equal(attackSlot + width, needleSlot);
		}

		[Fact(DisplayName = "DPS 상세 분석기가 기본 딜과 금빛침 및 조화의 수정 증폭 기여도를 정확히 분리하고 미연결 경고를 캐싱한다")]
		public void CalculateBreakdown_DecomposesBaseNeedleAndHarmonyDpsAndCachesTooltipStrings() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			SlotItemSnapshot attackArtifact = new SlotItemSnapshot(
				0,
				1901,
				1901,
				"화염 마법봉",
				SlotItemKind.Artifact,
				5,
				0,
				false,
				2,
				3,
				2,
				9,
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
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Core,
				attackDamageByLevel: new[] { 100f, 150f, 200f, 300f },
				baseProcsPerSecond: 2.0f,
				statBonusPercentByLevel: new[] { 10f, 15f, 20f, 30f }
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				1,
				1902,
				1902,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				2,
				3,
				2,
				8,
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
				needleDamageBonusByLevel: new[] { 20, 35, 50, 75 }
			);

			SlotItemSnapshot crystalOfHarmony = new SlotItemSnapshot(
				2,
				1903,
				1903,
				"조화의 수정",
				SlotItemKind.Artifact,
				2,
				0,
				false,
				2,
				3,
				3,
				9,
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
				ArtifactPriorityTier.High,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1f, 1f, 2f, 2f }
			);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { attackArtifact, goldenNeedle, crystalOfHarmony }
			);

			DpsBreakdown unlinkedCurrent = snapshot.CurrentBreakdown;
			Assert.False(unlinkedCurrent.HasActiveGoldenNeedle);
			Assert.True(unlinkedCurrent.HasOwnedGoldenNeedle);
			Assert.True(unlinkedCurrent.HasUnlinkedGoldenNeedle);
			Assert.Equal(0.0, unlinkedCurrent.GoldenNeedleAddedDps);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot, 200);
			DpsBreakdown optimized = result.OptimizedBreakdown;

			Assert.True(optimized.HasActiveGoldenNeedle);
			Assert.False(optimized.HasUnlinkedGoldenNeedle);
			Assert.Equal(50.0, optimized.GoldenNeedleAmpPercent, 3);
			Assert.True(optimized.GoldenNeedleAddedDps > 0.0);
			Assert.True(optimized.HasActiveHarmonyCrystal);
			Assert.Equal(4, optimized.HarmonyNeighborLevelSum);
			Assert.Equal(8.0, optimized.HarmonyAmpPercent, 3);
			Assert.True(optimized.HarmonyAddedDps > 0.0);

			double additiveSum = optimized.BaseEffectiveDps
				+ optimized.GoldenNeedleAddedDps
				+ optimized.HarmonyAddedDps;
			Assert.Equal(optimized.TotalEstimatedDps, additiveSum, 4);
			Assert.Equal(result.BestEstimatedDps, (int)Math.Round(optimized.TotalEstimatedDps));

			double shareSum = optimized.BaseSharePercent
				+ optimized.GoldenNeedleSharePercent
				+ optimized.HarmonySharePercent;
			Assert.Equal(100.0, shareSum, 3);

			DpsTooltipCache tooltipCache = DpsEstimator.BuildTooltipCache(
				result.CurrentBreakdown,
				result.OptimizedBreakdown,
				result.UsedExactBranchAndBound,
				result.ElapsedMilliseconds
			);
			Assert.True(tooltipCache.IsPopulated);
			Assert.True(tooltipCache.HasOptimizedComparison);
			Assert.True(tooltipCache.HasGoldenNeedleWarning);
			Assert.Contains("미적용 (0% - 미연결)", tooltipCache.GoldenNeedleValueLine);
			Assert.Contains("증폭 +50.0%", tooltipCache.GoldenNeedleValueLine);
			Assert.Contains("모든 피해 +8% (인접 8칸 레벨 합: 4)", tooltipCache.HarmonyValueLine);
		}

		[Fact(DisplayName = "우측 고정 모달의 툴팁 좌표가 좌측에 기본 배치되고 화면 상하좌우 경계 안으로 클램핑된다")]
		public void ComputeClampedTooltipPosition_ClampsLeftwardTooltipInsideScreenBounds() {
			(float normalX, float normalY) = DpsEstimator.ComputeClampedTooltipPosition(
				1620f,
				120f,
				442f,
				294f,
				1920f,
				1080f,
				12f
			);
			Assert.Equal(1168f, normalX);
			Assert.Equal(120f, normalY);

			(float narrowLeftX, float bottomOverflowY) = DpsEstimator.ComputeClampedTooltipPosition(
				200f,
				950f,
				442f,
				294f,
				1280f,
				1000f,
				12f
			);
			Assert.Equal(12f, narrowLeftX);
			Assert.Equal(694f, bottomOverflowY);
		}

		[Fact(DisplayName = "4-버킷 DPS 파이프라인이 가상 가중치 왜곡 없이 인게임 합연산·치명타·조화의수정 내림 공식을 정확히 계산한다")]
		public void CalculateBreakdown_SeparatesFourBucketsWithoutSyntheticInflationAndFloorsHarmonyBonus() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			SlotItemSnapshot attackCharm = new SlotItemSnapshot(
				0,
				2101,
				2101,
				"화염 투창",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				1,
				3,
				2,
				8,
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
				1u,
				attackDamageByLevel: new[] { 40f, 80f, 120f, 160f },
				baseProcsPerSecond: 1.5f
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				1,
				2102,
				2102,
				"북향의 금빛침",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				1,
				3,
				2,
				8,
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
				needleDamageBonusByLevel: new[] { 10, 25, 40, 60 }
			);

			SlotItemSnapshot chalcedonyKey = new SlotItemSnapshot(
				2,
				2103,
				2103,
				"캘세더니 열쇠",
				SlotItemKind.Artifact,
				1,
				0,
				false,
				1,
				3,
				2,
				7,
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
				3,
				ArtifactPriorityTier.Normal,
				rowCategoryIds: new[] { 0, 1 },
				rowElementalBonusByLevel: new[] { 2, 4, 6, 8 }
			);

			SlotItemSnapshot crystalOfHarmony = new SlotItemSnapshot(
				3,
				2104,
				2104,
				"조화의 수정",
				SlotItemKind.Artifact,
				4,
				0,
				false,
				1,
				2,
				3,
				9,
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
				4,
				ArtifactPriorityTier.High,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1.0f, 1.5f, 2.0f }
			);

			PlayerCombatBaseline baseline = new PlayerCombatBaseline(40f, 2.5f, 20f, 2.0f, 10f);
			ComboCategoryInfo[] combos = new[] {
				new ComboCategoryInfo(0, "EMBER", "잉걸불", 1, new[] { 2, 4 }, true, new[] { 6f, 12f })
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
				new[] { attackCharm, goldenNeedle, chalcedonyKey, crystalOfHarmony },
				combos,
				TalentRuleSnapshot.Default,
				2101,
				0,
				null,
				-1,
				-1,
				2,
				4,
				null,
				baseline
			);

			DpsBreakdown breakdown = DpsEstimator.CalculateCurrentBreakdown(snapshot);
			int evalDps = ObjectiveEvaluator.CalculateEstimatedDummyDps(
				snapshot,
				snapshot.InitialSlotToItemIndex,
				snapshot.InitialItemRotations
			);

			double expectedWeaponRaw = 40.0 * 2.5;
			double expectedCharmRaw = 80.0 * 1.5;
			double expectedBucketAUnamp = expectedWeaponRaw + expectedCharmRaw;
			double expectedNeedleGainRaw = expectedCharmRaw * 0.25;
			double expectedBucketB = 1.0 + ((10.0 + 4.0 + 6.0) * 0.01);
			double expectedBucketC = 1.0 + (0.20 * (2.0 - 1.0));
			double expectedGeneralMult = expectedBucketB * expectedBucketC;
			double expectedHarmonyPct = Math.Floor(3 * 1.5);
			double expectedTotalDps = (expectedBucketAUnamp + expectedNeedleGainRaw)
				* expectedGeneralMult
				* (1.0 + (expectedHarmonyPct * 0.01));

			Assert.Equal(expectedBucketAUnamp, breakdown.WeaponAndCharmBaseDps, 4);
			Assert.Equal(expectedGeneralMult, breakdown.GeneralStatMultiplier, 4);
			Assert.Equal(25.0, breakdown.GoldenNeedleAmpPercent, 4);
			Assert.Equal(3, breakdown.HarmonyNeighborLevelSum);
			Assert.Equal(4.0, breakdown.HarmonyAmpPercent, 4);
			Assert.Equal(expectedTotalDps, breakdown.TotalEstimatedDps, 4);
			Assert.Equal((int)Math.Round(expectedTotalDps), evalDps);
		}

		[Fact(DisplayName = "배치 가이드 평가기가 빈 슬롯과 이동 완료된 슬롯 및 대칭 동일 슬롯에서 잔류 가이드를 즉시 차단한다")]
		public void EvaluateSlotGuides_BlocksEmptySlotsAndHidesCompletedOrMovedSlotsImmediately() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			SlotItemSnapshot artA1 = CreateArtifact(0, 2201, 0, 1, 3, 5, ArtifactConstraintType.None, 10);
			SlotItemSnapshot artA2 = CreateArtifact(1, 2202, 1, 1, 3, 5, ArtifactConstraintType.None, 10);
			TabletPlacementRule[] dummyRules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				1
			);
			SlotItemSnapshot rotTablet = CreateTablet(2, 2203, 2, 0, true, dummyRules, 20);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { artA1, artA2, rotTablet }
			);

			int[] targetSlotToItem = new[] { 1, 0, -1, 2, -1, -1 };
			int[] targetItemToSlot = new[] { 1, 0, 3 };
			int[] targetRotations = new[] { 0, 0, 1 };

			int[] liveSlotToItemInitial = new[] { 0, 1, 2, -1, -1, -1 };
			int[] liveRotationsInitial = new[] { 0, 0, 0, 0, 0, 0 };
			SlotGuideOverlayState[] states = new SlotGuideOverlayState[unlockedSlots];

			CycleDecomposition.EvaluateSlotGuides(
				snapshot,
				liveSlotToItemInitial,
				liveRotationsInitial,
				targetSlotToItem,
				targetItemToSlot,
				targetRotations,
				states
			);

			Assert.True(states[0].HasCurrentItem);
			Assert.True(states[0].IsCompleted);
			Assert.False(states[0].ShowMoveGuide);

			Assert.True(states[1].HasCurrentItem);
			Assert.True(states[1].IsCompleted);
			Assert.False(states[1].ShowMoveGuide);

			Assert.True(states[2].HasCurrentItem);
			Assert.True(states[2].ShowMoveGuide);
			Assert.Equal(3, states[2].TargetDestinationSlot);
			Assert.False(states[2].ShowExpectedStatusBadge);

			for (int emptySlot = 3; emptySlot < unlockedSlots; emptySlot++) {
				Assert.False(states[emptySlot].HasCurrentItem);
				Assert.False(states[emptySlot].ShowMoveGuide);
				Assert.False(states[emptySlot].ShowRotateGuide);
				Assert.False(states[emptySlot].ShowPriorityBadge);
				Assert.False(states[emptySlot].ShowExpectedStatusBadge);
			}

			int[] liveAfterManualMove = new[] { 0, 1, -1, 2, -1, -1 };
			int[] liveRotAfterMove = new[] { 0, 0, 0, 0, 0, 0 };
			CycleDecomposition.EvaluateSlotGuides(
				snapshot,
				liveAfterManualMove,
				liveRotAfterMove,
				targetSlotToItem,
				targetItemToSlot,
				targetRotations,
				states
			);

			Assert.False(states[2].HasCurrentItem);
			Assert.False(states[2].ShowMoveGuide);
			Assert.True(states[3].HasCurrentItem);
			Assert.False(states[3].ShowMoveGuide);
			Assert.True(states[3].ShowRotateGuide);
			Assert.Equal(90, states[3].RotationDeltaDegrees);

			int[] liveRotAfterClick = new[] { 0, 0, 0, 1, 0, 0 };
			CycleDecomposition.EvaluateSlotGuides(
				snapshot,
				liveAfterManualMove,
				liveRotAfterClick,
				targetSlotToItem,
				targetItemToSlot,
				targetRotations,
				states
			);

			Assert.True(states[3].HasCurrentItem);
			Assert.True(states[3].IsCompleted);
			Assert.False(states[3].ShowMoveGuide);
			Assert.False(states[3].ShowRotateGuide);
		}

		[Fact(DisplayName = "무기 원소 공식과 온히트 속성 추가 피해 강화에 따라 캘세더니 열쇠 최적 행과 예상 DPS가 무기 속성에 맞춰 산출된다")]
		public void Solve_SelectsElementalRowAndComputesDpsAccordingToWeaponFormulaAndOnHitAddon() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			SlotItemSnapshot chalcedonyKey = new SlotItemSnapshot(
				0,
				2301,
				2301,
				"캘세더니 열쇠",
				SlotItemKind.Artifact,
				3,
				0,
				false,
				2,
				3,
				2,
				8,
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
				ArtifactPriorityTier.Core,
				rowCategoryIds: new[] { 0, 1 },
				rowElementalBonusByLevel: new[] { 4, 8, 12, 16 }
			);

			SlotItemSnapshot iceScalingCharm = new SlotItemSnapshot(
				1,
				2302,
				2302,
				"빙하 고드름",
				SlotItemKind.Artifact,
				1,
				0,
				false,
				1,
				3,
				2,
				8,
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
				new[] { "GLACIER" },
				Array.Empty<TabletPlacementRule>(),
				2,
				ArtifactPriorityTier.Core,
				1u << 1,
				attackDamageByLevel: new[] { 15f, 20f, 25f, 30f },
				baseProcsPerSecond: 1.5f,
				charmScalingElementKind: 3,
				charmElementalPercentByLevel: new[] { 80f, 100f, 120f, 140f }
			);

			ComboCategoryInfo[] combos = new[] {
				new ComboCategoryInfo(
					0,
					"EMBER",
					"잉걸불",
					0,
					new[] { 2 },
					false,
					new[] { 10f },
					new[] { new CombatStatDelta(fireDamage: 10f) }
				),
				new ComboCategoryInfo(
					1,
					"GLACIER",
					"빙하",
					1,
					new[] { 2 },
					false,
					new[] { 10f },
					new[] { new CombatStatDelta(iceDamage: 10f) }
				)
			};

			WeaponCombatProfile fireWeaponProfile = new WeaponCombatProfile(
				WeaponElementalFormulaMode.SingleFire,
				0x02,
				20f,
				28f,
				20f,
				20f,
				0f,
				20f,
				10f,
				0f,
				0f,
				0f,
				10f,
				50f,
				0f,
				0f,
				0f,
				false,
				1.6f,
				1.6f,
				0f,
				2.5f,
				0f,
				0f,
				0f,
				0,
				0f,
				0f,
				false,
				0f,
				1.0f,
				1.0f,
				false,
				1,
				20,
				10,
				0,
				0f,
				60f,
				0f,
				0f,
				WeaponConditionalStatMode.None,
				WeaponConditionalStatTarget.None,
				0,
				0f,
				0.5f,
				0,
				0f,
				0f,
				0f,
				1.0f,
				0f
			);

			WeaponCombatProfile iceWeaponProfile = new WeaponCombatProfile(
				WeaponElementalFormulaMode.SingleIce,
				0x04,
				20f,
				20f,
				28f,
				20f,
				0f,
				20f,
				10f,
				0f,
				0f,
				0f,
				10f,
				50f,
				0f,
				0f,
				0f,
				false,
				1.6f,
				1.6f,
				0f,
				2.5f,
				0f,
				0f,
				0f,
				0,
				0f,
				0f,
				false,
				0f,
				1.0f,
				1.0f,
				false,
				1,
				20,
				10,
				0,
				0f,
				0f,
				60f,
				0f,
				WeaponConditionalStatMode.None,
				WeaponConditionalStatTarget.None,
				0,
				0f,
				0f,
				0,
				0f,
				12f,
				90f,
				1.0f,
				0.5f
			);

			InventorySnapshot fireSnapshot = new InventorySnapshot(
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
				new[] { chalcedonyKey, iceScalingCharm },
				combos,
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				new PlayerCombatBaseline(in fireWeaponProfile)
			);

			InventorySnapshot iceSnapshot = new InventorySnapshot(
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
				new[] { chalcedonyKey, iceScalingCharm },
				combos,
				TalentRuleSnapshot.Default,
				-1,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				new PlayerCombatBaseline(in iceWeaponProfile)
			);

			int[] keyTopRowLayout = new[] { 0, 1, -1, -1, -1, -1 };
			int[] keyBottomRowLayout = new[] { -1, 1, -1, 0, -1, -1 };
			int[] zeroRots = new[] { 0, 0 };

			DpsBreakdown fireTopDps = DpsEstimator.CalculateBreakdown(fireSnapshot, keyTopRowLayout, zeroRots);
			DpsBreakdown fireBottomDps = DpsEstimator.CalculateBreakdown(fireSnapshot, keyBottomRowLayout, zeroRots);
			Assert.True(fireTopDps.TotalEstimatedDps > fireBottomDps.TotalEstimatedDps);

			DpsBreakdown iceTopDps = DpsEstimator.CalculateBreakdown(iceSnapshot, keyTopRowLayout, zeroRots);
			DpsBreakdown iceBottomDps = DpsEstimator.CalculateBreakdown(iceSnapshot, keyBottomRowLayout, zeroRots);
			Assert.True(iceBottomDps.TotalEstimatedDps > iceTopDps.TotalEstimatedDps);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult fireResult = solver.Solve(fireSnapshot, 200);
			OptimizationResult iceResult = solver.Solve(iceSnapshot, 200);

			Assert.Equal(0, fireResult.BestItemToSlotIndex[0] / width);
			Assert.Equal(1, iceResult.BestItemToSlotIndex[0] / width);
			Assert.Equal(fireResult.BestEstimatedDps, (int)Math.Round(fireResult.OptimizedBreakdown.TotalEstimatedDps));
			Assert.Equal(iceResult.BestEstimatedDps, (int)Math.Round(iceResult.OptimizedBreakdown.TotalEstimatedDps));
		}

		[Fact(DisplayName = "석궁 탄창·재장전 사이클과 공격속도 증폭 및 4원소 비례 산탄·무기 전용 치명타 처형 공식을 오차 없이 계산한다")]
		public void CalculateBreakdown_ModelsCrossbowReloadAttackSpeedAmpScaledSpreadAndExecutionCrit() {
			int width = 2;
			int height = 1;
			int unlockedSlots = 2;

			SlotItemSnapshot lightningHasteArtifact = new SlotItemSnapshot(
				0,
				2401,
				2401,
				"뇌전 가속 부적",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				1,
				1,
				2,
				7,
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
				1,
				ArtifactPriorityTier.Normal,
				statBonusPercentByLevel: new[] { 0f, 35f },
				statDeltaByLevel: new[] {
					CombatStatDelta.Zero,
					new CombatStatDelta(lightningDamage: 15f, attackSpeed: 20f)
				}
			);

			WeaponCombatProfile crossbowProfile = new WeaponCombatProfile(
				WeaponElementalFormulaMode.Highest,
				0x0F,
				30f,
				25f,
				20f,
				20f,
				10f,
				20f,
				10f,
				0f,
				0f,
				20f,
				80f,
				50f,
				40f,
				30f,
				25f,
				true,
				1.5f,
				1.5f,
				0f,
				2.0f,
				0.5f,
				0f,
				0.2f,
				6,
				1.0f,
				0f,
				false,
				0f,
				1.0f,
				1.0f,
				true,
				3,
				20,
				10,
				5,
				0f,
				0f,
				0f,
				0f,
				WeaponConditionalStatMode.None,
				WeaponConditionalStatTarget.None,
				0,
				0f,
				0f,
				0,
				0f,
				0f,
				0f,
				1.0f,
				0f
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
				new[] { lightningHasteArtifact },
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
				new PlayerCombatBaseline(in crossbowProfile)
			);

			DpsBreakdown breakdown = DpsEstimator.CalculateCurrentBreakdown(snapshot);
			int evalDps = ObjectiveEvaluator.CalculateEstimatedDummyDps(
				snapshot,
				snapshot.InitialSlotToItemIndex,
				snapshot.InitialItemRotations
			);

			double expectedSwingDamage = 35.0 * 1.5 * 2.0 * (1.20 * 1.10);
			double expectedSustainedRate = 2.25;
			double expectedRawDps = expectedSwingDamage * expectedSustainedRate;
			double expectedExecCritMult = 2.20;
			double expectedAllDmgMult = 1.10;
			double expectedTotalDps = expectedRawDps * expectedAllDmgMult * expectedExecCritMult;

			Assert.Equal(expectedRawDps, breakdown.WeaponAndCharmBaseDps, 4);
			Assert.Equal(expectedTotalDps, breakdown.TotalEstimatedDps, 4);
			Assert.Equal((int)Math.Round(expectedTotalDps), evalDps);
		}

		[Fact(DisplayName = "무기 강화 조건부 스탯 증폭 임계치 달성 여부와 다중 북향의 금빛침 합연산 증폭 및 유물 세트 보너스를 정확히 반영한다")]
		public void CalculateBreakdown_EvaluatesConditionalWeaponAddonThresholdAndAdditiveMultipleGoldenNeedles() {
			int width = 3;
			int height = 2;
			int unlockedSlots = 6;

			SlotItemSnapshot attackRoot = new SlotItemSnapshot(
				0,
				2501,
				2501,
				"물리 칼날 부적",
				SlotItemKind.Artifact,
				1,
				0,
				false,
				1,
				1,
				1,
				8,
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
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				1,
				ArtifactPriorityTier.Core,
				attackDamageByLevel: new[] { 100f, 100f },
				baseProcsPerSecond: 1.0f
			);

			SlotItemSnapshot needleLeft = new SlotItemSnapshot(
				1,
				2502,
				2502,
				"좌측 금빛침",
				SlotItemKind.Artifact,
				0,
				0,
				false,
				1,
				1,
				2,
				7,
				ArtifactConstraintType.None,
				false,
				false,
				false,
				false,
				true,
				1,
				0,
				false,
				false,
				Array.Empty<string>(),
				Array.Empty<TabletPlacementRule>(),
				2,
				ArtifactPriorityTier.Normal,
				needleDamageBonusByLevel: new[] { 30, 30 }
			);

			SlotItemSnapshot needleBottom = new SlotItemSnapshot(
				2,
				2503,
				2503,
				"하단 금빛침",
				SlotItemKind.Artifact,
				4,
				0,
				false,
				1,
				1,
				2,
				7,
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
				3,
				ArtifactPriorityTier.Normal,
				needleDamageBonusByLevel: new[] { 50, 50 }
			);

			SlotItemSnapshot critThresholdArtifact = new SlotItemSnapshot(
				3,
				2504,
				2504,
				"예리한 송곳",
				SlotItemKind.Artifact,
				2,
				0,
				false,
				1,
				1,
				2,
				7,
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
				4,
				ArtifactPriorityTier.Normal,
				statBonusPercentByLevel: new[] { 0f, 15f },
				statDeltaByLevel: new[] {
					CombatStatDelta.Zero,
					new CombatStatDelta(critChancePercent: 15f)
				}
			);

			WeaponCombatProfile conditionalProfile = new WeaponCombatProfile(
				WeaponElementalFormulaMode.SinglePhysical,
				0x01,
				20f,
				20f,
				20f,
				20f,
				0f,
				0f,
				0f,
				0f,
				20f,
				0f,
				20f,
				50f,
				0f,
				0f,
				0f,
				false,
				1.0f,
				1.0f,
				0f,
				2.0f,
				0f,
				0f,
				0f,
				0,
				0f,
				0f,
				false,
				0f,
				1.0f,
				1.0f,
				false,
				1,
				20,
				10,
				0,
				0f,
				0f,
				0f,
				0f,
				WeaponConditionalStatMode.GreaterOrEqual,
				WeaponConditionalStatTarget.CriticalRaw,
				3000,
				40f,
				0f,
				0,
				0f,
				0f,
				0f,
				1.0f,
				0f
			);

			ComboCategoryInfo[] combos = new[] {
				new ComboCategoryInfo(
					0,
					"EMBER",
					"잉걸불",
					2,
					new[] { 2 },
					false,
					new[] { 0f },
					new[] { CombatStatDelta.Zero }
				)
			};
			TalentRuleSnapshot talentWithComboBonus = new TalentRuleSnapshot(
				0,
				0,
				0,
				10,
				0,
				false,
				false,
				0,
				0,
				0,
				0,
				Array.Empty<ArrangementPattern>()
			);

			int[] baseDisable = new[] { 0, 0, 1, 0, 0, 0 };
			InventorySnapshot snapshot = new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				new int[unlockedSlots],
				baseDisable,
				new int[unlockedSlots],
				new int[unlockedSlots],
				new bool[unlockedSlots],
				true,
				1,
				new[] { attackRoot, needleLeft, needleBottom, critThresholdArtifact },
				combos,
				talentWithComboBonus,
				2501,
				-1,
				null,
				-1,
				-1,
				2,
				4,
				null,
				new PlayerCombatBaseline(in conditionalProfile)
			);

			int[] belowThresholdLayout = new[] { 1, 0, 3, -1, 2, -1 };
			int[] aboveThresholdLayout = new[] { 1, 0, -1, -1, 2, 3 };
			int[] zeroRots = new[] { 0, 0, 0, 0 };

			DpsBreakdown belowBreakdown = DpsEstimator.CalculateBreakdown(snapshot, belowThresholdLayout, zeroRots);
			DpsBreakdown aboveBreakdown = DpsEstimator.CalculateBreakdown(snapshot, aboveThresholdLayout, zeroRots);
			int belowEvalDps = ObjectiveEvaluator.CalculateEstimatedDummyDps(snapshot, belowThresholdLayout, zeroRots);
			int aboveEvalDps = ObjectiveEvaluator.CalculateEstimatedDummyDps(snapshot, aboveThresholdLayout, zeroRots);

			Assert.Equal(80.0, belowBreakdown.GoldenNeedleAmpPercent, 4);
			Assert.Equal(290.4, belowBreakdown.TotalEstimatedDps, 4);
			Assert.Equal(290, belowEvalDps);

			Assert.Equal(80.0, aboveBreakdown.GoldenNeedleAmpPercent, 4);
			Assert.Equal(434.28, aboveBreakdown.TotalEstimatedDps, 4);
			Assert.Equal(434, aboveEvalDps);
		}

		[Fact(DisplayName = "한 석판의 마이너스 효과가 다른 석판의 플러스 효과와 겹쳐 양수(>0)로 남는 손해 배치를 감지하고 무충돌 배치로 복원한다")]
		public void Solve_ResolvesCrossTabletMinusPlusOverlapOnNetPositiveSlotAndRestoresFullBuff() {
			int width = 4;
			int height = 2;
			int unlockedSlots = 8;

			TabletPlacementRule[] rightPlus2Rules = CreateDirectionalTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			TabletPlacementRule[] bipolarPlus1Minus1Rules = CreateBipolarTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				1,
				-1,
				0,
				-1
			);

			SlotItemSnapshot tabletA = CreateTablet(0, 2601, 0, 0, true, rightPlus2Rules, 1);
			SlotItemSnapshot tabletB = CreateTablet(1, 2602, 2, 0, true, bipolarPlus1Minus1Rules, 2);
			SlotItemSnapshot cap1Artifact = CreateArtifact(
				2,
				2603,
				1,
				0,
				1,
				6,
				ArtifactConstraintType.None,
				3
			);
			SlotItemSnapshot cap2Artifact = CreateArtifact(
				3,
				2604,
				3,
				0,
				2,
				8,
				ArtifactConstraintType.None,
				4
			);

			InventorySnapshot snapshot = CreateSimpleSnapshot(
				width,
				height,
				unlockedSlots,
				new[] { tabletA, tabletB, cap1Artifact, cap2Artifact }
			);

			int[] collidingLayout = new[] { 0, 2, 1, 3, -1, -1, -1, -1 };
			int[] collidingRots = new[] { 0, 0, 0, 0 };
			SlotEvaluationDetail[] collidingDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				collidingLayout,
				collidingRots
			);
			Assert.Equal(1, collidingDetails[1].DisplayedLevel);
			Assert.Equal(1, collidingDetails[1].EffectiveLevel);

			int[] cleanSameArtifactsLayout = new[] { 0, 2, 1, -1, -1, -1, 3, -1 };
			int[] cleanSameArtifactsRots = new[] { 0, 3, 0, 0 };
			int collidingScore = ObjectiveEvaluator.Evaluate(snapshot, collidingLayout, collidingRots);
			int cleanRotatedScore = ObjectiveEvaluator.Evaluate(
				snapshot,
				cleanSameArtifactsLayout,
				cleanSameArtifactsRots
			);
			Assert.True(cleanRotatedScore > collidingScore);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			int slotCap2 = result.BestItemToSlotIndex[3];
			int slotCap1 = result.BestItemToSlotIndex[2];
			Assert.True(slotCap2 >= 0);
			Assert.True(slotCap1 >= 0);
			Assert.Equal(2, result.BestSlotDetails[slotCap2].EffectiveLevel);
			Assert.Equal(1, result.BestSlotDetails[slotCap1].EffectiveLevel);
			Assert.True(result.BestScore > collidingScore);
		}

		[Fact(DisplayName = "5단계 석판 배치 알고리즘이 대규모 격자에서 석판 간 +/- 충돌과 아티팩트 고착 국소 최적점을 탈출한다")]
		public void Solve_StagedAlgorithmEscapesCoupledTabletArtifactLocalMinimumInLargeGrid() {
			int width = 5;
			int height = 3;
			int unlockedSlots = 15;

			TabletPlacementRule[] bipolarPlus2Minus1Rules = CreateBipolarTabletRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				2,
				-1,
				0,
				-1
			);
			TabletPlacementRule[] verticalPlus2Minus1Rules = CreateBipolarTabletRules(
				width,
				height,
				unlockedSlots,
				0,
				1,
				2,
				0,
				-1,
				-1
			);

			SlotItemSnapshot tablet0 = CreateTablet(0, 2701, 1, 0, true, bipolarPlus2Minus1Rules, 1);
			SlotItemSnapshot tablet1 = CreateTablet(1, 2702, 3, 2, true, bipolarPlus2Minus1Rules, 1);
			SlotItemSnapshot tablet2 = CreateTablet(2, 2703, 7, 0, true, verticalPlus2Minus1Rules, 2);

			SlotItemSnapshot[] items = new SlotItemSnapshot[11];
			items[0] = tablet0;
			items[1] = tablet1;
			items[2] = tablet2;
			for (int i = 3; i < 11; i++) {
				int maxLv = i <= 6 ? 3 : 2;
				int weight = i <= 6 ? 12 : 6;
				int initSlot = i + 2;
				items[i] = CreateArtifact(
					i,
					2700 + i,
					initSlot,
					0,
					maxLv,
					weight,
					ArtifactConstraintType.None,
					10 + i
				);
			}

			InventorySnapshot snapshot = CreateSimpleSnapshot(width, height, unlockedSlots, items);

			SlotEvaluationDetail[] initDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				snapshot.InitialSlotToItemIndex,
				snapshot.InitialItemRotations
			);
			Assert.True(initDetails[2].DisplayedLevel > 0);

			OptimizationSolver solver = new OptimizationSolver();
			OptimizationResult result = solver.Solve(snapshot);

			Assert.True(result.BestScore > result.InitialScore);

			int totalEffectiveLevels = 0;
			for (int s = 0; s < unlockedSlots; s++) {
				int idx = result.BestSlotToItemIndex[s];
				if (idx >= 3) {
					Assert.True(result.BestSlotDetails[s].IsEnabled);
					totalEffectiveLevels += result.BestSlotDetails[s].EffectiveLevel;
				}
			}
			Assert.True(totalEffectiveLevels >= 6);
		}

		[Fact(DisplayName = "모든 슬롯이 가득 차고 다른 방향 회전 시 핵심 아티팩트가 비활성화되는 불가피한 경우에는 +/- 중첩으로 양수(>0) 유지를 허용한다")]
		public void Solve_AllowsInevitablePlusMinusOverlapWhenAllOtherOrientationsDeactivateCoreArtifact() {
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
			TabletPlacementRule[] leftOrRightMinus1Rules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				int x = s % width;
				int y = s / width;
				for (int rot = 0; rot < 4; rot++) {
					int dx = (rot & 1) == 0 ? -1 : 1;
					int targetSlot = CoordToSlot(x + dx, y, width, height, unlockedSlots);
					PrecomputedEffect[] effs = new[] {
						new PrecomputedEffect(
							(sbyte)(x + dx),
							(sbyte)y,
							targetSlot,
							TabletEffectType.IncreaseConstLevel,
							-1
						)
					};
					leftOrRightMinus1Rules[(s * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						effs
					);
				}
			}

			SlotItemSnapshot pinnedLeftPlus2Tablet = CreateTablet(0, 2801, 0, 0, false, rightPlus2Rules, 1);
			SlotItemSnapshot centerArtifact = CreateArtifact(
				1,
				2802,
				1,
				0,
				2,
				10,
				ArtifactConstraintType.None,
				2
			);
			SlotItemSnapshot pinnedRightMinus1Tablet = CreateTablet(
				2,
				2803,
				2,
				1,
				true,
				leftOrRightMinus1Rules,
				3
			);

			bool[] pinned = new[] { true, false, false };
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
				new[] { pinnedLeftPlus2Tablet, centerArtifact, pinnedRightMinus1Tablet }
			);

			int[] forcedOverlapLayout = new[] { 0, 1, 2 };
			int[] overlapRots = new[] { 0, 0, 0 };
			SlotEvaluationDetail[] overlapDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				forcedOverlapLayout,
				overlapRots
			);
			Assert.True(overlapDetails[1].IsEnabled);
			Assert.Equal(1, overlapDetails[1].DisplayedLevel);

			int[] deactivatedLayout = new[] { 0, 2, 1 };
			int[] deactivatedRots = new[] { 0, 0, 1 };
			int overlapScore = ObjectiveEvaluator.Evaluate(snapshot, forcedOverlapLayout, overlapRots);
			int deactivatedScore = ObjectiveEvaluator.Evaluate(snapshot, deactivatedLayout, deactivatedRots);
			Assert.True(overlapScore > deactivatedScore);
		}


		private static int CountBits(ulong mask) {
			int count = 0;
			while (mask != 0UL) {
				mask &= mask - 1UL;
				count++;
			}
			return count;
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

		private static TabletPlacementRule[] CreateBipolarTabletRules(
			int width,
			int height,
			int unlockedSlots,
			int posBaseDx,
			int posBaseDy,
			int posLevelParam,
			int negBaseDx,
			int negBaseDy,
			int negLevelParam
		) {
			TabletPlacementRule[] rules = new TabletPlacementRule[unlockedSlots * 4];
			for (int slotIdx = 0; slotIdx < unlockedSlots; slotIdx++) {
				int x = slotIdx % width;
				int y = slotIdx / width;
				for (int rot = 0; rot < 4; rot++) {
					(int pDx, int pDy) = RotateOffset(posBaseDx, posBaseDy, rot);
					(int nDx, int nDy) = RotateOffset(negBaseDx, negBaseDy, rot);
					int posTarget = CoordToSlot(x + pDx, y + pDy, width, height, unlockedSlots);
					int negTarget = CoordToSlot(x + nDx, y + nDy, width, height, unlockedSlots);
					PrecomputedEffect[] effects = new[] {
						new PrecomputedEffect(
							(sbyte)(x + pDx),
							(sbyte)(y + pDy),
							posTarget,
							TabletEffectType.IncreaseConstLevel,
							posLevelParam
						),
						new PrecomputedEffect(
							(sbyte)(x + nDx),
							(sbyte)(y + nDy),
							negTarget,
							TabletEffectType.IncreaseConstLevel,
							negLevelParam
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
