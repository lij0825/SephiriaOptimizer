using System;

namespace SephiriaOptimizer.Models {
	public readonly struct PrecomputedCondition {
		public sbyte TargetX { get; }
		public sbyte TargetY { get; }
		public int TargetSlotIndex { get; }
		public TabletConditionType ConditionType { get; }

		public PrecomputedCondition(
			sbyte targetX,
			sbyte targetY,
			int targetSlotIndex,
			TabletConditionType conditionType
		) {
			TargetX = targetX;
			TargetY = targetY;
			TargetSlotIndex = targetSlotIndex;
			ConditionType = conditionType;
		}
	}

	public readonly struct PrecomputedEffect {
		public sbyte TargetX { get; }
		public sbyte TargetY { get; }
		public int TargetSlotIndex { get; }
		public TabletEffectType EffectType { get; }
		public int LevelParam { get; }

		public PrecomputedEffect(
			sbyte targetX,
			sbyte targetY,
			int targetSlotIndex,
			TabletEffectType effectType,
			int levelParam
		) {
			TargetX = targetX;
			TargetY = targetY;
			TargetSlotIndex = targetSlotIndex;
			EffectType = effectType;
			LevelParam = levelParam;
		}
	}

	public sealed class TabletPlacementRule {
		public static readonly TabletPlacementRule Empty = new TabletPlacementRule(
			Array.Empty<PrecomputedCondition>(),
			Array.Empty<PrecomputedEffect>()
		);

		public PrecomputedCondition[] Conditions { get; }
		public PrecomputedEffect[] Effects { get; }
		public bool HasPlacedCondition { get; }

		public TabletPlacementRule(PrecomputedCondition[] conditions, PrecomputedEffect[] effects) {
			Conditions = conditions ?? Array.Empty<PrecomputedCondition>();
			Effects = effects ?? Array.Empty<PrecomputedEffect>();
			bool hasPlaced = false;
			for (int i = 0; i < Conditions.Length; i++) {
				if (Conditions[i].ConditionType == TabletConditionType.Placed) {
					hasPlaced = true;
					break;
				}
			}
			HasPlacedCondition = hasPlaced;
		}
	}
}
