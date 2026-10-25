using System;
using System.Diagnostics;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Solver {
	public sealed class OptimizationSolver {
		public const int SmallScaleMaxSlots = 12;
		public const int SmallScaleMaxItems = 10;
		public const int DefaultTimeBudgetMs = 370;

		public OptimizationResult Solve(InventorySnapshot snapshot, int timeBudgetMs = DefaultTimeBudgetMs) {
			Stopwatch stopwatch = Stopwatch.StartNew();
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;

			int[] initialSlots = new int[unlockedSlots];
			Array.Copy(snapshot.InitialSlotToItemIndex, initialSlots, unlockedSlots);
			int[] initialRotations = new int[itemCount];
			Array.Copy(snapshot.InitialItemRotations, initialRotations, itemCount);

			int initialScore = ObjectiveEvaluator.Evaluate(snapshot, initialSlots, initialRotations);
			SlotEvaluationDetail[] initialDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				initialSlots,
				initialRotations
			);

			if (unlockedSlots <= 1 || itemCount == 0) {
				int[] initialItemSlots = BuildItemToSlotMap(initialSlots, itemCount);
				stopwatch.Stop();
				return new OptimizationResult(
					initialScore,
					initialScore,
					initialSlots,
					initialItemSlots,
					initialRotations,
					initialDetails,
					initialDetails,
					true,
					1,
					stopwatch.Elapsed.TotalMilliseconds
				);
			}

			bool isSmallScale = unlockedSlots <= SmallScaleMaxSlots && itemCount <= SmallScaleMaxItems;
			int[] bestSlots = new int[unlockedSlots];
			Array.Copy(initialSlots, bestSlots, unlockedSlots);
			int[] bestRotations = new int[itemCount];
			Array.Copy(initialRotations, bestRotations, itemCount);
			int bestScore = initialScore;
			int evaluations = 1;

			if (isSmallScale) {
				SolveBranchAndBound(
					snapshot,
					stopwatch,
					timeBudgetMs,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref evaluations
				);
			} else {
				SolveSimulatedAnnealing(
					snapshot,
					stopwatch,
					timeBudgetMs,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref evaluations
				);
			}

			RunDeterministicPolish(
				snapshot,
				bestSlots,
				bestRotations,
				ref bestScore,
				ref evaluations
			);

			int[] bestItemToSlot = BuildItemToSlotMap(bestSlots, itemCount);
			SlotEvaluationDetail[] bestDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				bestSlots,
				bestRotations
			);
			stopwatch.Stop();

			return new OptimizationResult(
				initialScore,
				bestScore,
				bestSlots,
				bestItemToSlot,
				bestRotations,
				initialDetails,
				bestDetails,
				isSmallScale,
				evaluations,
				stopwatch.Elapsed.TotalMilliseconds
			);
		}

		private static void SolveBranchAndBound(
			InventorySnapshot snapshot,
			Stopwatch stopwatch,
			int timeBudgetMs,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int evaluations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;

			int[] workingSlots = new int[unlockedSlots];
			Array.Fill(workingSlots, -1);
			int[] workingRotations = new int[itemCount];
			Array.Copy(snapshot.InitialItemRotations, workingRotations, itemCount);

			int[] unpinnedItems = new int[itemCount];
			int unpinnedItemCount = 0;

			for (int i = 0; i < itemCount; i++) {
				int initSlot = items[i].InitialSlotIndex;
				if (initSlot >= 0 && initSlot < unlockedSlots && snapshot.PinnedSlots[initSlot]) {
					workingSlots[initSlot] = i;
				} else {
					unpinnedItems[unpinnedItemCount++] = i;
				}
			}

			int[] unpinnedSlots = new int[unlockedSlots];
			int unpinnedSlotCount = 0;
			for (int s = 0; s < unlockedSlots; s++) {
				if (!snapshot.PinnedSlots[s] && workingSlots[s] < 0) {
					unpinnedSlots[unpinnedSlotCount++] = s;
				}
			}

			if (unpinnedItemCount == 0 || unpinnedSlotCount == 0) {
				return;
			}

			SortUnpinnedItemsForBranching(items, unpinnedItems, unpinnedItemCount);

			int[] itemMaxPotential = new int[unpinnedItemCount];
			for (int d = 0; d < unpinnedItemCount; d++) {
				SlotItemSnapshot item = items[unpinnedItems[d]];
				itemMaxPotential[d] = ComputeSingleItemMaxPotential(snapshot, item);
			}

			int[] suffixPotentialSum = new int[unpinnedItemCount + 1];
			for (int d = unpinnedItemCount - 1; d >= 0; d--) {
				suffixPotentialSum[d] = suffixPotentialSum[d + 1] + itemMaxPotential[d];
			}

			int pinnedBasePotential = ComputeGlobalComboAndPatternPotential(snapshot);
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = workingSlots[s];
				if (itemIdx >= 0) {
					pinnedBasePotential += ComputeSingleItemMaxPotential(snapshot, items[itemIdx]);
				}
			}

			int[] assignedOrderedSlotIndex = new int[unpinnedItemCount];
			long deadlineTicks = stopwatch.ElapsedTicks + ((long)timeBudgetMs * Stopwatch.Frequency / 1000L);

			BranchAndBoundRecurse(
				snapshot,
				stopwatch,
				deadlineTicks,
				unpinnedItems,
				unpinnedItemCount,
				unpinnedSlots,
				unpinnedSlotCount,
				suffixPotentialSum,
				pinnedBasePotential,
				0,
				assignedOrderedSlotIndex,
				workingSlots,
				workingRotations,
				bestSlots,
				bestRotations,
				ref bestScore,
				ref evaluations
			);
		}

		private static void BranchAndBoundRecurse(
			InventorySnapshot snapshot,
			Stopwatch stopwatch,
			long deadlineTicks,
			int[] unpinnedItems,
			int unpinnedItemCount,
			int[] unpinnedSlots,
			int unpinnedSlotCount,
			int[] suffixPotentialSum,
			int accumulatedPotential,
			int depth,
			int[] assignedOrderedSlotIndex,
			int[] workingSlots,
			int[] workingRotations,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int evaluations
		) {
			if ((evaluations & 255) == 0 && stopwatch.ElapsedTicks >= deadlineTicks) {
				return;
			}

			if (depth == unpinnedItemCount) {
				int score = ObjectiveEvaluator.Evaluate(snapshot, workingSlots, workingRotations);
				evaluations++;
				if (score > bestScore) {
					bestScore = score;
					Array.Copy(workingSlots, bestSlots, snapshot.UnlockedSlots);
					Array.Copy(workingRotations, bestRotations, snapshot.Items.Length);
				}
				return;
			}

			int optimisticUpperBound = accumulatedPotential + suffixPotentialSum[depth];
			if (optimisticUpperBound <= bestScore) {
				return;
			}

			int itemIdx = unpinnedItems[depth];
			SlotItemSnapshot item = snapshot.Items[itemIdx];

			int minSlotOrderIdx = 0;
			if (depth > 0) {
				int prevItemIdx = unpinnedItems[depth - 1];
				if (snapshot.Items[prevItemIdx].SymmetryGroupId == item.SymmetryGroupId) {
					minSlotOrderIdx = assignedOrderedSlotIndex[depth - 1] + 1;
				}
			}

			int remainingSameGroupCount = 0;
			for (int nextDepth = depth + 1; nextDepth < unpinnedItemCount; nextDepth++) {
				if (snapshot.Items[unpinnedItems[nextDepth]].SymmetryGroupId == item.SymmetryGroupId) {
					remainingSameGroupCount++;
				} else {
					break;
				}
			}

			int maxSlotOrderIdx = unpinnedSlotCount - 1 - remainingSameGroupCount;
			int itemPotential = suffixPotentialSum[depth] - suffixPotentialSum[depth + 1];

			for (int orderIdx = minSlotOrderIdx; orderIdx <= maxSlotOrderIdx; orderIdx++) {
				int targetSlot = unpinnedSlots[orderIdx];
				if (workingSlots[targetSlot] >= 0) {
					continue;
				}

				assignedOrderedSlotIndex[depth] = orderIdx;
				workingSlots[targetSlot] = itemIdx;

				if (item.Kind == SlotItemKind.StoneTablet && item.IsRotatable) {
					for (int rot = 0; rot < 4; rot++) {
						if (IsRedundantTabletRotation(item, targetSlot, rot)) {
							continue;
						}
						workingRotations[itemIdx] = rot;
						BranchAndBoundRecurse(
							snapshot,
							stopwatch,
							deadlineTicks,
							unpinnedItems,
							unpinnedItemCount,
							unpinnedSlots,
							unpinnedSlotCount,
							suffixPotentialSum,
							accumulatedPotential + itemPotential,
							depth + 1,
							assignedOrderedSlotIndex,
							workingSlots,
							workingRotations,
							bestSlots,
							bestRotations,
							ref bestScore,
							ref evaluations
						);
					}
					workingRotations[itemIdx] = item.InitialRotation;
				} else {
					workingRotations[itemIdx] = item.InitialRotation;
					BranchAndBoundRecurse(
						snapshot,
						stopwatch,
						deadlineTicks,
						unpinnedItems,
						unpinnedItemCount,
						unpinnedSlots,
						unpinnedSlotCount,
						suffixPotentialSum,
						accumulatedPotential + itemPotential,
						depth + 1,
						assignedOrderedSlotIndex,
						workingSlots,
						workingRotations,
						bestSlots,
						bestRotations,
						ref bestScore,
						ref evaluations
					);
				}

				workingSlots[targetSlot] = -1;
			}
		}

		private static void SolveSimulatedAnnealing(
			InventorySnapshot snapshot,
			Stopwatch stopwatch,
			int timeBudgetMs,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int evaluations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;

			int[] movableSlots = new int[unlockedSlots];
			int movableSlotCount = 0;
			for (int s = 0; s < unlockedSlots; s++) {
				if (!snapshot.PinnedSlots[s]) {
					movableSlots[movableSlotCount++] = s;
				}
			}

			int[] rotatableTablets = new int[itemCount];
			int rotatableTabletCount = 0;
			for (int i = 0; i < itemCount; i++) {
				int initSlot = items[i].InitialSlotIndex;
				bool isPinned = initSlot >= 0 && initSlot < unlockedSlots && snapshot.PinnedSlots[initSlot];
				if (!isPinned && items[i].Kind == SlotItemKind.StoneTablet && items[i].IsRotatable) {
					rotatableTablets[rotatableTabletCount++] = i;
				}
			}

			if (movableSlotCount <= 1 && rotatableTabletCount == 0) {
				return;
			}

			int[] currentSlots = new int[unlockedSlots];
			Array.Copy(bestSlots, currentSlots, unlockedSlots);
			int[] currentRotations = new int[itemCount];
			Array.Copy(bestRotations, currentRotations, itemCount);
			int currentScore = bestScore;

			const double initialTemperature = 750.0;
			const double minimumTemperature = 8.0;
			const double coolingFactor = 0.9965;
			const double reheatTemperature = 320.0;
			const int stagnationLimit = 1400;

			double temperature = initialTemperature;
			int iterationsSinceImprovement = 0;
			uint rngState = 0x9E3779B9u ^ (uint)unlockedSlots ^ ((uint)itemCount << 16);
			long deadlineTicks = stopwatch.ElapsedTicks + ((long)timeBudgetMs * Stopwatch.Frequency / 1000L);

			while (stopwatch.ElapsedTicks < deadlineTicks) {
				for (int step = 0; step < 64; step++) {
					bool tryRotate = rotatableTabletCount > 0
						&& (movableSlotCount <= 1 || (NextRandomUInt(ref rngState) & 3u) == 0u);

					if (tryRotate) {
						int tabletIdx = rotatableTablets[NextRandomUInt(ref rngState) % (uint)rotatableTabletCount];
						int oldRotation = currentRotations[tabletIdx];
						int deltaRot = 1 + (int)(NextRandomUInt(ref rngState) % 3u);
						int newRotation = (oldRotation + deltaRot) & 3;
						currentRotations[tabletIdx] = newRotation;

						int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
						evaluations++;
						int scoreDelta = candidateScore - currentScore;

						if (scoreDelta >= 0 || AcceptWorseMove(scoreDelta, temperature, ref rngState)) {
							currentScore = candidateScore;
							if (candidateScore > bestScore) {
								bestScore = candidateScore;
								Array.Copy(currentSlots, bestSlots, unlockedSlots);
								Array.Copy(currentRotations, bestRotations, itemCount);
								iterationsSinceImprovement = 0;
							} else {
								iterationsSinceImprovement++;
							}
						} else {
							currentRotations[tabletIdx] = oldRotation;
							iterationsSinceImprovement++;
						}
					} else if (movableSlotCount >= 2) {
						int idxA = (int)(NextRandomUInt(ref rngState) % (uint)movableSlotCount);
						int idxB = (int)(NextRandomUInt(ref rngState) % (uint)(movableSlotCount - 1));
						if (idxB >= idxA) {
							idxB++;
						}

						int slotA = movableSlots[idxA];
						int slotB = movableSlots[idxB];
						int itemA = currentSlots[slotA];
						int itemB = currentSlots[slotB];

						if (itemA == itemB) {
							continue;
						}
						if (itemA >= 0 && itemB >= 0 && items[itemA].SymmetryGroupId == items[itemB].SymmetryGroupId) {
							continue;
						}

						currentSlots[slotA] = itemB;
						currentSlots[slotB] = itemA;

						int rotItemIdx = -1;
						int oldRotVal = 0;
						if ((NextRandomUInt(ref rngState) & 1u) != 0u) {
							bool canRotA = itemA >= 0
								&& items[itemA].Kind == SlotItemKind.StoneTablet
								&& items[itemA].IsRotatable;
							if (canRotA) {
								rotItemIdx = itemA;
							} else if (
								itemB >= 0
								&& items[itemB].Kind == SlotItemKind.StoneTablet
								&& items[itemB].IsRotatable
							) {
								rotItemIdx = itemB;
							}
							if (rotItemIdx >= 0) {
								oldRotVal = currentRotations[rotItemIdx];
								int rotStep = 1 + (int)(NextRandomUInt(ref rngState) % 3u);
								currentRotations[rotItemIdx] = (oldRotVal + rotStep) & 3;
							}
						}

						int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
						evaluations++;
						int scoreDelta = candidateScore - currentScore;

						if (scoreDelta >= 0 || AcceptWorseMove(scoreDelta, temperature, ref rngState)) {
							currentScore = candidateScore;
							if (candidateScore > bestScore) {
								bestScore = candidateScore;
								Array.Copy(currentSlots, bestSlots, unlockedSlots);
								Array.Copy(currentRotations, bestRotations, itemCount);
								iterationsSinceImprovement = 0;
							} else {
								iterationsSinceImprovement++;
							}
						} else {
							currentSlots[slotA] = itemA;
							currentSlots[slotB] = itemB;
							if (rotItemIdx >= 0) {
								currentRotations[rotItemIdx] = oldRotVal;
							}
							iterationsSinceImprovement++;
						}
					}
				}

				temperature *= coolingFactor;
				if (temperature < minimumTemperature) {
					temperature = minimumTemperature;
				}

				if (iterationsSinceImprovement >= stagnationLimit) {
					Array.Copy(bestSlots, currentSlots, unlockedSlots);
					Array.Copy(bestRotations, currentRotations, itemCount);
					currentScore = bestScore;
					temperature = reheatTemperature;
					iterationsSinceImprovement = 0;
				}
			}
		}

		private static void RunDeterministicPolish(
			InventorySnapshot snapshot,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int evaluations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;

			bool improved = true;
			int passCount = 0;

			while (improved && passCount < 4) {
				improved = false;
				passCount++;

				for (int i = 0; i < itemCount; i++) {
					SlotItemSnapshot item = items[i];
					if (item.Kind != SlotItemKind.StoneTablet || !item.IsRotatable) {
						continue;
					}
					int currentRot = bestRotations[i];
					int bestRotForItem = currentRot;
					for (int rot = 0; rot < 4; rot++) {
						if (rot == currentRot) {
							continue;
						}
						bestRotations[i] = rot;
						int score = ObjectiveEvaluator.Evaluate(snapshot, bestSlots, bestRotations);
						evaluations++;
						if (score > bestScore) {
							bestScore = score;
							bestRotForItem = rot;
							improved = true;
						}
					}
					bestRotations[i] = bestRotForItem;
				}

				for (int sA = 0; sA < unlockedSlots; sA++) {
					if (snapshot.PinnedSlots[sA]) {
						continue;
					}
					for (int sB = sA + 1; sB < unlockedSlots; sB++) {
						if (snapshot.PinnedSlots[sB]) {
							continue;
						}
						int itemA = bestSlots[sA];
						int itemB = bestSlots[sB];
						if (itemA == itemB) {
							continue;
						}
						bestSlots[sA] = itemB;
						bestSlots[sB] = itemA;

						bool rotA = itemA >= 0
							&& items[itemA].Kind == SlotItemKind.StoneTablet
							&& items[itemA].IsRotatable;
						bool rotB = itemB >= 0
							&& items[itemB].Kind == SlotItemKind.StoneTablet
							&& items[itemB].IsRotatable;

						if (rotA || rotB) {
							int rotTarget = rotA ? itemA : itemB;
							int savedRot = bestRotations[rotTarget];
							int bestSwapRot = savedRot;
							bool swapImproved = false;

							for (int r = 0; r < 4; r++) {
								bestRotations[rotTarget] = r;
								int score = ObjectiveEvaluator.Evaluate(snapshot, bestSlots, bestRotations);
								evaluations++;
								if (score > bestScore) {
									bestScore = score;
									bestSwapRot = r;
									swapImproved = true;
									improved = true;
								}
							}

							if (swapImproved) {
								bestRotations[rotTarget] = bestSwapRot;
							} else {
								bestRotations[rotTarget] = savedRot;
								bestSlots[sA] = itemA;
								bestSlots[sB] = itemB;
							}
						} else {
							int score = ObjectiveEvaluator.Evaluate(snapshot, bestSlots, bestRotations);
							evaluations++;
							if (score > bestScore) {
								bestScore = score;
								improved = true;
							} else {
								bestSlots[sA] = itemA;
								bestSlots[sB] = itemB;
							}
						}
					}
				}
			}
		}

		private static int ComputeSingleItemMaxPotential(InventorySnapshot snapshot, SlotItemSnapshot item) {
			if (item.Kind == SlotItemKind.Artifact) {
				int scaledWeight = Math.Max(1, ObjectiveEvaluator.GetPriorityScaledWeight(item));
				int maxBonus = item.PriorityTier == ArtifactPriorityTier.Core
					? (ObjectiveEvaluator.MaxLevelReachedBonus * 2)
					: ObjectiveEvaluator.MaxLevelReachedBonus;
				int maxLvlScore = (ObjectiveEvaluator.BaseActiveArtifactScore
					+ (item.MaxLevel * ObjectiveEvaluator.EffectiveLevelScoreMultiplier)
					+ maxBonus
					+ ObjectiveEvaluator.IgnoreConstraintSynergyBonus
					+ (ObjectiveEvaluator.ConnectedCharmSynergyBonus * 4)) * scaledWeight;

				if (item.HasDependencyTarget) {
					maxLvlScore += 18000;
				}
				if (item.IsElementalByRowCharm || item.IsWhitePaperCharm || item.IsNearLevelDamageCharm) {
					maxLvlScore += 5000;
				}
				return maxLvlScore + 100;
			}
			if (item.Kind == SlotItemKind.StoneTablet) {
				int bestEffectBonus = 0;
				bool hasEngraving = snapshot.TalentRules.TabletEngravingCount > 0;
				TabletPlacementRule[] rules = item.PlacementRules;
				for (int r = 0; r < rules.Length; r++) {
					PrecomputedEffect[] effects = rules[r].Effects;
					int ruleBonus = 0;
					for (int e = 0; e < effects.Length; e++) {
						PrecomputedEffect eff = effects[e];
						if (eff.EffectType == TabletEffectType.IncreaseConstLevel && eff.LevelParam < 0) {
							ruleBonus += (-eff.LevelParam) * ObjectiveEvaluator.NeutralizedNegativeLevelBonus;
							if (hasEngraving) {
								ruleBonus += ObjectiveEvaluator.EngravingOutOfBoundsBonus;
							}
						} else if (eff.EffectType == TabletEffectType.Disable) {
							ruleBonus += ObjectiveEvaluator.NeutralizedDisableBonus;
							if (hasEngraving) {
								ruleBonus += ObjectiveEvaluator.EngravingOutOfBoundsBonus;
							}
						}
					}
					if (ruleBonus > bestEffectBonus) {
						bestEffectBonus = ruleBonus;
					}
				}
				return bestEffectBonus;
			}
			return 0;
		}

		private static int ComputeGlobalComboAndPatternPotential(InventorySnapshot snapshot) {
			int potential = 2000;
			ComboCategoryInfo[] cats = snapshot.ComboCategories;
			for (int i = 0; i < cats.Length; i++) {
				potential += (cats[i].Breakpoints.Length + 4) * ObjectiveEvaluator.TargetComboDynamicStackBonus * 2;
			}
			if (snapshot.TalentRules.ArrangementBonusStat > 0) {
				potential += snapshot.TalentRules.ArrangementPatterns.Length
					* ObjectiveEvaluator.ArrangementPatternMatchBonus;
			}
			return potential;
		}

		private static void SortUnpinnedItemsForBranching(
			SlotItemSnapshot[] items,
			int[] unpinnedItems,
			int count
		) {
			for (int i = 1; i < count; i++) {
				int keyItemIdx = unpinnedItems[i];
				int keyPriority = GetBranchingPriority(items[keyItemIdx]);
				int keySymGroup = items[keyItemIdx].SymmetryGroupId;
				int j = i - 1;

				while (j >= 0) {
					int cmpItemIdx = unpinnedItems[j];
					int cmpPriority = GetBranchingPriority(items[cmpItemIdx]);
					int cmpSymGroup = items[cmpItemIdx].SymmetryGroupId;
					bool shouldSwap = cmpPriority < keyPriority
						|| (cmpPriority == keyPriority && cmpSymGroup > keySymGroup);
					if (!shouldSwap) {
						break;
					}
					unpinnedItems[j + 1] = unpinnedItems[j];
					j--;
				}
				unpinnedItems[j + 1] = keyItemIdx;
			}
		}

		private static int GetBranchingPriority(SlotItemSnapshot item) {
			if (item.Kind == SlotItemKind.StoneTablet) {
				int effectLen = item.PlacementRules.Length > 0 ? item.PlacementRules[0].Effects.Length : 0;
				return 10000 + (effectLen * 50) + (item.IsRotatable ? 20 : 0);
			}
			if (item.Kind == SlotItemKind.Artifact) {
				int constraintBonus = item.ConstraintType != ArtifactConstraintType.None ? 500 : 0;
				int priorityBonus = (int)item.PriorityTier * 200;
				return 1000 + constraintBonus + priorityBonus + (item.CustomWeight * 25) + (item.MaxLevel * 10);
			}
			return 0;
		}

		private static bool IsRedundantTabletRotation(SlotItemSnapshot item, int slotIndex, int rotation) {
			if (rotation == 0) {
				return false;
			}
			TabletPlacementRule currentRule = item.GetPlacementRule(slotIndex, rotation);
			for (int prevRot = 0; prevRot < rotation; prevRot++) {
				TabletPlacementRule prevRule = item.GetPlacementRule(slotIndex, prevRot);
				if (ArePlacementRulesIdentical(currentRule, prevRule)) {
					return true;
				}
			}
			return false;
		}

		private static bool ArePlacementRulesIdentical(TabletPlacementRule first, TabletPlacementRule second) {
			if (first.Conditions.Length != second.Conditions.Length || first.Effects.Length != second.Effects.Length) {
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
				if (effA.TargetSlotIndex != effA.TargetSlotIndex
					|| effA.EffectType != effB.EffectType
					|| effA.LevelParam != effB.LevelParam) {
					return false;
				}
			}
			return true;
		}

		private static bool AcceptWorseMove(int scoreDelta, double temperature, ref uint rngState) {
			if (temperature <= 0.0001) {
				return false;
			}
			double probability = Math.Exp(scoreDelta / temperature);
			double randomUnit = (NextRandomUInt(ref rngState) & 0xFFFFFFu) / (double)0x1000000u;
			return randomUnit < probability;
		}

		private static uint NextRandomUInt(ref uint state) {
			uint x = state;
			x ^= x << 13;
			x ^= x >> 17;
			x ^= x << 5;
			state = x;
			return x;
		}

		private static int[] BuildItemToSlotMap(int[] slotToItemIndex, int itemCount) {
			int[] itemToSlot = new int[itemCount];
			Array.Fill(itemToSlot, -1);
			for (int s = 0; s < slotToItemIndex.Length; s++) {
				int itemIdx = slotToItemIndex[s];
				if (itemIdx >= 0 && itemIdx < itemCount) {
					itemToSlot[itemIdx] = s;
				}
			}
			return itemToSlot;
		}
	}
}
