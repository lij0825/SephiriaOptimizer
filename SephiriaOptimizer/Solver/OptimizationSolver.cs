using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SephiriaOptimizer.Execution;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Solver {
	public sealed class OptimizationSolver {
		public const int SmallScaleMaxSlots = 12;
		public const int SmallScaleMaxItems = 10;
		public const int MaxBranchNodes = 80_000;
		public const int LahcHistoryLength = 256;
		public const int InitialLahcMargin = 1800;
		public const int ReheatMargin = 2200;
		public const int DefaultTimeBudgetMs = 370;
		public const int MaxEliteCandidates = 3;
		private const int MultiStartCount = 4;
		private const int MaxPolishPasses = 12;
		private const int TargetedCycleSlotCap = 14;
		private const double DpsTieEpsilon = 0.005;

		private sealed class EliteCandidatePool {
			private readonly int capacity;
			private readonly int unlockedSlots;
			private readonly int itemCount;
			private readonly int[][] slotsPool;
			private readonly int[][] rotationsPool;
			private readonly CandidateMetrics[] metricsPool;
			private int count;

			public EliteCandidatePool(int capacity, int unlockedSlots, int itemCount) {
				this.capacity = Math.Max(1, capacity);
				this.unlockedSlots = unlockedSlots;
				this.itemCount = itemCount;
				slotsPool = new int[this.capacity][];
				rotationsPool = new int[this.capacity][];
				metricsPool = new CandidateMetrics[this.capacity];
				for (int i = 0; i < this.capacity; i++) {
					slotsPool[i] = new int[unlockedSlots];
					rotationsPool[i] = new int[itemCount];
				}
			}

			public int Count => count;

			public CandidateMetrics GetMetrics(int index) => metricsPool[index];

			public int[] GetSlots(int index) => slotsPool[index];

			public int[] GetRotations(int index) => rotationsPool[index];

			public void CopyTo(int index, Span<int> destSlots, Span<int> destRotations) {
				slotsPool[index].AsSpan(0, unlockedSlots).CopyTo(destSlots);
				rotationsPool[index].AsSpan(0, itemCount).CopyTo(destRotations);
			}

			public void CopyBestTo(int[] destSlots, int[] destRotations, out int bestScore) {
				slotsPool[0].AsSpan(0, unlockedSlots).CopyTo(destSlots);
				rotationsPool[0].AsSpan(0, itemCount).CopyTo(destRotations);
				bestScore = metricsPool[0].Score;
			}

			public bool TryOffer(
				InventorySnapshot snapshot,
				ReadOnlySpan<int> initialSlots,
				ReadOnlySpan<int> initialRotations,
				ReadOnlySpan<int> candSlots,
				ReadOnlySpan<int> candRotations,
				int fastScore,
				ref int evaluations,
				bool countEval = false,
				bool forceEval = false
			) {
				if (count >= capacity && !forceEval && fastScore < metricsPool[count - 1].Score - 180) {
					return false;
				}

				int score = ObjectiveEvaluator.EvaluateWithMetrics(
					snapshot,
					candSlots,
					candRotations,
					out double exactDps,
					out int hardViolations,
					out int utilitySum
				);
				if (countEval) {
					evaluations++;
				}

				if (count > 0 && metricsPool[0].HardViolationCount == 0 && hardViolations > 0
					&& exactDps <= metricsPool[0].ExactEstimatedDps + DpsTieEpsilon) {
					return false;
				}

				CycleDecomposition.CountExecutionCost(
					snapshot,
					initialSlots,
					candSlots,
					initialRotations,
					candRotations,
					out int swapCount,
					out int rotateClickCount
				);
				ulong canonicalHash = ObjectiveEvaluator.ComputeCanonicalLayoutHash(
					snapshot,
					candSlots,
					candRotations
				);
				CandidateMetrics candidateMetrics = new CandidateMetrics(
					score,
					exactDps,
					hardViolations,
					utilitySum,
					swapCount,
					rotateClickCount,
					canonicalHash
				);

				for (int i = 0; i < count; i++) {
					if (metricsPool[i].CanonicalHash == canonicalHash
						&& ObjectiveEvaluator.AreLayoutsCanonicallyEqual(
							snapshot,
							candSlots,
							candRotations,
							slotsPool[i],
							rotationsPool[i]
						)) {
						if (CandidateMetrics.CompareLexicographical(in candidateMetrics, in metricsPool[i]) < 0) {
							candSlots.Slice(0, unlockedSlots).CopyTo(slotsPool[i]);
							candRotations.Slice(0, itemCount).CopyTo(rotationsPool[i]);
							metricsPool[i] = candidateMetrics;
							SortEntries();
							return true;
						}
						return false;
					}
				}

				if (count < capacity) {
					candSlots.Slice(0, unlockedSlots).CopyTo(slotsPool[count]);
					candRotations.Slice(0, itemCount).CopyTo(rotationsPool[count]);
					metricsPool[count] = candidateMetrics;
					count++;
					SortEntries();
					return true;
				}

				int worstIdx = count - 1;
				if (CandidateMetrics.CompareLexicographical(in candidateMetrics, in metricsPool[worstIdx]) < 0) {
					candSlots.Slice(0, unlockedSlots).CopyTo(slotsPool[worstIdx]);
					candRotations.Slice(0, itemCount).CopyTo(rotationsPool[worstIdx]);
					metricsPool[worstIdx] = candidateMetrics;
					SortEntries();
					return true;
				}

				return false;
			}

			public void ReplaceEntry(
				int index,
				ReadOnlySpan<int> newSlots,
				ReadOnlySpan<int> newRotations,
				in CandidateMetrics newMetrics
			) {
				newSlots.Slice(0, unlockedSlots).CopyTo(slotsPool[index]);
				newRotations.Slice(0, itemCount).CopyTo(rotationsPool[index]);
				metricsPool[index] = newMetrics;
			}

			public void DeduplicateAndSort(InventorySnapshot snapshot) {
				for (int i = 0; i < count; i++) {
					for (int j = i + 1; j < count; j++) {
						if (metricsPool[i].CanonicalHash != metricsPool[j].CanonicalHash
							|| !ObjectiveEvaluator.AreLayoutsCanonicallyEqual(
								snapshot,
								slotsPool[i],
								rotationsPool[i],
								slotsPool[j],
								rotationsPool[j]
							)) {
							continue;
						}

						if (CandidateMetrics.CompareLexicographical(in metricsPool[j], in metricsPool[i]) < 0) {
							SwapEntries(i, j);
						}

						int last = count - 1;
						if (j != last) {
							SwapEntries(j, last);
						}
						count--;
						j--;
					}
				}
				SortEntries();
			}

			private void SortEntries() {
				for (int i = 1; i < count; i++) {
					int j = i;
					while (j > 0 && CandidateMetrics.CompareLexicographical(in metricsPool[j], in metricsPool[j - 1]) < 0) {
						SwapEntries(j - 1, j);
						j--;
					}
				}
			}

			private void SwapEntries(int leftIdx, int rightIdx) {
				int[] tmpSlots = slotsPool[leftIdx];
				slotsPool[leftIdx] = slotsPool[rightIdx];
				slotsPool[rightIdx] = tmpSlots;

				int[] tmpRots = rotationsPool[leftIdx];
				rotationsPool[leftIdx] = rotationsPool[rightIdx];
				rotationsPool[rightIdx] = tmpRots;

				CandidateMetrics tmpMetrics = metricsPool[leftIdx];
				metricsPool[leftIdx] = metricsPool[rightIdx];
				metricsPool[rightIdx] = tmpMetrics;
			}
		}

		public OptimizationResult Solve(InventorySnapshot snapshot, int timeBudgetMs = DefaultTimeBudgetMs) {
			Stopwatch stopwatch = Stopwatch.StartNew();
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;

			int[] initialSlots = new int[unlockedSlots];
			Array.Copy(snapshot.InitialSlotToItemIndex, initialSlots, unlockedSlots);
			int[] initialRotations = new int[itemCount];
			Array.Copy(snapshot.InitialItemRotations, initialRotations, itemCount);

			int initialScore = ObjectiveEvaluator.EvaluateWithMetrics(
				snapshot,
				initialSlots,
				initialRotations,
				out double initialExactDps,
				out int initialViolations,
				out int initialUtilitySum
			);
			int initialEstimatedDps = Math.Max(0, (int)Math.Round(initialExactDps));
			SlotEvaluationDetail[] initialDetails = ObjectiveEvaluator.EvaluateDetailed(
				snapshot,
				initialSlots,
				initialRotations
			);

			DpsBreakdown currentBreakdown = snapshot.CurrentBreakdown;

			if (unlockedSlots <= 1 || itemCount == 0) {
				int[] initialItemSlots = BuildItemToSlotMap(initialSlots, itemCount);
				ulong initHash = ObjectiveEvaluator.ComputeCanonicalLayoutHash(
					snapshot,
					initialSlots,
					initialRotations
				);
				CandidateMetrics initMetrics = new CandidateMetrics(
					initialScore,
					initialExactDps,
					initialViolations,
					initialUtilitySum,
					0,
					0,
					initHash
				);
				OptimizationCandidate singleCandidate = new OptimizationCandidate(
					1,
					in initMetrics,
					in currentBreakdown,
					initialSlots,
					initialItemSlots,
					initialRotations,
					initialDetails,
					"기본 추천"
				);
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
					currentBreakdown,
					new[] { singleCandidate }
				);
			}

			EliteCandidatePool elitePool = new EliteCandidatePool(MaxEliteCandidates, unlockedSlots, itemCount);
			int evaluations = 1;
			elitePool.TryOffer(
				snapshot,
				initialSlots,
				initialRotations,
				initialSlots,
				initialRotations,
				initialScore,
				ref evaluations,
				countEval: false,
				forceEval: true
			);

			int[][] seedSlots = new int[MultiStartCount][];
			int[][] seedRotations = new int[MultiStartCount][];
			int[] seedScores = new int[MultiStartCount];
			ulong[] seedHashes = new ulong[MultiStartCount];
			for (int i = 0; i < MultiStartCount; i++) {
				seedSlots[i] = new int[unlockedSlots];
				seedRotations[i] = new int[itemCount];
			}
			int seedCount = 0;

			RunMultiStartSeedSynthesis(
				snapshot,
				initialSlots,
				initialRotations,
				initialScore,
				elitePool,
				seedSlots,
				seedRotations,
				seedScores,
				seedHashes,
				ref seedCount,
				ref evaluations
			);

			int[] bestSlots = new int[unlockedSlots];
			int[] bestRotations = new int[itemCount];
			elitePool.CopyBestTo(bestSlots, bestRotations, out int bestScore);

			bool isSmallScale = unlockedSlots <= SmallScaleMaxSlots && itemCount <= SmallScaleMaxItems;
			bool usedExactBranchAndBound = false;

			if (isSmallScale) {
				int branchBudgetMs = Math.Max(45, (timeBudgetMs * 55) / 100);
				bool limitExceeded = SolveBranchAndBound(
					snapshot,
					stopwatch,
					branchBudgetMs,
					initialSlots,
					initialRotations,
					elitePool,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref evaluations
				);
				usedExactBranchAndBound = !limitExceeded;
				elitePool.CopyBestTo(bestSlots, bestRotations, out bestScore);
			}

			if (!isSmallScale || !usedExactBranchAndBound) {
				int polishReserveMs = Math.Max(60, Math.Min(150, (timeBudgetMs * 28) / 100));
				int remainingSaBudgetMs = Math.Max(
					45,
					timeBudgetMs - (int)stopwatch.ElapsedMilliseconds - polishReserveMs
				);
				SolveMultiStartSimulatedAnnealing(
					snapshot,
					stopwatch,
					remainingSaBudgetMs,
					seedSlots,
					seedRotations,
					seedCount,
					initialSlots,
					initialRotations,
					elitePool,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref evaluations
				);
				elitePool.CopyBestTo(bestSlots, bestRotations, out bestScore);
			}

			if (HasAnyStoneTablet(snapshot)) {
				RunTabletCollisionRepairPass(
					snapshot,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref evaluations,
					elitePool,
					initialSlots,
					initialRotations
				);
			}

			int polishDeadlineMs = Math.Max(timeBudgetMs + 220, 480);
			RunExhaustiveMultiPassPolish(
				snapshot,
				stopwatch,
				polishDeadlineMs,
				bestSlots,
				bestRotations,
				ref bestScore,
				ref evaluations,
				elitePool,
				initialSlots,
				initialRotations,
				maxPasses: MaxPolishPasses
			);

			int eliteSnapCount = elitePool.Count;
			int[] altSlotArr = new int[unlockedSlots];
			int[] altRotArr = new int[itemCount];
			for (int e = 1; e < eliteSnapCount; e++) {
				elitePool.CopyTo(e, altSlotArr, altRotArr);
				int altScore = elitePool.GetMetrics(e).Score;
				RunExhaustiveMultiPassPolish(
					snapshot,
					stopwatch,
					polishDeadlineMs,
					altSlotArr,
					altRotArr,
					ref altScore,
					ref evaluations,
					elitePool,
					initialSlots,
					initialRotations,
					maxPasses: 3
				);
			}

			elitePool.CopyBestTo(bestSlots, bestRotations, out bestScore);
			if (HasAnyStoneTablet(snapshot)) {
				RunTabletCollisionRepairPass(
					snapshot,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref evaluations,
					elitePool,
					initialSlots,
					initialRotations
				);
			}

			ExploreEqualOptimaAndMinimizeCost(
				snapshot,
				stopwatch,
				polishDeadlineMs,
				elitePool,
				initialSlots,
				initialRotations,
				ref evaluations
			);

			IReadOnlyList<OptimizationCandidate> topCandidates = FinalizeTopCandidates(
				snapshot,
				elitePool,
				initialSlots,
				initialRotations
			);
			OptimizationCandidate bestCandidate = topCandidates[0];
			stopwatch.Stop();

			return new OptimizationResult(
				initialScore,
				bestCandidate.Metrics.Score,
				bestCandidate.SlotToItemIndex,
				bestCandidate.ItemToSlotIndex,
				bestCandidate.ItemRotations,
				initialDetails,
				bestCandidate.SlotDetails,
				usedExactBranchAndBound,
				evaluations,
				stopwatch.Elapsed.TotalMilliseconds,
				initialEstimatedDps,
				bestCandidate.Metrics.EstimatedDps,
				currentBreakdown,
				bestCandidate.Breakdown,
				topCandidates
			);
		}

		private static bool SolveBranchAndBound(
			InventorySnapshot snapshot,
			Stopwatch stopwatch,
			int timeBudgetMs,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
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
				initialSlots,
				initialRotations,
				elitePool,
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
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
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
				if (score >= bestScore - 30) {
					elitePool.TryOffer(
						snapshot,
						initialSlots,
						initialRotations,
						workingSlots,
						workingRotations,
						score,
						ref evaluations,
						countEval: false,
						forceEval: score >= bestScore
					);
				}
				if (score > bestScore) {
					bestScore = score;
					workingSlots.CopyTo(bestSlots);
					workingRotations.CopyTo(bestRotations);
				}
				return;
			}

			int optimisticRemainingBound = suffixPotentialSum[depth];
			if (currentPartialScore + optimisticRemainingBound < bestScore) {
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
						if (currentPartialScore + rotBoundContribution + suffixPotentialSum[depth + 1] < bestScore) {
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
							initialSlots,
							initialRotations,
							elitePool,
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
					if (currentPartialScore + placementContribution + suffixPotentialSum[depth + 1] >= bestScore) {
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
							initialSlots,
							initialRotations,
							elitePool,
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

		private static void SolveMultiStartSimulatedAnnealing(
			InventorySnapshot snapshot,
			Stopwatch stopwatch,
			int totalSaBudgetMs,
			int[][] seedSlots,
			int[][] seedRotations,
			int seedCount,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
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
			Span<int> allUnpinnedTablets = stackalloc int[itemCount];
			int unpinnedTabletCount = 0;
			Span<int> needleItems = stackalloc int[itemCount];
			int needleItemCount = 0;
			Span<int> attackableItems = stackalloc int[itemCount];
			int attackableItemCount = 0;
			Span<int> crystalItems = stackalloc int[itemCount];
			int crystalItemCount = 0;
			Span<int> whitePaperItems = stackalloc int[itemCount];
			int whitePaperItemCount = 0;

			for (int i = 0; i < itemCount; i++) {
				SlotItemSnapshot item = items[i];
				int initSlot = item.InitialSlotIndex;
				bool isPinned = initSlot >= 0 && initSlot < unlockedSlots && pinnedSlots[initSlot];

				if (!isPinned && item.Kind == SlotItemKind.StoneTablet) {
					allUnpinnedTablets[unpinnedTabletCount++] = i;
					if (item.IsRotatable) {
						rotatableTablets[rotatableTabletCount++] = i;
					}
				}
				if (!isPinned && item.Kind == SlotItemKind.Artifact && item.HasDependencyTarget) {
					needleItems[needleItemCount++] = i;
				}
				if (item.Kind == SlotItemKind.Artifact
					&& item.IsAttackableCharm
					&& item.PriorityTier != ArtifactPriorityTier.Sacrifice) {
					attackableItems[attackableItemCount++] = i;
				}
				if (!isPinned && item.Kind == SlotItemKind.Artifact && item.IsCrystalOfHarmony) {
					crystalItems[crystalItemCount++] = i;
				}
				if (!isPinned && item.Kind == SlotItemKind.Artifact && item.IsWhitePaperCharm) {
					whitePaperItems[whitePaperItemCount++] = i;
				}
			}

			if (movableSlotCount <= 1 && rotatableTabletCount == 0) {
				return;
			}

			SortAttackableItemsByTargetPriority(snapshot, items, attackableItems, attackableItemCount);

			Span<int> currentSlots = stackalloc int[unlockedSlots];
			Span<int> currentRotations = stackalloc int[itemCount];
			Span<int> currentItemToSlot = stackalloc int[itemCount];
			Span<int> history = stackalloc int[LahcHistoryLength];

			long totalStartTicks = stopwatch.ElapsedTicks;
			long totalDeadlineTicks = totalStartTicks + ((long)Math.Max(20, totalSaBudgetMs) * Stopwatch.Frequency / 1000L);
			int actualStarts = MultiStartCount;

			for (int startIdx = 0; startIdx < actualStarts; startIdx++) {
				long nowTicks = stopwatch.ElapsedTicks;
				if (startIdx > 0 && nowTicks >= totalDeadlineTicks) {
					break;
				}

				long remainingTicks = Math.Max(1L, totalDeadlineTicks - nowTicks);
				long sliceTicks = remainingTicks / (actualStarts - startIdx);
				long startDeadlineTicks = nowTicks + sliceTicks;

				if (startIdx == 0) {
					bestSlots.AsSpan(0, unlockedSlots).CopyTo(currentSlots);
					bestRotations.AsSpan(0, itemCount).CopyTo(currentRotations);
				} else if (startIdx - 1 < seedCount) {
					seedSlots[startIdx - 1].AsSpan(0, unlockedSlots).CopyTo(currentSlots);
					seedRotations[startIdx - 1].AsSpan(0, itemCount).CopyTo(currentRotations);
				} else {
					bestSlots.AsSpan(0, unlockedSlots).CopyTo(currentSlots);
					bestRotations.AsSpan(0, itemCount).CopyTo(currentRotations);
					uint perturbSeed = 0x517CC1B7u ^ ((uint)startIdx * 0x9E3779B9u) ^ (uint)unlockedSlots;
					ApplyMacroPerturbation(
						snapshot,
						movableSlots.Slice(0, movableSlotCount),
						rotatableTablets.Slice(0, rotatableTabletCount),
						currentSlots,
						currentRotations,
						ref perturbSeed
					);
				}

				SyncItemToSlot(currentSlots, currentItemToSlot);
				int currentScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;
				if (currentScore >= bestScore - 30) {
					elitePool.TryOffer(
						snapshot,
						initialSlots,
						initialRotations,
						currentSlots,
						currentRotations,
						currentScore,
						ref evaluations,
						countEval: false,
						forceEval: currentScore >= bestScore
					);
				}
				if (currentScore > bestScore) {
					bestScore = currentScore;
					currentSlots.CopyTo(bestSlots);
					currentRotations.CopyTo(bestRotations);
				}

				history.Fill(currentScore - InitialLahcMargin);

				const double initialTemperature = 2200.0;
				const double minimumTemperature = 15.0;
				const double coolingFactor = 0.9960;
				const double reheatTemperature = 1350.0;
				const int stagnationLimit = 1200;

				double temperature = initialTemperature;
				int iterationsSinceImprovement = 0;
				int iteration = 0;
				uint rngState = (0x9E3779B9u * (uint)(startIdx + 1)) ^ (uint)unlockedSlots ^ ((uint)itemCount << 16);
				bool hasNeedleMoves = needleItemCount > 0 && attackableItemCount > 0 && unlockedSlots > width;
				bool hasCrystalMoves = crystalItemCount > 0 && unlockedSlots >= 9;
				bool hasWhitePaperMoves = whitePaperItemCount > 0 && unlockedSlots >= 3;
				bool hasArrangementMoves = snapshot.TalentRules.ArrangementBonusStat > 0
					&& snapshot.TalentRules.ArrangementPatterns.Length > 0
					&& movableSlotCount >= 2;

				while (stopwatch.ElapsedTicks < startDeadlineTicks) {
					for (int step = 0; step < 64; step++) {
						uint mutationRoll = NextRandomUInt(ref rngState) % 100u;

						if (rotatableTabletCount > 0 && (movableSlotCount <= 1 || mutationRoll < 14u)) {
							int randIdx = (int)(NextRandomUInt(ref rngState) % (uint)rotatableTabletCount);
							int tabletIdx = rotatableTablets[randIdx];
							int oldRotation = currentRotations[tabletIdx];
							int deltaRot = 1 + (int)(NextRandomUInt(ref rngState) % 3u);
							int newRotation = (oldRotation + deltaRot) & 3;
							currentRotations[tabletIdx] = newRotation;

							int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
							evaluations++;

							if (!TryAcceptCandidate(
								snapshot,
								initialSlots,
								initialRotations,
								elitePool,
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
								bestRotations,
								ref evaluations
							)) {
								currentRotations[tabletIdx] = oldRotation;
								iterationsSinceImprovement++;
							}
						} else if (hasNeedleMoves && mutationRoll >= 14u && mutationRoll < 28u) {
							ExecuteNeedleOrCompassMutation(
								snapshot,
								needleItems.Slice(0, needleItemCount),
								attackableItems.Slice(0, attackableItemCount),
								movableVerticalTopSlots.Slice(0, movableVerticalPairCount),
								movableSlots.Slice(0, movableSlotCount),
								initialSlots,
								initialRotations,
								elitePool,
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
						} else if (hasCrystalMoves && mutationRoll >= 28u && mutationRoll < 36u) {
							ExecuteCrystalHubMutation(
								snapshot,
								crystalItems.Slice(0, crystalItemCount),
								movableSlots.Slice(0, movableSlotCount),
								initialSlots,
								initialRotations,
								elitePool,
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
						} else if (hasWhitePaperMoves && mutationRoll >= 36u && mutationRoll < 44u) {
							ExecuteWhitePaperSandwichMutation(
								snapshot,
								whitePaperItems.Slice(0, whitePaperItemCount),
								movableSlots.Slice(0, movableSlotCount),
								initialSlots,
								initialRotations,
								elitePool,
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
						} else if (hasArrangementMoves && mutationRoll >= 44u && mutationRoll < 49u) {
							ExecuteArrangementClusterMutation(
								snapshot,
								initialSlots,
								initialRotations,
								elitePool,
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
						} else if (unpinnedTabletCount > 0 && movableSlotCount >= 2
							&& mutationRoll >= 49u && mutationRoll < 56u) {
							ExecuteTabletRelocateRotateMutation(
								snapshot,
								allUnpinnedTablets.Slice(0, unpinnedTabletCount),
								movableSlots.Slice(0, movableSlotCount),
								initialSlots,
								initialRotations,
								elitePool,
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
						} else if (unpinnedTabletCount >= 2 && mutationRoll >= 56u && mutationRoll < 61u) {
							ExecuteTabletPairSwapMutation(
								snapshot,
								allUnpinnedTablets.Slice(0, unpinnedTabletCount),
								initialSlots,
								initialRotations,
								elitePool,
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
						} else if (movableSlotCount >= 3 && mutationRoll >= 61u && mutationRoll < 74u) {
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
								snapshot,
								initialSlots,
								initialRotations,
								elitePool,
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
								bestRotations,
								ref evaluations
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
								snapshot,
								initialSlots,
								initialRotations,
								elitePool,
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
								bestRotations,
								ref evaluations
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
		}

		private static void ApplyMacroPerturbation(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> movableSlots,
			ReadOnlySpan<int> rotatableTablets,
			Span<int> slots,
			Span<int> rotations,
			ref uint rngState
		) {
			if (movableSlots.Length >= 2) {
				int swaps = Math.Min(4, movableSlots.Length / 2 + 1);
				for (int s = 0; s < swaps; s++) {
					int a = movableSlots[(int)(NextRandomUInt(ref rngState) % (uint)movableSlots.Length)];
					int b = movableSlots[(int)(NextRandomUInt(ref rngState) % (uint)movableSlots.Length)];
					if (a != b) {
						int tmp = slots[a];
						slots[a] = slots[b];
						slots[b] = tmp;
					}
				}
			}
			if (rotatableTablets.Length > 0) {
				int tIdx = rotatableTablets[(int)(NextRandomUInt(ref rngState) % (uint)rotatableTablets.Length)];
				int delta = 1 + (int)(NextRandomUInt(ref rngState) % 3u);
				rotations[tIdx] = (rotations[tIdx] + delta) & 3;
			}
		}

		private static void ExecuteNeedleOrCompassMutation(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> needleItems,
			ReadOnlySpan<int> attackableItems,
			ReadOnlySpan<int> movableVerticalTopSlots,
			ReadOnlySpan<int> movableSlots,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
			Span<int> currentSlots,
			Span<int> currentRotations,
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
			int unlockedSlots = snapshot.UnlockedSlots;
			int width = snapshot.Width;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			int randNeedleIdx = (int)(NextRandomUInt(ref rngState) % (uint)needleItems.Length);
			int needleItemIdx = needleItems[randNeedleIdx];
			int needleSlot = currentItemToSlot[needleItemIdx];
			if (needleSlot < 0 || pinnedSlots[needleSlot]) {
				return;
			}

			SlotItemSnapshot needleItem = items[needleItemIdx];
			(sbyte X, sbyte Y)[] parentOffsets = needleItem.DependencyParentOffsets;
			int offsetCount = parentOffsets.Length;
			int chosenOffIdx = offsetCount > 1
				? (int)(NextRandomUInt(ref rngState) % (uint)offsetCount)
				: 0;
			int offX = offsetCount > 0 ? parentOffsets[chosenOffIdx].X : needleItem.DependencyOffsetX;
			int offY = offsetCount > 0 ? parentOffsets[chosenOffIdx].Y : needleItem.DependencyOffsetY;
			if (offX == 0 && offY == 0) {
				offY = -1;
			}

			SlotCoord nCoord = snapshot.Slots[needleSlot];
			int parentSlot = snapshot.CoordToSlotIndex(nCoord.X + offX, nCoord.Y + offY);
			int parentItemIdx = parentSlot >= 0 ? currentSlots[parentSlot] : -1;
			bool isPairedWithAttackable = parentSlot >= 0
				&& parentItemIdx >= 0
				&& items[parentItemIdx].Kind == SlotItemKind.Artifact
				&& items[parentItemIdx].IsAttackableCharm
				&& items[parentItemIdx].PriorityTier != ArtifactPriorityTier.Sacrifice;

			if (isPairedWithAttackable
				&& !pinnedSlots[parentSlot]
				&& offX == 0
				&& offY == -1
				&& movableVerticalTopSlots.Length >= 2
				&& (attackableItems.Length == 1 || (NextRandomUInt(ref rngState) % 4u) != 0u)) {
				int destPairIdx = (int)(NextRandomUInt(ref rngState) % (uint)movableVerticalTopSlots.Length);
				int destSlot = movableVerticalTopSlots[destPairIdx];
				if (destSlot == parentSlot) {
					destSlot = movableVerticalTopSlots[(destPairIdx + 1) % movableVerticalTopSlots.Length];
				}
				if (destSlot == parentSlot) {
					return;
				}
				ExecuteVerticalPairMutation(
					snapshot,
					parentSlot,
					needleSlot,
					destSlot,
					destSlot + width,
					initialSlots,
					initialRotations,
					elitePool,
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
				return;
			}

			int topPool = attackableItems.Length > 3 ? 3 : attackableItems.Length;
			int chosenRank = ((NextRandomUInt(ref rngState) & 3u) != 0u)
				? (int)(NextRandomUInt(ref rngState) % (uint)topPool)
				: (int)(NextRandomUInt(ref rngState) % (uint)attackableItems.Length);
			int targetAttackItemIdx = attackableItems[chosenRank];
			if (targetAttackItemIdx == needleItemIdx) {
				return;
			}

			int targetSlot = currentItemToSlot[targetAttackItemIdx];
			if (targetSlot < 0) {
				return;
			}

			SlotCoord tCoord = snapshot.Slots[targetSlot];
			int desiredNeedleSlot = snapshot.CoordToSlotIndex(tCoord.X - offX, tCoord.Y - offY);
			if (desiredNeedleSlot >= 0 && desiredNeedleSlot < unlockedSlots) {
				if (pinnedSlots[desiredNeedleSlot] || desiredNeedleSlot == needleSlot || targetSlot == needleSlot) {
					return;
				}
				int displacedItem = currentSlots[desiredNeedleSlot];
				currentSlots[desiredNeedleSlot] = needleItemIdx;
				currentSlots[needleSlot] = displacedItem;

				int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;

				if (TryAcceptCandidate(
					snapshot,
					initialSlots,
					initialRotations,
					elitePool,
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
					bestRotations,
					ref evaluations
				)) {
					currentItemToSlot[needleItemIdx] = desiredNeedleSlot;
					if (displacedItem >= 0) {
						currentItemToSlot[displacedItem] = needleSlot;
					}
				} else {
					currentSlots[needleSlot] = needleItemIdx;
					currentSlots[desiredNeedleSlot] = displacedItem;
					iterationsSinceImprovement++;
				}
				return;
			}

			if (!pinnedSlots[targetSlot] && movableSlots.Length >= 2) {
				int attemptStart = (int)(NextRandomUInt(ref rngState) % (uint)movableSlots.Length);
				int destRoot = -1;
				for (int attempt = 0; attempt < movableSlots.Length; attempt++) {
					int candRoot = movableSlots[(attemptStart + attempt) % movableSlots.Length];
					if (candRoot == targetSlot || candRoot == needleSlot) {
						continue;
					}
					SlotCoord cCoord = snapshot.Slots[candRoot];
					int candChild = snapshot.CoordToSlotIndex(cCoord.X - offX, cCoord.Y - offY);
					if (candChild >= 0 && candChild < unlockedSlots && !pinnedSlots[candChild]) {
						destRoot = candRoot;
						break;
					}
				}
				if (destRoot < 0) {
					return;
				}
				int displacedTopItem = currentSlots[destRoot];
				currentSlots[destRoot] = targetAttackItemIdx;
				currentSlots[targetSlot] = displacedTopItem;

				int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;

				if (TryAcceptCandidate(
					snapshot,
					initialSlots,
					initialRotations,
					elitePool,
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
					bestRotations,
					ref evaluations
				)) {
					currentItemToSlot[targetAttackItemIdx] = destRoot;
					if (displacedTopItem >= 0) {
						currentItemToSlot[displacedTopItem] = targetSlot;
					}
				} else {
					currentSlots[targetSlot] = targetAttackItemIdx;
					currentSlots[destRoot] = displacedTopItem;
					iterationsSinceImprovement++;
				}
			}
		}

		private static void ExecuteCrystalHubMutation(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> crystalItems,
			ReadOnlySpan<int> movableSlots,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
			Span<int> currentSlots,
			Span<int> currentRotations,
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
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;
			int crystalIdx = crystalItems[(int)(NextRandomUInt(ref rngState) % (uint)crystalItems.Length)];
			int crystalSlot = currentItemToSlot[crystalIdx];
			if (crystalSlot < 0 || pinnedSlots[crystalSlot]) {
				return;
			}

			bool pullNeighborIn = (NextRandomUInt(ref rngState) & 1u) != 0u;
			if (pullNeighborIn) {
				ulong mask = snapshot.MooreNeighborMasks[crystalSlot];
				int bestTargetNeighbor = -1;
				int worstNeighborMetric = int.MaxValue;
				ulong iter = mask;
				while (iter != 0UL) {
					int nSlot = BitOperations.TrailingZeroCount(iter);
					iter &= iter - 1UL;
					if (pinnedSlots[nSlot]) {
						continue;
					}
					int nItem = currentSlots[nSlot];
					int metric = nItem < 0
						? -1000
						: (items[nItem].Kind == SlotItemKind.StoneTablet ? 500 : items[nItem].MaxLevel);
					if (metric < worstNeighborMetric) {
						worstNeighborMetric = metric;
						bestTargetNeighbor = nSlot;
					}
				}
				if (bestTargetNeighbor >= 0 && movableSlots.Length > 0) {
					int srcSlot = movableSlots[(int)(NextRandomUInt(ref rngState) % (uint)movableSlots.Length)];
					int srcItem = currentSlots[srcSlot];
					int dstItem = currentSlots[bestTargetNeighbor];
					if (srcSlot == bestTargetNeighbor || srcItem == dstItem) {
						return;
					}
					currentSlots[bestTargetNeighbor] = srcItem;
					currentSlots[srcSlot] = dstItem;
					int candScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
					evaluations++;
					if (TryAcceptCandidate(
						snapshot,
						initialSlots,
						initialRotations,
						elitePool,
						candScore,
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
						bestRotations,
						ref evaluations
					)) {
						if (srcItem >= 0) {
							currentItemToSlot[srcItem] = bestTargetNeighbor;
						}
						if (dstItem >= 0) {
							currentItemToSlot[dstItem] = srcSlot;
						}
					} else {
						currentSlots[bestTargetNeighbor] = dstItem;
						currentSlots[srcSlot] = srcItem;
						iterationsSinceImprovement++;
					}
				}
				return;
			}

			int bestHubSlot = -1;
			int bestHubNeighborCount = -1;
			for (int sample = 0; sample < 6; sample++) {
				int s = movableSlots[(int)(NextRandomUInt(ref rngState) % (uint)movableSlots.Length)];
				if (s == crystalSlot) {
					continue;
				}
				int pop = BitOperations.PopCount(snapshot.MooreNeighborMasks[s]);
				if (pop > bestHubNeighborCount) {
					bestHubNeighborCount = pop;
					bestHubSlot = s;
				}
			}
			if (bestHubSlot < 0 || bestHubSlot == crystalSlot) {
				return;
			}

			int displaced = currentSlots[bestHubSlot];
			currentSlots[bestHubSlot] = crystalIdx;
			currentSlots[crystalSlot] = displaced;
			int candidateScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
			evaluations++;
			if (TryAcceptCandidate(
				snapshot,
				initialSlots,
				initialRotations,
				elitePool,
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
				bestRotations,
				ref evaluations
			)) {
				currentItemToSlot[crystalIdx] = bestHubSlot;
				if (displaced >= 0) {
					currentItemToSlot[displaced] = crystalSlot;
				}
			} else {
				currentSlots[crystalSlot] = crystalIdx;
				currentSlots[bestHubSlot] = displaced;
				iterationsSinceImprovement++;
			}
		}

		private static void ExecuteWhitePaperSandwichMutation(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> whitePaperItems,
			ReadOnlySpan<int> movableSlots,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
			Span<int> currentSlots,
			Span<int> currentRotations,
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
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			int wpIdx = whitePaperItems[(int)(NextRandomUInt(ref rngState) % (uint)whitePaperItems.Length)];
			int wpSlot = currentItemToSlot[wpIdx];
			if (wpSlot < 0 || pinnedSlots[wpSlot]) {
				return;
			}

			SlotCoord wpCoord = snapshot.Slots[wpSlot];
			int leftSlot = snapshot.CoordToSlotIndex(wpCoord.X - 1, wpCoord.Y);
			int rightSlot = snapshot.CoordToSlotIndex(wpCoord.X + 1, wpCoord.Y);

			bool canSlideSandwich = leftSlot >= 0
				&& rightSlot >= 0
				&& !pinnedSlots[leftSlot]
				&& !pinnedSlots[rightSlot];

			if (canSlideSandwich && (NextRandomUInt(ref rngState) % 3u) == 0u) {
				int destCenter = movableSlots[(int)(NextRandomUInt(ref rngState) % (uint)movableSlots.Length)];
				SlotCoord dCoord = snapshot.Slots[destCenter];
				int destLeft = snapshot.CoordToSlotIndex(dCoord.X - 1, dCoord.Y);
				int destRight = snapshot.CoordToSlotIndex(dCoord.X + 1, dCoord.Y);
				if (destLeft >= 0 && destRight >= 0 && destCenter != wpSlot
					&& !pinnedSlots[destLeft] && !pinnedSlots[destRight]
					&& destLeft != leftSlot && destLeft != wpSlot && destLeft != rightSlot
					&& destCenter != leftSlot && destCenter != wpSlot && destCenter != rightSlot
					&& destRight != leftSlot && destRight != wpSlot && destRight != rightSlot) {
					int sL = currentSlots[leftSlot];
					int sC = currentSlots[wpSlot];
					int sR = currentSlots[rightSlot];
					int dL = currentSlots[destLeft];
					int dC = currentSlots[destCenter];
					int dR = currentSlots[destRight];

					currentSlots[destLeft] = sL;
					currentSlots[destCenter] = sC;
					currentSlots[destRight] = sR;
					currentSlots[leftSlot] = dL;
					currentSlots[wpSlot] = dC;
					currentSlots[rightSlot] = dR;

					int score = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
					evaluations++;
					if (TryAcceptCandidate(
						snapshot,
						initialSlots,
						initialRotations,
						elitePool,
						score,
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
						bestRotations,
						ref evaluations
					)) {
						SyncItemToSlot(currentSlots, currentItemToSlot);
					} else {
						currentSlots[leftSlot] = sL;
						currentSlots[wpSlot] = sC;
						currentSlots[rightSlot] = sR;
						currentSlots[destLeft] = dL;
						currentSlots[destCenter] = dC;
						currentSlots[destRight] = dR;
						iterationsSinceImprovement++;
					}
					return;
				}
			}

			int destSlot = movableSlots[(int)(NextRandomUInt(ref rngState) % (uint)movableSlots.Length)];
			if (destSlot < 0 || destSlot >= unlockedSlots || destSlot == wpSlot) {
				return;
			}
			int targetSlot = (NextRandomUInt(ref rngState) & 1u) != 0u && rightSlot >= 0 && !pinnedSlots[rightSlot]
				? rightSlot
				: (leftSlot >= 0 && !pinnedSlots[leftSlot] ? leftSlot : -1);
			if (targetSlot < 0 || targetSlot == destSlot) {
				return;
			}
			int itemT = currentSlots[targetSlot];
			int itemD = currentSlots[destSlot];
			if (itemT == itemD) {
				return;
			}
			currentSlots[targetSlot] = itemD;
			currentSlots[destSlot] = itemT;

			int candScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
			evaluations++;
			if (TryAcceptCandidate(
				snapshot,
				initialSlots,
				initialRotations,
				elitePool,
				candScore,
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
				bestRotations,
				ref evaluations
			)) {
				if (itemD >= 0) {
					currentItemToSlot[itemD] = targetSlot;
				}
				if (itemT >= 0) {
					currentItemToSlot[itemT] = destSlot;
				}
			} else {
				currentSlots[targetSlot] = itemT;
				currentSlots[destSlot] = itemD;
				iterationsSinceImprovement++;
			}
		}

		private static void ExecuteArrangementClusterMutation(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
			Span<int> currentSlots,
			Span<int> currentRotations,
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
			ArrangementPattern[] patterns = snapshot.TalentRules.ArrangementPatterns;
			if (patterns.Length == 0) {
				return;
			}
			bool[] pinnedSlots = snapshot.PinnedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			ArrangementPattern pat = patterns[(int)(NextRandomUInt(ref rngState) % (uint)patterns.Length)];
			if (pat.Entries.Length < 2) {
				return;
			}

			int anchorIdx = -1;
			ArrangementOffsetEntry firstEntry = pat.Entries[0];
			for (int i = 0; i < items.Length; i++) {
				if (items[i].EntityId == firstEntry.RequiredEntityId) {
					anchorIdx = i;
					break;
				}
			}
			if (anchorIdx < 0) {
				return;
			}
			int anchorSlot = currentItemToSlot[anchorIdx];
			if (anchorSlot < 0) {
				return;
			}
			SlotCoord aCoord = snapshot.Slots[anchorSlot];

			for (int e = 1; e < pat.Entries.Length; e++) {
				ArrangementOffsetEntry entry = pat.Entries[e];
				int reqSlot = snapshot.CoordToSlotIndex(
					aCoord.X + (entry.OffsetX - firstEntry.OffsetX),
					aCoord.Y + (entry.OffsetY - firstEntry.OffsetY)
				);
				if (reqSlot < 0 || pinnedSlots[reqSlot]) {
					continue;
				}
				int reqItemIdx = currentSlots[reqSlot];
				if (reqItemIdx >= 0 && items[reqItemIdx].EntityId == entry.RequiredEntityId) {
					continue;
				}

				int donorItemIdx = -1;
				for (int i = 0; i < items.Length; i++) {
					if (items[i].EntityId == entry.RequiredEntityId && i != anchorIdx) {
						int s = currentItemToSlot[i];
						if (s >= 0 && !pinnedSlots[s]) {
							donorItemIdx = i;
							break;
						}
					}
				}
				if (donorItemIdx < 0) {
					return;
				}
				int donorSlot = currentItemToSlot[donorItemIdx];
				currentSlots[reqSlot] = donorItemIdx;
				currentSlots[donorSlot] = reqItemIdx;

				int score = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;
				if (TryAcceptCandidate(
					snapshot,
					initialSlots,
					initialRotations,
					elitePool,
					score,
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
					bestRotations,
					ref evaluations
				)) {
					currentItemToSlot[donorItemIdx] = reqSlot;
					if (reqItemIdx >= 0) {
						currentItemToSlot[reqItemIdx] = donorSlot;
					}
				} else {
					currentSlots[reqSlot] = reqItemIdx;
					currentSlots[donorSlot] = donorItemIdx;
					iterationsSinceImprovement++;
				}
				return;
			}
		}

		private static void ExecuteTabletRelocateRotateMutation(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> tablets,
			ReadOnlySpan<int> movableSlots,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
			Span<int> currentSlots,
			Span<int> currentRotations,
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
			SlotItemSnapshot[] items = snapshot.Items;
			int tIdx = tablets[(int)(NextRandomUInt(ref rngState) % (uint)tablets.Length)];
			int curSlot = currentItemToSlot[tIdx];
			int destSlot = movableSlots[(int)(NextRandomUInt(ref rngState) % (uint)movableSlots.Length)];
			if (curSlot < 0 || destSlot == curSlot) {
				return;
			}
			int displaced = currentSlots[destSlot];
			if (displaced >= 0 && items[displaced].SymmetryGroupId == items[tIdx].SymmetryGroupId) {
				return;
			}

			int savedRot = currentRotations[tIdx];
			currentSlots[destSlot] = tIdx;
			currentSlots[curSlot] = displaced;

			int bestRot = savedRot;
			int bestCandScore = int.MinValue;
			int maxRot = items[tIdx].IsRotatable ? 4 : 1;
			for (int r = 0; r < maxRot; r++) {
				int rot = items[tIdx].IsRotatable ? r : savedRot;
				if (items[tIdx].IsRotatable && IsRedundantTabletRotation(items[tIdx], destSlot, rot)) {
					continue;
				}
				currentRotations[tIdx] = rot;
				int sc = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;
				if (sc > bestCandScore) {
					bestCandScore = sc;
					bestRot = rot;
				}
			}

			if (bestCandScore == int.MinValue) {
				currentRotations[tIdx] = savedRot;
				bestCandScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
				evaluations++;
			} else {
				currentRotations[tIdx] = bestRot;
			}

			if (TryAcceptCandidate(
				snapshot,
				initialSlots,
				initialRotations,
				elitePool,
				bestCandScore,
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
				bestRotations,
				ref evaluations
			)) {
				currentItemToSlot[tIdx] = destSlot;
				if (displaced >= 0) {
					currentItemToSlot[displaced] = curSlot;
				}
			} else {
				currentSlots[curSlot] = tIdx;
				currentSlots[destSlot] = displaced;
				currentRotations[tIdx] = savedRot;
				iterationsSinceImprovement++;
			}
		}

		private static void ExecuteTabletPairSwapMutation(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> tablets,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
			Span<int> currentSlots,
			Span<int> currentRotations,
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
			SlotItemSnapshot[] items = snapshot.Items;
			int idxA = (int)(NextRandomUInt(ref rngState) % (uint)tablets.Length);
			int idxB = (int)(NextRandomUInt(ref rngState) % (uint)(tablets.Length - 1));
			if (idxB >= idxA) {
				idxB++;
			}
			int tA = tablets[idxA];
			int tB = tablets[idxB];
			int sA = currentItemToSlot[tA];
			int sB = currentItemToSlot[tB];
			if (sA < 0 || sB < 0 || items[tA].SymmetryGroupId == items[tB].SymmetryGroupId) {
				return;
			}

			int oldRotA = currentRotations[tA];
			int oldRotB = currentRotations[tB];
			currentSlots[sA] = tB;
			currentSlots[sB] = tA;
			if (items[tA].IsRotatable) {
				currentRotations[tA] = (oldRotA + (int)(NextRandomUInt(ref rngState) & 3u)) & 3;
			}
			if (items[tB].IsRotatable) {
				currentRotations[tB] = (oldRotB + (int)(NextRandomUInt(ref rngState) & 3u)) & 3;
			}

			int candScore = ObjectiveEvaluator.Evaluate(snapshot, currentSlots, currentRotations);
			evaluations++;
			if (TryAcceptCandidate(
				snapshot,
				initialSlots,
				initialRotations,
				elitePool,
				candScore,
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
				bestRotations,
				ref evaluations
			)) {
				currentItemToSlot[tA] = sB;
				currentItemToSlot[tB] = sA;
			} else {
				currentSlots[sA] = tA;
				currentSlots[sB] = tB;
				currentRotations[tA] = oldRotA;
				currentRotations[tB] = oldRotB;
				iterationsSinceImprovement++;
			}
		}

		private static void ExecuteVerticalPairMutation(
			InventorySnapshot snapshot,
			int topA,
			int botA,
			int topB,
			int botB,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
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
					snapshot,
					initialSlots,
					initialRotations,
					elitePool,
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
					bestRotations,
					ref evaluations
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
					snapshot,
					initialSlots,
					initialRotations,
					elitePool,
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
					bestRotations,
					ref evaluations
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
					snapshot,
					initialSlots,
					initialRotations,
					elitePool,
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
					bestRotations,
					ref evaluations
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
			InventorySnapshot snapshot,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			EliteCandidatePool elitePool,
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
			int[] bestRotations,
			ref int evaluations
		) {
			int histIdx = iteration & (LahcHistoryLength - 1);
			int histScore = history[histIdx];
			int scoreDelta = candidateScore - currentScore;
			iteration++;

			if (candidateScore >= bestScore - 30) {
				elitePool.TryOffer(
					snapshot,
					initialSlots,
					initialRotations,
					currentSlots,
					currentRotations,
					candidateScore,
					ref evaluations,
					countEval: false,
					forceEval: candidateScore >= bestScore
				);
			}

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
		private static bool IsBetterPolishState(
			int candScore,
			double candExactDps,
			int candViolations,
			int candUtilitySum,
			int bestScore,
			double bestExactDps,
			int bestViolations,
			int bestUtilitySum
		) {
			if (candScore != bestScore) {
				return candScore > bestScore;
			}
			long candCenti = (long)Math.Round(candExactDps * 100.0);
			long bestCenti = (long)Math.Round(bestExactDps * 100.0);
			if (candCenti != bestCenti) {
				return candCenti > bestCenti;
			}
			if (candViolations != bestViolations) {
				return candViolations < bestViolations;
			}
			return candUtilitySum > bestUtilitySum;
		}

		private static void RunExhaustiveMultiPassPolish(
			InventorySnapshot snapshot,
			Stopwatch stopwatch,
			int polishDeadlineMs,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref int evaluations,
			EliteCandidatePool elitePool,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			int maxPasses
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int width = snapshot.Width;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			bestScore = ObjectiveEvaluator.EvaluateWithMetrics(
				snapshot,
				bestSlots,
				bestRotations,
				out double bestExactDps,
				out int bestViolations,
				out int bestUtilitySum
			);
			evaluations++;

			int rotatableTabletCount = 0;
			for (int i = 0; i < itemCount; i++) {
				if (items[i].Kind == SlotItemKind.StoneTablet && items[i].IsRotatable) {
					rotatableTabletCount++;
				}
			}

			bool improved = true;
			int passCount = 0;

			while (improved && passCount < maxPasses) {
				if (passCount >= 1 && stopwatch.ElapsedMilliseconds >= polishDeadlineMs) {
					break;
				}
				improved = false;
				passCount++;

				for (int i = 0; i < itemCount; i++) {
					SlotItemSnapshot item = items[i];
					if (item.Kind != SlotItemKind.StoneTablet || !item.IsRotatable) {
						continue;
					}
					int tabletSlot = FindItemSlot(bestSlots, i);
					if (tabletSlot >= 0 && pinnedSlots[tabletSlot]) {
						continue;
					}
					int currentRot = bestRotations[i];
					int bestRotForItem = currentRot;
					for (int rot = 0; rot < 4; rot++) {
						if (rot == currentRot) {
							continue;
						}
						if (tabletSlot >= 0 && IsRedundantTabletRotation(item, tabletSlot, rot)) {
							continue;
						}
						bestRotations[i] = rot;
						int score = ObjectiveEvaluator.EvaluateWithMetrics(
							snapshot,
							bestSlots,
							bestRotations,
							out double candDps,
							out int candViol,
							out int candUtil
						);
						evaluations++;
						if (score >= bestScore - 30) {
							elitePool.TryOffer(
								snapshot,
								initialSlots,
								initialRotations,
								bestSlots,
								bestRotations,
								score,
								ref evaluations,
								countEval: false,
								forceEval: score >= bestScore
							);
						}
						if (IsBetterPolishState(
							score,
							candDps,
							candViol,
							candUtil,
							bestScore,
							bestExactDps,
							bestViolations,
							bestUtilitySum
						)) {
							bestScore = score;
							bestExactDps = candDps;
							bestViolations = candViol;
							bestUtilitySum = candUtil;
							bestRotForItem = rot;
							improved = true;
						}
					}
					bestRotations[i] = bestRotForItem;
				}

				if (rotatableTabletCount >= 2 && rotatableTabletCount <= 10) {
					for (int iA = 0; iA < itemCount; iA++) {
						if (items[iA].Kind != SlotItemKind.StoneTablet || !items[iA].IsRotatable) {
							continue;
						}
						int sTA = FindItemSlot(bestSlots, iA);
						if (sTA < 0 || pinnedSlots[sTA]) {
							continue;
						}
						for (int iB = iA + 1; iB < itemCount; iB++) {
							if (items[iB].Kind != SlotItemKind.StoneTablet || !items[iB].IsRotatable) {
								continue;
							}
							int sTB = FindItemSlot(bestSlots, iB);
							if (sTB < 0 || pinnedSlots[sTB]) {
								continue;
							}
							int savedRotA = bestRotations[iA];
							int savedRotB = bestRotations[iB];
							int bestPairRotA = savedRotA;
							int bestPairRotB = savedRotB;
							bool pairImproved = false;

							for (int rA = 0; rA < 4; rA++) {
								if (rA != savedRotA && IsRedundantTabletRotation(items[iA], sTA, rA)) {
									continue;
								}
								bestRotations[iA] = rA;
								for (int rB = 0; rB < 4; rB++) {
									if (rA == savedRotA && rB == savedRotB) {
										continue;
									}
									if (rB != savedRotB && IsRedundantTabletRotation(items[iB], sTB, rB)) {
										continue;
									}
									bestRotations[iB] = rB;
									int score = ObjectiveEvaluator.EvaluateWithMetrics(
										snapshot,
										bestSlots,
										bestRotations,
										out double candDps,
										out int candViol,
										out int candUtil
									);
									evaluations++;
									if (score >= bestScore - 30) {
										elitePool.TryOffer(
											snapshot,
											initialSlots,
											initialRotations,
											bestSlots,
											bestRotations,
											score,
											ref evaluations,
											countEval: false,
											forceEval: score >= bestScore
										);
									}
									if (IsBetterPolishState(
										score,
										candDps,
										candViol,
										candUtil,
										bestScore,
										bestExactDps,
										bestViolations,
										bestUtilitySum
									)) {
										bestScore = score;
										bestExactDps = candDps;
										bestViolations = candViol;
										bestUtilitySum = candUtil;
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
						if (itemA >= 0 && itemB >= 0 && items[itemA].SymmetryGroupId == items[itemB].SymmetryGroupId) {
							bool canSymTabletDiffRot = items[itemA].Kind == SlotItemKind.StoneTablet
								&& items[itemA].IsRotatable;
							if (!canSymTabletDiffRot) {
								continue;
							}
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
								if (IsRedundantTabletRotation(items[itemA], sB, rA)) {
									continue;
								}
								bestRotations[itemA] = rA;
								for (int rB = 0; rB < 4; rB++) {
									if (IsRedundantTabletRotation(items[itemB], sA, rB)) {
										continue;
									}
									bestRotations[itemB] = rB;
									int score = ObjectiveEvaluator.EvaluateWithMetrics(
										snapshot,
										bestSlots,
										bestRotations,
										out double candDps,
										out int candViol,
										out int candUtil
									);
									evaluations++;
									if (score >= bestScore - 30) {
										elitePool.TryOffer(
											snapshot,
											initialSlots,
											initialRotations,
											bestSlots,
											bestRotations,
											score,
											ref evaluations,
											countEval: false,
											forceEval: score >= bestScore
										);
									}
									if (IsBetterPolishState(
										score,
										candDps,
										candViol,
										candUtil,
										bestScore,
										bestExactDps,
										bestViolations,
										bestUtilitySum
									)) {
										bestScore = score;
										bestExactDps = candDps;
										bestViolations = candViol;
										bestUtilitySum = candUtil;
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
							int destSlotForRot = rotA ? sB : sA;
							int savedRot = bestRotations[rotTarget];
							int bestSwapRot = savedRot;
							bool swapImproved = false;

							for (int r = 0; r < 4; r++) {
								if (IsRedundantTabletRotation(items[rotTarget], destSlotForRot, r)) {
									continue;
								}
								bestRotations[rotTarget] = r;
								int score = ObjectiveEvaluator.EvaluateWithMetrics(
									snapshot,
									bestSlots,
									bestRotations,
									out double candDps,
									out int candViol,
									out int candUtil
								);
								evaluations++;
								if (score >= bestScore - 30) {
									elitePool.TryOffer(
										snapshot,
										initialSlots,
										initialRotations,
										bestSlots,
										bestRotations,
										score,
										ref evaluations,
										countEval: false,
										forceEval: score >= bestScore
									);
								}
								if (IsBetterPolishState(
									score,
									candDps,
									candViol,
									candUtil,
									bestScore,
									bestExactDps,
									bestViolations,
									bestUtilitySum
								)) {
									bestScore = score;
									bestExactDps = candDps;
									bestViolations = candViol;
									bestUtilitySum = candUtil;
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
							int score = ObjectiveEvaluator.EvaluateWithMetrics(
								snapshot,
								bestSlots,
								bestRotations,
								out double candDps,
								out int candViol,
								out int candUtil
							);
							evaluations++;
							if (score >= bestScore - 30) {
								elitePool.TryOffer(
									snapshot,
									initialSlots,
									initialRotations,
									bestSlots,
									bestRotations,
									score,
									ref evaluations,
									countEval: false,
									forceEval: score >= bestScore
								);
							}
							if (IsBetterPolishState(
								score,
								candDps,
								candViol,
								candUtil,
								bestScore,
								bestExactDps,
								bestViolations,
								bestUtilitySum
							)) {
								bestScore = score;
								bestExactDps = candDps;
								bestViolations = candViol;
								bestUtilitySum = candUtil;
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
								ref bestExactDps,
								ref bestViolations,
								ref bestUtilitySum,
								ref evaluations,
								elitePool,
								initialSlots,
								initialRotations
							)) {
								improved = true;
								break;
							}
						}
					}
				}

				if (TryPolishWhitePaperSandwich(
					snapshot,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref bestExactDps,
					ref bestViolations,
					ref bestUtilitySum,
					ref evaluations,
					elitePool,
					initialSlots,
					initialRotations
				)) {
					improved = true;
				}

				if (TryPolishCrystalHub(
					snapshot,
					bestSlots,
					bestRotations,
					ref bestScore,
					ref bestExactDps,
					ref bestViolations,
					ref bestUtilitySum,
					ref evaluations,
					elitePool,
					initialSlots,
					initialRotations
				)) {
					improved = true;
				}

				if (!improved && stopwatch.ElapsedMilliseconds < polishDeadlineMs) {
					if (TryPolishTargetedThreeCycles(
						snapshot,
						bestSlots,
						bestRotations,
						ref bestScore,
						ref bestExactDps,
						ref bestViolations,
						ref bestUtilitySum,
						ref evaluations,
						elitePool,
						initialSlots,
						initialRotations
					)) {
						improved = true;
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
			ref double bestExactDps,
			ref int bestViolations,
			ref int bestUtilitySum,
			ref int evaluations,
			EliteCandidatePool elitePool,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations
		) {
			if (topB == botA) {
				int i0 = bestSlots[topA];
				int i1 = bestSlots[botA];
				int i2 = bestSlots[botB];
				bestSlots[topA] = i2;
				bestSlots[botA] = i0;
				bestSlots[botB] = i1;
				int score = ObjectiveEvaluator.EvaluateWithMetrics(
					snapshot,
					bestSlots,
					bestRotations,
					out double candDps,
					out int candViol,
					out int candUtil
				);
				evaluations++;
				if (score >= bestScore - 30) {
					elitePool.TryOffer(
						snapshot,
						initialSlots,
						initialRotations,
						bestSlots,
						bestRotations,
						score,
						ref evaluations,
						countEval: false,
						forceEval: score >= bestScore
					);
				}
				if (IsBetterPolishState(
					score,
					candDps,
					candViol,
					candUtil,
					bestScore,
					bestExactDps,
					bestViolations,
					bestUtilitySum
				)) {
					bestScore = score;
					bestExactDps = candDps;
					bestViolations = candViol;
					bestUtilitySum = candUtil;
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
				int score = ObjectiveEvaluator.EvaluateWithMetrics(
					snapshot,
					bestSlots,
					bestRotations,
					out double candDps,
					out int candViol,
					out int candUtil
				);
				evaluations++;
				if (score >= bestScore - 30) {
					elitePool.TryOffer(
						snapshot,
						initialSlots,
						initialRotations,
						bestSlots,
						bestRotations,
						score,
						ref evaluations,
						countEval: false,
						forceEval: score >= bestScore
					);
				}
				if (IsBetterPolishState(
					score,
					candDps,
					candViol,
					candUtil,
					bestScore,
					bestExactDps,
					bestViolations,
					bestUtilitySum
				)) {
					bestScore = score;
					bestExactDps = candDps;
					bestViolations = candViol;
					bestUtilitySum = candUtil;
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
			int disjointScore = ObjectiveEvaluator.EvaluateWithMetrics(
				snapshot,
				bestSlots,
				bestRotations,
				out double disDps,
				out int disViol,
				out int disUtil
			);
			evaluations++;
			if (disjointScore >= bestScore - 30) {
				elitePool.TryOffer(
					snapshot,
					initialSlots,
					initialRotations,
					bestSlots,
					bestRotations,
					disjointScore,
					ref evaluations,
					countEval: false,
					forceEval: disjointScore >= bestScore
				);
			}
			if (IsBetterPolishState(
				disjointScore,
				disDps,
				disViol,
				disUtil,
				bestScore,
				bestExactDps,
				bestViolations,
				bestUtilitySum
			)) {
				bestScore = disjointScore;
				bestExactDps = disDps;
				bestViolations = disViol;
				bestUtilitySum = disUtil;
				return true;
			}
			bestSlots[topA] = aTop;
			bestSlots[botA] = aBot;
			bestSlots[topB] = bTop;
			bestSlots[botB] = bBot;
			return false;
		}

		private static bool TryPolishWhitePaperSandwich(
			InventorySnapshot snapshot,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref double bestExactDps,
			ref int bestViolations,
			ref int bestUtilitySum,
			ref int evaluations,
			EliteCandidatePool elitePool,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;
			bool anyImproved = false;

			for (int s = 0; s < unlockedSlots; s++) {
				if (pinnedSlots[s]) {
					continue;
				}
				int wpIdx = bestSlots[s];
				if (wpIdx < 0 || items[wpIdx].Kind != SlotItemKind.Artifact || !items[wpIdx].IsWhitePaperCharm) {
					continue;
				}
				SlotCoord curCoord = snapshot.Slots[s];
				int leftA = snapshot.CoordToSlotIndex(curCoord.X - 1, curCoord.Y);
				int rightA = snapshot.CoordToSlotIndex(curCoord.X + 1, curCoord.Y);
				if (leftA < 0 || rightA < 0 || pinnedSlots[leftA] || pinnedSlots[rightA]) {
					continue;
				}

				for (int dest = 0; dest < unlockedSlots; dest++) {
					if (dest == s || pinnedSlots[dest]) {
						continue;
					}
					SlotCoord dCoord = snapshot.Slots[dest];
					int leftB = snapshot.CoordToSlotIndex(dCoord.X - 1, dCoord.Y);
					int rightB = snapshot.CoordToSlotIndex(dCoord.X + 1, dCoord.Y);
					if (leftB < 0 || rightB < 0 || pinnedSlots[leftB] || pinnedSlots[rightB]) {
						continue;
					}
					if (leftB == leftA || leftB == s || leftB == rightA
						|| dest == leftA || dest == s || dest == rightA
						|| rightB == leftA || rightB == s || rightB == rightA) {
						continue;
					}

					int aL = bestSlots[leftA];
					int aC = bestSlots[s];
					int aR = bestSlots[rightA];
					int bL = bestSlots[leftB];
					int bC = bestSlots[dest];
					int bR = bestSlots[rightB];

					bestSlots[leftB] = aL;
					bestSlots[dest] = aC;
					bestSlots[rightB] = aR;
					bestSlots[leftA] = bL;
					bestSlots[s] = bC;
					bestSlots[rightA] = bR;

					int score = ObjectiveEvaluator.EvaluateWithMetrics(
						snapshot,
						bestSlots,
						bestRotations,
						out double candDps,
						out int candViol,
						out int candUtil
					);
					evaluations++;
					if (score >= bestScore - 30) {
						elitePool.TryOffer(
							snapshot,
							initialSlots,
							initialRotations,
							bestSlots,
							bestRotations,
							score,
							ref evaluations,
							countEval: false,
							forceEval: score >= bestScore
						);
					}
					if (IsBetterPolishState(
						score,
						candDps,
						candViol,
						candUtil,
						bestScore,
						bestExactDps,
						bestViolations,
						bestUtilitySum
					)) {
						bestScore = score;
						bestExactDps = candDps;
						bestViolations = candViol;
						bestUtilitySum = candUtil;
						anyImproved = true;
						break;
					}
					bestSlots[leftA] = aL;
					bestSlots[s] = aC;
					bestSlots[rightA] = aR;
					bestSlots[leftB] = bL;
					bestSlots[dest] = bC;
					bestSlots[rightB] = bR;
				}
			}

			return anyImproved;
		}

		private static bool TryPolishCrystalHub(
			InventorySnapshot snapshot,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref double bestExactDps,
			ref int bestViolations,
			ref int bestUtilitySum,
			ref int evaluations,
			EliteCandidatePool elitePool,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;
			bool anyImproved = false;

			for (int i = 0; i < items.Length; i++) {
				if (items[i].Kind != SlotItemKind.Artifact || !items[i].IsCrystalOfHarmony) {
					continue;
				}
				int cSlot = FindItemSlot(bestSlots, i);
				if (cSlot < 0 || pinnedSlots[cSlot]) {
					continue;
				}
				int cPop = BitOperations.PopCount(snapshot.MooreNeighborMasks[cSlot]);
				if (cPop >= 8) {
					continue;
				}
				for (int dest = 0; dest < unlockedSlots; dest++) {
					if (dest == cSlot || pinnedSlots[dest]) {
						continue;
					}
					if (BitOperations.PopCount(snapshot.MooreNeighborMasks[dest]) <= cPop) {
						continue;
					}
					int displaced = bestSlots[dest];
					bestSlots[dest] = i;
					bestSlots[cSlot] = displaced;
					int score = ObjectiveEvaluator.EvaluateWithMetrics(
						snapshot,
						bestSlots,
						bestRotations,
						out double candDps,
						out int candViol,
						out int candUtil
					);
					evaluations++;
					if (score >= bestScore - 30) {
						elitePool.TryOffer(
							snapshot,
							initialSlots,
							initialRotations,
							bestSlots,
							bestRotations,
							score,
							ref evaluations,
							countEval: false,
							forceEval: score >= bestScore
						);
					}
					if (IsBetterPolishState(
						score,
						candDps,
						candViol,
						candUtil,
						bestScore,
						bestExactDps,
						bestViolations,
						bestUtilitySum
					)) {
						bestScore = score;
						bestExactDps = candDps;
						bestViolations = candViol;
						bestUtilitySum = candUtil;
						anyImproved = true;
						break;
					}
					bestSlots[cSlot] = i;
					bestSlots[dest] = displaced;
				}
			}

			return anyImproved;
		}

		private static bool TryPolishTargetedThreeCycles(
			InventorySnapshot snapshot,
			int[] bestSlots,
			int[] bestRotations,
			ref int bestScore,
			ref double bestExactDps,
			ref int bestViolations,
			ref int bestUtilitySum,
			ref int evaluations,
			EliteCandidatePool elitePool,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			Span<int> keySlots = stackalloc int[TargetedCycleSlotCap];
			Span<int> keyPriorities = stackalloc int[TargetedCycleSlotCap];
			int keyCount = 0;

			for (int s = 0; s < unlockedSlots; s++) {
				if (pinnedSlots[s]) {
					continue;
				}
				int idx = bestSlots[s];
				int priority = 0;
				if (idx < 0) {
					priority = 10;
				} else {
					SlotItemSnapshot item = items[idx];
					if (item.Kind == SlotItemKind.StoneTablet) {
						priority = 100;
					} else if (item.HasDependencyTarget || item.IsCrystalOfHarmony || item.IsWhitePaperCharm
						|| item.IsElementalByRowCharm) {
						priority = 90;
					} else if (item.IsAttackableCharm) {
						priority = 80;
					} else if (item.ConstraintType != ArtifactConstraintType.None) {
						priority = 70;
					} else {
						priority = 20 + item.MaxLevel;
					}
				}

				if (keyCount < TargetedCycleSlotCap) {
					keySlots[keyCount] = s;
					keyPriorities[keyCount] = priority;
					keyCount++;
				} else {
					int minIdx = 0;
					for (int k = 1; k < keyCount; k++) {
						if (keyPriorities[k] < keyPriorities[minIdx]) {
							minIdx = k;
						}
					}
					if (priority > keyPriorities[minIdx]) {
						keySlots[minIdx] = s;
						keyPriorities[minIdx] = priority;
					}
				}
			}

			if (keyCount < 3) {
				return false;
			}

			for (int a = 0; a < keyCount; a++) {
				int sA = keySlots[a];
				for (int b = a + 1; b < keyCount; b++) {
					int sB = keySlots[b];
					for (int c = b + 1; c < keyCount; c++) {
						int sC = keySlots[c];
						int itemA = bestSlots[sA];
						int itemB = bestSlots[sB];
						int itemC = bestSlots[sC];
						if (itemA < 0 && itemB < 0 && itemC < 0) {
							continue;
						}

						for (int dir = 0; dir < 2; dir++) {
							if (dir == 0) {
								bestSlots[sA] = itemC;
								bestSlots[sB] = itemA;
								bestSlots[sC] = itemB;
							} else {
								bestSlots[sA] = itemB;
								bestSlots[sB] = itemC;
								bestSlots[sC] = itemA;
							}

							int score = ObjectiveEvaluator.EvaluateWithMetrics(
								snapshot,
								bestSlots,
								bestRotations,
								out double candDps,
								out int candViol,
								out int candUtil
							);
							evaluations++;
							if (score >= bestScore - 30) {
								elitePool.TryOffer(
									snapshot,
									initialSlots,
									initialRotations,
									bestSlots,
									bestRotations,
									score,
									ref evaluations,
									countEval: false,
									forceEval: score >= bestScore
								);
							}
							if (IsBetterPolishState(
								score,
								candDps,
								candViol,
								candUtil,
								bestScore,
								bestExactDps,
								bestViolations,
								bestUtilitySum
							)) {
								bestScore = score;
								bestExactDps = candDps;
								bestViolations = candViol;
								bestUtilitySum = candUtil;
								return true;
							}
						}

						bestSlots[sA] = itemA;
						bestSlots[sB] = itemB;
						bestSlots[sC] = itemC;
					}
				}
			}

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
								contribution += (-eff.LevelParam) * ObjectiveEvaluator.NeutralizedNegativeLevelBonus * 4;
								if (hasEngraving) {
									contribution += ObjectiveEvaluator.EngravingOutOfBoundsBonus;
								}
							}
						} else if (eff.LevelParam < 0) {
							contribution += (-eff.LevelParam) * ObjectiveEvaluator.NeutralizedNegativeLevelBonus * 4;
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
					return ObjectiveEvaluator.SacrificeActiveTieBreakerScore + 200;
				}

				double weight = ObjectiveEvaluator.GetNormalizedWeight(item);
				int rawBaseMax = ObjectiveEvaluator.BaseActiveArtifactScore
					+ (item.MaxLevel * ObjectiveEvaluator.EffectiveLevelScoreMultiplier)
					+ ObjectiveEvaluator.MaxLevelReachedBonus
					+ ObjectiveEvaluator.IgnoreConstraintSynergyBonus
					+ 600;
				int maxLvlScore = (int)Math.Ceiling(rawBaseMax * weight)
					+ (64 * ObjectiveEvaluator.MaxLevelOverflowTieBreakerBonus);

				if (item.HasDependencyTarget) {
					SlotItemSnapshot[] allItems = snapshot.Items;
					double maxRootWeight = 1.0;
					int maxRootMaxLevel = 0;
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
						}
					}
					double maxAmpFactor = 1.0 + (0.25 * (item.MaxLevel + 1));
					double maxNeedleAmp = maxAmpFactor
						* (ObjectiveEvaluator.GoldenNeedleRootLevelFactor * maxRootMaxLevel)
						* maxRootWeight
						+ (35.0 * (item.MaxLevel + 1));
					int singleParentSynergy = (int)Math.Ceiling(
						ObjectiveEvaluator.GoldenNeedleLinkBaseBonus * maxRootWeight
					) + (int)Math.Ceiling(maxNeedleAmp) + 250;
					int parentCount = Math.Max(1, item.DependencyParentOffsets.Length);
					maxLvlScore += singleParentSynergy * parentCount;
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
					) + (8 * ObjectiveEvaluator.CrystalNeighborSlotBonus) + 120;
					maxLvlScore += maxHarmonyBonus;
				}

				if (item.IsElementalByRowCharm) {
					int maxElemBonus = item.GetRowElementalBonus(item.MaxLevel);
					maxLvlScore += ObjectiveEvaluator.ChalcedonyTargetMatchBonus
						+ (maxElemBonus * 140)
						+ 400;
				}
				if (item.IsWhitePaperCharm) {
					maxLvlScore += ObjectiveEvaluator.WhitePaperTargetCopiedBonus + 300;
				}
				return maxLvlScore + 150;
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
							ruleBonus += (-eff.LevelParam) * ObjectiveEvaluator.NeutralizedNegativeLevelBonus * 4;
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
			int potential = 2500 + ComputeMaxDpsGainPotential(snapshot);
			ComboCategoryInfo[] cats = snapshot.ComboCategories;
			for (int i = 0; i < cats.Length; i++) {
				potential += (cats[i].Breakpoints.Length * ObjectiveEvaluator.TargetComboBreakpointBonus)
					+ (8 * ObjectiveEvaluator.TargetComboDynamicStackBonus);
			}
			if (snapshot.TalentRules.ArrangementBonusStat > 0) {
				potential += snapshot.TalentRules.ArrangementPatterns.Length
					* ObjectiveEvaluator.ArrangementPatternMatchBonus;
			}
			return potential;
		}

		private static int ComputeMaxDpsGainPotential(InventorySnapshot snapshot) {
			SlotItemSnapshot[] items = snapshot.Items;
			double maxAttackDps = 0.0;
			double totalMaxNeedlePct = 0.0;
			double totalMaxStatPct = 0.0;
			double finalDamageMul = 1.0;
			double maxHarmonyPct = 0.0;

			int maxNeighborLevel = 1;
			for (int i = 0; i < items.Length; i++) {
				if (items[i].Kind == SlotItemKind.Artifact && items[i].MaxLevel > maxNeighborLevel) {
					maxNeighborLevel = items[i].MaxLevel;
				}
			}

			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot item = items[i];
				if (item.Kind != SlotItemKind.Artifact || item.IsDisabledByWeaponOrDual) {
					continue;
				}
				if (item.PriorityTier != ArtifactPriorityTier.Sacrifice) {
					if (item.HasDependencyTarget) {
						totalMaxNeedlePct += item.GetNeedleBonusPercent(item.MaxLevel, 0, true);
					}
					if (item.IsCrystalOfHarmony) {
						maxHarmonyPct += Math.Floor(8.0 * maxNeighborLevel * item.GetCrystalRatePerLevel(item.MaxLevel));
					}
					float statPct = item.GetStatBonusPercentAtLevel(item.MaxLevel);
					if (statPct > 0f) {
						totalMaxStatPct += statPct;
					}
					float finalPct = item.GetFinalDamagePercentAtLevel(item.MaxLevel);
					if (finalPct > 0f) {
						finalDamageMul *= 1.0 + (finalPct * 0.01);
					}
					if (item.IsElementalByRowCharm) {
						totalMaxStatPct += item.GetRowElementalBonus(item.MaxLevel);
					}
				}
			}

			double maxComboStat = 0.0;
			ComboCategoryInfo[] cats = snapshot.ComboCategories;
			for (int i = 0; i < cats.Length; i++) {
				maxComboStat += cats[i].EvaluateStatBonusPercent(
					cats[i].StaticCount + items.Length,
					snapshot.TalentRules.UnlimitedComboStat
				);
			}
			totalMaxStatPct += maxComboStat + snapshot.TalentRules.ArrangementBonusStat + maxHarmonyPct;

			if (snapshot.CombatBaseline.HasDetailedWeaponProfile) {
				double baseDps = snapshot.CombatBaseline.BaseWeaponDps;
				CombatStatDelta upperDelta = new CombatStatDelta(
					allDamageBonus: (float)totalMaxStatPct + snapshot.TalentRules.ComboBonusDamageStat * cats.Length
				);
				for (int i = 0; i < items.Length; i++) {
					SlotItemSnapshot item = items[i];
					if (item.Kind != SlotItemKind.Artifact || item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
						continue;
					}
					upperDelta = upperDelta.Add(item.GetStatDeltaAtLevel(item.MaxLevel));
					if (item.IsWoodenBoxCharm) {
						upperDelta = upperDelta.Add(new CombatStatDelta(allDamageBonus: item.GetWoodenBoxApPerCharm(item.MaxLevel) * 6));
					}
					if (item.IsFireIcePositionCharm) {
						item.GetFireIceStats(item.MaxLevel, true, out byte fiLeftElem, out int l1, out byte fiRightElem, out int r1);
						item.GetFireIceStats(item.MaxLevel, false, out _, out int l2, out _, out int r2);
						upperDelta = upperDelta.AddElementalByKind(fiLeftElem, Math.Max(0, Math.Max(l1, l2)));
						upperDelta = upperDelta.AddElementalByKind(fiRightElem, Math.Max(0, Math.Max(r1, r2)));
					}
				}
				WeaponCombatEvaluation weaponEval = snapshot.CombatBaseline.WeaponProfile.Evaluate(
					in upperDelta,
					finalDamageMul
				);
				for (int i = 0; i < items.Length; i++) {
					SlotItemSnapshot item = items[i];
					if (item.Kind == SlotItemKind.Artifact
						&& item.PriorityTier != ArtifactPriorityTier.Sacrifice
						&& item.IsAttackableCharm) {
						double raw = item.GetAttackRawDpsAtLevelWithElement(
							item.MaxLevel,
							weaponEval.ResolveElementalStat(item.CharmScalingElementKind)
						);
						double amp = 1.0 + ((weaponEval.LiveCharmDamageBonusPercent + totalMaxNeedlePct) * 0.01);
						maxAttackDps += raw * Math.Max(0.1, amp);
					}
				}
				double maxTotal = weaponEval.WeaponAndAddonEffectiveDps + (maxAttackDps * weaponEval.CharmPostMultiplier);
				double gain = Math.Max(0.0, (maxTotal - baseDps) / Math.Max(100.0, baseDps));
				return (int)Math.Ceiling(gain * 1000.0) + 120;
			}

			double baseWeaponRaw = snapshot.CombatBaseline.ComputeBaseWeaponRawDps();
			double baseCrit = snapshot.CombatBaseline.ComputeCritExpectedMultiplier();
			double staticComboPct = 0.0;
			for (int i = 0; i < cats.Length; i++) {
				staticComboPct += cats[i].EvaluateStatBonusPercent(
					cats[i].StaticCount,
					snapshot.TalentRules.UnlimitedComboStat
				);
			}
			double baseWeaponOnly = baseWeaponRaw
				* (1.0 + ((snapshot.CombatBaseline.ElementalDamageBonusPercent + staticComboPct) * 0.01))
				* baseCrit;
			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot item = items[i];
				if (item.Kind == SlotItemKind.Artifact
					&& item.PriorityTier != ArtifactPriorityTier.Sacrifice
					&& item.IsAttackableCharm) {
					maxAttackDps += item.GetAttackRawDpsAtLevel(item.MaxLevel) * (1.0 + (totalMaxNeedlePct * 0.01));
				}
			}
			double legacyStatMul = 1.0 + ((snapshot.CombatBaseline.ElementalDamageBonusPercent + totalMaxStatPct) * 0.01);
			double legacyMax = (baseWeaponRaw + maxAttackDps)
				* legacyStatMul
				* baseCrit
				* finalDamageMul
				* (1.0 + (maxHarmonyPct * 0.01));
			double legacyGain = Math.Max(0.0, (legacyMax - baseWeaponOnly) / Math.Max(100.0, baseWeaponOnly));
			return (int)Math.Ceiling(legacyGain * 1000.0) + 120;
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
			return ObjectiveEvaluator.ArePlacementRulesEqual(first, second);
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

		private static void RegisterDistinctSeed(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> candSlots,
			ReadOnlySpan<int> candRotations,
			int score,
			int[][] seedSlots,
			int[][] seedRotations,
			int[] seedScores,
			ulong[] seedHashes,
			ref int seedCount
		) {
			ulong hash = ObjectiveEvaluator.ComputeCanonicalLayoutHash(snapshot, candSlots, candRotations);
			for (int i = 0; i < seedCount; i++) {
				if (seedHashes[i] == hash
					&& ObjectiveEvaluator.AreLayoutsCanonicallyEqual(
						snapshot,
						candSlots,
						candRotations,
						seedSlots[i],
						seedRotations[i]
					)) {
					if (score > seedScores[i]) {
						candSlots.CopyTo(seedSlots[i]);
						candRotations.CopyTo(seedRotations[i]);
						seedScores[i] = score;
					}
					return;
				}
			}

			if (seedCount < seedSlots.Length) {
				candSlots.CopyTo(seedSlots[seedCount]);
				candRotations.CopyTo(seedRotations[seedCount]);
				seedScores[seedCount] = score;
				seedHashes[seedCount] = hash;
				seedCount++;
				return;
			}

			int worstIdx = 0;
			for (int i = 1; i < seedCount; i++) {
				if (seedScores[i] < seedScores[worstIdx]) {
					worstIdx = i;
				}
			}
			if (score > seedScores[worstIdx]) {
				candSlots.CopyTo(seedSlots[worstIdx]);
				candRotations.CopyTo(seedRotations[worstIdx]);
				seedScores[worstIdx] = score;
				seedHashes[worstIdx] = hash;
			}
		}

		private static void RunMultiStartSeedSynthesis(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			int initialScore,
			EliteCandidatePool elitePool,
			int[][] seedSlots,
			int[][] seedRotations,
			int[] seedScores,
			ulong[] seedHashes,
			ref int seedCount,
			ref int evaluations
		) {
			RegisterDistinctSeed(
				snapshot,
				initialSlots,
				initialRotations,
				initialScore,
				seedSlots,
				seedRotations,
				seedScores,
				seedHashes,
				ref seedCount
			);

			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			Span<int> candidateSlots = stackalloc int[unlockedSlots];
			Span<int> candidateRotations = stackalloc int[itemCount];

			BuildDomainFirstSynergySeed(snapshot, initialSlots, initialRotations, candidateSlots, candidateRotations);
			RunArtifactOnlyLocalPolish(snapshot, candidateSlots, candidateRotations, ref evaluations);
			int domainScore = ObjectiveEvaluator.Evaluate(snapshot, candidateSlots, candidateRotations);
			evaluations++;
			elitePool.TryOffer(
				snapshot,
				initialSlots,
				initialRotations,
				candidateSlots,
				candidateRotations,
				domainScore,
				ref evaluations,
				countEval: false,
				forceEval: true
			);
			RegisterDistinctSeed(
				snapshot,
				candidateSlots,
				candidateRotations,
				domainScore,
				seedSlots,
				seedRotations,
				seedScores,
				seedHashes,
				ref seedCount
			);

			if (HasAnyStoneTablet(snapshot)) {
				initialSlots.CopyTo(candidateSlots);
				initialRotations.CopyTo(candidateRotations);
				for (int i = 0; i < itemCount; i++) {
					if (items[i].Kind != SlotItemKind.StoneTablet || !items[i].IsRotatable) {
						continue;
					}
					int tSlot = FindItemSlot(candidateSlots, i);
					if (tSlot < 0 || pinnedSlots[tSlot]) {
						continue;
					}
					int bestRot = candidateRotations[i];
					int bestRotScore = ObjectiveEvaluator.Evaluate(snapshot, candidateSlots, candidateRotations);
					evaluations++;
					for (int r = 0; r < 4; r++) {
						if (r == bestRot || IsRedundantTabletRotation(items[i], tSlot, r)) {
							continue;
						}
						candidateRotations[i] = r;
						int sc = ObjectiveEvaluator.Evaluate(snapshot, candidateSlots, candidateRotations);
						evaluations++;
						if (sc > bestRotScore) {
							bestRotScore = sc;
							bestRot = r;
						}
					}
					candidateRotations[i] = bestRot;
				}
				RunArtifactOnlyLocalPolish(snapshot, candidateSlots, candidateRotations, ref evaluations);
				int rotInitScore = ObjectiveEvaluator.Evaluate(snapshot, candidateSlots, candidateRotations);
				evaluations++;
				elitePool.TryOffer(
					snapshot,
					initialSlots,
					initialRotations,
					candidateSlots,
					candidateRotations,
					rotInitScore,
					ref evaluations,
					countEval: false,
					forceEval: false
				);
				RegisterDistinctSeed(
					snapshot,
					candidateSlots,
					candidateRotations,
					rotInitScore,
					seedSlots,
					seedRotations,
					seedScores,
					seedHashes,
					ref seedCount
				);

				for (int mode = 0; mode < 3; mode++) {
					candidateSlots.Fill(-1);
					initialRotations.CopyTo(candidateRotations);

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
						mode
					);
					AssignArtifactsToTabletField(snapshot, candidateSlots, candidateRotations);
					RunArtifactOnlyLocalPolish(snapshot, candidateSlots, candidateRotations, ref evaluations);

					int score = ObjectiveEvaluator.Evaluate(
						snapshot,
						candidateSlots,
						candidateRotations
					);
					evaluations++;
					elitePool.TryOffer(
						snapshot,
						initialSlots,
						initialRotations,
						candidateSlots,
						candidateRotations,
						score,
						ref evaluations,
						countEval: false,
						forceEval: true
					);
					RegisterDistinctSeed(
						snapshot,
						candidateSlots,
						candidateRotations,
						score,
						seedSlots,
						seedRotations,
						seedScores,
						seedHashes,
						ref seedCount
					);
				}
			}
		}

		private static void BuildDomainFirstSynergySeed(
			InventorySnapshot snapshot,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			Span<int> outSlots,
			Span<int> outRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int width = snapshot.Width;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			outSlots.Fill(-1);
			initialRotations.CopyTo(outRotations);
			Span<byte> itemPlaced = stackalloc byte[itemCount];
			itemPlaced.Clear();

			for (int s = 0; s < unlockedSlots; s++) {
				if (pinnedSlots[s]) {
					int idx = initialSlots[s];
					if (idx >= 0 && idx < itemCount) {
						outSlots[s] = idx;
						itemPlaced[idx] = 1;
					}
				}
			}

			int crystalSlot = -1;
			for (int i = 0; i < itemCount; i++) {
				if (itemPlaced[i] != 0 || items[i].Kind != SlotItemKind.Artifact || !items[i].IsCrystalOfHarmony
					|| items[i].PriorityTier == ArtifactPriorityTier.Sacrifice || items[i].IsDisabledByWeaponOrDual) {
					continue;
				}
				int bestSlot = -1;
				int bestHubScore = int.MinValue;
				for (int s = 0; s < unlockedSlots; s++) {
					if (outSlots[s] >= 0 || snapshot.BaseSlotDisable[s] > 0) {
						continue;
					}
					int pop = BitOperations.PopCount(snapshot.MooreNeighborMasks[s]);
					int hubScore = (pop * 200)
						+ (snapshot.BaseSlotLevel[s] * 120)
						+ (IsStaticSlotConstraintMet(snapshot, s, items[i].ConstraintType) ? 400 : -4000);
					if (hubScore > bestHubScore) {
						bestHubScore = hubScore;
						bestSlot = s;
					}
				}
				if (bestSlot >= 0) {
					outSlots[bestSlot] = i;
					itemPlaced[i] = 1;
					crystalSlot = bestSlot;
				}
			}

			Span<int> needles = stackalloc int[itemCount];
			int needleCount = 0;
			Span<int> attacks = stackalloc int[itemCount];
			int attackCount = 0;
			for (int i = 0; i < itemCount; i++) {
				if (itemPlaced[i] != 0 || items[i].Kind != SlotItemKind.Artifact
					|| items[i].PriorityTier == ArtifactPriorityTier.Sacrifice || items[i].IsDisabledByWeaponOrDual) {
					continue;
				}
				if (items[i].HasDependencyTarget) {
					needles[needleCount++] = i;
				} else if (items[i].IsAttackableCharm) {
					attacks[attackCount++] = i;
				}
			}
			SortAttackableItemsByTargetPriority(snapshot, items, attacks, attackCount);

			for (int n = 0; n < needleCount; n++) {
				int needleIdx = needles[n];
				SlotItemSnapshot needle = items[needleIdx];
				(sbyte X, sbyte Y)[] parentOffsets = needle.DependencyParentOffsets;
				int offX = parentOffsets.Length > 0 ? parentOffsets[0].X : needle.DependencyOffsetX;
				int offY = parentOffsets.Length > 0 ? parentOffsets[0].Y : needle.DependencyOffsetY;
				if (offX == 0 && offY == 0) {
					offY = -1;
				}

				int bestRootSlot = -1;
				int bestChildSlot = -1;
				int bestAttackIdx = -1;
				int bestPairScore = int.MinValue;

				for (int a = 0; a < attackCount; a++) {
					int atkIdx = attacks[a];
					if (itemPlaced[atkIdx] != 0) {
						continue;
					}
					for (int rSlot = 0; rSlot < unlockedSlots; rSlot++) {
						if (outSlots[rSlot] >= 0 || snapshot.BaseSlotDisable[rSlot] > 0) {
							continue;
						}
						SlotCoord rCoord = snapshot.Slots[rSlot];
						int cSlot = snapshot.CoordToSlotIndex(rCoord.X - offX, rCoord.Y - offY);
						if (cSlot < 0 || outSlots[cSlot] >= 0 || snapshot.BaseSlotDisable[cSlot] > 0) {
							continue;
						}
						if (!IsStaticSlotConstraintMet(snapshot, rSlot, items[atkIdx].ConstraintType)
							|| !IsStaticSlotConstraintMet(snapshot, cSlot, needle.ConstraintType)) {
							continue;
						}
						int pairScore = GetNeedleTargetSelectionScore(snapshot, items[atkIdx]);
						if (crystalSlot >= 0) {
							ulong cMask = snapshot.MooreNeighborMasks[crystalSlot];
							if ((cMask & (1UL << rSlot)) != 0UL) {
								pairScore += 500;
							}
							if ((cMask & (1UL << cSlot)) != 0UL) {
								pairScore += 400;
							}
						}
						if (rSlot + width < unlockedSlots) {
							pairScore += 300;
						}
						if (pairScore > bestPairScore) {
							bestPairScore = pairScore;
							bestRootSlot = rSlot;
							bestChildSlot = cSlot;
							bestAttackIdx = atkIdx;
						}
					}
					break;
				}

				if (bestAttackIdx >= 0 && bestRootSlot >= 0 && bestChildSlot >= 0) {
					outSlots[bestRootSlot] = bestAttackIdx;
					outSlots[bestChildSlot] = needleIdx;
					itemPlaced[bestAttackIdx] = 1;
					itemPlaced[needleIdx] = 1;
				}
			}

			for (int i = 0; i < itemCount; i++) {
				if (itemPlaced[i] != 0 || items[i].Kind != SlotItemKind.Artifact || !items[i].IsWhitePaperCharm
					|| items[i].PriorityTier == ArtifactPriorityTier.Sacrifice || items[i].IsDisabledByWeaponOrDual) {
					continue;
				}
				for (int s = 0; s < unlockedSlots; s++) {
					if (outSlots[s] >= 0 || snapshot.BaseSlotDisable[s] > 0) {
						continue;
					}
					SlotCoord sc = snapshot.Slots[s];
					int lSlot = snapshot.CoordToSlotIndex(sc.X - 1, sc.Y);
					int rSlot = snapshot.CoordToSlotIndex(sc.X + 1, sc.Y);
					if (lSlot >= 0 && rSlot >= 0 && (outSlots[lSlot] < 0 || outSlots[rSlot] < 0)) {
						outSlots[s] = i;
						itemPlaced[i] = 1;
						for (int n = 0; n < itemCount; n++) {
							if (itemPlaced[n] != 0 || items[n].Kind != SlotItemKind.Artifact
								|| items[n].StaticCategoryMask == 0u) {
								continue;
							}
							bool matchesTarget = snapshot.UserTargetCategoryMask == 0u
								|| (items[n].StaticCategoryMask & snapshot.UserTargetCategoryMask) != 0u;
							if (!matchesTarget) {
								continue;
							}
							if (outSlots[lSlot] < 0) {
								outSlots[lSlot] = n;
								itemPlaced[n] = 1;
								if (items[i].WhitePaperMatchCount <= 1) {
									break;
								}
							} else if (outSlots[rSlot] < 0) {
								outSlots[rSlot] = n;
								itemPlaced[n] = 1;
								break;
							}
						}
						break;
					}
				}
			}

			Span<int> unplacedTablets = stackalloc int[itemCount];
			int tabletCount = 0;
			for (int i = 0; i < itemCount; i++) {
				if (itemPlaced[i] == 0 && items[i].Kind == SlotItemKind.StoneTablet) {
					unplacedTablets[tabletCount++] = i;
				}
			}
			SortTabletsByFieldImpact(items, unplacedTablets, tabletCount, negativeFirst: false);

			for (int t = 0; t < tabletCount; t++) {
				int tIdx = unplacedTablets[t];
				SlotItemSnapshot tablet = items[tIdx];
				int bestSlot = -1;
				int bestRot = initialRotations[tIdx] & 3;
				int bestScore = int.MinValue;
				int maxRot = tablet.IsRotatable ? 4 : 1;

				for (int s = 0; s < unlockedSlots; s++) {
					if (outSlots[s] >= 0) {
						continue;
					}
					outSlots[s] = tIdx;
					for (int r = 0; r < maxRot; r++) {
						int rot = tablet.IsRotatable ? r : (initialRotations[tIdx] & 3);
						if (tablet.IsRotatable && IsRedundantTabletRotation(tablet, s, rot)) {
							continue;
						}
						outRotations[tIdx] = rot;
						int sc = ObjectiveEvaluator.Evaluate(snapshot, outSlots, outRotations);
						if (sc > bestScore) {
							bestScore = sc;
							bestSlot = s;
							bestRot = rot;
						}
					}
					outSlots[s] = -1;
				}
				if (bestSlot >= 0) {
					outSlots[bestSlot] = tIdx;
					outRotations[tIdx] = bestRot;
					itemPlaced[tIdx] = 1;
				}
			}

			for (int i = 0; i < itemCount; i++) {
				if (itemPlaced[i] != 0) {
					continue;
				}
				int bestSlot = -1;
				int bestScore = int.MinValue;
				for (int s = 0; s < unlockedSlots; s++) {
					if (outSlots[s] >= 0) {
						continue;
					}
					outSlots[s] = i;
					int sc = ObjectiveEvaluator.Evaluate(snapshot, outSlots, outRotations);
					if (sc > bestScore) {
						bestScore = sc;
						bestSlot = s;
					}
					outSlots[s] = -1;
				}
				if (bestSlot >= 0) {
					outSlots[bestSlot] = i;
					itemPlaced[i] = 1;
				}
			}
		}

		private static void ConstructConflictFreeTabletField(
			InventorySnapshot snapshot,
			Span<int> tabletSlots,
			Span<int> tabletRotations,
			int mode
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

			SortTabletsByFieldImpact(items, unpinnedTablets, tabletCount, negativeFirst: mode == 2);

			if (mode != 1) {
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
			Span<int> otherPosLevel = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> negLevel = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> mulLevel = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> disable = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<int> ignore = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			Span<byte> isTablet = stackalloc byte[InventorySnapshot.MaxSupportedSlots];
			Span<byte> reqCharm = stackalloc byte[InventorySnapshot.MaxSupportedSlots];

			for (int s = 0; s < unlockedSlots; s++) {
				int perm = snapshot.BaseSlotLevel[s];
				posLevel[s] = Math.Max(0, perm);
				otherPosLevel[s] = Math.Max(0, perm);
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
									if (target != s) {
										otherPosLevel[target] += eff.LevelParam;
									}
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
			Span<int> openSlotMuls = stackalloc int[InventorySnapshot.MaxSupportedSlots];
			int openSlotCount = 0;

			for (int s = 0; s < unlockedSlots; s++) {
				int baseMul = mulLevel[s];
				int mysticMul = (mysticMask & (1UL << s)) != 0UL ? 2 : 0;
				int totalMul = baseMul + mysticMul;
				int effMul = totalMul != 0 ? Math.Abs(totalMul) : 1;
				int rawPos = posLevel[s];
				int rawNeg = negLevel[s];
				int rawDelta = rawPos - rawNeg;
				int finalDelta = totalMul != 0 ? (rawDelta * totalMul) : rawDelta;

				if (baseMul > 1 && rawDelta == 0) {
					fieldScore -= (baseMul - 1) * ObjectiveEvaluator.WastedMultiplierPenalty;
				}

				if (isTablet[s] != 0) {
					int extraOtherScale = totalMul > 1 ? Math.Max(0, rawDelta * (totalMul - 1)) : 0;
					int posTotal = (otherPosLevel[s] * effMul) + extraOtherScale + ignore[s];
					if (posTotal > 0) {
						fieldScore -= posTotal * ObjectiveEvaluator.TabletOnBuffedSlotPenalty;
						collisionCount++;
					} else if (rawNeg > 0 || disable[s] > 0) {
						fieldScore += (rawNeg * effMul * 95) + (disable[s] * 140);
					}
					continue;
				}

				if (rawPos > 0 && rawNeg > 0 && rawDelta < 4) {
					collisionCount++;
					int rawCanceled = Math.Min(rawPos, rawNeg);
					int canceledScaled = rawCanceled * effMul;
					fieldScore -= canceledScaled * (ObjectiveEvaluator.TabletPlusMinusCollisionPenalty + 220);
					if (finalDelta > 0) {
						int netPosPen = ObjectiveEvaluator.TabletMinusOnNetPositiveSlotPenalty + 160;
						fieldScore -= (rawNeg * effMul) * netPosPen;
					}
				}

				if (disable[s] > 0 && (rawPos > 0 || ignore[s] > 0)) {
					collisionCount++;
					fieldScore -= ((rawPos * effMul) + ignore[s] + 2) * 1800;
				}

				int existingIdx = slotToItem[s];
				if (existingIdx >= 0 && snapshot.PinnedSlots[s] && items[existingIdx].Kind == SlotItemKind.Artifact) {
					SlotItemSnapshot pinnedArt = items[existingIdx];
					if (pinnedArt.PriorityTier == ArtifactPriorityTier.Sacrifice
						|| pinnedArt.IsDisabledByWeaponOrDual) {
						if (rawPos > 0) {
							fieldScore -= (rawPos * effMul) * ObjectiveEvaluator.WastedPositiveOnEmptySlotPenalty;
						} else if (rawNeg > 0 || disable[s] > 0) {
							fieldScore += (rawNeg * effMul * 80) + (disable[s] * 110);
						}
					} else {
						int preMul = pinnedArt.BaseLevel + rawDelta;
						int disp = totalMul != 0 ? (preMul * totalMul) : preMul;
						if (disable[s] > 0 || disp <= -1) {
							fieldScore -= ObjectiveEvaluator.DeactivationBasePenalty;
						} else {
							int eff = Math.Clamp(disp, 0, pinnedArt.MaxLevel);
							fieldScore += eff * 220;
						}
					}
					continue;
				}

				int encodedSlotDelta = disable[s] > 0 ? -100 : rawDelta;
				openSlotDeltas[openSlotCount] = encodedSlotDelta;
				openSlotMuls[openSlotCount] = disable[s] > 0 ? 1 : (totalMul != 0 ? totalMul : 1);
				openSlotCount++;
			}

			for (int a = 0; a < openSlotCount; a++) {
				for (int b = a + 1; b < openSlotCount; b++) {
					int scoreA = openSlotDeltas[a] * openSlotMuls[a];
					int scoreB = openSlotDeltas[b] * openSlotMuls[b];
					if (scoreB > scoreA) {
						int td = openSlotDeltas[a];
						openSlotDeltas[a] = openSlotDeltas[b];
						openSlotDeltas[b] = td;
						int tm = openSlotMuls[a];
						openSlotMuls[a] = openSlotMuls[b];
						openSlotMuls[b] = tm;
					}
				}
			}

			Span<int> artifactCaps = stackalloc int[items.Length];
			Span<int> artifactBases = stackalloc int[items.Length];
			Span<int> artifactValues = stackalloc int[items.Length];
			int unpinnedActiveArtCount = 0;

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
				int rawDelta = openSlotDeltas[j];
				int sMul = openSlotMuls[j];
				if (j < unpinnedActiveArtCount) {
					if (rawDelta <= -90) {
						fieldScore -= ObjectiveEvaluator.DeactivationBasePenalty;
					} else {
						int disp = (artifactBases[j] + rawDelta) * sMul;
						int gain = disp - artifactBases[j];
						if (disp <= -1) {
							fieldScore -= ObjectiveEvaluator.DeactivationBasePenalty;
						} else if (gain >= 0) {
							int used = Math.Min(gain, artifactCaps[j]);
							int overflow = Math.Max(0, gain - used);
							fieldScore += (used * artifactValues[j]) + (overflow * 8);
							if (artifactCaps[j] > 0 && used == artifactCaps[j]) {
								fieldScore += ObjectiveEvaluator.MaxLevelReachedBonus;
							}
						} else {
							fieldScore += gain * 200;
						}
					}
				} else {
					int scaled = rawDelta * sMul;
					if (rawDelta <= -90) {
						fieldScore += ObjectiveEvaluator.NeutralizedDisableBonus;
					} else if (scaled < 0) {
						fieldScore += (-scaled) * (ObjectiveEvaluator.NeutralizedNegativeLevelBonus + 35);
					} else if (scaled > 0) {
						fieldScore -= scaled * ObjectiveEvaluator.WastedPositiveOnEmptySlotPenalty;
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
						if (isTablet[cond.TargetSlotIndex] == 0) {
							reqCharm[cond.TargetSlotIndex] = 1;
						}
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

			if (estimatedMysticTags >= snapshot.MysticTier1Threshold) {
				if ((uint)snapshot.MysticSlot0 < (uint)unlockedSlots) {
					mask |= 1UL << snapshot.MysticSlot0;
				}
				int[] extraTier1 = snapshot.ExtraMysticTier1Slots;
				for (int m = 0; m < extraTier1.Length; m++) {
					if ((uint)extraTier1[m] < (uint)unlockedSlots) {
						mask |= 1UL << extraTier1[m];
					}
				}
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
				int totalMul = slotMul[s] + ((mysticMask & (1UL << s)) != 0UL ? 2 : 0);
				slotMul[s] = totalMul;
				slotDelta[s] = totalMul != 0 ? (slotDelta[s] * totalMul) : slotDelta[s];
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
					(sbyte X, sbyte Y)[] parentOffsets = needleItem.DependencyParentOffsets;
					int offCount = Math.Max(1, parentOffsets.Length);

					int bestTopSlot = -1;
					int bestBotSlot = -1;
					int bestPairScore = int.MinValue;

					for (int o = 0; o < offCount; o++) {
						int offX = parentOffsets.Length > 0 ? parentOffsets[o].X : needleItem.DependencyOffsetX;
						int offY = parentOffsets.Length > 0 ? parentOffsets[o].Y : needleItem.DependencyOffsetY;
						if (offX == 0 && offY == 0) {
							offY = -1;
						}
						for (int top = 0; top < unlockedSlots; top++) {
							if (workingSlots[top] >= 0 || slotDisable[top] > 0) {
								continue;
							}
							SlotCoord tCoord = snapshot.Slots[top];
							int bot = snapshot.CoordToSlotIndex(tCoord.X - offX, tCoord.Y - offY);
							if (bot < 0 || bot == top || workingSlots[bot] >= 0 || slotDisable[bot] > 0) {
								continue;
							}
							int topMul = slotMul[top];
							int botMul = slotMul[bot];
							int rawTopDelta = (slotMul[top] != 0 ? slotDelta[top] / slotMul[top] : slotDelta[top]);
							int rawBotDelta = (slotMul[bot] != 0 ? slotDelta[bot] / slotMul[bot] : slotDelta[bot]);
							int topPre = attackItem.BaseLevel + rawTopDelta;
							int botPre = needleItem.BaseLevel + rawBotDelta;
							int topDisp = topMul != 0 ? (topPre * topMul) : topPre;
							int botDisp = botMul != 0 ? (botPre * botMul) : botPre;
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
								bestBotSlot = bot;
							}
						}
					}

					if (bestTopSlot >= 0 && bestBotSlot >= 0) {
						workingSlots[bestTopSlot] = attackIdx;
						workingSlots[bestBotSlot] = needleIdx;
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
					int sMul = slotMul[s];
					int rawDelta = sMul != 0 ? (slotDelta[s] / sMul) : slotDelta[s];
					int disp = sMul != 0 ? ((crystal.BaseLevel + rawDelta) * sMul) : (crystal.BaseLevel + rawDelta);
					if (disp <= -1) {
						continue;
					}
					ulong mask = snapshot.MooreNeighborMasks[s];
					int totalNeighbors = BitOperations.PopCount(mask);
					int neighborPotentialSum = 0;
					int tabletNeighbors = 0;
					while (mask != 0UL) {
						int nSlot = BitOperations.TrailingZeroCount(mask);
						mask &= mask - 1UL;
						if (isTablet[nSlot] != 0) {
							tabletNeighbors++;
						} else if (slotDisable[nSlot] == 0) {
							int existing = workingSlots[nSlot];
							if (existing >= 0 && items[existing].Kind == SlotItemKind.Artifact) {
								int eMul = slotMul[nSlot];
								int eRaw = eMul != 0 ? (slotDelta[nSlot] / eMul) : slotDelta[nSlot];
								int eDisp = eMul != 0 ? ((items[existing].BaseLevel + eRaw) * eMul)
									: (items[existing].BaseLevel + eRaw);
								neighborPotentialSum += Math.Min(items[existing].MaxLevel, Math.Max(0, eDisp));
							} else {
								neighborPotentialSum += Math.Max(0, slotDelta[nSlot]);
							}
						}
					}
					int eff = Math.Clamp(disp, 0, crystal.MaxLevel);
					int candScore = (eff * 240)
						+ (totalNeighbors * 20)
						+ (neighborPotentialSum * 180)
						- (tabletNeighbors * 35);
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
					int sMul = slotMul[s];
					int rawDelta = sMul != 0 ? (slotDelta[s] / sMul) : slotDelta[s];
					int disp = sMul != 0 ? ((art.BaseLevel + rawDelta) * sMul) : (art.BaseLevel + rawDelta);
					int delta = disp - art.BaseLevel;
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
			ref int evaluations,
			EliteCandidatePool elitePool,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations
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

							int score = ObjectiveEvaluator.Evaluate(
								snapshot,
								workingSlots,
								workingRotations
							);
							evaluations++;
							if (score >= bestScore - 30) {
								elitePool.TryOffer(
									snapshot,
									initialSlots,
									initialRotations,
									workingSlots,
									workingRotations,
									score,
									ref evaluations,
									countEval: false,
									forceEval: score >= bestScore
								);
							}

							if (score > bestScore) {
								bestScore = score;
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

		private static void ExploreEqualOptimaAndMinimizeCost(
			InventorySnapshot snapshot,
			Stopwatch stopwatch,
			int deadlineMs,
			EliteCandidatePool elitePool,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			ref int evaluations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			Span<int> workSlots = stackalloc int[unlockedSlots];
			Span<int> workRots = stackalloc int[itemCount];

			for (int e = 0; e < elitePool.Count; e++) {
				elitePool.CopyTo(e, workSlots, workRots);
				AlignSymmetricItemsToInitialSlots(snapshot, workSlots, workRots, initialSlots, initialRotations);
				MinimizeExecutionCostWithoutLoss(
					snapshot,
					workSlots,
					workRots,
					initialSlots,
					initialRotations,
					ref evaluations,
					out CandidateMetrics refinedMetrics
				);
				elitePool.ReplaceEntry(e, workSlots, workRots, in refinedMetrics);
			}
			elitePool.DeduplicateAndSort(snapshot);

			if (elitePool.Count > 0 && stopwatch.ElapsedMilliseconds < deadlineMs + 80) {
				elitePool.CopyTo(0, workSlots, workRots);
				CandidateMetrics bestMetrics = elitePool.GetMetrics(0);

				for (int i = 0; i < itemCount; i++) {
					if (items[i].Kind != SlotItemKind.StoneTablet || !items[i].IsRotatable) {
						continue;
					}
					int tSlot = FindItemSlot(workSlots, i);
					if (tSlot >= 0 && pinnedSlots[tSlot]) {
						continue;
					}
					int savedRot = workRots[i];
					for (int rot = 0; rot < 4; rot++) {
						if (rot == savedRot) {
							continue;
						}
						if (tSlot >= 0 && IsRedundantTabletRotation(items[i], tSlot, rot)) {
							continue;
						}
						workRots[i] = rot;
						int sc = ObjectiveEvaluator.Evaluate(snapshot, workSlots, workRots);
						evaluations++;
						if (sc >= bestMetrics.Score - 200) {
							elitePool.TryOffer(
								snapshot,
								initialSlots,
								initialRotations,
								workSlots,
								workRots,
								sc,
								ref evaluations,
								countEval: false,
								forceEval: true
							);
						}
					}
					workRots[i] = savedRot;
				}

				for (int sA = 0; sA < unlockedSlots; sA++) {
					if (pinnedSlots[sA]) {
						continue;
					}
					for (int sB = sA + 1; sB < unlockedSlots; sB++) {
						if (pinnedSlots[sB]) {
							continue;
						}
						int itemA = workSlots[sA];
						int itemB = workSlots[sB];
						if (itemA == itemB) {
							continue;
						}
						if (itemA >= 0 && itemB >= 0 && items[itemA].SymmetryGroupId == items[itemB].SymmetryGroupId) {
							continue;
						}

						workSlots[sA] = itemB;
						workSlots[sB] = itemA;
						int sc = ObjectiveEvaluator.Evaluate(snapshot, workSlots, workRots);
						evaluations++;
						if (sc >= bestMetrics.Score - 200) {
							elitePool.TryOffer(
								snapshot,
								initialSlots,
								initialRotations,
								workSlots,
								workRots,
								sc,
								ref evaluations,
								countEval: false,
								forceEval: true
							);
						}
						workSlots[sA] = itemA;
						workSlots[sB] = itemB;
					}
				}

				for (int e = 0; e < elitePool.Count; e++) {
					elitePool.CopyTo(e, workSlots, workRots);
					AlignSymmetricItemsToInitialSlots(snapshot, workSlots, workRots, initialSlots, initialRotations);
					MinimizeExecutionCostWithoutLoss(
						snapshot,
						workSlots,
						workRots,
						initialSlots,
						initialRotations,
						ref evaluations,
						out CandidateMetrics refinedMetrics
					);
					elitePool.ReplaceEntry(e, workSlots, workRots, in refinedMetrics);
				}
				elitePool.DeduplicateAndSort(snapshot);
			}
		}

		private static void AlignSymmetricItemsToInitialSlots(
			InventorySnapshot snapshot,
			Span<int> slotToItem,
			Span<int> itemRotations,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			SlotItemSnapshot[] items = snapshot.Items;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			Span<int> symGroupSlots = stackalloc int[unlockedSlots];
			Span<int> symGroupItems = stackalloc int[unlockedSlots];

			for (int i = 0; i < itemCount; i++) {
				int groupId = items[i].SymmetryGroupId;
				int slot = FindItemSlot(slotToItem, i);
				if (slot < 0 || pinnedSlots[slot]) {
					continue;
				}

				int count = 0;
				for (int s = 0; s < unlockedSlots; s++) {
					if (pinnedSlots[s]) {
						continue;
					}
					int idx = slotToItem[s];
					if (idx >= 0 && items[idx].SymmetryGroupId == groupId) {
						symGroupSlots[count] = s;
						symGroupItems[count] = idx;
						count++;
					}
				}
				if (count <= 1) {
					continue;
				}

				for (int a = 0; a < count; a++) {
					int targetSlot = symGroupSlots[a];
					int initItemAtSlot = initialSlots[targetSlot];
					if (initItemAtSlot < 0 || items[initItemAtSlot].SymmetryGroupId != groupId) {
						continue;
					}
					int curHolder = FindItemSlot(slotToItem, initItemAtSlot);
					if (curHolder >= 0 && curHolder != targetSlot && !pinnedSlots[curHolder]) {
						int displacedItem = slotToItem[targetSlot];
						slotToItem[targetSlot] = initItemAtSlot;
						slotToItem[curHolder] = displacedItem;
						int tmpRot = itemRotations[initItemAtSlot];
						itemRotations[initItemAtSlot] = itemRotations[displacedItem];
						itemRotations[displacedItem] = tmpRot;
					}
				}
			}

			for (int s = 0; s < unlockedSlots; s++) {
				int itemIdx = slotToItem[s];
				if (itemIdx < 0 || items[itemIdx].Kind != SlotItemKind.StoneTablet || !items[itemIdx].IsRotatable) {
					continue;
				}
				int initRot = initialRotations[itemIdx] & 3;
				int targetRot = itemRotations[itemIdx] & 3;
				int canonicalTarget = ObjectiveEvaluator.GetCanonicalRotation(items[itemIdx], s, targetRot);
				int bestRot = targetRot;
				int bestClicks = (targetRot - initRot) & 3;
				for (int r = 0; r < 4; r++) {
					if (ObjectiveEvaluator.GetCanonicalRotation(items[itemIdx], s, r) == canonicalTarget) {
						int clicks = (r - initRot) & 3;
						if (clicks < bestClicks) {
							bestClicks = clicks;
							bestRot = r;
						}
					}
				}
				itemRotations[itemIdx] = bestRot;
			}
		}

		private static void MinimizeExecutionCostWithoutLoss(
			InventorySnapshot snapshot,
			Span<int> workSlots,
			Span<int> workRotations,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations,
			ref int evaluations,
			out CandidateMetrics finalMetrics
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			bool[] pinnedSlots = snapshot.PinnedSlots;

			int curScore = ObjectiveEvaluator.EvaluateWithMetrics(
				snapshot,
				workSlots,
				workRotations,
				out double curDps,
				out int curViol,
				out int curUtil
			);
			CycleDecomposition.CountExecutionCost(
				snapshot,
				initialSlots,
				workSlots,
				initialRotations,
				workRotations,
				out int curSwaps,
				out int curRots
			);
			int curCost = curSwaps + curRots;

			for (int pass = 0; pass < 3; pass++) {
				bool reduced = false;
				for (int sA = 0; sA < unlockedSlots; sA++) {
					if (pinnedSlots[sA] || workSlots[sA] == initialSlots[sA]) {
						continue;
					}
					for (int sB = sA + 1; sB < unlockedSlots; sB++) {
						if (pinnedSlots[sB]) {
							continue;
						}
						int itemA = workSlots[sA];
						int itemB = workSlots[sB];
						if (itemA == itemB) {
							continue;
						}
						if (itemB != initialSlots[sA] && itemA != initialSlots[sB]) {
							continue;
						}

						workSlots[sA] = itemB;
						workSlots[sB] = itemA;
						int candScore = ObjectiveEvaluator.EvaluateWithMetrics(
							snapshot,
							workSlots,
							workRotations,
							out double candDps,
							out int candViol,
							out int candUtil
						);
						evaluations++;

						long curCenti = (long)Math.Round(curDps * 100.0);
						long candCenti = (long)Math.Round(candDps * 100.0);
						if (candCenti >= curCenti && candViol <= curViol && candUtil >= curUtil && candScore >= curScore) {
							CycleDecomposition.CountExecutionCost(
								snapshot,
								initialSlots,
								workSlots,
								initialRotations,
								workRotations,
								out int nextSwaps,
								out int nextRots
							);
							int nextCost = nextSwaps + nextRots;
							if (nextCost < curCost || (nextCost == curCost && nextSwaps < curSwaps)) {
								curScore = candScore;
								curDps = candDps;
								curViol = candViol;
								curUtil = candUtil;
								curSwaps = nextSwaps;
								curRots = nextRots;
								curCost = nextCost;
								reduced = true;
								continue;
							}
						}

						workSlots[sA] = itemA;
						workSlots[sB] = itemB;
					}
				}
				if (!reduced) {
					break;
				}
			}

			ulong finalHash = ObjectiveEvaluator.ComputeCanonicalLayoutHash(snapshot, workSlots, workRotations);
			finalMetrics = new CandidateMetrics(
				curScore,
				curDps,
				curViol,
				curUtil,
				curSwaps,
				curRots,
				finalHash
			);
		}

		private static IReadOnlyList<OptimizationCandidate> FinalizeTopCandidates(
			InventorySnapshot snapshot,
			EliteCandidatePool elitePool,
			ReadOnlySpan<int> initialSlots,
			ReadOnlySpan<int> initialRotations
		) {
			int unlockedSlots = snapshot.UnlockedSlots;
			int itemCount = snapshot.Items.Length;
			List<OptimizationCandidate> resultList = new List<OptimizationCandidate>(MaxEliteCandidates);
			CandidateMetrics bestMetrics = elitePool.GetMetrics(0);

			for (int i = 0; i < elitePool.Count; i++) {
				CandidateMetrics candMetrics = elitePool.GetMetrics(i);
				if (i > 0 && !IsEligibleAlternative(in bestMetrics, in candMetrics)) {
					continue;
				}

				int[] candSlots = new int[unlockedSlots];
				Array.Copy(elitePool.GetSlots(i), candSlots, unlockedSlots);
				int[] candRots = new int[itemCount];
				Array.Copy(elitePool.GetRotations(i), candRots, itemCount);
				int[] candItemToSlot = BuildItemToSlotMap(candSlots, itemCount);

				SlotEvaluationDetail[] candDetails = ObjectiveEvaluator.EvaluateDetailed(
					snapshot,
					candSlots,
					candRots
				);
				DpsBreakdown candBreakdown = DpsEstimator.CalculateBreakdown(
					snapshot,
					candSlots,
					candRots
				);

				CycleDecomposition.CountExecutionCost(
					snapshot,
					initialSlots,
					candSlots,
					initialRotations,
					candRots,
					out int swaps,
					out int rots
				);
				CandidateMetrics exactMetrics = new CandidateMetrics(
					candMetrics.Score,
					candBreakdown.TotalEstimatedDps,
					candMetrics.HardViolationCount,
					candMetrics.UtilityEffectiveLevelSum,
					swaps,
					rots,
					candMetrics.CanonicalHash
				);

				int rank = resultList.Count + 1;
				string diffSummary = BuildCandidateDiffSummary(in bestMetrics, in exactMetrics, rank);
				resultList.Add(
					new OptimizationCandidate(
						rank,
						in exactMetrics,
						in candBreakdown,
						candSlots,
						candItemToSlot,
						candRots,
						candDetails,
						diffSummary
					)
				);
			}

			return resultList;
		}

		private static bool IsEligibleAlternative(in CandidateMetrics best, in CandidateMetrics alt) {
			if (alt.HardViolationCount > best.HardViolationCount) {
				return false;
			}
			if (best.ExactEstimatedDps > 1.0) {
				return alt.ExactEstimatedDps >= best.ExactEstimatedDps * 0.999
					&& alt.Score >= best.Score - 200;
			}
			return alt.Score == best.Score;
		}

		private static string BuildCandidateDiffSummary(
			in CandidateMetrics best,
			in CandidateMetrics alt,
			int rank
		) {
			if (rank == 1) {
				return "기본 추천";
			}

			long bestCenti = (long)Math.Round(best.ExactEstimatedDps * 100.0);
			long altCenti = (long)Math.Round(alt.ExactEstimatedDps * 100.0);
			string dpsText;
			if (bestCenti == altCenti) {
				dpsText = "동일 DPS";
			} else {
				int dpsDelta = alt.EstimatedDps - best.EstimatedDps;
				dpsText = dpsDelta == 0 ? "동일 DPS" : $"DPS {(dpsDelta > 0 ? "+" : "")}{dpsDelta:N0}";
			}

			int violDiff = alt.HardViolationCount - best.HardViolationCount;
			int utilDiff = alt.UtilityEffectiveLevelSum - best.UtilityEffectiveLevelSum;
			int swapDiff = best.SwapCount - alt.SwapCount;
			int rotDiff = best.RotateClickCount - alt.RotateClickCount;

			string detailText;
			if (violDiff > 0) {
				detailText = $"위반 +{violDiff}";
			} else if (utilDiff != 0) {
				detailText = $"유틸 레벨 {(utilDiff > 0 ? "+" : "")}{utilDiff}";
			} else if (swapDiff > 0) {
				detailText = $"스왑 {swapDiff}회 단축";
			} else if (swapDiff < 0) {
				detailText = $"스왑 +{-swapDiff}회";
			} else if (rotDiff > 0) {
				detailText = $"회전 {rotDiff}회 단축";
			} else if (rotDiff < 0) {
				detailText = $"회전 +{-rotDiff}회";
			} else {
				detailText = "대안 배치";
			}

			return $"{dpsText} · {detailText}";
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
			int count,
			bool negativeFirst
		) {
			for (int i = 1; i < count; i++) {
				int keyIdx = tablets[i];
				int keyImpact = GetTabletFieldImpactScore(items[keyIdx], negativeFirst);
				int j = i - 1;
				while (j >= 0 && GetTabletFieldImpactScore(items[tablets[j]], negativeFirst) < keyImpact) {
					tablets[j + 1] = tablets[j];
					j--;
				}
				tablets[j + 1] = keyIdx;
			}
		}

		private static int GetTabletFieldImpactScore(SlotItemSnapshot tablet, bool negativeFirst) {
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
						score += negativeFirst ? 300 : 800;
						break;
					}
					case TabletEffectType.Disable: {
						score += negativeFirst ? 900 : 350;
						break;
					}
					case TabletEffectType.IgnoreCriteria: {
						score += 250;
						break;
					}
					case TabletEffectType.IncreaseConstLevel: {
						if (eff.LevelParam < 0) {
							score += (-eff.LevelParam) * (negativeFirst ? 500 : 120);
						} else {
							score += eff.LevelParam * (negativeFirst ? 90 : 120);
						}
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
