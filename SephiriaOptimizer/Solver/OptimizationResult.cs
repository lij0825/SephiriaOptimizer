using System;
using System.Collections.Generic;

namespace SephiriaOptimizer.Solver {
	public readonly struct CandidateMetrics {
		public int Score { get; }
		public double ExactEstimatedDps { get; }
		public int EstimatedDps { get; }
		public int HardViolationCount { get; }
		public int UtilityEffectiveLevelSum { get; }
		public int SwapCount { get; }
		public int RotateClickCount { get; }
		public int ExecutionCost => SwapCount + RotateClickCount;
		public ulong CanonicalHash { get; }

		public CandidateMetrics(
			int score,
			double exactEstimatedDps,
			int hardViolationCount,
			int utilityEffectiveLevelSum,
			int swapCount,
			int rotateClickCount,
			ulong canonicalHash
		) {
			Score = score;
			ExactEstimatedDps = Math.Max(0.0, exactEstimatedDps);
			EstimatedDps = Math.Max(0, (int)Math.Round(ExactEstimatedDps));
			HardViolationCount = Math.Max(0, hardViolationCount);
			UtilityEffectiveLevelSum = utilityEffectiveLevelSum;
			SwapCount = Math.Max(0, swapCount);
			RotateClickCount = Math.Max(0, rotateClickCount);
			CanonicalHash = canonicalHash;
		}

		public static int CompareLexicographical(in CandidateMetrics left, in CandidateMetrics right) {
			long leftCentiDps = (long)Math.Round(left.ExactEstimatedDps * 100.0);
			long rightCentiDps = (long)Math.Round(right.ExactEstimatedDps * 100.0);
			if (leftCentiDps != rightCentiDps) {
				return rightCentiDps.CompareTo(leftCentiDps);
			}

			bool leftZeroViol = left.HardViolationCount == 0;
			bool rightZeroViol = right.HardViolationCount == 0;
			if (leftZeroViol != rightZeroViol) {
				return leftZeroViol ? -1 : 1;
			}
			if (left.HardViolationCount != right.HardViolationCount) {
				return left.HardViolationCount.CompareTo(right.HardViolationCount);
			}

			if (left.UtilityEffectiveLevelSum != right.UtilityEffectiveLevelSum) {
				return right.UtilityEffectiveLevelSum.CompareTo(left.UtilityEffectiveLevelSum);
			}
			if (left.Score != right.Score) {
				return right.Score.CompareTo(left.Score);
			}

			if (left.ExecutionCost != right.ExecutionCost) {
				return left.ExecutionCost.CompareTo(right.ExecutionCost);
			}
			if (left.SwapCount != right.SwapCount) {
				return left.SwapCount.CompareTo(right.SwapCount);
			}
			return left.CanonicalHash.CompareTo(right.CanonicalHash);
		}
	}

	public sealed class OptimizationCandidate {
		public int Rank { get; }
		public CandidateMetrics Metrics { get; }
		public DpsBreakdown Breakdown { get; }
		public int[] SlotToItemIndex { get; }
		public int[] ItemToSlotIndex { get; }
		public int[] ItemRotations { get; }
		public SlotEvaluationDetail[] SlotDetails { get; }
		public string DiffSummary { get; }

		public OptimizationCandidate(
			int rank,
			in CandidateMetrics metrics,
			in DpsBreakdown breakdown,
			int[] slotToItemIndex,
			int[] itemToSlotIndex,
			int[] itemRotations,
			SlotEvaluationDetail[] slotDetails,
			string diffSummary
		) {
			Rank = rank;
			Metrics = metrics;
			Breakdown = breakdown;
			SlotToItemIndex = slotToItemIndex ?? Array.Empty<int>();
			ItemToSlotIndex = itemToSlotIndex ?? Array.Empty<int>();
			ItemRotations = itemRotations ?? Array.Empty<int>();
			SlotDetails = slotDetails ?? Array.Empty<SlotEvaluationDetail>();
			DiffSummary = diffSummary ?? string.Empty;
		}
	}

	public readonly struct SlotEvaluationDetail {
		public int SlotIndex { get; }
		public int ItemIndex { get; }
		public int DisplayedLevel { get; }
		public int EffectiveLevel { get; }
		public int MaxLevel { get; }
		public bool IsEnabled { get; }
		public bool IsDeactivatedByNegativeLevel { get; }
		public bool IsDeactivatedByDisableEffect { get; }
		public bool IsDeactivatedByCriteria { get; }
		public bool IsCriteriaIgnored { get; }
		public uint DynamicCategoryMask { get; }
		public int ConnectedTargetSlotIndex { get; }
		public bool IsEngravingReady { get; }

		public SlotEvaluationDetail(
			int slotIndex,
			int itemIndex,
			int displayedLevel,
			int effectiveLevel,
			int maxLevel,
			bool isEnabled,
			bool isDeactivatedByNegativeLevel,
			bool isDeactivatedByDisableEffect,
			bool isDeactivatedByCriteria,
			bool isCriteriaIgnored,
			uint dynamicCategoryMask = 0u,
			int connectedTargetSlotIndex = -1,
			bool isEngravingReady = false
		) {
			SlotIndex = slotIndex;
			ItemIndex = itemIndex;
			DisplayedLevel = displayedLevel;
			EffectiveLevel = effectiveLevel;
			MaxLevel = maxLevel;
			IsEnabled = isEnabled;
			IsDeactivatedByNegativeLevel = isDeactivatedByNegativeLevel;
			IsDeactivatedByDisableEffect = isDeactivatedByDisableEffect;
			IsDeactivatedByCriteria = isDeactivatedByCriteria;
			IsCriteriaIgnored = isCriteriaIgnored;
			DynamicCategoryMask = dynamicCategoryMask;
			ConnectedTargetSlotIndex = connectedTargetSlotIndex;
			IsEngravingReady = isEngravingReady;
		}
	}

	public sealed class OptimizationResult {
		public int InitialScore { get; }
		public int BestScore { get; }
		public int InitialEstimatedDps { get; }
		public int BestEstimatedDps { get; }
		public int EstimatedDpsDelta { get; }
		public double EstimatedDpsGainPercent { get; }
		public DpsBreakdown CurrentBreakdown { get; }
		public DpsBreakdown OptimizedBreakdown { get; }
		public int[] BestSlotToItemIndex { get; }
		public int[] BestItemToSlotIndex { get; }
		public int[] BestItemRotations { get; }
		public SlotEvaluationDetail[] InitialSlotDetails { get; }
		public SlotEvaluationDetail[] BestSlotDetails { get; }
		public bool UsedExactBranchAndBound { get; }
		public int EvaluationsPerformed { get; }
		public double ElapsedMilliseconds { get; }
		public IReadOnlyList<OptimizationCandidate> TopCandidates { get; }
		public bool HasAlternativeCandidates => TopCandidates.Count > 1;

		public OptimizationResult(
			int initialScore,
			int bestScore,
			int[] bestSlotToItemIndex,
			int[] bestItemToSlotIndex,
			int[] bestItemRotations,
			SlotEvaluationDetail[] initialSlotDetails,
			SlotEvaluationDetail[] bestSlotDetails,
			bool usedExactBranchAndBound,
			int evaluationsPerformed,
			double elapsedMilliseconds,
			int initialEstimatedDps = 0,
			int bestEstimatedDps = 0,
			DpsBreakdown currentBreakdown = default,
			DpsBreakdown optimizedBreakdown = default,
			IReadOnlyList<OptimizationCandidate>? topCandidates = null
		) {
			InitialScore = initialScore;
			BestScore = bestScore;
			InitialEstimatedDps = Math.Max(0, initialEstimatedDps);
			BestEstimatedDps = Math.Max(0, bestEstimatedDps);
			EstimatedDpsDelta = BestEstimatedDps - InitialEstimatedDps;
			EstimatedDpsGainPercent = InitialEstimatedDps > 0
				? ((double)EstimatedDpsDelta * 100.0) / InitialEstimatedDps
				: 0.0;
			CurrentBreakdown = currentBreakdown;
			OptimizedBreakdown = optimizedBreakdown;
			BestSlotToItemIndex = bestSlotToItemIndex ?? Array.Empty<int>();
			BestItemToSlotIndex = bestItemToSlotIndex ?? Array.Empty<int>();
			BestItemRotations = bestItemRotations ?? Array.Empty<int>();
			InitialSlotDetails = initialSlotDetails ?? Array.Empty<SlotEvaluationDetail>();
			BestSlotDetails = bestSlotDetails ?? Array.Empty<SlotEvaluationDetail>();
			UsedExactBranchAndBound = usedExactBranchAndBound;
			EvaluationsPerformed = evaluationsPerformed;
			ElapsedMilliseconds = elapsedMilliseconds;
			if (topCandidates != null && topCandidates.Count > 0) {
				TopCandidates = topCandidates;
			} else {
				double fallbackExactDps = optimizedBreakdown.TotalEstimatedDps > 0.0
					? optimizedBreakdown.TotalEstimatedDps
					: BestEstimatedDps;
				CandidateMetrics primaryMetrics = new CandidateMetrics(
					BestScore,
					fallbackExactDps,
					0,
					0,
					0,
					0,
					0UL
				);
				TopCandidates = new OptimizationCandidate[] {
					new OptimizationCandidate(
						1,
						in primaryMetrics,
						in optimizedBreakdown,
						BestSlotToItemIndex,
						BestItemToSlotIndex,
						BestItemRotations,
						BestSlotDetails,
						"기본 추천"
					)
				};
			}
		}
	}
}
