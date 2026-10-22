using System;

namespace SephiriaOptimizer.Models {
	public sealed class SlotItemSnapshot {
		public int ItemIndex { get; }
		public int InstanceId { get; }
		public int EntityId { get; }
		public string DisplayName { get; }
		public SlotItemKind Kind { get; }
		public int InitialSlotIndex { get; }
		public int InitialRotation { get; }
		public bool IsRotatable { get; }
		public int BaseLevel { get; }
		public int MaxLevel { get; }
		public int RarityTier { get; }
		public int CustomWeight { get; }
		public ArtifactConstraintType ConstraintType { get; }
		public bool IsDisabledByWeaponOrDual { get; }
		public bool IsMagicBook { get; }
		public bool WantsRightSideMagicBook { get; }
		public bool IsAttackableCharm { get; }
		public bool HasDependencyTarget { get; }
		public sbyte DependencyOffsetX { get; }
		public sbyte DependencyOffsetY { get; }
		public bool IsElementalByRowCharm { get; }
		public bool IsWhitePaperCharm { get; }
		public string[] Categories { get; }
		public TabletPlacementRule[] PlacementRules { get; }
		public int SymmetryGroupId { get; }

		public SlotItemSnapshot(
			int itemIndex,
			int instanceId,
			int entityId,
			string displayName,
			SlotItemKind kind,
			int initialSlotIndex,
			int initialRotation,
			bool isRotatable,
			int baseLevel,
			int maxLevel,
			int rarityTier,
			int customWeight,
			ArtifactConstraintType constraintType,
			bool isDisabledByWeaponOrDual,
			bool isMagicBook,
			bool wantsRightSideMagicBook,
			bool isAttackableCharm,
			bool hasDependencyTarget,
			sbyte dependencyOffsetX,
			sbyte dependencyOffsetY,
			bool isElementalByRowCharm,
			bool isWhitePaperCharm,
			string[] categories,
			TabletPlacementRule[] placementRules,
			int symmetryGroupId
		) {
			ItemIndex = itemIndex;
			InstanceId = instanceId;
			EntityId = entityId;
			DisplayName = displayName ?? string.Empty;
			Kind = kind;
			InitialSlotIndex = initialSlotIndex;
			InitialRotation = initialRotation & 3;
			IsRotatable = isRotatable;
			BaseLevel = baseLevel;
			MaxLevel = Math.Max(0, maxLevel);
			RarityTier = rarityTier;
			CustomWeight = Math.Max(1, customWeight);
			ConstraintType = constraintType;
			IsDisabledByWeaponOrDual = isDisabledByWeaponOrDual;
			IsMagicBook = isMagicBook;
			WantsRightSideMagicBook = wantsRightSideMagicBook;
			IsAttackableCharm = isAttackableCharm;
			HasDependencyTarget = hasDependencyTarget;
			DependencyOffsetX = dependencyOffsetX;
			DependencyOffsetY = dependencyOffsetY;
			IsElementalByRowCharm = isElementalByRowCharm;
			IsWhitePaperCharm = isWhitePaperCharm;
			Categories = categories ?? Array.Empty<string>();
			PlacementRules = placementRules ?? Array.Empty<TabletPlacementRule>();
			SymmetryGroupId = symmetryGroupId;
		}

		public TabletPlacementRule GetPlacementRule(int slotIndex, int rotation) {
			int lookupIndex = (slotIndex * 4) + (rotation & 3);
			if (lookupIndex >= 0 && lookupIndex < PlacementRules.Length) {
				return PlacementRules[lookupIndex];
			}
			return TabletPlacementRule.Empty;
		}
	}
}
