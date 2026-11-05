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
		public bool IsCrystalOfHarmony { get; }
		public float[] CrystalDamageBonusByLevel { get; }
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
		public float[] AttackDamageByLevel { get; }
		public float BaseProcsPerSecond { get; }
		public float[] StatBonusPercentByLevel { get; }
		public float[] FinalDamagePercentByLevel { get; }

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
			bool isFireIcePositionCharm = false,
			bool isCrystalOfHarmony = false,
			float[]? crystalDamageBonusByLevel = null,
			float[]? attackDamageByLevel = null,
			float baseProcsPerSecond = 1f,
			float[]? statBonusPercentByLevel = null,
			float[]? finalDamagePercentByLevel = null
		) {
			bool crystalFlag = isCrystalOfHarmony || isNearLevelDamageCharm;
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
			IsNearLevelDamageCharm = crystalFlag;
			IsCrystalOfHarmony = crystalFlag;
			CrystalDamageBonusByLevel = crystalDamageBonusByLevel ?? new[] { 1f, 2f };
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
			BaseProcsPerSecond = baseProcsPerSecond > 0.05f ? baseProcsPerSecond : 1f;
			AttackDamageByLevel = attackDamageByLevel ?? BuildDefaultAttackDamageTable(
				kind,
				isAttackableCharm,
				MaxLevel,
				rarityTier,
				CustomWeight,
				priorityTier
			);
			StatBonusPercentByLevel = statBonusPercentByLevel ?? BuildDefaultStatBonusTable(
				kind,
				isAttackableCharm,
				hasDependencyTarget,
				crystalFlag,
				isElementalByRowCharm,
				MaxLevel,
				rarityTier,
				CustomWeight,
				priorityTier
			);
			FinalDamagePercentByLevel = finalDamagePercentByLevel ?? Array.Empty<float>();
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

		public float GetCrystalRatePerLevel(int effectiveLevel) {
			if (CrystalDamageBonusByLevel.Length == 0) {
				return effectiveLevel > 0 ? 2f : 1f;
			}
			int clampedLevel = Math.Clamp(effectiveLevel, 0, MaxLevel);
			int idx = Math.Clamp(clampedLevel, 0, CrystalDamageBonusByLevel.Length - 1);
			float rate = CrystalDamageBonusByLevel[idx];
			return rate > 0f ? rate : (effectiveLevel > 0 ? 2f : 1f);
		}

		public float GetAttackRawDpsAtLevel(int effectiveLevel) {
			if (AttackDamageByLevel.Length == 0) {
				return 0f;
			}
			int clampedLevel = Math.Clamp(effectiveLevel, 0, MaxLevel);
			int idx = Math.Clamp(clampedLevel, 0, AttackDamageByLevel.Length - 1);
			return AttackDamageByLevel[idx] * BaseProcsPerSecond;
		}

		public float GetStatBonusPercentAtLevel(int effectiveLevel) {
			if (StatBonusPercentByLevel.Length == 0) {
				return 0f;
			}
			int clampedLevel = Math.Clamp(effectiveLevel, 0, MaxLevel);
			int idx = Math.Clamp(clampedLevel, 0, StatBonusPercentByLevel.Length - 1);
			return StatBonusPercentByLevel[idx];
		}

		public float GetFinalDamagePercentAtLevel(int effectiveLevel) {
			if (FinalDamagePercentByLevel.Length == 0) {
				return 0f;
			}
			int clampedLevel = Math.Clamp(effectiveLevel, 0, MaxLevel);
			int idx = Math.Clamp(clampedLevel, 0, FinalDamagePercentByLevel.Length - 1);
			return FinalDamagePercentByLevel[idx];
		}

		private static float[] BuildDefaultAttackDamageTable(
			SlotItemKind kind,
			bool isAttackableCharm,
			int maxLevel,
			int rarityTier,
			int customWeight,
			ArtifactPriorityTier priorityTier
		) {
			if (kind != SlotItemKind.Artifact || !isAttackableCharm || priorityTier == ArtifactPriorityTier.Sacrifice) {
				return Array.Empty<float>();
			}
			float tierScale = priorityTier switch {
				ArtifactPriorityTier.Core => 1.6f,
				ArtifactPriorityTier.High => 1.25f,
				ArtifactPriorityTier.Normal => 1.0f,
				ArtifactPriorityTier.Low => 0.65f,
				_ => 0.25f
			};
			float baseDmg = (35f + (Math.Clamp(rarityTier, 0, 5) * 14f) + (Math.Min(customWeight, 30) * 2.5f)) * tierScale;
			float perLevelDmg = (18f + (Math.Clamp(rarityTier, 0, 5) * 6f) + (Math.Min(customWeight, 30) * 1.5f)) * tierScale;
			float[] table = new float[maxLevel + 1];
			for (int lv = 0; lv <= maxLevel; lv++) {
				table[lv] = baseDmg + (lv * perLevelDmg);
			}
			return table;
		}

		private static float[] BuildDefaultStatBonusTable(
			SlotItemKind kind,
			bool isAttackableCharm,
			bool hasDependencyTarget,
			bool isCrystalOfHarmony,
			bool isElementalByRowCharm,
			int maxLevel,
			int rarityTier,
			int customWeight,
			ArtifactPriorityTier priorityTier
		) {
			if (kind != SlotItemKind.Artifact
				|| isAttackableCharm
				|| hasDependencyTarget
				|| isCrystalOfHarmony
				|| isElementalByRowCharm
				|| priorityTier == ArtifactPriorityTier.Sacrifice) {
				return Array.Empty<float>();
			}
			float tierScale = priorityTier switch {
				ArtifactPriorityTier.Core => 1.5f,
				ArtifactPriorityTier.High => 1.2f,
				ArtifactPriorityTier.Normal => 1.0f,
				ArtifactPriorityTier.Low => 0.6f,
				_ => 0.2f
			};
			float basePct = (4f + (Math.Clamp(rarityTier, 0, 5) * 2f) + (Math.Min(customWeight, 30) * 0.5f)) * tierScale;
			float perLevelPct = (3.5f + (Math.Clamp(rarityTier, 0, 5) * 1.2f) + (Math.Min(customWeight, 30) * 0.4f)) * tierScale;
			float[] table = new float[maxLevel + 1];
			for (int lv = 0; lv <= maxLevel; lv++) {
				table[lv] = basePct + (lv * perLevelPct);
			}
			return table;
		}
	}
}
