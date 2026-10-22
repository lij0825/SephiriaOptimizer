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
		public ArtifactPriorityTier PriorityTier { get; }
		public ArtifactConstraintType ConstraintType { get; }
		public bool IsDisabledByWeaponOrDual { get; }
		public bool IsMagicBook { get; }
		public bool WantsRightSideMagicBook { get; }
		public bool IsAttackableCharm { get; }
		public bool HasDependencyTarget { get; }
		public sbyte DependencyOffsetX { get; }
		public sbyte DependencyOffsetY { get; }
		public bool HasDependencyCondition { get; }
		public int DependencyMaxRarity { get; }
		public int[] NeedleDamageBonusByLevel { get; }
		public int[] NeedleDependencyBonusByLevel { get; }
		public bool IsElementalByRowCharm { get; }
		public int[] RowCategoryIds { get; }
		public int[] RowElementalBonusByLevel { get; }
		public bool IsWhitePaperCharm { get; }
		public int WhitePaperMatchCount { get; }
		public bool IsNearLevelDamageCharm { get; }
		public bool IsCompanionChaosCharm { get; }
		public bool IsCompanionCharm { get; }
		public bool IsPlanetModuleCharm { get; }
		public bool IsPlanetCharm { get; }
		public bool IsNearMagicBulletCharm { get; }
		public bool IsWoodenBoxCharm { get; }
		public bool IsFireIcePositionCharm { get; }
		public uint StaticCategoryMask { get; }
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
			int symmetryGroupId,
			ArtifactPriorityTier priorityTier = ArtifactPriorityTier.Normal,
			uint staticCategoryMask = 0u,
			bool hasDependencyCondition = false,
			int dependencyMaxRarity = 1,
			int[]? needleDamageBonusByLevel = null,
			int[]? needleDependencyBonusByLevel = null,
			int[]? rowCategoryIds = null,
			int[]? rowElementalBonusByLevel = null,
			int whitePaperMatchCount = 2,
			bool isNearLevelDamageCharm = false,
			bool isCompanionChaosCharm = false,
			bool isCompanionCharm = false,
			bool isPlanetModuleCharm = false,
			bool isPlanetCharm = false,
			bool isNearMagicBulletCharm = false,
			bool isWoodenBoxCharm = false,
			bool isFireIcePositionCharm = false
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
			PriorityTier = priorityTier;
			ConstraintType = constraintType;
			IsDisabledByWeaponOrDual = isDisabledByWeaponOrDual;
			IsMagicBook = isMagicBook;
			WantsRightSideMagicBook = wantsRightSideMagicBook;
			IsAttackableCharm = isAttackableCharm;
			HasDependencyTarget = hasDependencyTarget;
			DependencyOffsetX = dependencyOffsetX;
			DependencyOffsetY = dependencyOffsetY;
			HasDependencyCondition = hasDependencyCondition;
			DependencyMaxRarity = dependencyMaxRarity;
			NeedleDamageBonusByLevel = needleDamageBonusByLevel ?? new[] { 5, 10, 15, 20, 30 };
			NeedleDependencyBonusByLevel = needleDependencyBonusByLevel ?? new[] { 5, 10, 15, 20, 30 };
			IsElementalByRowCharm = isElementalByRowCharm;
			RowCategoryIds = rowCategoryIds ?? Array.Empty<int>();
			RowElementalBonusByLevel = rowElementalBonusByLevel ?? new[] { 2, 4, 6, 8 };
			IsWhitePaperCharm = isWhitePaperCharm;
			WhitePaperMatchCount = Math.Max(1, whitePaperMatchCount);
			IsNearLevelDamageCharm = isNearLevelDamageCharm;
			IsCompanionChaosCharm = isCompanionChaosCharm;
			IsCompanionCharm = isCompanionCharm;
			IsPlanetModuleCharm = isPlanetModuleCharm;
			IsPlanetCharm = isPlanetCharm;
			IsNearMagicBulletCharm = isNearMagicBulletCharm;
			IsWoodenBoxCharm = isWoodenBoxCharm;
			IsFireIcePositionCharm = isFireIcePositionCharm;
			StaticCategoryMask = staticCategoryMask;
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

		public int GetNeedleBonusPercent(int needleEffectiveLevel, int rootRarityTier) {
			int levelIdx = Math.Clamp(needleEffectiveLevel, 0, Math.Max(0, NeedleDamageBonusByLevel.Length - 1));
			int bonus = NeedleDamageBonusByLevel.Length > 0 ? NeedleDamageBonusByLevel[levelIdx] : 10;
			if (HasDependencyCondition && rootRarityTier <= DependencyMaxRarity) {
				int depIdx = Math.Clamp(needleEffectiveLevel, 0, Math.Max(0, NeedleDependencyBonusByLevel.Length - 1));
				bonus += NeedleDependencyBonusByLevel.Length > 0 ? NeedleDependencyBonusByLevel[depIdx] : 10;
			}
			return bonus;
		}

		public int GetRowElementalBonus(int effectiveLevel) {
			if (RowElementalBonusByLevel.Length == 0) {
				return 2;
			}
			int idx = Math.Clamp(effectiveLevel, 0, RowElementalBonusByLevel.Length - 1);
			return RowElementalBonusByLevel[idx];
		}
	}
}
