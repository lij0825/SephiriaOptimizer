using System;
using System.Collections.Generic;

namespace SephiriaOptimizer.Models {
	public readonly struct PlayerCombatBaseline {
		public static readonly PlayerCombatBaseline Default = new PlayerCombatBaseline(
			50f,
			2.0f,
			10f,
			1.5f,
			0f
		);

		public float BaseAttackDamage { get; }
		public float AttacksPerSecond { get; }
		public float CritChancePercent { get; }
		public float CritDamageMultiplier { get; }
		public float ElementalDamageBonusPercent { get; }
		public float BaseWeaponDps { get; }

		public PlayerCombatBaseline(
			float baseAttackDamage,
			float attacksPerSecond,
			float critChancePercent,
			float critDamageMultiplier,
			float elementalDamageBonusPercent
		) {
			BaseAttackDamage = baseAttackDamage > 1f ? baseAttackDamage : 50f;
			AttacksPerSecond = attacksPerSecond > 0.1f ? attacksPerSecond : 2.0f;
			CritChancePercent = Math.Clamp(critChancePercent, 0f, 100f);
			CritDamageMultiplier = Math.Max(1.0f, critDamageMultiplier);
			ElementalDamageBonusPercent = Math.Max(0f, elementalDamageBonusPercent);

			double critFactor = 1.0 + ((CritChancePercent * 0.01) * (CritDamageMultiplier - 1.0));
			double elemFactor = 1.0 + (ElementalDamageBonusPercent * 0.01);
			BaseWeaponDps = (float)(BaseAttackDamage * AttacksPerSecond * critFactor * elemFactor);
		}

		public double ComputeBaseWeaponRawDps() {
			return BaseWeaponDps > 1f ? BaseWeaponDps : 105.0;
		}
	}

	public sealed class ComboCategoryInfo {
		public int CategoryId { get; }
		public string CategoryKey { get; }
		public string DisplayName { get; }
		public int StaticCount { get; }
		public int[] Breakpoints { get; }
		public float[] BreakpointStatBonuses { get; }
		public int HighestBreakpoint { get; }
		public bool IsUserTarget { get; }

		public ComboCategoryInfo(
			int categoryId,
			string categoryKey,
			string displayName,
			int staticCount,
			int[] breakpoints,
			bool isUserTarget,
			float[]? breakpointStatBonuses = null
		) {
			CategoryId = categoryId;
			CategoryKey = categoryKey ?? string.Empty;
			DisplayName = string.IsNullOrEmpty(displayName) ? CategoryKey : displayName;
			StaticCount = Math.Max(0, staticCount);
			Breakpoints = breakpoints ?? Array.Empty<int>();
			BreakpointStatBonuses = breakpointStatBonuses ?? Array.Empty<float>();
			int maxBp = 0;
			for (int i = 0; i < Breakpoints.Length; i++) {
				if (Breakpoints[i] > maxBp) {
					maxBp = Breakpoints[i];
				}
			}
			HighestBreakpoint = maxBp;
			IsUserTarget = isUserTarget;
		}

		public double EvaluateStatBonusPercent(int totalCount, int unlimitedComboStat = 0) {
			if (totalCount <= 0) {
				return 0.0;
			}

			double totalBonus = 0.0;
			int bpLen = Breakpoints.Length;
			for (int b = 0; b < bpLen; b++) {
				if (totalCount >= Breakpoints[b]) {
					if (b < BreakpointStatBonuses.Length && BreakpointStatBonuses[b] > 0f) {
						totalBonus += BreakpointStatBonuses[b];
					} else {
						double tierBase = IsUserTarget ? 14.0 : 8.0;
						totalBonus += tierBase + (b * 4.0);
					}
				}
			}

			if (unlimitedComboStat > 0 && HighestBreakpoint > 0 && totalCount > HighestBreakpoint) {
				int surplus = totalCount - HighestBreakpoint;
				totalBonus += surplus * Math.Clamp(unlimitedComboStat, 1, 4) * 3.5;
			}

			return totalBonus;
		}
	}

	public readonly struct ArrangementOffsetEntry {
		public sbyte OffsetX { get; }
		public sbyte OffsetY { get; }
		public int RequiredEntityId { get; }

		public ArrangementOffsetEntry(sbyte offsetX, sbyte offsetY, int requiredEntityId) {
			OffsetX = offsetX;
			OffsetY = offsetY;
			RequiredEntityId = requiredEntityId;
		}
	}

	public sealed class ArrangementPattern {
		public ArrangementOffsetEntry[] Entries { get; }

		public ArrangementPattern(ArrangementOffsetEntry[] entries) {
			Entries = entries ?? Array.Empty<ArrangementOffsetEntry>();
		}
	}

	public sealed class TalentRuleSnapshot {
		public static readonly TalentRuleSnapshot Default = new TalentRuleSnapshot(
			0,
			0,
			0,
			0,
			0,
			false,
			false,
			0,
			0,
			0,
			0,
			Array.Empty<ArrangementPattern>()
		);

		public int TabletEngravingCount { get; }
		public int UniquePairCount { get; }
		public int UnlimitedComboStat { get; }
		public int ComboBonusDamageStat { get; }
		public int ArrangementBonusStat { get; }
		public bool DisableDualArtifact { get; }
		public bool PreventOverlapItemCombo { get; }
		public int FireDamageStat { get; }
		public int IceDamageStat { get; }
		public int LightningDamageStat { get; }
		public int PhysicalDamageStat { get; }
		public ArrangementPattern[] ArrangementPatterns { get; }

		public TalentRuleSnapshot(
			int tabletEngravingCount,
			int uniquePairCount,
			int unlimitedComboStat,
			int comboBonusDamageStat,
			int arrangementBonusStat,
			bool disableDualArtifact,
			bool preventOverlapItemCombo,
			int fireDamageStat,
			int iceDamageStat,
			int lightningDamageStat,
			int physicalDamageStat,
			ArrangementPattern[] arrangementPatterns
		) {
			TabletEngravingCount = Math.Max(0, tabletEngravingCount);
			UniquePairCount = Math.Max(0, uniquePairCount);
			UnlimitedComboStat = Math.Max(0, unlimitedComboStat);
			ComboBonusDamageStat = Math.Max(0, comboBonusDamageStat);
			ArrangementBonusStat = Math.Max(0, arrangementBonusStat);
			DisableDualArtifact = disableDualArtifact;
			PreventOverlapItemCombo = preventOverlapItemCombo;
			FireDamageStat = fireDamageStat;
			IceDamageStat = iceDamageStat;
			LightningDamageStat = lightningDamageStat;
			PhysicalDamageStat = physicalDamageStat;
			ArrangementPatterns = arrangementPatterns ?? Array.Empty<ArrangementPattern>();
		}
	}

	public sealed class OptimizerUserSettings {
		public Dictionary<int, ArtifactPriorityTier> InstancePriorities { get; } =
			new Dictionary<int, ArtifactPriorityTier>();

		public string PreferredChalcedonyCategory { get; set; } = string.Empty;

		public HashSet<string> TargetComboCategories { get; } =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public int PreferredNeedleTargetInstanceId { get; set; } = -1;

		public ArtifactPriorityTier GetPriority(
			int instanceId,
			ArtifactPriorityTier defaultTier = ArtifactPriorityTier.Normal
		) {
			if (InstancePriorities.TryGetValue(instanceId, out ArtifactPriorityTier tier)) {
				return tier;
			}
			return defaultTier;
		}

		public void CyclePriority(int instanceId) {
			ArtifactPriorityTier current = GetPriority(instanceId);
			ArtifactPriorityTier next = current switch {
				ArtifactPriorityTier.Normal => ArtifactPriorityTier.Core,
				ArtifactPriorityTier.Core => ArtifactPriorityTier.High,
				ArtifactPriorityTier.High => ArtifactPriorityTier.Low,
				ArtifactPriorityTier.Low => ArtifactPriorityTier.Sacrifice,
				_ => ArtifactPriorityTier.Normal
			};
			if (next == ArtifactPriorityTier.Normal) {
				InstancePriorities.Remove(instanceId);
			} else {
				InstancePriorities[instanceId] = next;
			}
		}

		public void ToggleChalcedonyCategory(string categoryKey) {
			if (string.IsNullOrEmpty(categoryKey)
				|| string.Equals(PreferredChalcedonyCategory, categoryKey, StringComparison.OrdinalIgnoreCase)) {
				PreferredChalcedonyCategory = string.Empty;
			} else {
				PreferredChalcedonyCategory = categoryKey;
			}
		}

		public bool ToggleTargetCombo(string categoryKey) {
			if (string.IsNullOrEmpty(categoryKey)) {
				return false;
			}
			if (!TargetComboCategories.Add(categoryKey)) {
				TargetComboCategories.Remove(categoryKey);
				return false;
			}
			return true;
		}

		public int ComputeSettingsHash() {
			int hash = 23;
			hash = (hash * 31) + PreferredNeedleTargetInstanceId;
			if (!string.IsNullOrEmpty(PreferredChalcedonyCategory)) {
				hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(PreferredChalcedonyCategory);
			}
			foreach (KeyValuePair<int, ArtifactPriorityTier> kvp in InstancePriorities) {
				hash = (hash * 31) + kvp.Key;
				hash = (hash * 31) + (int)kvp.Value;
			}
			foreach (string cat in TargetComboCategories) {
				hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(cat);
			}
			return hash;
		}
	}
}
