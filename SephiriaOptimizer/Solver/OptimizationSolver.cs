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
			int bestEstimatedDps = initialEstimatedDps;
			int evaluations = 1;

			if (HasAnyStoneTablet(snapshot)) {
				RunStagedTabletAndArtifactSynthesis(
					snapshot,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref bestEstimatedDps,
					ref evaluations
				);
			}

			int remainingAfterStageMs = Math.Max(60, timeBudgetMs - (int)stopwatch.ElapsedMilliseconds);
			if (isSmallScale) {
				bool limitExceeded = SolveBranchAndBound(
					snapshot,
					stopwatch,
					remainingAfterStageMs,
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
					remainingAfterStageMs,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref evaluations
				);
			}

			bestEstimatedDps = ObjectiveEvaluator.CalculateEstimatedDummyDps(
				snapshot,
				bestSlots,
				bestRotations
			);

			if (HasAnyStoneTablet(snapshot)) {
				RunTabletCollisionRepairPass(
					snapshot,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref bestEstimatedDps,
					ref evaluations
				);
			}

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

				for (int iA = 0; iA < itemCount; iA++) {
					if (items[iA].Kind != SlotItemKind.StoneTablet || !items[iA].IsRotatable) {
						continue;
					}
					for (int iB = iA + 1; iB < itemCount; iB++) {
						if (items[iB].Kind != SlotItemKind.StoneTablet || !items[iB].IsRotatable) {
							continue;
						}
						int savedRotA = bestRotations[iA];
						int savedRotB = bestRotations[iB];
						int bestPairRotA = savedRotA;
						int bestPairRotB = savedRotB;
						bool pairImproved = false;

						for (int rA = 0; rA < 4; rA++) {
							bestRotations[iA] = rA;
							for (int rB = 0; rB < 4; rB++) {
								if (rA == savedRotA && rB == savedRotB) {
									continue;
								}
								bestRotations[iB] = rB;
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
									bestPairRotA = rA;
									bestPairRotB = rB;
									pairImproved = true;
									improved = true;
								}
							}
						}
						bestRotations[iA] = pairImproved ? bestPairRotA : savedRotA;
						bestRotations[iB] = pairImproved ? bestPairRotB : savedRotB;
					}
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

						if (rotA && rotB) {
							int savedRotA = bestRotations[itemA];
							int savedRotB = bestRotations[itemB];
							int bestSwapRotA = savedRotA;
							int bestSwapRotB = savedRotB;
							bool swapImproved = false;

							for (int rA = 0; rA < 4; rA++) {
								bestRotations[itemA] = rA;
								for (int rB = 0; rB < 4; rB++) {
									bestRotations[itemB] = rB;
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
										bestSwapRotA = rA;
										bestSwapRotB = rB;
										swapImproved = true;
										improved = true;
									}
								}
							}

							if (swapImproved) {
								bestRotations[itemA] = bestSwapRotA;
								bestRotations[itemB] = bestSwapRotB;
							} else {
								bestRotations[itemA] = savedRotA;
								bestRotations[itemB] = savedRotB;
								bestSlots[sA] = itemA;
								bestSlots[sB] = itemB;
							}
						} else if (rotA || rotB) {
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

		private static bool HasAnyStoneTablet(InventorySnapshot snapshot) {
			SlotItemSnapshot[] items = snapshot.Items;
			for (int i = 0; i < items.Length; i++) {
				if (items[i].Kind == SlotItemKind.StoneTablet) {
					return true;
				}
			}
			return false;
		}

		private static void RunStagedTabletAndArtifactSynthesis(
			InventorySnapshot snapshot,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int bestDps,
			ref int evaluations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			Span<int> candidateSlots = stackalloc int[unlockedSlots];
			Span<int> candidateRotations = stackalloc int[itemCount];

			for (int mode = 0; mode < 2; mode++) {
				candidateSlots.Fill(-1);
				snapshot.InitialItemRotations.AsSpan(0, itemCount).CopyTo(candidateRotations);

				for (int i = 0; i < itemCount; i++) {
					int initSlot = items[i].InitialSlotIndex;
					if (initSlot < 0 || initSlot >= unlockedSlots) {
						continue;
					}
					if (pinnedSlots[initSlot] || (mode == 1 && items[i].Kind == SlotItemKind.StoneTablet)) {
						candidateSlots[initSlot] = i;
					}
				}

				ConstructConflictFreeTabletField(
					snapshot,
					candidateSlots,
					candidateRotations,
					placeFromScratch: mode == 0
				);
				AssignArtifactsToTabletField(snapshot, candidateSlots, candidateRotations);
				RunArtifactOnlyLocalPolish(snapshot, candidateSlots, candidateRotations, ref evaluations);

				int score = ObjectiveEvaluator.EvaluateWithDps(
					snapshot,
					candidateSlots,
					candidateRotations,
					out int dps
				);
				evaluations++;

				if (IsBetterPolishCandidate(score, dps, bestScore, bestDps) || score > bestScore) {
					bestScore = score;
					bestDps = dps;
					candidateSlots.CopyTo(bestSlots);
					candidateRotations.CopyTo(bestRotations);
				}
			}
		}

		private static void ConstructConflictFreeTabletField(
			InventorySnapshot snapshot,
			Span<int> tabletSlots,
			Span<int> tabletRotations,
			bool placeFromScratch
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			Span<int> unpinnedTablets = stackalloc int[items.Length];
			int tabletCount = 0;
			for (int i = 0; i < items.Length; i++) {
				if (items[i].Kind != SlotItemKind.StoneTablet) {
					continue;
				}
				int initSlot = items[i].InitialSlotIndex;
				bool isPinned = initSlot >= 0 && initSlot < unlockedSlots && pinnedSlots[initSlot];
				if (!isPinned) {
					unpinnedTablets[tabletCount++] = i;
				}
			}

			if (tabletCount == 0) {
				return;
			}

			SortTabletsByFieldImpact(items, unpinnedTablets, tabletCount);

			if (placeFromScratch) {
				for (int t = 0; t < tabletCount; t++) {
					int tabletIdx = unpinnedTablets[t];
					SlotItemSnapshot tablet = items[tabletIdx];
					int bestSlot = -1;
					int bestRot = tablet.InitialRotation & 3;
					int bestFieldScore = int.MinValue;

					for (int s = 0; s < unlockedSlots; s++) {
						if (pinnedSlots[s] || tabletSlots[s] >= 0) {
							continue;
						}
						tabletSlots[s] = tabletIdx;
						int maxRot = tablet.IsRotatable ? 4 : 1;
						for (int r = 0; r < maxRot; r++) {
							int rot = tablet.IsRotatable ? r : (tablet.InitialRotation & 3);
							if (tablet.IsRotatable && IsRedundantTabletRotation(tablet, s, rot)) {
								continue;
							}
							tabletRotations[tabletIdx] = rot;
							int fieldScore = EvaluatePureTabletFieldScore(
								snapshot,
								tabletSlots,
								tabletRotations,
								out _
							);
							if (fieldScore > bestFieldScore) {
								bestFieldScore = fieldScore;
								bestSlot = s;
								bestRot = rot;
							}
						}
						tabletSlots[s] = -1;
					}

					if (bestSlot >= 0) {
						tabletSlots[bestSlot] = tabletIdx;
						tabletRotations[tabletIdx] = bestRot;
					}
				}
			}

			int currentFieldScore = EvaluatePureTabletFieldScore(
				snapshot,
				tabletSlots,
				tabletRotations,
				out _
			);

			for (int pass = 0; pass < 3; pass++) {
				bool improved = false;

				for (int t = 0; t < tabletCount; t++) {
					int tabletIdx = unpinnedTablets[t];
					SlotItemSnapshot tablet = items[tabletIdx];
					int curSlot = FindItemSlot(tabletSlots, tabletIdx);
					if (curSlot < 0) {
						continue;
					}

					int curRot = tabletRotations[tabletIdx];
					int bestSlot = curSlot;
					int bestRot = curRot;
					tabletSlots[curSlot] = -1;

					for (int s = 0; s < unlockedSlots; s++) {
						if (pinnedSlots[s] || tabletSlots[s] >= 0) {
							continue;
						}
						tabletSlots[s] = tabletIdx;
						int maxRot = tablet.IsRotatable ? 4 : 1;
						for (int r = 0; r < maxRot; r++) {
							int rot = tablet.IsRotatable ? r : (tablet.InitialRotation & 3);
							if (tablet.IsRotatable && IsRedundantTabletRotation(tablet, s, rot)) {
								continue;
							}
							tabletRotations[tabletIdx] = rot;
							int candFieldScore = EvaluatePureTabletFieldScore(
								snapshot,
								tabletSlots,
								tabletRotations,
								out _
							);
							if (candFieldScore > currentFieldScore) {
								currentFieldScore = candFieldScore;
								bestSlot = s;
								bestRot = rot;
								improved = true;
							}
						}
						tabletSlots[s] = -1;
					}

					tabletSlots[bestSlot] = tabletIdx;
					tabletRotations[tabletIdx] = bestRot;
				}

				for (int a = 0; a < tabletCount; a++) {
					int tA = unpinnedTablets[a];
					int slotA = FindItemSlot(tabletSlots, tA);
					if (slotA < 0) {
						continue;
					}
					for (int b = a + 1; b < tabletCount; b++) {
						int tB = unpinnedTablets[b];
						int slotB = FindItemSlot(tabletSlots, tB);
						if (slotB < 0) {
							continue;
						}

						int origSlotA = tabletSlots[slotA];
						int origSlotB = tabletSlots[slotB];
						int origRotA = tabletRotations[tA];
						int origRotB = tabletRotations[tB];

						int bestSlotAItem = origSlotA;
						int bestSlotBItem = origSlotB;
						int bestRotA = origRotA;
						int bestRotB = origRotB;

						int maxRotA = items[tA].IsRotatable ? 4 : 1;
						int maxRotB = items[tB].IsRotatable ? 4 : 1;

						for (int swapMode = 0; swapMode < 2; swapMode++) {
							tabletSlots[slotA] = swapMode == 0 ? tA : tB;
							tabletSlots[slotB] = swapMode == 0 ? tB : tA;

							for (int rA = 0; rA < maxRotA; rA++) {
								int rotA = items[tA].IsRotatable ? rA : origRotA;
								tabletRotations[tA] = rotA;
								for (int rB = 0; rB < maxRotB; rB++) {
									int rotB = items[tB].IsRotatable ? rB : origRotB;
									tabletRotations[tB] = rotB;
									int candScore = EvaluatePureTabletFieldScore(
										snapshot,
										tabletSlots,
										tabletRotations,
										out _
									);
									if (candScore > currentFieldScore) {
										currentFieldScore = candScore;
										bestSlotAItem = tabletSlots[slotA];
										bestSlotBItem = tabletSlots[slotB];
										bestRotA = rotA;
										bestRotB = rotB;
										improved = true;
									}
								}
							}
						}

						tabletSlots[slotA] = bestSlotAItem;
						tabletSlots[slotB] = bestSlotBItem;
						tabletRotations[tA] = bestRotA;
						tabletRotations[tB] = bestRotB;
					}
				}

				if (!improved) {
					break;
				}
			}
		}

		private static int EvaluatePureTabletFieldScore(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> slotToItem,
			ReadOnlySpan<int> itemRotations,
			out int collisionCount
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			bool hasEngraving = snapshot.TalentRules.TabletEngravingCount > 0;
			collisionCount = 0;

			Span<int> posLevel = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> negLevel = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulLevel = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disable = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignore = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isTablet = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> reqCharm = stackalloc byte[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				int perm = snapshot.BaseSlotLevel[s];
				posLevel[s] = Math.Max(0, perm);
				negLevel[s] = Math.Max(0, -perm);
				mulLevel[s] = snapshot.BaseSlotMultiplyLevel[s];
				disable[s] = snapshot.BaseSlotDisable[s];
				ignore[s] = snapshot.BaseSlotIgnoreCriteria[s];
				int idx = slotToItem[s];
				isTablet[s] = (idx >= 0 && items[idx].Kind == SlotItemKind.StoneTablet) ? (byte)1 : (byte)0;
				reqCharm[s] = 0;
			}

			int fieldScore = 0;
			for (int s = 0; s < unlockedSlots; s++) {
				if (isTablet[s] == 0) {
					continue;
				}
				int tabletIdx = slotToItem[s];
				SlotItemSnapshot tablet = items[tabletIdx];
				int rot = itemRotations[tabletIdx] & 3;
				TabletPlacementRule rule = tablet.GetPlacementRule(s, rot);

				if (!IsTabletFieldRuleCompatible(snapshot, isTablet, s, rule, ref reqCharm)) {
					fieldScore -= ObjectiveEvaluator.UnmetTabletConditionPenalty;
					collisionCount += 2;
					continue;
				}

				PrecomputedEffect[] effects = rule.Effects;
				int outOfBoundsNegCount = 0;
				for (int e = 0; e < effects.Length; e++) {
					PrecomputedEffect eff = effects[e];
					int target = eff.TargetSlotIndex;
					switch (eff.EffectType) {
						case TabletEffectType.IncreaseConstLevel: {
							if (target >= 0 && target < unlockedSlots) {
								if (eff.LevelParam > 0) {
									posLevel[target] += eff.LevelParam;
								} else if (eff.LevelParam < 0) {
									negLevel[target] += -eff.LevelParam;
								}
							} else if (eff.LevelParam > 0) {
								fieldScore -= eff.LevelParam * ObjectiveEvaluator.WastedPositiveOutOfBoundsPenalty;
							} else if (eff.LevelParam < 0) {
								int negOutBonus = ObjectiveEvaluator.NeutralizedNegativeLevelBonus + 95;
								fieldScore += (-eff.LevelParam) * negOutBonus;
								outOfBoundsNegCount++;
							}
							break;
						}
						case TabletEffectType.Disable: {
							if (target >= 0 && target < unlockedSlots) {
								disable[target]++;
							} else {
								fieldScore += ObjectiveEvaluator.NeutralizedDisableBonus + 140;
								outOfBoundsNegCount++;
							}
							break;
						}
						case TabletEffectType.IgnoreCriteria: {
							if (target >= 0 && target < unlockedSlots) {
								ignore[target]++;
							} else {
								fieldScore -= ObjectiveEvaluator.WastedPositiveOutOfBoundsPenalty;
							}
							break;
						}
						case TabletEffectType.MultiplyConstLevel: {
							if (target >= 0 && target < unlockedSlots) {
								mulLevel[target] += eff.LevelParam;
							} else if (eff.LevelParam > 1) {
								int wastedMul = eff.LevelParam - 1;
								fieldScore -= wastedMul * ObjectiveEvaluator.WastedPositiveOutOfBoundsPenalty;
							}
							break;
						}
					}
				}
				if (hasEngraving && outOfBoundsNegCount > 0) {
					fieldScore += outOfBoundsNegCount * ObjectiveEvaluator.EngravingOutOfBoundsBonus;
				}
			}

			ulong mysticMask = ComputeLikelyMysticSlotMask(snapshot);
			Span<int> openSlotDeltas = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			int openSlotCount = 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int baseMul = mulLevel[s] != 0 ? mulLevel[s] : 1;
				int absMul = Math.Max(1, Math.Abs(baseMul));
				int mysticMul = (mysticMask & (1UL << s)) != 0UL ? 2 : 1;
				int slotMul = absMul * mysticMul;
				int rawPos = posLevel[s];
				int rawNeg = negLevel[s];
				int rawDelta = rawPos - rawNeg;
				int finalDelta = rawDelta * baseMul * mysticMul;

				if (mulLevel[s] > 1 && rawDelta == 0) {
					fieldScore -= (mulLevel[s] - 1) * ObjectiveEvaluator.WastedMultiplierPenalty;
				}

				if (isTablet[s] != 0) {
					int posTotal = (rawPos * slotMul) + ignore[s];
					if (posTotal > 0) {
						fieldScore -= posTotal * ObjectiveEvaluator.TabletOnBuffedSlotPenalty;
						collisionCount++;
					} else if (rawNeg > 0 || disable[s] > 0) {
						fieldScore += (rawNeg * slotMul * 95) + (disable[s] * 140);
					}
					continue;
				}

				if (rawPos > 0 && rawNeg > 0) {
					collisionCount++;
					int rawCanceled = Math.Min(rawPos, rawNeg);
					int canceledScaled = rawCanceled * slotMul;
					fieldScore -= canceledScaled * (ObjectiveEvaluator.TabletPlusMinusCollisionPenalty + 220);
					if (finalDelta > 0) {
						int netPosPen = ObjectiveEvaluator.TabletMinusOnNetPositiveSlotPenalty + 160;
						fieldScore -= (rawNeg * slotMul) * netPosPen;
					}
				}

				if (disable[s] > 0 && (rawPos > 0 || ignore[s] > 0)) {
					collisionCount++;
					fieldScore -= ((rawPos * slotMul) + ignore[s] + 2) * 1800;
				}

				int existingIdx = slotToItem[s];
				if (existingIdx >= 0 && snapshot.PinnedSlots[s] && items[existingIdx].Kind == SlotItemKind.Artifact) {
					SlotItemSnapshot pinnedArt = items[existingIdx];
					if (pinnedArt.PriorityTier == ArtifactPriorityTier.Sacrifice
						|| pinnedArt.IsDisabledByWeaponOrDual) {
						if (rawPos > 0) {
							fieldScore -= (rawPos * slotMul) * ObjectiveEvaluator.WastedPositiveOnEmptySlotPenalty;
						} else if (rawNeg > 0 || disable[s] > 0) {
							fieldScore += (rawNeg * slotMul * 80) + (disable[s] * 110);
						}
					} else {
						int disp = pinnedArt.BaseLevel + finalDelta;
						if (disable[s] > 0 || disp <= -1) {
							fieldScore -= ObjectiveEvaluator.DeactivationBasePenalty;
						} else {
							int eff = Math.Clamp(disp, 0, pinnedArt.MaxLevel);
							fieldScore += eff * 220;
						}
					}
					continue;
				}

				int encodedSlotDelta = disable[s] > 0 ? -100 : finalDelta;
				openSlotDeltas[openSlotCount++] = encodedSlotDelta;
			}

			SortDescending(openSlotDeltas, openSlotCount);

			Span<int> artifactCaps = stackalloc int[items.Length];
			Span<int> artifactBases = stackalloc int[items.Length];
			Span<int> artifactValues = stackalloc int[items.Length];
			int unpinnedActiveArtCount = 0;
			int unpinnedSacrificeCount = 0;

			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot item = items[i];
				if (item.Kind != SlotItemKind.Artifact) {
					continue;
				}
				int initSlot = item.InitialSlotIndex;
				if (initSlot >= 0 && initSlot < unlockedSlots && snapshot.PinnedSlots[initSlot]) {
					continue;
				}
				if (item.PriorityTier == ArtifactPriorityTier.Sacrifice || item.IsDisabledByWeaponOrDual) {
					unpinnedSacrificeCount++;
					continue;
				}
				int idx = unpinnedActiveArtCount++;
				artifactCaps[idx] = Math.Max(0, item.MaxLevel - item.BaseLevel);
				artifactBases[idx] = item.BaseLevel;
				artifactValues[idx] = (int)Math.Round(
					ObjectiveEvaluator.EffectiveLevelScoreMultiplier * ObjectiveEvaluator.GetNormalizedWeight(item)
				);
			}

			for (int i = 1; i < unpinnedActiveArtCount; i++) {
				int capKey = artifactCaps[i];
				int baseKey = artifactBases[i];
				int valKey = artifactValues[i];
				int rankKey = (capKey * 300) + valKey;
				int j = i - 1;
				while (j >= 0 && ((artifactCaps[j] * 300) + artifactValues[j]) < rankKey) {
					artifactCaps[j + 1] = artifactCaps[j];
					artifactBases[j + 1] = artifactBases[j];
					artifactValues[j + 1] = artifactValues[j];
					j--;
				}
				artifactCaps[j + 1] = capKey;
				artifactBases[j + 1] = baseKey;
				artifactValues[j + 1] = valKey;
			}

			for (int j = 0; j < openSlotCount; j++) {
				int delta = openSlotDeltas[j];
				if (j < unpinnedActiveArtCount) {
					if (delta <= -90) {
						fieldScore -= ObjectiveEvaluator.DeactivationBasePenalty;
					} else if (delta >= 0) {
						int used = Math.Min(delta, artifactCaps[j]);
						int overflow = Math.Max(0, delta - used);
						fieldScore += (used * artifactValues[j]) + (overflow * 8);
						if (artifactCaps[j] > 0 && used == artifactCaps[j]) {
							fieldScore += ObjectiveEvaluator.MaxLevelReachedBonus;
						}
					} else {
						if (artifactBases[j] + delta <= -1) {
							fieldScore -= ObjectiveEvaluator.DeactivationBasePenalty;
						} else {
							fieldScore += delta * 200;
						}
					}
				} else {
					if (delta <= -90) {
						fieldScore += ObjectiveEvaluator.NeutralizedDisableBonus;
					} else if (delta < 0) {
						fieldScore += (-delta) * (ObjectiveEvaluator.NeutralizedNegativeLevelBonus + 35);
					} else if (delta > 0) {
						fieldScore -= delta * ObjectiveEvaluator.WastedPositiveOnEmptySlotPenalty;
					}
				}
			}

			return fieldScore;
		}

		private static bool IsTabletFieldRuleCompatible(
			InventorySnapshot snapshot,
			ReadOnlySpan<byte> isTablet,
			int tabletSlotIndex,
			TabletPlacementRule rule,
			ref Span<byte> reqCharm
		) {
			PrecomputedCondition[] conditions = rule.Conditions;
			if (conditions.Length == 0) {
				return true;
			}

			SlotCoord origin = snapshot.Slots[tabletSlotIndex];
			int unlockedSlots = snapshot.UnlockedSlots;
			bool anyPlacedMatched = false;

			for (int i = 0; i < conditions.Length; i++) {
				PrecomputedCondition cond = conditions[i];
				switch (cond.ConditionType) {
					case TabletConditionType.AnyItem: {
						if (cond.TargetSlotIndex < 0 || cond.TargetSlotIndex >= unlockedSlots) {
							return false;
						}
						reqCharm[cond.TargetSlotIndex] = 1;
						break;
					}
					case TabletConditionType.OnlyCharm: {
						if (cond.TargetSlotIndex < 0
							|| cond.TargetSlotIndex >= unlockedSlots
							|| isTablet[cond.TargetSlotIndex] != 0) {
							return false;
						}
						reqCharm[cond.TargetSlotIndex] = 1;
						break;
					}
					case TabletConditionType.Placed: {
						if (cond.TargetSlotIndex == tabletSlotIndex
							&& origin.X == cond.TargetX
							&& origin.Y == cond.TargetY) {
							anyPlacedMatched = true;
						}
						break;
					}
				}
			}

			if (rule.HasPlacedCondition && !anyPlacedMatched) {
				return false;
			}
			return true;
		}

		private static ulong ComputeLikelyMysticSlotMask(InventorySnapshot snapshot) {
			ulong mask = 0UL;
			int unlockedSlots = snapshot.UnlockedSlots;
			int mysticCatId = snapshot.MysticCategoryId;
			int estimatedMysticTags = snapshot.StaticMysticTagCount;

			SlotItemSnapshot[] items = snapshot.Items;
			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot item = items[i];
				if (item.Kind != SlotItemKind.Artifact) {
					continue;
				}
				if (item.IsElementalByRowCharm && mysticCatId >= 0) {
					for (int r = 0; r < item.RowCategoryIds.Length; r++) {
						if (item.RowCategoryIds[r] == mysticCatId) {
							estimatedMysticTags++;
							break;
						}
					}
				}
			}

			if (estimatedMysticTags >= snapshot.MysticTier1Threshold
				&& (uint)snapshot.MysticSlot0 < (uint)unlockedSlots) {
				mask |= 1UL << snapshot.MysticSlot0;
			}
			if (estimatedMysticTags >= snapshot.MysticTier2Threshold) {
				if ((uint)snapshot.MysticSlot1 < (uint)unlockedSlots) {
					mask |= 1UL << snapshot.MysticSlot1;
				}
				int[] extra = snapshot.ExtraMysticTier2Slots;
				for (int m = 0; m < extra.Length; m++) {
					if ((uint)extra[m] < (uint)unlockedSlots) {
						mask |= 1UL << extra[m];
					}
				}
			}
			return mask;
		}

		private static void AssignArtifactsToTabletField(
			InventorySnapshot snapshot,
			Span<int> workingSlots,
			ReadOnlySpan<int> workingRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int width = snapshot.Width;
			SlotItemSnapshot[] items = snapshot.Items;

			Span<int> slotDelta = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> slotDisable = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> slotIgnore = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> slotMul = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> posLevel = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> negLevel = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isTablet = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> reqCharm = stackalloc byte[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				int perm = snapshot.BaseSlotLevel[s];
				slotDelta[s] = perm;
				posLevel[s] = Math.Max(0, perm);
				negLevel[s] = Math.Max(0, -perm);
				slotDisable[s] = snapshot.BaseSlotDisable[s];
				slotIgnore[s] = snapshot.BaseSlotIgnoreCriteria[s];
				slotMul[s] = snapshot.BaseSlotMultiplyLevel[s];
				int idx = workingSlots[s];
				isTablet[s] = (idx >= 0 && items[idx].Kind == SlotItemKind.StoneTablet) ? (byte)1 : (byte)0;
				reqCharm[s] = 0;
			}

			for (int s = 0; s < unlockedSlots; s++) {
				if (isTablet[s] == 0) {
					continue;
				}
				int tIdx = workingSlots[s];
				int rot = workingRotations[tIdx] & 3;
				TabletPlacementRule rule = items[tIdx].GetPlacementRule(s, rot);
				if (!IsTabletFieldRuleCompatible(snapshot, isTablet, s, rule, ref reqCharm)) {
					continue;
				}
				PrecomputedEffect[] effects = rule.Effects;
				for (int e = 0; e < effects.Length; e++) {
					PrecomputedEffect eff = effects[e];
					int target = eff.TargetSlotIndex;
					if (target < 0 || target >= unlockedSlots) {
						continue;
					}
					switch (eff.EffectType) {
						case TabletEffectType.IncreaseConstLevel: {
							slotDelta[target] += eff.LevelParam;
							if (eff.LevelParam > 0) {
								posLevel[target] += eff.LevelParam;
							} else if (eff.LevelParam < 0) {
								negLevel[target] += -eff.LevelParam;
							}
							break;
						}
						case TabletEffectType.Disable: {
							slotDisable[target]++;
							break;
						}
						case TabletEffectType.IgnoreCriteria: {
							slotIgnore[target]++;
							break;
						}
						case TabletEffectType.MultiplyConstLevel: {
							slotMul[target] += eff.LevelParam;
							break;
						}
					}
				}
			}

			ulong mysticMask = ComputeLikelyMysticSlotMask(snapshot);
			for (int s = 0; s < unlockedSlots; s++) {
				int mul = slotMul[s] != 0 ? slotMul[s] : 1;
				int mystic = (mysticMask & (1UL << s)) != 0UL ? 2 : 1;
				slotDelta[s] = slotDelta[s] * mul * mystic;
			}

			Span<byte> itemPlaced = stackalloc byte[items.Length];
			itemPlaced.Clear();
			for (int s = 0; s < unlockedSlots; s++) {
				int idx = workingSlots[s];
				if (idx >= 0) {
					itemPlaced[idx] = 1;
				}
			}

			if (unlockedSlots > width) {
				Span<int> needles = stackalloc int[items.Length];
				int needleCount = 0;
				Span<int> attacks = stackalloc int[items.Length];
				int attackCount = 0;

				for (int i = 0; i < items.Length; i++) {
					if (itemPlaced[i] != 0 || items[i].Kind != SlotItemKind.Artifact) {
						continue;
					}
					if (items[i].PriorityTier == ArtifactPriorityTier.Sacrifice
						|| items[i].IsDisabledByWeaponOrDual) {
						continue;
					}
					if (items[i].HasDependencyTarget) {
						needles[needleCount++] = i;
					} else if (items[i].IsAttackableCharm) {
						attacks[attackCount++] = i;
					}
				}

				SortAttackableItemsByTargetPriority(snapshot, items, attacks, attackCount);

				int pairLimit = Math.Min(needleCount, attackCount);
				for (int p = 0; p < pairLimit; p++) {
					int needleIdx = needles[p];
					int attackIdx = attacks[p];
					SlotItemSnapshot needleItem = items[needleIdx];
					SlotItemSnapshot attackItem = items[attackIdx];

					int bestTopSlot = -1;
					int bestPairScore = int.MinValue;

					for (int top = 0; top + width < unlockedSlots; top++) {
						int bot = top + width;
						if (workingSlots[top] >= 0 || workingSlots[bot] >= 0) {
							continue;
						}
						if (slotDisable[top] > 0 || slotDisable[bot] > 0) {
							continue;
						}
						int topDisp = attackItem.BaseLevel + slotDelta[top];
						int botDisp = needleItem.BaseLevel + slotDelta[bot];
						if (topDisp <= -1 || botDisp <= -1) {
							continue;
						}
						bool topCrit = slotIgnore[top] > 0
							|| IsStaticSlotConstraintMet(snapshot, top, attackItem.ConstraintType);
						bool botCrit = slotIgnore[bot] > 0
							|| IsStaticSlotConstraintMet(snapshot, bot, needleItem.ConstraintType);
						if (!topCrit || !botCrit) {
							continue;
						}

						int topEff = Math.Clamp(topDisp, 0, attackItem.MaxLevel);
						int botEff = Math.Clamp(botDisp, 0, needleItem.MaxLevel);
						int pairScore = (topEff * 360) + (botEff * 300) + (topEff * botEff * 90);
						if (posLevel[top] > 0 && negLevel[top] > 0) {
							pairScore -= 300;
						}
						if (posLevel[bot] > 0 && negLevel[bot] > 0) {
							pairScore -= 300;
						}
						if (pairScore > bestPairScore) {
							bestPairScore = pairScore;
							bestTopSlot = top;
						}
					}

					if (bestTopSlot >= 0) {
						workingSlots[bestTopSlot] = attackIdx;
						workingSlots[bestTopSlot + width] = needleIdx;
						itemPlaced[attackIdx] = 1;
						itemPlaced[needleIdx] = 1;
					}
				}
			}

			for (int i = 0; i < items.Length; i++) {
				if (itemPlaced[i] != 0
					|| items[i].Kind != SlotItemKind.Artifact
					|| !items[i].IsCrystalOfHarmony
					|| items[i].PriorityTier == ArtifactPriorityTier.Sacrifice) {
					continue;
				}
				SlotItemSnapshot crystal = items[i];
				int bestSlot = -1;
				int bestScore = int.MinValue;
				for (int s = 0; s < unlockedSlots; s++) {
					if (workingSlots[s] >= 0 || slotDisable[s] > 0) {
						continue;
					}
					int disp = crystal.BaseLevel + slotDelta[s];
					if (disp <= -1) {
						continue;
					}
					ulong mask = snapshot.MooreNeighborMasks[s];
					int nonTabletNeighbors = 0;
					int neighborBuffSum = 0;
					while (mask != 0UL) {
						int nSlot = BitOperations.TrailingZeroCount(mask);
						mask &= mask - 1UL;
						if (isTablet[nSlot] == 0 && slotDisable[nSlot] == 0) {
							nonTabletNeighbors++;
							neighborBuffSum += Math.Max(0, slotDelta[nSlot]);
						}
					}
					int eff = Math.Clamp(disp, 0, crystal.MaxLevel);
					int candScore = (eff * 240) + (nonTabletNeighbors * 110) + (neighborBuffSum * 90);
					if (candScore > bestScore) {
						bestScore = candScore;
						bestSlot = s;
					}
				}
				if (bestSlot >= 0) {
					workingSlots[bestSlot] = i;
					itemPlaced[i] = 1;
				}
			}

			Span<int> remainingArtifacts = stackalloc int[items.Length];
			int remCount = 0;
			for (int i = 0; i < items.Length; i++) {
				if (itemPlaced[i] != 0 || items[i].Kind != SlotItemKind.Artifact) {
					continue;
				}
				if (items[i].PriorityTier == ArtifactPriorityTier.Sacrifice
					|| items[i].IsDisabledByWeaponOrDual) {
					continue;
				}
				remainingArtifacts[remCount++] = i;
			}

			for (int i = 1; i < remCount; i++) {
				int keyIdx = remainingArtifacts[i];
				int keyRank = GetArtifactAssignmentPriority(items[keyIdx]);
				int j = i - 1;
				while (j >= 0 && GetArtifactAssignmentPriority(items[remainingArtifacts[j]]) < keyRank) {
					remainingArtifacts[j + 1] = remainingArtifacts[j];
					j--;
				}
				remainingArtifacts[j + 1] = keyIdx;
			}

			for (int r = 0; r < remCount; r++) {
				int artIdx = remainingArtifacts[r];
				SlotItemSnapshot art = items[artIdx];
				int neededLevels = Math.Max(0, art.MaxLevel - art.BaseLevel);
				double weight = ObjectiveEvaluator.GetNormalizedWeight(art);
				int bestSlot = -1;
				int bestSlotScore = int.MinValue;

				for (int s = 0; s < unlockedSlots; s++) {
					if (workingSlots[s] >= 0) {
						continue;
					}
					int delta = slotDelta[s];
					int disp = art.BaseLevel + delta;
					bool critOk = slotIgnore[s] > 0 || IsStaticSlotConstraintMet(snapshot, s, art.ConstraintType);
					bool disabled = slotDisable[s] > 0 || disp <= -1 || !critOk;

					int sScore = 0;
					if (disabled) {
						sScore -= ObjectiveEvaluator.DeactivationBasePenalty + (Math.Abs(disp) * 500);
					} else {
						int eff = Math.Clamp(disp, 0, art.MaxLevel);
						sScore += (int)Math.Round(
							(ObjectiveEvaluator.BaseActiveArtifactScore
								+ (eff * ObjectiveEvaluator.EffectiveLevelScoreMultiplier)
								+ (art.MaxLevel > 0 && eff == art.MaxLevel
									? ObjectiveEvaluator.MaxLevelReachedBonus
									: 0))
							* weight
						);
						if (delta > neededLevels) {
							int wastedExcess = delta - neededLevels;
							sScore -= wastedExcess * (neededLevels == 0 ? 180 : 65);
						}
						if (art.IsElementalByRowCharm && art.RowCategoryIds.Length > 0) {
							int rowIdx = snapshot.Slots[s].Y % art.RowCategoryIds.Length;
							int catId = art.RowCategoryIds[rowIdx];
							if (snapshot.PreferredChalcedonyCategoryId >= 0) {
								sScore += catId == snapshot.PreferredChalcedonyCategoryId
									? ObjectiveEvaluator.ChalcedonyTargetMatchBonus
									: -ObjectiveEvaluator.ChalcedonyTargetMismatchPenalty;
							}
						}
					}

					if (reqCharm[s] != 0) {
						sScore += 1200;
					}
					if (sScore > bestSlotScore) {
						bestSlotScore = sScore;
						bestSlot = s;
					}
				}

				if (bestSlot >= 0) {
					workingSlots[bestSlot] = artIdx;
					itemPlaced[artIdx] = 1;
				}
			}

			for (int i = 0; i < items.Length; i++) {
				if (itemPlaced[i] != 0 || items[i].Kind != SlotItemKind.Artifact) {
					continue;
				}
				int bestSinkSlot = -1;
				int bestSinkScore = int.MinValue;
				for (int s = 0; s < unlockedSlots; s++) {
					if (workingSlots[s] >= 0) {
						continue;
					}
					int sinkScore = 0;
					if (reqCharm[s] != 0) {
						sinkScore += 2000;
					}
					if (posLevel[s] > 0) {
						sinkScore -= posLevel[s] * 500;
					} else if (negLevel[s] > 0 || slotDisable[s] > 0) {
						sinkScore += (negLevel[s] * 120) + (slotDisable[s] * 180);
					}
					if (sinkScore > bestSinkScore) {
						bestSinkScore = sinkScore;
						bestSinkSlot = s;
					}
				}
				if (bestSinkSlot >= 0) {
					workingSlots[bestSinkSlot] = i;
					itemPlaced[i] = 1;
				}
			}
		}

		private static void RunArtifactOnlyLocalPolish(
			InventorySnapshot snapshot,
			Span<int> workingSlots,
			ReadOnlySpan<int> workingRotations,
			ref int evaluations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			int bestScore = ObjectiveEvaluator.Evaluate(snapshot, workingSlots, workingRotations);
			evaluations++;

			for (int pass = 0; pass < 2; pass++) {
				bool improved = false;
				for (int sA = 0; sA < unlockedSlots; sA++) {
					if (pinnedSlots[sA]) {
						continue;
					}
					int itemA = workingSlots[sA];
					if (itemA >= 0 && items[itemA].Kind == SlotItemKind.StoneTablet) {
						continue;
					}
					for (int sB = sA + 1; sB < unlockedSlots; sB++) {
						if (pinnedSlots[sB]) {
							continue;
						}
						int itemB = workingSlots[sB];
						if (itemB >= 0 && items[itemB].Kind == SlotItemKind.StoneTablet) {
							continue;
						}
						if (itemA == itemB) {
							continue;
						}

						workingSlots[sA] = itemB;
						workingSlots[sB] = itemA;
						int candScore = ObjectiveEvaluator.Evaluate(snapshot, workingSlots, workingRotations);
						evaluations++;
						if (candScore > bestScore) {
							bestScore = candScore;
							itemA = workingSlots[sA];
							improved = true;
						} else {
							workingSlots[sA] = itemA;
							workingSlots[sB] = itemB;
						}
					}
				}
				if (!improved) {
					break;
				}
			}
		}

		private static void RunTabletCollisionRepairPass(
			InventorySnapshot snapshot,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int bestDps,
			ref int evaluations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			int baselineFieldScore = EvaluatePureTabletFieldScore(
				snapshot,
				bestSlots,
				bestRotations,
				out int baselineCollisions
			);
			if (baselineCollisions == 0) {
				return;
			}

			Span<int> tabletOnlySlots = stackalloc int[unlockedSlots];
			Span<int> workingSlots = stackalloc int[unlockedSlots];
			Span<int> workingRotations = stackalloc int[itemCount];

			for (int pass = 0; pass < 2; pass++) {
				bool repairedInPass = false;

				for (int sTab = 0; sTab < unlockedSlots; sTab++) {
					if (pinnedSlots[sTab]) {
						continue;
					}
					int tabletIdx = bestSlots[sTab];
					if (tabletIdx < 0 || items[tabletIdx].Kind != SlotItemKind.StoneTablet) {
						continue;
					}

					SlotItemSnapshot tablet = items[tabletIdx];
					int maxRot = tablet.IsRotatable ? 4 : 1;

					for (int candSlot = 0; candSlot < unlockedSlots; candSlot++) {
						if (pinnedSlots[candSlot]) {
							continue;
						}

						for (int r = 0; r < maxRot; r++) {
							int rot = tablet.IsRotatable ? r : (bestRotations[tabletIdx] & 3);
							if (candSlot == sTab && rot == bestRotations[tabletIdx]) {
								continue;
							}
							if (tablet.IsRotatable && IsRedundantTabletRotation(tablet, candSlot, rot)) {
								continue;
							}

							tabletOnlySlots.Fill(-1);
							for (int s = 0; s < unlockedSlots; s++) {
								int idx = bestSlots[s];
								if (idx >= 0 && (pinnedSlots[s] || items[idx].Kind == SlotItemKind.StoneTablet)) {
									tabletOnlySlots[s] = idx;
								}
							}
							bestRotations.AsSpan(0, itemCount).CopyTo(workingRotations);

							int displacedTablet = tabletOnlySlots[candSlot];
							tabletOnlySlots[sTab] = displacedTablet;
							tabletOnlySlots[candSlot] = tabletIdx;
							workingRotations[tabletIdx] = rot;

							int candFieldScore = EvaluatePureTabletFieldScore(
								snapshot,
								tabletOnlySlots,
								workingRotations,
								out int candCollisions
							);
							if (candCollisions > baselineCollisions
								&& candFieldScore < baselineFieldScore - 400) {
								continue;
							}

							tabletOnlySlots.CopyTo(workingSlots);
							AssignArtifactsToTabletField(snapshot, workingSlots, workingRotations);
							RunArtifactOnlyLocalPolish(snapshot, workingSlots, workingRotations, ref evaluations);

							int score = ObjectiveEvaluator.EvaluateWithDps(
								snapshot,
								workingSlots,
								workingRotations,
								out int dps
							);
							evaluations++;

							if (IsBetterPolishCandidate(score, dps, bestScore, bestDps)) {
								bestScore = score;
								bestDps = dps;
								workingSlots.CopyTo(bestSlots);
								workingRotations.CopyTo(bestRotations);
								baselineFieldScore = candFieldScore;
								baselineCollisions = candCollisions;
								repairedInPass = true;
								if (baselineCollisions == 0) {
									return;
								}
								break;
							}
						}
						if (repairedInPass) {
							break;
						}
					}
					if (repairedInPass) {
						break;
					}
				}

				if (!repairedInPass || baselineCollisions == 0) {
					break;
				}
			}
		}

		private static bool IsStaticSlotConstraintMet(
			InventorySnapshot snapshot,
			int slotIndex,
			ArtifactConstraintType constraintType
		) {
			if (constraintType == ArtifactConstraintType.None) {
				return true;
			}
			SlotCoord coord = snapshot.Slots[slotIndex];
			int width = snapshot.Width;
			int storage = snapshot.UnlockedSlots;
			return constraintType switch {
				ArtifactConstraintType.TopInInventory => coord.Y == 0,
				ArtifactConstraintType.BottomInInventory => slotIndex >= storage - width,
				ArtifactConstraintType.SideEnd => coord.X == 0 || coord.X == width - 1,
				ArtifactConstraintType.Outlined => coord.X <= 0
					|| coord.Y <= 0
					|| coord.X >= width - 1
					|| slotIndex >= storage - width,
				ArtifactConstraintType.Inside => coord.X > 0
					&& coord.Y > 0
					&& coord.X < width - 1
					&& (slotIndex + width + 1) <= (storage - 1),
				ArtifactConstraintType.FullHp => snapshot.IsFullHpSatisfied,
				_ => true
			};
		}

		private static int GetArtifactAssignmentPriority(SlotItemSnapshot item) {
			int constraintBonus = item.ConstraintType != ArtifactConstraintType.None ? 600 : 0;
			int rowElemBonus = item.IsElementalByRowCharm ? 500 : 0;
			int whitePaperBonus = item.IsWhitePaperCharm ? 420 : 0;
			int weightRank = (int)Math.Round(ObjectiveEvaluator.GetNormalizedWeight(item) * 200.0);
			int capRank = Math.Max(0, item.MaxLevel - item.BaseLevel) * 45;
			int attackBonus = item.IsAttackableCharm ? 60 : 0;
			return constraintBonus + rowElemBonus + whitePaperBonus + weightRank + capRank + attackBonus;
		}

		private static void SortTabletsByFieldImpact(
			SlotItemSnapshot[] items,
			Span<int> tablets,
			int count
		) {
			for (int i = 1; i < count; i++) {
				int keyIdx = tablets[i];
				int keyImpact = GetTabletFieldImpactScore(items[keyIdx]);
				int j = i - 1;
				while (j >= 0 && GetTabletFieldImpactScore(items[tablets[j]]) < keyImpact) {
					tablets[j + 1] = tablets[j];
					j--;
				}
				tablets[j + 1] = keyIdx;
			}
		}

		private static int GetTabletFieldImpactScore(SlotItemSnapshot tablet) {
			if (tablet.PlacementRules.Length == 0) {
				return 0;
			}
			TabletPlacementRule rule = tablet.PlacementRules[0];
			int score = rule.HasPlacedCondition ? 2000 : 0;
			PrecomputedEffect[] effects = rule.Effects;
			for (int e = 0; e < effects.Length; e++) {
				PrecomputedEffect eff = effects[e];
				switch (eff.EffectType) {
					case TabletEffectType.MultiplyConstLevel: {
						score += 800;
						break;
					}
					case TabletEffectType.Disable: {
						score += 350;
						break;
					}
					case TabletEffectType.IgnoreCriteria: {
						score += 250;
						break;
					}
					case TabletEffectType.IncreaseConstLevel: {
						score += Math.Abs(eff.LevelParam) * 120;
						break;
					}
				}
			}
			return score;
		}

		private static int FindItemSlot(ReadOnlySpan<int> slotToItem, int targetItemIdx) {
			for (int s = 0; s < slotToItem.Length; s++) {
				if (slotToItem[s] == targetItemIdx) {
					return s;
				}
			}
			return -1;
		}

		private static void SortDescending(Span<int> values, int count) {
			for (int i = 1; i < count; i++) {
				int key = values[i];
				int j = i - 1;
				while (j >= 0 && values[j] < key) {
					values[j + 1] = values[j];
					j--;
				}
				values[j + 1] = key;
			}
		}
	}
}
