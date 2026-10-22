using System;

namespace SephiriaOptimizer.Solver {
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
		public int[] BestSlotToItemIndex { get; }
		public int[] BestItemToSlotIndex { get; }
		public int[] BestItemRotations { get; }
		public SlotEvaluationDetail[] InitialSlotDetails { get; }
		public SlotEvaluationDetail[] BestSlotDetails { get; }
		public bool UsedExactBranchAndBound { get; }
		public int EvaluationsPerformed { get; }
		public double ElapsedMilliseconds { get; }

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
			double elapsedMilliseconds
		) {
			InitialScore = initialScore;
			BestScore = bestScore;
			BestSlotToItemIndex = bestSlotToItemIndex ?? Array.Empty<int>();
			BestItemToSlotIndex = bestItemToSlotIndex ?? Array.Empty<int>();
			BestItemRotations = bestItemRotations ?? Array.Empty<int>();
			InitialSlotDetails = initialSlotDetails ?? Array.Empty<SlotEvaluationDetail>();
			BestSlotDetails = bestSlotDetails ?? Array.Empty<SlotEvaluationDetail>();
			UsedExactBranchAndBound = usedExactBranchAndBound;
			EvaluationsPerformed = evaluationsPerformed;
			ElapsedMilliseconds = elapsedMilliseconds;
		}
	}
}
