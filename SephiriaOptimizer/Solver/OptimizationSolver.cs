using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Solver {
	public sealed class OptimizationSolver {
		public const int SmallScaleMaxSlots = 12;
		public const int SmallScaleMaxItems = 10;
		public const int MaxBranchNodes = 40_000;
		public const int LahcHistoryLength = 256;
		public const int InitialLahcMargin = 1800;
		public const int ReheatMargin = 2200;
		public const int DefaultTimeBudgetMs = 370;

		public OptimizationResult Solve(InventorySnapshot snapshot, int timeBudgetMs = DefaultTimeBudgetMs) {
			Stopwatch stopwatch = Stopwatch.StartNew();
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;

			int[] initialSlots = new int[unlockedSlots];
			Array.Copy(snapshot.InitialSlotToItemIndex, initialSlots, unlockedSlots);
			int[] initialRotations = new int[itemCount];
			Array.Copy(snapshot.InitialItemRotations, initialRotations, itemCount);

			int initialScore = ObjectiveEvaluator.EvaluateWithDps(
				snapshot,
				initialSlots,
				initialRotations,
				out int initialEstimatedDps
			);
			SlotEvaluationDetail[] initialDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				initialSlots,
				initialRotations
			);

			DpsBreakdown currentBreakdown = snapshot.CurrentBreakdown;

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
					stopwatch.Elapsed.TotalMilliseconds,
					initialEstimatedDps,
					initialEstimatedDps,
					currentBreakdown,
					currentBreakdown
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

			int bestEstimatedDps = ObjectiveEvaluator.CalculateEstimatedDummyDps(
				snapshot,
				bestSlots,
				bestRotations
			);

			RunDeterministicPolish(
				snapshot,
				bestSlots,
				bestRotations,
				ref bestScore,
				ref bestEstimatedDps,
				ref evaluations
			);

			bestScore = ObjectiveEvaluator.EvaluateWithDps(
				snapshot,
				bestSlots,
				bestRotations,
				out bestEstimatedDps
			);

			int[] bestItemToSlot = BuildItemToSlotMap(bestSlots, itemCount);
			SlotEvaluationDetail[] bestDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				bestSlots,
				bestRotations
			);
			DpsBreakdown optimizedBreakdown = DpsEstimator.CalculateBreakdown(
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
				stopwatch.Elapsed.TotalMilliseconds,
				initialEstimatedDps,
				bestEstimatedDps,
				currentBreakdown,
				optimizedBreakdown
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
			int width = snapshot.Width;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			Span<int> movableSlots = stackalloc int[unlockedSlots];
			int movableSlotCount = 0;
			for (int s = 0; s < unlockedSlots; s++) {
				if (!pinnedSlots[s]) {
					movableSlots[movableSlotCount++] = s;
				}
			}

			Span<int> movableVerticalTopSlots = stackalloc int[unlockedSlots];
			int movableVerticalPairCount = 0;
			for (int s = 0; s + width < unlockedSlots; s++) {
				if (!pinnedSlots[s] && !pinnedSlots[s + width]) {
					movableVerticalTopSlots[movableVerticalPairCount++] = s;
				}
			}

			Span<int> rotatableTablets = stackalloc int[itemCount];
			int rotatableTabletCount = 0;
			Span<int> needleItems = stackalloc int[itemCount];
			int needleItemCount = 0;
			Span<int> attackableItems = stackalloc int[itemCount];
			int attackableItemCount = 0;

			for (int i = 0; i < itemCount; i++) {
				SlotItemSnapshot item = items[i];
				int initSlot = item.InitialSlotIndex;
				bool isPinned = initSlot >= 0 && initSlot < unlockedSlots && pinnedSlots[initSlot];

				if (!isPinned && item.Kind == SlotItemKind.StoneTablet && item.IsRotatable) {
					rotatableTablets[rotatableTabletCount++] = i;
				}
				if (!isPinned && item.Kind == SlotItemKind.Artifact && item.HasDependencyTarget) {
					needleItems[needleItemCount++] = i;
				}
				if (item.Kind == SlotItemKind.Artifact
					&& item.IsAttackableCharm
					&& item.PriorityTier != ArtifactPriorityTier.Sacrifice) {
					attackableItems[attackableItemCount++] = i;
				}
			}

			if (movableSlotCount <= 1 && rotatableTabletCount == 0) {
				return;
			}

			SortAttackableItemsByTargetPriority(snapshot, items, attackableItems, attackableItemCount);

			Span<int> currentSlots = stackalloc int[unlockedSlots];
			bestSlots.AsSpan(0, unlockedSlots).CopyTo(currentSlots);
			Span<int> currentRotations = stackalloc int[itemCount];
			bestRotations.AsSpan(0, itemCount).CopyTo(currentRotations);
			Span<int> currentItemToSlot = stackalloc int[itemCount];
			SyncItemToSlot(currentSlots, currentItemToSlot);
			int currentScore = bestScore;

			Span<int> history = stackalloc int[LahcHistoryLength];
			history.Fill(currentScore - InitialLahcMargin);

			const double initialTemperature = 2200.0;
			const double minimumTemperature = 15.0;
			const double coolingFactor = 0.9965;
			const double reheatTemperature = 1350.0;
			const int stagnationLimit = 1400;

			double temperature = initialTemperature;
			int iterationsSinceImprovement = 0;
			int iteration = 0;
			uint rngState = 0x9E3779B9u ^ (uint)unlockedSlots ^ ((uint)itemCount << 16);
			long deadlineTicks = stopwatch.ElapsedTicks + ((long)timeBudgetMs * Stopwatch.Frequency / 1000L);
			bool hasNeedleMoves = needleItemCount > 0 && attackableItemCount > 0 && unlockedSlots > width;

			while (stopwatch.ElapsedTicks < deadlineTicks) {
				for (int step = 0; step < 64; step++) {
					uint mutationRoll = NextRandomUInt(ref rngState) % 100u;
					bool trySingleRotate = rotatableTabletCount > 0
						&& (movableSlotCount <= 1 || mutationRoll < 18u);

					if (trySingleRotate) {
						int randIdx = (int)(NextRandomUInt(ref rngState) % (uint)rotatableTabletCount);
						int tabletIdx = rotatableTablets[randIdx];
						int oldRotation = currentRotations[tabletIdx];
						int deltaRot = 1 + (int)(NextRandomUInt(ref rngState) % 3u);
						int newRotation = (oldRotation + deltaRot) & 3;
						currentRotations[tabletIdx] = newRotation;

						int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
						evaluations++;

						if (TryAcceptCandidate(
							candidateScore,
							ref currentScore,
							ref bestScore,
							ref iteration,
							ref iterationsSinceImprovement,
							temperature,
							ref rngState,
							history,
							currentSlots,
							currentRotations,
							bestSlots,
							bestRotations
						)) {
							continue;
						}
						currentRotations[tabletIdx] = oldRotation;
						iterationsSinceImprovement++;
					} else if (hasNeedleMoves && mutationRoll >= 18u && mutationRoll < 36u) {
						int randNeedleIdx = (int)(NextRandomUInt(ref rngState) % (uint)needleItemCount);
						int needleItemIdx = needleItems[randNeedleIdx];
						int needleSlot = currentItemToSlot[needleItemIdx];
						if (needleSlot < 0 || pinnedSlots[needleSlot]) {
							continue;
						}

						int aboveSlot = needleSlot - width;
						int aboveItemIdx = aboveSlot >= 0 ? currentSlots[aboveSlot] : -1;
						bool isPairedWithAttackable = aboveSlot >= 0
							&& aboveItemIdx >= 0
							&& items[aboveItemIdx].Kind == SlotItemKind.Artifact
							&& items[aboveItemIdx].IsAttackableCharm
							&& items[aboveItemIdx].PriorityTier != ArtifactPriorityTier.Sacrifice;

						bool canCoupledMove = isPairedWithAttackable
							&& !pinnedSlots[aboveSlot]
							&& movableVerticalPairCount >= 2;
						bool chooseCoupledMove = canCoupledMove
							&& (attackableItemCount == 1 || (NextRandomUInt(ref rngState) % 4u) != 0u);

						if (chooseCoupledMove) {
							int destPairIdx = (int)(NextRandomUInt(ref rngState) % (uint)movableVerticalPairCount);
							int destSlot = movableVerticalTopSlots[destPairIdx];
							if (destSlot == aboveSlot) {
								destSlot = movableVerticalTopSlots[(destPairIdx + 1) % movableVerticalPairCount];
							}
							if (destSlot == aboveSlot) {
								continue;
							}

							int destBelowSlot = destSlot + width;
							ExecuteVerticalPairMutation(
								snapshot,
								aboveSlot,
								needleSlot,
								destSlot,
								destBelowSlot,
								currentSlots,
								currentRotations,
								currentItemToSlot,
								history,
								bestSlots,
								bestRotations,
								temperature,
								ref rngState,
								ref currentScore,
								ref bestScore,
								ref evaluations,
								ref iteration,
								ref iterationsSinceImprovement
							);
						} else {
							int topPool = attackableItemCount > 2 ? 2 : attackableItemCount;
							int chosenRank = ((NextRandomUInt(ref rngState) & 3u) != 0u)
								? (int)(NextRandomUInt(ref rngState) % (uint)topPool)
								: (int)(NextRandomUInt(ref rngState) % (uint)attackableItemCount);
							int targetAttackItemIdx = attackableItems[chosenRank];
							if (targetAttackItemIdx == needleItemIdx) {
								continue;
							}

							int targetSlot = currentItemToSlot[targetAttackItemIdx];
							if (targetSlot < 0) {
								continue;
							}

							int belowSlot = targetSlot + width;
							if (belowSlot < unlockedSlots) {
								if (pinnedSlots[belowSlot] || belowSlot == needleSlot || targetSlot == needleSlot) {
									continue;
								}
								int displacedItem = currentSlots[belowSlot];
								currentSlots[belowSlot] = needleItemIdx;
								currentSlots[needleSlot] = displacedItem;

								int candidateScore = ObjectiveEvaluator.Evaluate(
									snapshot,
									currentSlots,
									currentRotations
								);
								evaluations++;

								if (TryAcceptCandidate(
									candidateScore,
									ref currentScore,
									ref bestScore,
									ref iteration,
									ref iterationsSinceImprovement,
									temperature,
									ref rngState,
									history,
									currentSlots,
									currentRotations,
									bestSlots,
									bestRotations
								)) {
									currentItemToSlot[needleItemIdx] = belowSlot;
									if (displacedItem >= 0) {
										currentItemToSlot[displacedItem] = needleSlot;
									}
								} else {
									currentSlots[needleSlot] = needleItemIdx;
									currentSlots[belowSlot] = displacedItem;
									iterationsSinceImprovement++;
								}
							} else if (!pinnedSlots[targetSlot] && movableVerticalPairCount > 0) {
								int liftIdx = (int)(NextRandomUInt(ref rngState) % (uint)movableVerticalPairCount);
								int destTopSlot = movableVerticalTopSlots[liftIdx];
								if (destTopSlot == targetSlot) {
									continue;
								}
								int displacedTopItem = currentSlots[destTopSlot];
								currentSlots[destTopSlot] = targetAttackItemIdx;
								currentSlots[targetSlot] = displacedTopItem;

								int candidateScore = ObjectiveEvaluator.Evaluate(
									snapshot,
									currentSlots,
									currentRotations
								);
								evaluations++;

								if (TryAcceptCandidate(
									candidateScore,
									ref currentScore,
									ref bestScore,
									ref iteration,
									ref iterationsSinceImprovement,
									temperature,
									ref rngState,
									history,
									currentSlots,
									currentRotations,
									bestSlots,
									bestRotations
								)) {
									currentItemToSlot[targetAttackItemIdx] = destTopSlot;
									if (displacedTopItem >= 0) {
										currentItemToSlot[displacedTopItem] = targetSlot;
									}
								} else {
									currentSlots[targetSlot] = targetAttackItemIdx;
									currentSlots[destTopSlot] = displacedTopItem;
									iterationsSinceImprovement++;
								}
							}
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

						if (TryAcceptCandidate(
							candidateScore,
							ref currentScore,
							ref bestScore,
							ref iteration,
							ref iterationsSinceImprovement,
							temperature,
							ref rngState,
							history,
							currentSlots,
							currentRotations,
							bestSlots,
							bestRotations
						)) {
							if (itemA >= 0) {
								currentItemToSlot[itemA] = slotB;
							}
							if (itemB >= 0) {
								currentItemToSlot[itemB] = slotC;
							}
							if (itemC >= 0) {
								currentItemToSlot[itemC] = slotA;
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

						if (TryAcceptCandidate(
							candidateScore,
							ref currentScore,
							ref bestScore,
							ref iteration,
							ref iterationsSinceImprovement,
							temperature,
							ref rngState,
							history,
							currentSlots,
							currentRotations,
							bestSlots,
							bestRotations
						)) {
							if (itemB >= 0) {
								currentItemToSlot[itemB] = slotA;
							}
							if (itemA >= 0) {
								currentItemToSlot[itemA] = slotB;
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
					SyncItemToSlot(currentSlots, currentItemToSlot);
					currentScore = bestScore;
					history.Fill(bestScore - ReheatMargin);
					temperature = reheatTemperature;
					iterationsSinceImprovement = 0;
				}
			}
		}

		private static void ExecuteVerticalPairMutation(
			InventorySnapshot snapshot,
			int topA,
			int botA,
			int topB,
			int botB,
			Span<int> currentSlots,
			ReadOnlySpan<int> currentRotations,
			Span<int> currentItemToSlot,
			Span<int> history,
			int[] bestSlots,
			int[] bestRotations,
			double temperature,
			ref uint rngState,
			ref int currentScore,
			ref int bestScore,
			ref int evaluations,
			ref int iteration,
			ref int iterationsSinceImprovement
		) {
			if (topB == botA) {
				int item0 = currentSlots[topA];
				int item1 = currentSlots[botA];
				int item2 = currentSlots[botB];
				currentSlots[topA] = item2;
				currentSlots[botA] = item0;
				currentSlots[botB] = item1;

				int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;

				if (TryAcceptCandidate(
					candidateScore,
					ref currentScore,
					ref bestScore,
					ref iteration,
					ref iterationsSinceImprovement,
					temperature,
					ref rngState,
					history,
					currentSlots,
					currentRotations,
					bestSlots,
					bestRotations
				)) {
					if (item2 >= 0) {
						currentItemToSlot[item2] = topA;
					}
					if (item0 >= 0) {
						currentItemToSlot[item0] = botA;
					}
					if (item1 >= 0) {
						currentItemToSlot[item1] = botB;
					}
				} else {
					currentSlots[topA] = item0;
					currentSlots[botA] = item1;
					currentSlots[botB] = item2;
					iterationsSinceImprovement++;
				}
			} else if (botB == topA) {
				int item0 = currentSlots[topB];
				int item1 = currentSlots[topA];
				int item2 = currentSlots[botA];
				currentSlots[topB] = item1;
				currentSlots[topA] = item2;
				currentSlots[botA] = item0;

				int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;

				if (TryAcceptCandidate(
					candidateScore,
					ref currentScore,
					ref bestScore,
					ref iteration,
					ref iterationsSinceImprovement,
					temperature,
					ref rngState,
					history,
					currentSlots,
					currentRotations,
					bestSlots,
					bestRotations
				)) {
					if (item1 >= 0) {
						currentItemToSlot[item1] = topB;
					}
					if (item2 >= 0) {
						currentItemToSlot[item2] = topA;
					}
					if (item0 >= 0) {
						currentItemToSlot[item0] = botA;
					}
				} else {
					currentSlots[topB] = item0;
					currentSlots[topA] = item1;
					currentSlots[botA] = item2;
					iterationsSinceImprovement++;
				}
			} else {
				int itemTopA = currentSlots[topA];
				int itemBotA = currentSlots[botA];
				int itemTopB = currentSlots[topB];
				int itemBotB = currentSlots[botB];

				currentSlots[topA] = itemTopB;
				currentSlots[botA] = itemBotB;
				currentSlots[topB] = itemTopA;
				currentSlots[botB] = itemBotA;

				int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;

				if (TryAcceptCandidate(
					candidateScore,
					ref currentScore,
					ref bestScore,
					ref iteration,
					ref iterationsSinceImprovement,
					temperature,
					ref rngState,
					history,
					currentSlots,
					currentRotations,
					bestSlots,
					bestRotations
				)) {
					if (itemTopB >= 0) {
						currentItemToSlot[itemTopB] = topA;
					}
					if (itemBotB >= 0) {
						currentItemToSlot[itemBotB] = botA;
					}
					if (itemTopA >= 0) {
						currentItemToSlot[itemTopA] = topB;
					}
					if (itemBotA >= 0) {
						currentItemToSlot[itemBotA] = botB;
					}
				} else {
					currentSlots[topA] = itemTopA;
					currentSlots[botA] = itemBotA;
					currentSlots[topB] = itemTopB;
					currentSlots[botB] = itemBotB;
					iterationsSinceImprovement++;
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool TryAcceptCandidate(
			int candidateScore,
			ref int currentScore,
			ref int bestScore,
			ref int iteration,
			ref int iterationsSinceImprovement,
			double temperature,
			ref uint rngState,
			Span<int> history,
			ReadOnlySpan<int> currentSlots,
			ReadOnlySpan<int> currentRotations,
			int[] bestSlots,
			int[] bestRotations
		) {
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
					: (histScore + ((currentScore - histScore) / 2));
				if (candidateScore > bestScore) {
					bestScore = candidateScore;
					currentSlots.CopyTo(bestSlots);
					currentRotations.CopyTo(bestRotations);
					iterationsSinceImprovement = 0;
				} else {
					iterationsSinceImprovement++;
				}
				return true;
			}
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool IsBetterPolishCandidate(
			int candidateScore,
			int candidateDps,
			int bestScore,
			int bestDps
		) {
			int nonDpsPenaltyDrop = (bestScore - (bestDps * ObjectiveEvaluator.DpsScaleFactor))
				- (candidateScore - (candidateDps * ObjectiveEvaluator.DpsScaleFactor));
			if (candidateDps > bestDps && nonDpsPenaltyDrop < 2000) {
				return true;
			}
			return candidateDps >= bestDps && candidateScore > bestScore;
		}

		private static void RunDeterministicPolish(
			InventorySnapshot snapshot,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int bestDps,
			ref int evaluations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int width = snapshot.Width;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

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
						int score = ObjectiveEvaluator.EvaluateWithDps(
							snapshot,
							bestSlots,
							bestRotations,
							out int candidateDps
						);
						evaluations++;
						if (IsBetterPolishCandidate(score, candidateDps, bestScore, bestDps)) {
							bestScore = score;
							bestDps = candidateDps;
							bestRotForItem = rot;
							improved = true;
						}
					}
					bestRotations[i] = bestRotForItem;
				}

				for (int sA = 0; sA < unlockedSlots; sA++) {
					if (pinnedSlots[sA]) {
						continue;
					}
					for (int sB = sA + 1; sB < unlockedSlots; sB++) {
						if (pinnedSlots[sB]) {
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
								int score = ObjectiveEvaluator.EvaluateWithDps(
									snapshot,
									bestSlots,
									bestRotations,
									out int candidateDps
								);
								evaluations++;
								if (IsBetterPolishCandidate(score, candidateDps, bestScore, bestDps)) {
									bestScore = score;
									bestDps = candidateDps;
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
							int score = ObjectiveEvaluator.EvaluateWithDps(
								snapshot,
								bestSlots,
								bestRotations,
								out int candidateDps
							);
							evaluations++;
							if (IsBetterPolishCandidate(score, candidateDps, bestScore, bestDps)) {
								bestScore = score;
								bestDps = candidateDps;
								improved = true;
							} else {
								bestSlots[sA] = itemA;
								bestSlots[sB] = itemB;
							}
						}
					}
				}

				if (unlockedSlots > width) {
					for (int topA = 0; topA + width < unlockedSlots; topA++) {
						int botA = topA + width;
						if (pinnedSlots[topA] || pinnedSlots[botA]) {
							continue;
						}
						int topItem = bestSlots[topA];
						int botItem = bestSlots[botA];
						bool isLinkedNeedlePair = topItem >= 0
							&& botItem >= 0
							&& items[topItem].Kind == SlotItemKind.Artifact
							&& items[topItem].IsAttackableCharm
							&& items[botItem].Kind == SlotItemKind.Artifact
							&& items[botItem].HasDependencyTarget;
						if (!isLinkedNeedlePair) {
							continue;
						}

						for (int topB = 0; topB + width < unlockedSlots; topB++) {
							if (topB == topA) {
								continue;
							}
							int botB = topB + width;
							if (pinnedSlots[topB] || pinnedSlots[botB]) {
								continue;
							}
							if (TryPolishVerticalPairSwap(
								snapshot,
								topA,
								botA,
								topB,
								botB,
								bestSlots,
								bestRotations,
								ref bestScore,
								ref bestDps,
								ref evaluations
							)) {
								improved = true;
								break;
							}
						}
					}
				}
			}
		}

		private static bool TryPolishVerticalPairSwap(
			InventorySnapshot snapshot,
			int topA,
			int botA,
			int topB,
			int botB,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int bestDps,
			ref int evaluations
		) {
			if (topB == botA) {
				int i0 = bestSlots[topA];
				int i1 = bestSlots[botA];
				int i2 = bestSlots[botB];
				bestSlots[topA] = i2;
				bestSlots[botA] = i0;
				bestSlots[botB] = i1;
				int score = ObjectiveEvaluator.EvaluateWithDps(
					snapshot,
					bestSlots,
					bestRotations,
					out int candidateDps
				);
				evaluations++;
				if (IsBetterPolishCandidate(score, candidateDps, bestScore, bestDps)) {
					bestScore = score;
					bestDps = candidateDps;
					return true;
				}
				bestSlots[topA] = i0;
				bestSlots[botA] = i1;
				bestSlots[botB] = i2;
				return false;
			}

			if (botB == topA) {
				int i0 = bestSlots[topB];
				int i1 = bestSlots[topA];
				int i2 = bestSlots[botA];
				bestSlots[topB] = i1;
				bestSlots[topA] = i2;
				bestSlots[botA] = i0;
				int score = ObjectiveEvaluator.EvaluateWithDps(
					snapshot,
					bestSlots,
					bestRotations,
					out int candidateDps
				);
				evaluations++;
				if (IsBetterPolishCandidate(score, candidateDps, bestScore, bestDps)) {
					bestScore = score;
					bestDps = candidateDps;
					return true;
				}
				bestSlots[topB] = i0;
				bestSlots[topA] = i1;
				bestSlots[botA] = i2;
				return false;
			}

			int aTop = bestSlots[topA];
			int aBot = bestSlots[botA];
			int bTop = bestSlots[topB];
			int bBot = bestSlots[botB];
			bestSlots[topA] = bTop;
			bestSlots[botA] = bBot;
			bestSlots[topB] = aTop;
			bestSlots[botB] = aBot;
			int disjointScore = ObjectiveEvaluator.EvaluateWithDps(
				snapshot,
				bestSlots,
				bestRotations,
				out int disjointDps
			);
			evaluations++;
			if (IsBetterPolishCandidate(disjointScore, disjointDps, bestScore, bestDps)) {
				bestScore = disjointScore;
				bestDps = disjointDps;
				return true;
			}
			bestSlots[topA] = aTop;
			bestSlots[botA] = aBot;
			bestSlots[topB] = bTop;
			bestSlots[botB] = bBot;
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void SyncItemToSlot(ReadOnlySpan<int> slotToItem, Span<int> itemToSlot) {
			itemToSlot.Fill(-1);
			for (int s = 0; s < slotToItem.Length; s++) {
				int itemIdx = slotToItem[s];
				if (itemIdx >= 0 && itemIdx < itemToSlot.Length) {
					itemToSlot[itemIdx] = s;
				}
			}
		}

		private static void SortAttackableItemsByTargetPriority(
			InventorySnapshot snapshot,
			SlotItemSnapshot[] items,
			Span<int> attackableItems,
			int count
		) {
			for (int i = 1; i < count; i++) {
				int keyIdx = attackableItems[i];
				int keyScore = GetNeedleTargetSelectionScore(snapshot, items[keyIdx]);
				int j = i - 1;
				while (j >= 0 && GetNeedleTargetSelectionScore(snapshot, items[attackableItems[j]]) < keyScore) {
					attackableItems[j + 1] = attackableItems[j];
					j--;
				}
				attackableItems[j + 1] = keyIdx;
			}
		}

		private static int GetNeedleTargetSelectionScore(InventorySnapshot snapshot, SlotItemSnapshot item) {
			double rootWeight = ObjectiveEvaluator.GetNeedleRootWeight(snapshot, item);
			int centiWeight = (int)Math.Round(rootWeight * 100.0);
			int maxDpsBonus = (int)Math.Round(item.GetAttackRawDpsAtLevel(item.MaxLevel));
			return (centiWeight * (4 + item.MaxLevel)) + maxDpsBonus;
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
							contribution -= ObjectiveEvaluator.WastedPositiveOutOfBoundsPenalty;
						}
						break;
					}
					case TabletEffectType.MultiplyConstLevel: {
						if (targetSlot < 0 && eff.LevelParam > 1) {
							contribution -= (eff.LevelParam - 1) * ObjectiveEvaluator.WastedPositiveOutOfBoundsPenalty;
						}
						break;
					}
				}
			}

			if (rule.Conditions.Length > 0 && contribution < -ObjectiveEvaluator.UnmetTabletConditionPenalty) {
				contribution = -ObjectiveEvaluator.UnmetTabletConditionPenalty;
			}
			return contribution;
		}

		private static int ComputeSingleItemMaxPotential(InventorySnapshot snapshot, SlotItemSnapshot item) {
			if (item.Kind == SlotItemKind.Artifact) {
				if (item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
					return ObjectiveEvaluator.SacrificeActiveTieBreakerScore + 120;
				}

				double weight = ObjectiveEvaluator.GetNormalizedWeight(item);
				int rawBaseMax = ObjectiveEvaluator.BaseActiveArtifactScore
					+ (item.MaxLevel * ObjectiveEvaluator.EffectiveLevelScoreMultiplier)
					+ ObjectiveEvaluator.MaxLevelReachedBonus
					+ ObjectiveEvaluator.IgnoreConstraintSynergyBonus
					+ 440;
				int maxLvlScore = (int)Math.Ceiling(rawBaseMax * weight)
					+ (32 * ObjectiveEvaluator.MaxLevelOverflowTieBreakerBonus);

				int maxDpsScore = 0;
				if (item.IsAttackableCharm) {
					double maxRawAttackDps = item.GetAttackRawDpsAtLevel(item.MaxLevel) * 3.5;
					maxDpsScore += (int)Math.Ceiling(maxRawAttackDps * ObjectiveEvaluator.DpsScaleFactor);
				}
				float maxStatPct = item.GetStatBonusPercentAtLevel(item.MaxLevel);
				if (maxStatPct > 0f) {
					maxDpsScore += (int)Math.Ceiling(maxStatPct * 18.0 * ObjectiveEvaluator.DpsScaleFactor);
				}
				maxLvlScore += maxDpsScore;

				if (item.HasDependencyTarget) {
					SlotItemSnapshot[] allItems = snapshot.Items;
					double maxRootWeight = 1.0;
					int maxRootMaxLevel = 0;
					double maxRootDps = 200.0;
					for (int i = 0; i < allItems.Length; i++) {
						SlotItemSnapshot cand = allItems[i];
						if (cand.Kind == SlotItemKind.Artifact && cand.IsAttackableCharm) {
							double candWeight = ObjectiveEvaluator.GetNeedleRootWeight(snapshot, cand);
							if (candWeight > maxRootWeight) {
								maxRootWeight = candWeight;
							}
							if (cand.MaxLevel > maxRootMaxLevel) {
								maxRootMaxLevel = cand.MaxLevel;
							}
							double candDps = cand.GetAttackRawDpsAtLevel(cand.MaxLevel);
							if (candDps > maxRootDps) {
								maxRootDps = candDps;
							}
						}
					}
					double maxAmpFactor = 1.0 + (0.25 * (item.MaxLevel + 1));
					double maxNeedleAmp = maxAmpFactor
						* (ObjectiveEvaluator.GoldenNeedleRootLevelFactor * maxRootMaxLevel)
						* maxRootWeight
						+ (35.0 * (item.MaxLevel + 1));
					int maxNeedlePct = item.GetNeedleBonusPercent(item.MaxLevel, 0);
					int maxNeedleDpsGain = (int)Math.Ceiling(
						maxRootDps * (maxNeedlePct * 0.01) * 3.0 * ObjectiveEvaluator.DpsScaleFactor
					);
					int maxNeedleSynergy = (int)Math.Ceiling(
						ObjectiveEvaluator.GoldenNeedleLinkBaseBonus * maxRootWeight
					) + (int)Math.Ceiling(maxNeedleAmp) + maxNeedleDpsGain + 250;
					maxLvlScore += maxNeedleSynergy;
				}

				if (item.IsCrystalOfHarmony) {
					SlotItemSnapshot[] allItems = snapshot.Items;
					int maxNeighborLv = 1;
					for (int i = 0; i < allItems.Length; i++) {
						if (allItems[i].Kind == SlotItemKind.Artifact && allItems[i].MaxLevel > maxNeighborLv) {
							maxNeighborLv = allItems[i].MaxLevel;
						}
					}
					float maxRate = item.GetCrystalRatePerLevel(item.MaxLevel);
					int maxHarmonyBonus = (int)Math.Ceiling(
						8 * maxNeighborLv * maxRate * ObjectiveEvaluator.CrystalNeighborLevelFactor * weight
					) + (8 * ObjectiveEvaluator.CrystalNeighborSlotBonus)
						+ (int)Math.Ceiling(8 * maxNeighborLv * maxRate * 25.0 * ObjectiveEvaluator.DpsScaleFactor)
						+ 120;
					maxLvlScore += maxHarmonyBonus;
				}

				if (item.IsElementalByRowCharm) {
					int maxElemBonus = item.GetRowElementalBonus(item.MaxLevel);
					maxLvlScore += ObjectiveEvaluator.ChalcedonyTargetMatchBonus
						+ (maxElemBonus * 50 * ObjectiveEvaluator.DpsScaleFactor)
						+ 800;
				}
				if (item.IsWhitePaperCharm) {
					maxLvlScore += ObjectiveEvaluator.WhitePaperTargetCopiedBonus
						+ (150 * ObjectiveEvaluator.DpsScaleFactor)
						+ 200;
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
			int potential = 2500 + (400 * ObjectiveEvaluator.DpsScaleFactor);
			ComboCategoryInfo[] cats = snapshot.ComboCategories;
			for (int i = 0; i < cats.Length; i++) {
				potential += (cats[i].Breakpoints.Length * ObjectiveEvaluator.TargetComboBreakpointBonus)
					+ (8 * ObjectiveEvaluator.TargetComboDynamicStackBonus)
					+ (150 * ObjectiveEvaluator.DpsScaleFactor);
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
					|| condA.TargetX != condA.TargetX
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
