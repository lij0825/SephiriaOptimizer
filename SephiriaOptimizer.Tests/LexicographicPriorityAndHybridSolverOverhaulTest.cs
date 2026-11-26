using System;
using SephiriaOptimizer.Models;
using SephiriaOptimizer.Snapshot;
using SephiriaOptimizer.Solver;
using Xunit;

namespace SephiriaOptimizer.Tests {
	public sealed class LexicographicPriorityAndHybridSolverOverhaulTest {
		[Fact(DisplayName = "동일 인벤토리에서 사용자 지정 우선순위만 변경하면 더미 DPS를 무시하고 배치·버프·금빛침 대상이 즉시 전환된다")]
		public void Solve_RespectsUserPriorityOverrideImmediatelyAndIgnoresDummyDpsDominance() {
			// Given
			const int width = 4;
			const int height = 2;
			const int unlockedSlots = 8;

			OptimizerUserSettings cycleSettings = new OptimizerUserSettings();
			ArtifactPriorityTier currentCycle = ArtifactPriorityTier.Normal;
			int testEntityId = 7101;
			int initialHash = cycleSettings.ComputeSettingsHash();
			for (int step = 0; step < 5; step++) {
				cycleSettings.CyclePriority(testEntityId, currentCycle);
				currentCycle = cycleSettings.GetPriority(testEntityId);
				Assert.True(cycleSettings.HasExplicitPriority(testEntityId));
			}
			Assert.Equal(ArtifactPriorityTier.Normal, currentCycle);
			Assert.NotEqual(initialHash, cycleSettings.ComputeSettingsHash());

			SlotItemSnapshot artifactWeakDps = new SlotItemSnapshot(
				itemIndex: 0,
				instanceId: 8101,
				entityId: 8101,
				displayName: "저공격 우선대상",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 0,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 5,
				rarityTier: 2,
				customWeight: 5,
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
				symmetryGroupId: 11,
				priorityTier: ArtifactPriorityTier.Normal,
				attackDamageByLevel: new[] { 1f, 2f, 3f, 4f, 5f, 6f },
				statBonusPercentByLevel: new float[6]
			);

			SlotItemSnapshot artifactHugeDps = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 8102,
				entityId: 8102,
				displayName: "고공격 일반대상",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 3,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 5,
				rarityTier: 2,
				customWeight: 5,
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
				symmetryGroupId: 12,
				priorityTier: ArtifactPriorityTier.Normal,
				attackDamageByLevel: new[] { 2000f, 3000f, 4500f, 6500f, 9000f, 12000f },
				statBonusPercentByLevel: new float[6]
			);

			SlotItemSnapshot goldenNeedle = new SlotItemSnapshot(
				itemIndex: 2,
				instanceId: 8103,
				entityId: 8103,
				displayName: "북향의 금빛침",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 6,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 1,
				customWeight: 4,
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
				symmetryGroupId: 13,
				priorityTier: ArtifactPriorityTier.High
			);

			TabletPlacementRule[] buffRightRules = BuildDirectionalRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				3
			);
			SlotItemSnapshot buffTablet = CreateTablet(3, 8104, 1, true, buffRightRules, 201);

			InventorySnapshot baseSnapshot = new InventorySnapshot(
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
				new[] { artifactWeakDps, artifactHugeDps, goldenNeedle, buffTablet },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default
			);

			OptimizerUserSettings prioritizeWeak = new OptimizerUserSettings();
			prioritizeWeak.SetPriority(8101, ArtifactPriorityTier.Core);
			prioritizeWeak.SetPriority(8102, ArtifactPriorityTier.Low);
			prioritizeWeak.PreferredNeedleTargetInstanceId = 8101;

			OptimizerUserSettings prioritizeHuge = new OptimizerUserSettings();
			prioritizeHuge.SetPriority(8101, ArtifactPriorityTier.Low);
			prioritizeHuge.SetPriority(8102, ArtifactPriorityTier.Core);
			prioritizeHuge.PreferredNeedleTargetInstanceId = 8102;

			InventorySnapshot weakSnapshot = baseSnapshot.WithUserSettings(prioritizeWeak);
			InventorySnapshot hugeSnapshot = baseSnapshot.WithUserSettings(prioritizeHuge);

			OptimizationSolver solver = new OptimizationSolver();

			// When
			OptimizationResult weakFirstResult = solver.Solve(weakSnapshot, 400);
			OptimizationResult hugeFirstResult = solver.Solve(hugeSnapshot, 400);

			// Then
			Assert.Equal(0, weakFirstResult.TopCandidates[0].Metrics.HardViolationCount);
			Assert.Equal(0, hugeFirstResult.TopCandidates[0].Metrics.HardViolationCount);

			int weakTargetSlotA = weakFirstResult.BestItemToSlotIndex[0];
			int hugeTargetSlotA = weakFirstResult.BestItemToSlotIndex[1];
			Assert.True(
				weakFirstResult.BestSlotDetails[weakTargetSlotA].EffectiveLevel
					> weakFirstResult.BestSlotDetails[hugeTargetSlotA].EffectiveLevel
			);
			Assert.Equal(weakTargetSlotA + width, weakFirstResult.BestItemToSlotIndex[2]);

			int weakTargetSlotB = hugeFirstResult.BestItemToSlotIndex[0];
			int hugeTargetSlotB = hugeFirstResult.BestItemToSlotIndex[1];
			Assert.True(
				hugeFirstResult.BestSlotDetails[hugeTargetSlotB].EffectiveLevel
					> hugeFirstResult.BestSlotDetails[weakTargetSlotB].EffectiveLevel
			);
			Assert.Equal(hugeTargetSlotB + width, hugeFirstResult.BestItemToSlotIndex[2]);
		}

		[Fact(DisplayName = "페널티 상쇄가 필수인 복합 석판 배치에서 연쇄 수리와 3단계 탐색으로 완전 상쇄 전역 최적해를 도출한다")]
		public void Solve_FindsSteppingStonePenaltyOffsetOptimumWithZeroHardViolations() {
			// Given
			const int width = 4;
			const int height = 3;
			const int unlockedSlots = 12;

			SlotItemSnapshot fragileCore = CreateStatArtifact(
				0,
				8201,
				0,
				"강화핵심",
				baseLevel: 1,
				maxLevel: 5,
				weight: 10,
				ArtifactPriorityTier.Core,
				21,
				new[] { 1f, 3f, 6f, 10f, 15f, 22f }
			);
			SlotItemSnapshot constrainedArt = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 8202,
				entityId: 8202,
				displayName: "상단빈칸제약",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 4,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 1,
				customWeight: 8,
				constraintType: ArtifactConstraintType.TopInInventory,
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
				symmetryGroupId: 22,
				priorityTier: ArtifactPriorityTier.High
			);
			SlotItemSnapshot subArt = CreateStatArtifact(
				2,
				8203,
				5,
				"보조성물",
				baseLevel: 1,
				maxLevel: 3,
				weight: 4,
				ArtifactPriorityTier.Normal,
				23,
				new[] { 1f, 2f, 4f, 6f }
			);

			TabletPlacementRule[] bipolarRules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				int x = s % width;
				int y = s / width;
				for (int rot = 0; rot < 4; rot++) {
					(int pdx, int pdy) = RotateOffset(1, 0, rot);
					(int ndx, int ndy) = RotateOffset(-1, 0, rot);
					int posTarget = ResolveSlot(x + pdx, y + pdy, width, height, unlockedSlots);
					int negTarget = ResolveSlot(x + ndx, y + ndy, width, height, unlockedSlots);
					bipolarRules[(s * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						new[] {
							new PrecomputedEffect(
								(sbyte)(x + pdx),
								(sbyte)(y + pdy),
								posTarget,
								TabletEffectType.IncreaseConstLevel,
								3
							),
							new PrecomputedEffect(
								(sbyte)(x + ndx),
								(sbyte)(y + ndy),
								negTarget,
								TabletEffectType.IncreaseConstLevel,
								-3
							)
						}
					);
				}
			}

			TabletPlacementRule[] downPlus4LeftIgnoreRules = new TabletPlacementRule[unlockedSlots * 4];
			for (int s = 0; s < unlockedSlots; s++) {
				int x = s % width;
				int y = s / width;
				for (int rot = 0; rot < 4; rot++) {
					(int ddx, int ddy) = RotateOffset(0, 1, rot);
					(int ldx, int ldy) = RotateOffset(-1, 0, rot);
					int boostTarget = ResolveSlot(x + ddx, y + ddy, width, height, unlockedSlots);
					int ignoreTarget = ResolveSlot(x + ldx, y + ldy, width, height, unlockedSlots);
					downPlus4LeftIgnoreRules[(s * 4) + rot] = new TabletPlacementRule(
						Array.Empty<PrecomputedCondition>(),
						new[] {
							new PrecomputedEffect(
								(sbyte)(x + ddx),
								(sbyte)(y + ddy),
								boostTarget,
								TabletEffectType.IncreaseConstLevel,
								4
							),
							new PrecomputedEffect(
								(sbyte)(x + ldx),
								(sbyte)(y + ldy),
								ignoreTarget,
								TabletEffectType.IgnoreCriteria,
								1
							)
						}
					);
				}
			}

			SlotItemSnapshot bipolarTablet = CreateTablet(3, 8204, 1, true, bipolarRules, 211);
			SlotItemSnapshot offsetTablet = CreateTablet(4, 8205, 9, true, downPlus4LeftIgnoreRules, 212);

			OptimizerUserSettings userSettings = new OptimizerUserSettings();
			userSettings.SetPriority(8201, ArtifactPriorityTier.Core);
			userSettings.SetPriority(8202, ArtifactPriorityTier.High);

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
				new[] { fragileCore, constrainedArt, subArt, bipolarTablet, offsetTablet },
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default
			).WithUserSettings(userSettings);

			OptimizationSolver solver = new OptimizationSolver();

			// When
			OptimizationResult result = solver.Solve(snapshot, 600);

			// Then
			Assert.Equal(0, result.TopCandidates[0].Metrics.HardViolationCount);
			for (int i = 0; i < 3; i++) {
				int slot = result.BestItemToSlotIndex[i];
				Assert.True(slot >= 0);
				Assert.True(result.BestSlotDetails[slot].IsEnabled);
				Assert.True(result.BestSlotDetails[slot].EffectiveLevel >= snapshot.Items[i].BaseLevel);
			}
			int coreSlot = result.BestItemToSlotIndex[0];
			Assert.True(result.BestSlotDetails[coreSlot].EffectiveLevel >= 4);
		}

		[Fact(DisplayName = "동료 휘장 아티팩트 고려 ON/OFF 전환 시 동료 혼돈 행 시너지 포함과 미배치 후보 교체 투입으로 정확히 분기한다")]
		public void Solve_BranchesCandidateInclusionAndChaosSynergyByCompanionEmblemToggle() {
			// Given
			const int width = 3;
			const int height = 2;
			const int unlockedSlots = 6;

			SlotItemSnapshot coreAttack = CreateStatArtifact(
				0,
				8301,
				0,
				"주력 성물",
				baseLevel: 1,
				maxLevel: 3,
				weight: 10,
				ArtifactPriorityTier.Core,
				31,
				new[] { 1f, 3f, 6f, 10f }
			);
			SlotItemSnapshot chaosEmblem = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 8302,
				entityId: 8302,
				displayName: "헌신의 휘장",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 1,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 2,
				customWeight: 8,
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
				symmetryGroupId: 32,
				priorityTier: ArtifactPriorityTier.High,
				isCompanionChaosCharm: true,
				statBonusPercentByLevel: new[] { 2f, 5f, 9f, 14f },
				isCompanionEmblemArtifact: true
			);
			SlotItemSnapshot friendEmblem = new SlotItemSnapshot(
				itemIndex: 2,
				instanceId: 8303,
				entityId: 8303,
				displayName: "동료의 휘장",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 5,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 1,
				customWeight: 6,
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
				symmetryGroupId: 33,
				priorityTier: ArtifactPriorityTier.Normal,
				isCompanionCharm: true,
				statBonusPercentByLevel: new[] { 1f, 3f, 6f, 10f },
				isCompanionEmblemArtifact: true,
				isCompanionAttackCharm: true
			);
			SlotItemSnapshot reserveEmblem = new SlotItemSnapshot(
				itemIndex: 3,
				instanceId: 8304,
				entityId: 8304,
				displayName: "전우의 휘장",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 3,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 1,
				customWeight: 6,
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
				symmetryGroupId: 34,
				priorityTier: ArtifactPriorityTier.Normal,
				isCompanionCharm: true,
				statBonusPercentByLevel: new[] { 1f, 3f, 6f, 10f },
				isCompanionEmblemArtifact: true
			);
			TabletPlacementRule[] buffRightRules = BuildDirectionalRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			SlotItemSnapshot plusTablet = CreateTablet(4, 8305, 4, true, buffRightRules, 301);
			SlotItemSnapshot benchCandidate = CreateStatArtifact(
				5,
				8306,
				initialSlot: -1,
				"대기 중 일반 성물",
				baseLevel: 1,
				maxLevel: 3,
				weight: 9,
				ArtifactPriorityTier.High,
				35,
				new[] { 2f, 5f, 9f, 15f }
			);
			SlotItemSnapshot spareArt = CreateStatArtifact(
				6,
				8307,
				2,
				"기본 아티팩트",
				baseLevel: 1,
				maxLevel: 2,
				weight: 4,
				ArtifactPriorityTier.Normal,
				36,
				new[] { 1f, 3f, 5f }
			);

			OptimizerUserSettings settingsOn = new OptimizerUserSettings {
				ConsiderCompanionEmblemArtifacts = true
			};
			OptimizerUserSettings settingsOff = new OptimizerUserSettings {
				ConsiderCompanionEmblemArtifacts = false
			};

			InventorySnapshot snapshotOn = new InventorySnapshot(
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
				new[] {
					coreAttack,
					chaosEmblem,
					friendEmblem,
					reserveEmblem,
					plusTablet,
					benchCandidate,
					spareArt
				},
				Array.Empty<ComboCategoryInfo>(),
				TalentRuleSnapshot.Default
			).WithUserSettings(settingsOn);
			InventorySnapshot snapshotOff = snapshotOn.WithUserSettings(settingsOff);

			OptimizationSolver solver = new OptimizationSolver();

			// When
			OptimizationResult resultOn = solver.Solve(snapshotOn, 500);
			OptimizationResult resultOff = solver.Solve(snapshotOff, 500);

			// Then
			Assert.True(snapshotOn.HasAnyCompanionEmblem);
			Assert.False(snapshotOn.IsExcludedCompanionEmblem(chaosEmblem));
			Assert.True(snapshotOff.IsExcludedCompanionEmblem(chaosEmblem));

			int chaosRowOn = resultOn.BestItemToSlotIndex[1] / width;
			int friendRowOn = resultOn.BestItemToSlotIndex[2] / width;
			Assert.Equal(chaosRowOn, friendRowOn);
			Assert.True(resultOn.BestItemToSlotIndex[1] >= 0);

			Assert.True(resultOff.BestItemToSlotIndex[5] >= 0);
			Assert.Equal(0, resultOff.TopCandidates[0].Metrics.HardViolationCount);
		}

		[Fact(DisplayName = "동적 54슬롯 대규모 인벤토리에서도 2초 이내에 연산을 완료하고 연결 블록 이동 및 대칭 변환으로 최적해를 도출한다")]
		public void Solve_LargeDynamicInventoryCompletesUnderTwoSecondsAndExercisesHybridMoves() {
			// Given
			const int width = 9;
			const int height = 6;
			const int unlockedSlots = 54;

			SlotItemSnapshot[] items = new SlotItemSnapshot[20];
			items[0] = new SlotItemSnapshot(
				itemIndex: 0,
				instanceId: 8400,
				entityId: 8400,
				displayName: "최우선 핵심",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 53,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 6,
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
				symmetryGroupId: 40,
				priorityTier: ArtifactPriorityTier.Core,
				attackDamageByLevel: new[] { 10f, 20f, 35f, 55f, 80f, 110f, 150f },
				statBonusPercentByLevel: new float[7]
			);
			items[1] = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 8401,
				entityId: 8401,
				displayName: "북향의 금빛침",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 2,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 1,
				customWeight: 8,
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
				symmetryGroupId: 41,
				priorityTier: ArtifactPriorityTier.High
			);
			items[2] = new SlotItemSnapshot(
				itemIndex: 2,
				instanceId: 8402,
				entityId: 8402,
				displayName: "조화의 수정",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 0,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 2,
				customWeight: 12,
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
				symmetryGroupId: 42,
				priorityTier: ArtifactPriorityTier.High,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1f, 2f, 3f, 4f }
			);

			for (int i = 3; i < 14; i++) {
				items[i] = CreateStatArtifact(
					i,
					8400 + i,
					i + 4,
					$"유물 {i}",
					baseLevel: 1,
					maxLevel: 4,
					weight: 6,
					i < 6 ? ArtifactPriorityTier.High : ArtifactPriorityTier.Normal,
					100 + i,
					new[] { 2f, 5f, 9f, 14f, 20f },
					i % 2 == 0 ? new[] { "MYSTIC" } : Array.Empty<string>(),
					i % 2 == 0 ? 1u : 0u
				);
			}

			TabletPlacementRule[] rightPlus2 = BuildDirectionalRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				2
			);
			TabletPlacementRule[] leftMinus1 = BuildDirectionalRules(
				width,
				height,
				unlockedSlots,
				-1,
				0,
				TabletEffectType.IncreaseConstLevel,
				-1
			);
			for (int t = 14; t < 20; t++) {
				TabletPlacementRule[] r = t < 18 ? rightPlus2 : leftMinus1;
				items[t] = CreateTablet(t, 8400 + t, t + 18, true, r, 300 + t);
			}

			OptimizerUserSettings userSettings = new OptimizerUserSettings();
			userSettings.SetPriority(8400, ArtifactPriorityTier.Core);
			userSettings.PreferredNeedleTargetInstanceId = 8400;

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
				new[] { new ComboCategoryInfo(0, "MYSTIC", "신비", 5, new[] { 2, 4 }, true) },
				TalentRuleSnapshot.Default,
				mooreNeighborMasks: GameInventoryReader.BuildMooreNeighborMasks(width, height, unlockedSlots),
				mysticSlot0: 12,
				mysticSlot1: 21,
				mysticTier1Threshold: 2,
				mysticTier2Threshold: 4
			).WithUserSettings(userSettings);

			OptimizationSolver solver = new OptimizationSolver();

			// When
			OptimizationResult result = solver.Solve(snapshot);

			// Then
			Assert.True(result.ElapsedMilliseconds < 2000.0);
			Assert.True(result.ElapsedMilliseconds < 1500.0);
			Assert.Equal(0, result.TopCandidates[0].Metrics.HardViolationCount);
			LexicographicScore initialLex = ObjectiveEvaluator.EvaluateLexicographic(
				snapshot,
				snapshot.InitialSlotToItemIndex,
				snapshot.InitialItemRotations,
				out _,
				out _,
				out _
			);
			Assert.True(result.TopCandidates[0].Metrics.LexScore.CompareTo(initialLex) >= 0);
			int coreSlot = result.BestItemToSlotIndex[0];
			Assert.Equal(coreSlot + width, result.BestItemToSlotIndex[1]);
		}

		[Fact(DisplayName = "연결 블록 평행 이동·3-Opt 순환·대칭 반전 연산자가 단일 1:1 교환의 국소 최적 함정을 탈출한다")]
		public void Solve_ConnectedBlockShiftAndThreeCycleAndSymmetryEscapeGreedyTrap() {
			// Given
			const int width = 5;
			const int height = 3;
			const int unlockedSlots = 15;

			int[] baseSlotLevels = new int[unlockedSlots];
			baseSlotLevels[0] = 2;
			baseSlotLevels[1] = 2;

			SlotItemSnapshot[] items = new SlotItemSnapshot[11];
			items[0] = new SlotItemSnapshot(
				itemIndex: 0,
				instanceId: 8500,
				entityId: 8500,
				displayName: "핵심 공격 아티팩트",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 4,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 6,
				rarityTier: 1,
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
				symmetryGroupId: 50,
				priorityTier: ArtifactPriorityTier.Core,
				staticCategoryMask: 1u,
				attackDamageByLevel: new[] { 10f, 25f, 45f, 70f, 100f, 135f, 180f },
				statBonusPercentByLevel: new float[7]
			);
			items[1] = new SlotItemSnapshot(
				itemIndex: 1,
				instanceId: 8501,
				entityId: 8501,
				displayName: "북향의 금빛침",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 9,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 1,
				customWeight: 10,
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
				symmetryGroupId: 51,
				priorityTier: ArtifactPriorityTier.High
			);
			items[2] = new SlotItemSnapshot(
				itemIndex: 2,
				instanceId: 8502,
				entityId: 8502,
				displayName: "조화의 수정",
				kind: SlotItemKind.Artifact,
				initialSlotIndex: 8,
				initialRotation: 0,
				isRotatable: false,
				baseLevel: 1,
				maxLevel: 3,
				rarityTier: 2,
				customWeight: 12,
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
				symmetryGroupId: 52,
				priorityTier: ArtifactPriorityTier.High,
				isCrystalOfHarmony: true,
				crystalDamageBonusByLevel: new[] { 1f, 2f, 3f, 4f }
			);

			TabletPlacementRule[] rightPlus3 = BuildDirectionalRules(
				width,
				height,
				unlockedSlots,
				1,
				0,
				TabletEffectType.IncreaseConstLevel,
				3
			);
			items[3] = CreateTablet(3, 8503, 3, true, rightPlus3, 350);

			for (int i = 4; i < 11; i++) {
				int init = i - 4;
				if (init >= 3) {
					init += 2;
				}
				if (init >= 8) {
					init += 2;
				}
				items[i] = CreateStatArtifact(
					i,
					8500 + i,
					init,
					$"순환 유물 {i}",
					baseLevel: 1,
					maxLevel: 3,
					weight: 4,
					ArtifactPriorityTier.Normal,
					60 + i,
					new[] { 1f, 3f, 6f, 9f },
					i == 4 ? new[] { "MYSTIC" } : Array.Empty<string>(),
					i == 4 ? 1u : 0u
				);
			}

			OptimizerUserSettings userSettings = new OptimizerUserSettings();
			userSettings.SetPriority(8500, ArtifactPriorityTier.Core);
			userSettings.SetPriority(8501, ArtifactPriorityTier.High);
			userSettings.PreferredNeedleTargetInstanceId = 8500;

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
				items,
				new[] { new ComboCategoryInfo(0, "MYSTIC", "신비", 2, new[] { 2, 4 }, true) },
				TalentRuleSnapshot.Default,
				mooreNeighborMasks: GameInventoryReader.BuildMooreNeighborMasks(width, height, unlockedSlots),
				mysticSlot0: 0,
				mysticSlot1: 1,
				mysticTier1Threshold: 2,
				mysticTier2Threshold: 4
			).WithUserSettings(userSettings);

			OptimizationSolver solver = new OptimizationSolver();

			// When
			OptimizationResult result = solver.Solve(snapshot, 600);

			// Then
			Assert.False(result.UsedExactBranchAndBound);
			Assert.Equal(0, result.TopCandidates[0].Metrics.HardViolationCount);
			int coreSlot = result.BestItemToSlotIndex[0];
			Assert.True(coreSlot == 0 || coreSlot == 1);
			Assert.Equal(coreSlot + width, result.BestItemToSlotIndex[1]);
			Assert.True(result.BestSlotDetails[coreSlot].EffectiveLevel >= 4);
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

		private static SlotItemSnapshot CreateTablet(
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

		private static TabletPlacementRule[] BuildDirectionalRules(
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

		private static int ResolveSlot(int x, int y, int width, int height, int unlockedSlots) {
			if (x < 0 || y < 0 || x >= width || y >= height) {
				return -1;
			}
			int slot = (y * width) + x;
			return slot < unlockedSlots ? slot : -1;
		}

		private static (int X, int Y) RotateOffset(int dx, int dy, int rotation) {
			return (rotation & 3) switch {
				1 => (dy, -dx),
				2 => (-dx, -dy),
				3 => (-dy, dx),
				_ => (dx, dy)
			};
		}
	}
}
