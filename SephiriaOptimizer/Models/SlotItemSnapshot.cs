using System;
using System.Runtime.CompilerServices;

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
		public CombatStatDelta[] StatDeltaByLevel { get; }
		public bool HasExplicitStatDeltas { get; }
		public byte CharmScalingElementKind { get; }
		public float[] CharmElementalPercentByLevel { get; }

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
			float[]? finalDamagePercentByLevel = null,
			CombatStatDelta[]? statDeltaByLevel = null,
			byte charmScalingElementKind = 0,
			float[]? charmElementalPercentByLevel = null
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
			HasExplicitStatDeltas = statDeltaByLevel != null;
			StatDeltaByLevel = statDeltaByLevel ?? BuildDefaultStatDeltaTable(StatBonusPercentByLevel);
			CharmScalingElementKind = charmScalingElementKind;
			CharmElementalPercentByLevel = charmElementalPercentByLevel ?? Array.Empty<float>();
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float GetAttackRawDpsAtLevel(int effectiveLevel) {
			return (float)GetAttackRawDpsAtLevelWithElement(effectiveLevel, 20.0);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public double GetAttackRawDpsAtLevelWithElement(int effectiveLevel, double resolvedElementStat) {
			int clampedLevel = Math.Clamp(effectiveLevel, 0, MaxLevel);
			double baseFlat = 0.0;
			if (AttackDamageByLevel.Length > 0) {
				int idx = Math.Clamp(clampedLevel, 0, AttackDamageByLevel.Length - 1);
				baseFlat = AttackDamageByLevel[idx];
			}
			if (CharmElementalPercentByLevel.Length > 0) {
				int pctIdx = Math.Clamp(clampedLevel, 0, CharmElementalPercentByLevel.Length - 1);
				double elemPart = resolvedElementStat * (CharmElementalPercentByLevel[pctIdx] * 0.01);
				return Math.Max(0.0, baseFlat + elemPart) * BaseProcsPerSecond;
			}
			return Math.Max(0.0, baseFlat) * BaseProcsPerSecond;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float GetStatBonusPercentAtLevel(int effectiveLevel) {
			if (StatBonusPercentByLevel.Length == 0) {
				return 0f;
			}
			int clampedLevel = Math.Clamp(effectiveLevel, 0, MaxLevel);
			int idx = Math.Clamp(clampedLevel, 0, StatBonusPercentByLevel.Length - 1);
			return StatBonusPercentByLevel[idx];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public CombatStatDelta GetStatDeltaAtLevel(int effectiveLevel) {
			if (StatDeltaByLevel.Length == 0) {
				return CombatStatDelta.Zero;
			}
			int clampedLevel = Math.Clamp(effectiveLevel, 0, MaxLevel);
			int idx = Math.Clamp(clampedLevel, 0, StatDeltaByLevel.Length - 1);
			return StatDeltaByLevel[idx];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float GetFinalDamagePercentAtLevel(int effectiveLevel) {
			if (FinalDamagePercentByLevel.Length == 0) {
				return 0f;
			}
			int clampedLevel = Math.Clamp(effectiveLevel, 0, MaxLevel);
			int idx = Math.Clamp(clampedLevel, 0, FinalDamagePercentByLevel.Length - 1);
			return FinalDamagePercentByLevel[idx];
		}

		private static CombatStatDelta[] BuildDefaultStatDeltaTable(float[] statBonusPercentByLevel) {
			if (statBonusPercentByLevel.Length == 0) {
				return Array.Empty<CombatStatDelta>();
			}
			CombatStatDelta[] table = new CombatStatDelta[statBonusPercentByLevel.Length];
			for (int i = 0; i < statBonusPercentByLevel.Length; i++) {
				table[i] = new CombatStatDelta(allDamageBonus: statBonusPercentByLevel[i]);
			}
			return table;
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
			float baseDmg = 30f + (Math.Clamp(rarityTier, 0, 5) * 12f) + (Math.Min(customWeight, 30) * 1.5f);
			float perLevelDmg = 15f + (Math.Clamp(rarityTier, 0, 5) * 5f) + (Math.Min(customWeight, 30) * 1.0f);
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
			float basePct = 3f + (Math.Clamp(rarityTier, 0, 5) * 1.5f) + (Math.Min(customWeight, 30) * 0.25f);
			float perLevelPct = 2.5f + (Math.Clamp(rarityTier, 0, 5) * 1.0f) + (Math.Min(customWeight, 30) * 0.2f);
			float[] table = new float[maxLevel + 1];
			for (int lv = 0; lv <= maxLevel; lv++) {
				table[lv] = basePct + (lv * perLevelPct);
			}
			return table;
		}
	}
}
