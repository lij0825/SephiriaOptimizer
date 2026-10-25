using System;
using System.Diagnostics;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Solver {
	public sealed class OptimizationSolver {
		public const int SmallScaleMaxSlots = 12;
		public const int SmallScaleMaxItems = 10;
		public const int MaxBranchNodes = 40_000;
		public const int LahcHistoryLength = 256;
		public const int ReheatMargin = 650;
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
			bool usedExactBranchAndBound = false;
			int[] bestSlots = new int[unlockedSlots];
			Array.Copy(initialSlots, bestSlots, unlockedSlots);
			int[] bestRotations = new int[itemCount];
			Array.Copy(initialRotations, bestRotations, itemCount);
			int bestScore = initialScore;
			int evaluations = 1;

			if (isSmallScale) {
				bool limitExceeded = SolveBranchAndBound(
					snapshot,
					stopwatch,
					timeBudgetMs,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref evaluations
				);
				usedExactBranchAndBound = !limitExceeded;
				if (limitExceeded) {
					int remainingBudgetMs = Math.Max(50, timeBudgetMs - (int)stopwatch.ElapsedMilliseconds);
					SolveSimulatedAnnealing(
						snapshot,
						stopwatch,
						remainingBudgetMs,
						bestSlots,
						bestRotations,
						ref bestScore,
						ref evaluations
					);
				}
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
				usedExactBranchAndBound,
				evaluations,
				stopwatch.Elapsed.TotalMilliseconds
			);
		}

		private static bool SolveBranchAndBound(
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

			Span<int> workingSlots = stackalloc int[unlockedSlots];
			workingSlots.Fill(-1);
			Span<int> workingRotations = stackalloc int[itemCount];
			snapshot.InitialItemRotations.AsSpan(0, itemCount).CopyTo(workingRotations);

			Span<int> unpinnedItems = stackalloc int[itemCount];
			int unpinnedItemCount = 0;

			for (int i = 0; i < itemCount; i++) {
				int initSlot = items[i].InitialSlotIndex;
				if (initSlot >= 0 && initSlot < unlockedSlots && snapshot.PinnedSlots[initSlot]) {
					workingSlots[initSlot] = i;
				} else {
					unpinnedItems[unpinnedItemCount++] = i;
				}
			}

			Span<int> unpinnedSlots = stackalloc int[unlockedSlots];
			int unpinnedSlotCount = 0;
			for (int s = 0; s < unlockedSlots; s++) {
				if (!snapshot.PinnedSlots[s] && workingSlots[s] < 0) {
					unpinnedSlots[unpinnedSlotCount++] = s;
				}
			}

			if (unpinnedItemCount == 0 || unpinnedSlotCount == 0) {
				return false;
			}

			SortUnpinnedItemsForBranching(items, unpinnedItems, unpinnedItemCount);

			Span<int> itemMaxPotential = stackalloc int[unpinnedItemCount];
			for (int d = 0; d < unpinnedItemCount; d++) {
				SlotItemSnapshot item = items[unpinnedItems[d]];
				itemMaxPotential[d] = ComputeSingleItemMaxPotential(snapshot, item);
			}

			Span<int> suffixPotentialSum = stackalloc int[unpinnedItemCount + 1];
			suffixPotentialSum[unpinnedItemCount] = 0;
			for (int d = unpinnedItemCount - 1; d >= 0; d--) {
				suffixPotentialSum[d] = suffixPotentialSum[d + 1] + itemMaxPotential[d];
			}

			int currentPartialScore = ComputeGlobalComboAndPatternPotential(snapshot);
			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = workingSlots[s];
				if (itemIdx >= 0) {
					currentPartialScore += ComputeSingleItemMaxPotential(snapshot, items[itemIdx]);
				}
			}

			Span<int> assignedOrderedSlotIndex = stackalloc int[unpinnedItemCount];
			long deadlineTicks = stopwatch.ElapsedTicks + ((long)timeBudgetMs * Stopwatch.Frequency / 1000L);
			int branchNodesVisited = 0;
			bool limitExceeded = false;

			BranchAndBoundRecurse(
				snapshot,
				stopwatch,
				deadlineTicks,
				unpinnedItems.Slice(0, unpinnedItemCount),
				unpinnedSlots.Slice(0, unpinnedSlotCount),
				suffixPotentialSum,
				currentPartialScore,
				0,
				assignedOrderedSlotIndex,
				workingSlots,
				workingRotations,
				bestSlots,
				bestRotations,
				ref bestScore,
				ref evaluations,
				ref branchNodesVisited,
				ref limitExceeded
			);

			return limitExceeded;
		}

		private static void BranchAndBoundRecurse(
			InventorySnapshot snapshot,
			Stopwatch stopwatch,
			long deadlineTicks,
			ReadOnlySpan<int> unpinnedItems,
			ReadOnlySpan<int> unpinnedSlots,
			ReadOnlySpan<int> suffixPotentialSum,
			int currentPartialScore,
			int depth,
			Span<int> assignedOrderedSlotIndex,
			Span<int> workingSlots,
			Span<int> workingRotations,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int evaluations,
			ref int branchNodesVisited,
			ref bool limitExceeded
		) {
			if (limitExceeded) {
				return;
			}
			if (branchNodesVisited >= MaxBranchNodes) {
				limitExceeded = true;
				return;
			}
			if ((branchNodesVisited & 255) == 0 && stopwatch.ElapsedTicks >= deadlineTicks) {
				limitExceeded = true;
				return;
			}
			branchNodesVisited++;

			int unpinnedItemCount = unpinnedItems.Length;
			if (depth == unpinnedItemCount) {
				int score = ObjectiveEvaluator.Evaluate(snapshot, workingSlots, workingRotations);
				evaluations++;
				if (score > bestScore) {
					bestScore = score;
					workingSlots.CopyTo(bestSlots);
					workingRotations.CopyTo(bestRotations);
				}
				return;
			}

			int optimisticRemainingBound = suffixPotentialSum[depth];
			if (currentPartialScore + optimisticRemainingBound <= bestScore) {
				return;
			}

			int itemIdx = unpinnedItems[depth];
			SlotItemSnapshot item = snapshot.Items[itemIdx];
			int unpinnedSlotCount = unpinnedSlots.Length;

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
						int rotBoundContribution = ComputeTabletPlacementUpperContribution(
							snapshot,
							item,
							targetSlot,
							rot
						);
						if (currentPartialScore + rotBoundContribution + suffixPotentialSum[depth + 1] <= bestScore) {
							continue;
						}
						workingRotations[itemIdx] = rot;
						BranchAndBoundRecurse(
							snapshot,
							stopwatch,
							deadlineTicks,
							unpinnedItems,
							unpinnedSlots,
							suffixPotentialSum,
							currentPartialScore + rotBoundContribution,
							depth + 1,
							assignedOrderedSlotIndex,
							workingSlots,
							workingRotations,
							bestSlots,
							bestRotations,
							ref bestScore,
							ref evaluations,
							ref branchNodesVisited,
							ref limitExceeded
						);
						if (limitExceeded) {
							break;
						}
					}
					workingRotations[itemIdx] = item.InitialRotation;
				} else {
					int placementContribution = item.Kind == SlotItemKind.StoneTablet
						? ComputeTabletPlacementUpperContribution(snapshot, item, targetSlot, item.InitialRotation)
						: itemPotential;
					if (currentPartialScore + placementContribution + suffixPotentialSum[depth + 1] > bestScore) {
						workingRotations[itemIdx] = item.InitialRotation;
						BranchAndBoundRecurse(
							snapshot,
							stopwatch,
							deadlineTicks,
							unpinnedItems,
							unpinnedSlots,
							suffixPotentialSum,
							currentPartialScore + placementContribution,
							depth + 1,
							assignedOrderedSlotIndex,
							workingSlots,
							workingRotations,
							bestSlots,
							bestRotations,
							ref bestScore,
							ref evaluations,
							ref branchNodesVisited,
							ref limitExceeded
						);
					}
				}

				workingSlots[targetSlot] = -1;
				if (limitExceeded) {
					return;
				}
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

			Span<int> movableSlots = stackalloc int[unlockedSlots];
			int movableSlotCount = 0;
			for (int s = 0; s < unlockedSlots; s++) {
				if (!snapshot.PinnedSlots[s]) {
					movableSlots[movableSlotCount++] = s;
				}
			}

			Span<int> rotatableTablets = stackalloc int[itemCount];
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

			Span<int> currentSlots = stackalloc int[unlockedSlots];
			bestSlots.AsSpan(0, unlockedSlots).CopyTo(currentSlots);
			Span<int> currentRotations = stackalloc int[itemCount];
			bestRotations.AsSpan(0, itemCount).CopyTo(currentRotations);
			int currentScore = bestScore;

			Span<int> history = stackalloc int[LahcHistoryLength];
			history.Fill(currentScore);

			const double initialTemperature = 1600.0;
			const double minimumTemperature = 12.0;
			const double coolingFactor = 0.9965;
			const double reheatTemperature = 950.0;
			const int stagnationLimit = 1400;

			double temperature = initialTemperature;
			int iterationsSinceImprovement = 0;
			int iteration = 0;
			uint rngState = 0x9E3779B9u ^ (uint)unlockedSlots ^ ((uint)itemCount << 16);
			long deadlineTicks = stopwatch.ElapsedTicks + ((long)timeBudgetMs * Stopwatch.Frequency / 1000L);

			while (stopwatch.ElapsedTicks < deadlineTicks) {
				for (int step = 0; step < 64; step++) {
					uint mutationRoll = NextRandomUInt(ref rngState) % 100u;
					bool trySingleRotate = rotatableTabletCount > 0
						&& (movableSlotCount <= 1 || mutationRoll < 20u);

					if (trySingleRotate) {
						int randIdx = (int)(NextRandomUInt(ref rngState) % (uint)rotatableTabletCount);
						int tabletIdx = rotatableTablets[randIdx];
						int oldRotation = currentRotations[tabletIdx];
						int deltaRot = 1 + (int)(NextRandomUInt(ref rngState) % 3u);
						int newRotation = (oldRotation + deltaRot) & 3;
						currentRotations[tabletIdx] = newRotation;

						int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
						evaluations++;

						int histIdx = iteration & (LahcHistoryLength - 1);
						int histScore = history[histIdx];
						int scoreDelta = candidateScore - currentScore;
						iteration++;

						if (candidateScore >= currentScore
							|| candidateScore >= histScore
							|| AcceptWorseMove(scoreDelta, temperature, ref rngState)) {
							currentScore = candidateScore;
							history[histIdx] = currentScore > histScore
								? currentScore
								: ((histScore + currentScore) >> 1);
							if (candidateScore > bestScore) {
								bestScore = candidateScore;
								currentSlots.CopyTo(bestSlots);
								currentRotations.CopyTo(bestRotations);
								iterationsSinceImprovement = 0;
							} else {
								iterationsSinceImprovement++;
							}
						} else {
							currentRotations[tabletIdx] = oldRotation;
							iterationsSinceImprovement++;
						}
					} else if (movableSlotCount >= 3 && mutationRoll >= 85u) {
						int idxA = (int)(NextRandomUInt(ref rngState) % (uint)movableSlotCount);
						int idxB = (int)(NextRandomUInt(ref rngState) % (uint)(movableSlotCount - 1));
						if (idxB >= idxA) {
							idxB++;
						}
						int idxC = (int)(NextRandomUInt(ref rngState) % (uint)(movableSlotCount - 2));
						int minAB = idxA < idxB ? idxA : idxB;
						int maxAB = idxA < idxB ? idxB : idxA;
						if (idxC >= minAB) {
							idxC++;
						}
						if (idxC >= maxAB) {
							idxC++;
						}

						int slotA = movableSlots[idxA];
						int slotB = movableSlots[idxB];
						int slotC = movableSlots[idxC];
						int itemA = currentSlots[slotA];
						int itemB = currentSlots[slotB];
						int itemC = currentSlots[slotC];

						if (itemA < 0 && itemB < 0 && itemC < 0) {
							continue;
						}

						currentSlots[slotB] = itemA;
						currentSlots[slotC] = itemB;
						currentSlots[slotA] = itemC;

						int rotItemIdx = -1;
						int oldRotVal = 0;
						if ((NextRandomUInt(ref rngState) & 1u) != 0u) {
							if (itemA >= 0
								&& items[itemA].Kind == SlotItemKind.StoneTablet
								&& items[itemA].IsRotatable) {
								rotItemIdx = itemA;
							} else if (itemB >= 0
								&& items[itemB].Kind == SlotItemKind.StoneTablet
								&& items[itemB].IsRotatable) {
								rotItemIdx = itemB;
							} else if (itemC >= 0
								&& items[itemC].Kind == SlotItemKind.StoneTablet
								&& items[itemC].IsRotatable) {
								rotItemIdx = itemC;
							}
							if (rotItemIdx >= 0) {
								oldRotVal = currentRotations[rotItemIdx];
								int rotStep = 1 + (int)(NextRandomUInt(ref rngState) % 3u);
								currentRotations[rotItemIdx] = (oldRotVal + rotStep) & 3;
							}
						}

						int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
						evaluations++;

						int histIdx = iteration & (LahcHistoryLength - 1);
						int histScore = history[histIdx];
						int scoreDelta = candidateScore - currentScore;
						iteration++;

						if (candidateScore >= currentScore
							|| candidateScore >= histScore
							|| AcceptWorseMove(scoreDelta, temperature, ref rngState)) {
							currentScore = candidateScore;
							history[histIdx] = currentScore > histScore
								? currentScore
								: ((histScore + currentScore) >> 1);
							if (candidateScore > bestScore) {
								bestScore = candidateScore;
								currentSlots.CopyTo(bestSlots);
								currentRotations.CopyTo(bestRotations);
								iterationsSinceImprovement = 0;
							} else {
								iterationsSinceImprovement++;
							}
						} else {
							currentSlots[slotA] = itemA;
							currentSlots[slotB] = itemB;
							currentSlots[slotC] = itemC;
							if (rotItemIdx >= 0) {
								currentRotations[rotItemIdx] = oldRotVal;
							}
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

						int histIdx = iteration & (LahcHistoryLength - 1);
						int histScore = history[histIdx];
						int scoreDelta = candidateScore - currentScore;
						iteration++;

						if (candidateScore >= currentScore
							|| candidateScore >= histScore
							|| AcceptWorseMove(scoreDelta, temperature, ref rngState)) {
							currentScore = candidateScore;
							history[histIdx] = currentScore > histScore
								? currentScore
								: ((histScore + currentScore) >> 1);
							if (candidateScore > bestScore) {
								bestScore = candidateScore;
								currentSlots.CopyTo(bestSlots);
								currentRotations.CopyTo(bestRotations);
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
					bestSlots.AsSpan(0, unlockedSlots).CopyTo(currentSlots);
					bestRotations.AsSpan(0, itemCount).CopyTo(currentRotations);
					currentScore = bestScore;
					history.Fill(bestScore - ReheatMargin);
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

		private static int ComputeTabletPlacementUpperContribution(
			InventorySnapshot snapshot,
			SlotItemSnapshot tabletItem,
			int slotIndex,
			int rotation
		) {
			TabletPlacementRule rule = tabletItem.GetPlacementRule(slotIndex, rotation);
			PrecomputedEffect[] effects = rule.Effects;
			bool hasEngraving = snapshot.TalentRules.TabletEngravingCount > 0;
			int contribution = 0;

			for (int e = 0; e < effects.Length; e++) {
				PrecomputedEffect eff = effects[e];
				int targetSlot = eff.TargetSlotIndex;
				switch (eff.EffectType) {
					case TabletEffectType.IncreaseConstLevel: {
						if (targetSlot < 0) {
							if (eff.LevelParam > 0) {
								contribution -= eff.LevelParam * ObjectiveEvaluator.WastedPositiveOutOfBoundsPenalty;
							} else if (eff.LevelParam < 0) {
								contribution += (-eff.LevelParam) * ObjectiveEvaluator.NeutralizedNegativeLevelBonus;
								if (hasEngraving) {
									contribution += ObjectiveEvaluator.EngravingOutOfBoundsBonus;
								}
							}
						} else if (eff.LevelParam < 0) {
							contribution += (-eff.LevelParam) * ObjectiveEvaluator.NeutralizedNegativeLevelBonus;
						}
						break;
					}
					case TabletEffectType.Disable: {
						contribution += ObjectiveEvaluator.NeutralizedDisableBonus;
						if (targetSlot < 0 && hasEngraving) {
							contribution += ObjectiveEvaluator.EngravingOutOfBoundsBonus;
						}
						break;
					}
					case TabletEffectType.IgnoreCriteria: {
						if (targetSlot < 0) {
							contribution -= 180;
						}
						break;
					}
					case TabletEffectType.MultiplyConstLevel: {
						if (targetSlot < 0 && eff.LevelParam > 1) {
							contribution -= eff.LevelParam * 220;
						}
						break;
					}
				}
			}

			return contribution;
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
			Span<int> unpinnedItems,
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
