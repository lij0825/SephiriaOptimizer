using System;
using System.Collections.Generic;

namespace SephiriaOptimizer.Models {
	public sealed class ComboCategoryInfo {
		public int CategoryId { get; }
		public string CategoryKey { get; }
		public string DisplayName { get; }
		public int StaticCount { get; }
		public int[] Breakpoints { get; }
		public int HighestBreakpoint { get; }
		public bool IsUserTarget { get; }

		public ComboCategoryInfo(
			int categoryId,
			string categoryKey,
			string displayName,
			int staticCount,
			int[] breakpoints,
			bool isUserTarget
		) {
			CategoryId = categoryId;
			CategoryKey = categoryKey ?? string.Empty;
			DisplayName = string.IsNullOrEmpty(displayName) ? CategoryKey : displayName;
			StaticCount = Math.Max(0, staticCount);
			Breakpoints = breakpoints ?? Array.Empty<int>();
			int maxBp = 0;
			for (int i = 0; i < Breakpoints.Length; i++) {
				if (Breakpoints[i] > maxBp) {
					maxBp = Breakpoints[i];
				}
			}
			HighestBreakpoint = maxBp;
			IsUserTarget = isUserTarget;
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
