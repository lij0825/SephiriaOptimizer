using System;
using SephiriaOptimizer.Execution;
using SephiriaOptimizer.Models;
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
				items
			);

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
				items
			);

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
