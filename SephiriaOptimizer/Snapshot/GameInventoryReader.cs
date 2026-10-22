using System;
using System.Collections.Generic;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Snapshot {
	public static class GameInventoryReader {
		public static InventorySnapshot CaptureSnapshot(
			PlayerAvatar avatar,
			GridInventory inventory,
			HashSet<int> pinnedSlotIndices,
			Dictionary<int, int> customEntityWeights
		) {
			int width = Math.Max(1, (int)inventory.Width);
			int unlockedSlots = Math.Clamp(
				(int)inventory.CurrentInventoryStorage,
				0,
				InventorySnapshot.MaxSupportedSlots
			);
			int height = Math.Max(1, (int)inventory.Height);

			int[] baseSlotLevel = new int[unlockedSlots];
			int[] baseSlotDisable = new int[unlockedSlots];
			int[] baseSlotIgnoreCriteria = new int[unlockedSlots];
			int[] baseSlotMultiplyLevel = new int[unlockedSlots];
			bool[] pinnedSlots = new bool[unlockedSlots];

			if (pinnedSlotIndices != null) {
				foreach (int pinnedIdx in pinnedSlotIndices) {
					if (pinnedIdx >= 0 && pinnedIdx < unlockedSlots) {
						pinnedSlots[pinnedIdx] = true;
					}
				}
			}

			foreach (KeyValuePair<ItemPosition, int> entry in inventory.dungeonTempLevels) {
				int slotIdx = CoordToUnlockedSlot(entry.Key.x, entry.Key.y, width, height, unlockedSlots);
				if (slotIdx >= 0) {
					baseSlotLevel[slotIdx] += entry.Value;
				}
			}

			if (inventory.fixedEngravingsOnServer != null) {
				for (int i = 0; i < inventory.fixedEngravingsOnServer.Count; i++) {
					FixedEngraving engraving = inventory.fixedEngravingsOnServer[i];
					if (engraving == null) {
						continue;
					}
					AccumulateFixedDictionary(engraving.fixedLevel, baseSlotLevel, width, height, unlockedSlots);
					AccumulateFixedDictionary(engraving.fixedDisable, baseSlotDisable, width, height, unlockedSlots);
					AccumulateFixedDictionary(
						engraving.fixedIgnoreCriteria,
						baseSlotIgnoreCriteria,
						width,
						height,
						unlockedSlots
					);
					AccumulateFixedDictionary(
						engraving.fixedMultiplyLevel,
						baseSlotMultiplyLevel,
						width,
						height,
						unlockedSlots
					);
				}
			}

			for (int i = 0; i < inventory.engravings.Count; i++) {
				StoneTablet engravedTablet = inventory.engravings[i];
				if (engravedTablet == null || !engravedTablet.IsApplied) {
					continue;
				}
				for (int e = 0; e < engravedTablet.EffectRange.Count; e++) {
					StoneTablet.AdditionEffectData eff = engravedTablet.EffectRange[e];
					int slotIdx = CoordToUnlockedSlot(
						eff.position.x,
						eff.position.y,
						width,
						height,
						unlockedSlots
					);
					if (slotIdx < 0) {
						continue;
					}
					switch (eff.effectType) {
						case StoneTablet.EffectType.IncreaseConstLevel: {
							baseSlotLevel[slotIdx] += eff.levelParam;
							break;
						}
						case StoneTablet.EffectType.Disable: {
							baseSlotDisable[slotIdx]++;
							break;
						}
						case StoneTablet.EffectType.IgnoreCriteria: {
							baseSlotIgnoreCriteria[slotIdx]++;
							break;
						}
						case StoneTablet.EffectType.MultiplyConstLevel: {
							baseSlotMultiplyLevel[slotIdx] += eff.levelParam;
							break;
						}
					}
				}
			}

			List<SlotItemSnapshot> itemSnapshots = new List<SlotItemSnapshot>(unlockedSlots);
			Dictionary<string, int> symmetryGroupMap = new Dictionary<string, int>(StringComparer.Ordinal);
			int nextSymmetryGroupId = 1;

			for (int slotIdx = 0; slotIdx < unlockedSlots; slotIdx++) {
				sbyte xCoord = (sbyte)(slotIdx % width);
				sbyte yCoord = (sbyte)(slotIdx / width);
				NewItemOwnInstance itemInstance = inventory.FindItem(xCoord, yCoord);
				if (itemInstance == null || itemInstance.Entity == null) {
					continue;
				}

				int itemIndex = itemSnapshots.Count;
				ItemEntity entity = itemInstance.Entity;
				string displayName = entity.Name ?? $"Item_{entity.id}";
				int rarityTier = (int)entity.rarity;

				if (entity.type == EItemType.Charm) {
					Charm_Basic charm = itemInstance.Charm;
					int baseLevel = ReadEnchantLevel(itemInstance.InstanceID);
					int maxLevel = charm != null ? charm.maxLevel : 0;
					ArtifactConstraintType constraintType = MapConstraintType(
						charm != null ? charm.criteria : null
					);

					bool dualDisabled = entity.IsDisabledFor(avatar);
					bool weaponMismatch = charm != null
						&& charm.isWeaponRelatedCharm
						&& (charm.WeaponController == null
							|| charm.WeaponController.currentWeapon == null
							|| charm.WeaponController.currentWeapon.weaponType != charm.relatedWeapon);
					bool isDisabledByWeaponOrDual = dualDisabled || weaponMismatch;

					bool isMagicBook = charm is Charm_Magic;
					bool wantsRightMagicBook = charm is Charm_AutoMagic
						|| charm is Charm_ReduceMPCost
						|| charm is Charm_RightSpellCooldownHelper;
					bool isAttackable = charm is IAttackableCharm attackable && attackable.IsAttackableCharm();
					bool hasDependency = charm is Charm_UpCharmDamage;
					sbyte depX = charm is Charm_UpCharmDamage upX ? upX.xOffset : (sbyte)0;
					sbyte depY = charm is Charm_UpCharmDamage upY ? upY.yOffset : (sbyte)0;
					bool isElementalByRow = charm is Charm_3Elemental_ByRow;
					bool isWhitePaper = charm is Charm_WhitePaper;
					string[] categories = entity.categories != null
						? entity.categories.ToArray()
						: Array.Empty<string>();

					int weight = ComputeArtifactWeight(entity, charm, customEntityWeights);
					string symmetryKey = $"C:{entity.id}:{baseLevel}:{maxLevel}:{(int)constraintType}";
					int symmetryGroupId = GetOrAssignSymmetryGroup(
						symmetryGroupMap,
						symmetryKey,
						ref nextSymmetryGroupId
					);

					itemSnapshots.Add(new SlotItemSnapshot(
						itemIndex,
						itemInstance.InstanceID,
						entity.id,
						displayName,
						SlotItemKind.Artifact,
						slotIdx,
						0,
						false,
						baseLevel,
						maxLevel,
						rarityTier,
						weight,
						constraintType,
						isDisabledByWeaponOrDual,
						isMagicBook,
						wantsRightMagicBook,
						isAttackable,
						hasDependency,
						depX,
						depY,
						isElementalByRow,
						isWhitePaper,
						categories,
						Array.Empty<TabletPlacementRule>(),
						symmetryGroupId
					));
				} else if (entity.type == EItemType.StoneTablet) {
					StoneTablet tablet = itemInstance.StoneTablet;
					int initialRotation = tablet != null ? (tablet.rotation & 3) : 0;
					bool isRotatable = tablet != null
						&& DungeonManager.IsTabletRotatable(itemInstance.InstanceID, tablet.isRotatable);
					string conditionQuery = tablet != null
						? (tablet.GetConditionQuery(itemInstance.InstanceID) ?? string.Empty)
						: string.Empty;
					string effectQuery = tablet != null
						? (tablet.GetQuery(itemInstance.InstanceID) ?? string.Empty)
						: string.Empty;

					TabletPlacementRule[] placementRules = BuildTabletPlacementRules(
						conditionQuery,
						effectQuery,
						width,
						height,
						unlockedSlots
					);

					string symmetryKey = isRotatable
						? $"T:{entity.id}:R1:{conditionQuery}:{effectQuery}"
						: $"T:{entity.id}:R0:{initialRotation}:{conditionQuery}:{effectQuery}";
					int symmetryGroupId = GetOrAssignSymmetryGroup(
						symmetryGroupMap,
						symmetryKey,
						ref nextSymmetryGroupId
					);

					itemSnapshots.Add(new SlotItemSnapshot(
						itemIndex,
						itemInstance.InstanceID,
						entity.id,
						displayName,
						SlotItemKind.StoneTablet,
						slotIdx,
						initialRotation,
						isRotatable,
						0,
						0,
						rarityTier,
						1,
						ArtifactConstraintType.None,
						false,
						false,
						false,
						false,
						false,
						0,
						0,
						false,
						false,
						Array.Empty<string>(),
						placementRules,
						symmetryGroupId
					));
				} else {
					string symmetryKey = $"O:{entity.id}";
					int symmetryGroupId = GetOrAssignSymmetryGroup(
						symmetryGroupMap,
						symmetryKey,
						ref nextSymmetryGroupId
					);
					itemSnapshots.Add(new SlotItemSnapshot(
						itemIndex,
						itemInstance.InstanceID,
						entity.id,
						displayName,
						SlotItemKind.OtherItem,
						slotIdx,
						0,
						false,
						0,
						0,
						rarityTier,
						1,
						ArtifactConstraintType.None,
						false,
						false,
						false,
						false,
						false,
						0,
						0,
						false,
						false,
						Array.Empty<string>(),
						Array.Empty<TabletPlacementRule>(),
						symmetryGroupId
					));
				}
			}

			bool isFullHp = avatar != null && Math.Abs(avatar.hp - avatar.MaxHp) < 0.01f;

			return new InventorySnapshot(
				width,
				height,
				unlockedSlots,
				baseSlotLevel,
				baseSlotDisable,
				baseSlotIgnoreCriteria,
				baseSlotMultiplyLevel,
				pinnedSlots,
				isFullHp,
				inventory.globalActiveValue,
				itemSnapshots.ToArray()
			);
		}

		public static TabletPlacementRule[] BuildTabletPlacementRules(
			string conditionQuery,
			string effectQuery,
			int width,
			int height,
			int unlockedSlots
		) {
			TabletPlacementRule[] rules = new TabletPlacementRule[unlockedSlots * 4];
			for (int slotIdx = 0; slotIdx < unlockedSlots; slotIdx++) {
				ItemPosition originPos = new ItemPosition(
					(sbyte)(slotIdx % width),
					(sbyte)(slotIdx / width)
				);
				for (int rot = 0; rot < 4; rot++) {
					List<StoneTablet.AdditionMetadata> rawConditions = string.IsNullOrWhiteSpace(conditionQuery)
						? new List<StoneTablet.AdditionMetadata>(0)
						: StoneTablet.ParseQuery(
							conditionQuery,
							width,
							height,
							unlockedSlots,
							originPos,
							rot,
							out _
						);

					PrecomputedCondition[] conditions = new PrecomputedCondition[rawConditions.Count];
					for (int c = 0; c < rawConditions.Count; c++) {
						StoneTablet.AdditionCriteriaData parsedCond =
							new StoneTablet.AdditionCriteriaData(rawConditions[c]);
						int targetSlot = CoordToUnlockedSlot(
							parsedCond.position.x,
							parsedCond.position.y,
							width,
							height,
							unlockedSlots
						);
						conditions[c] = new PrecomputedCondition(
							parsedCond.position.x,
							parsedCond.position.y,
							targetSlot,
							MapConditionType(parsedCond.effectType)
						);
					}

					List<StoneTablet.AdditionMetadata> rawEffects = string.IsNullOrWhiteSpace(effectQuery)
						? new List<StoneTablet.AdditionMetadata>(0)
						: StoneTablet.ParseQuery(
							effectQuery,
							width,
							height,
							unlockedSlots,
							originPos,
							rot,
							out _
						);

					PrecomputedEffect[] effects = new PrecomputedEffect[rawEffects.Count];
					for (int e = 0; e < rawEffects.Count; e++) {
						StoneTablet.AdditionEffectData parsedEff =
							new StoneTablet.AdditionEffectData(rawEffects[e]);
						int targetSlot = CoordToUnlockedSlot(
							parsedEff.position.x,
							parsedEff.position.y,
							width,
							height,
							unlockedSlots
						);
						effects[e] = new PrecomputedEffect(
							parsedEff.position.x,
							parsedEff.position.y,
							targetSlot,
							MapEffectType(parsedEff.effectType),
							parsedEff.levelParam
						);
					}

					rules[(slotIdx * 4) + rot] = new TabletPlacementRule(conditions, effects);
				}
			}
			return rules;
		}

		private static int ReadEnchantLevel(int instanceId) {
			if (DungeonManager.Instance == null) {
				return 0;
			}
			string rawEnchant = DungeonManager.Instance.GetGlobalItemStatValue(instanceId, "Enchant");
			if (!string.IsNullOrEmpty(rawEnchant) && int.TryParse(rawEnchant, out int parsed)) {
				return parsed;
			}
			return 0;
		}

		private static int ComputeArtifactWeight(
			ItemEntity entity,
			Charm_Basic? charm,
			Dictionary<int, int>? customEntityWeights
		) {
			if (customEntityWeights != null
				&& customEntityWeights.TryGetValue(entity.id, out int customWeight)
				&& customWeight > 0) {
				return customWeight;
			}
			int rarityWeight = entity.rarity switch {
				EItemRarity.Common => 2,
				EItemRarity.Uncommon => 3,
				EItemRarity.Rare => 5,
				EItemRarity.Legend => 8,
				EItemRarity.Eternal => 12,
				_ => 2
			};
			if (charm != null && charm.isUniqueEffect) {
				rarityWeight += 2;
			}
			return rarityWeight;
		}

		private static ArtifactConstraintType MapConstraintType(CharmActivateCriteria? criteria) {
			if (criteria == null) {
				return ArtifactConstraintType.None;
			}
			if (criteria is CharmActivateCriteria_TopInInventory) {
				return ArtifactConstraintType.TopInInventory;
			}
			if (criteria is CharmActivateCriteria_BottomInInventory) {
				return ArtifactConstraintType.BottomInInventory;
			}
			if (criteria is CharmActivateCriteria_SideEnd) {
				return ArtifactConstraintType.SideEnd;
			}
			if (criteria is CharmActivateCriteria_Outlined) {
				return ArtifactConstraintType.Outlined;
			}
			if (criteria is CharmActivateCriteria_Inside) {
				return ArtifactConstraintType.Inside;
			}
			if (criteria is CharmActivateCriteria_BothSideCharm) {
				return ArtifactConstraintType.BothSideCharm;
			}
			if (criteria is CharmActivateCriteria_BothSidesAreEmpty) {
				return ArtifactConstraintType.BothSidesAreEmpty;
			}
			if (criteria is CharmActivateCriteria_Near8MagicBook) {
				return ArtifactConstraintType.Near8MagicBook;
			}
			if (criteria is CharmActivateCriteria_NeighborsAreFull) {
				return ArtifactConstraintType.NeighborsAreFull;
			}
			if (criteria is CharmActivateCriteria_FullHP) {
				return ArtifactConstraintType.FullHp;
			}
			return ArtifactConstraintType.None;
		}

		private static TabletConditionType MapConditionType(StoneTablet.CriteriaType criteriaType) {
			return criteriaType switch {
				StoneTablet.CriteriaType.AnyItem => TabletConditionType.AnyItem,
				StoneTablet.CriteriaType.OnlyCharm => TabletConditionType.OnlyCharm,
				StoneTablet.CriteriaType.Placed => TabletConditionType.Placed,
				_ => TabletConditionType.None
			};
		}

		private static TabletEffectType MapEffectType(StoneTablet.EffectType effectType) {
			return effectType switch {
				StoneTablet.EffectType.IncreaseConstLevel => TabletEffectType.IncreaseConstLevel,
				StoneTablet.EffectType.Disable => TabletEffectType.Disable,
				StoneTablet.EffectType.IgnoreCriteria => TabletEffectType.IgnoreCriteria,
				StoneTablet.EffectType.MultiplyConstLevel => TabletEffectType.MultiplyConstLevel,
				_ => TabletEffectType.None
			};
		}

		private static int CoordToUnlockedSlot(
			int xCoord,
			int yCoord,
			int width,
			int height,
			int unlockedSlots
		) {
			if (xCoord < 0 || xCoord >= width || yCoord < 0 || yCoord >= height) {
				return -1;
			}
			int linearIndex = (yCoord * width) + xCoord;
			if (linearIndex < 0 || linearIndex >= unlockedSlots) {
				return -1;
			}
			return linearIndex;
		}

		private static void AccumulateFixedDictionary(
			Dictionary<ItemPosition, int> source,
			int[] destination,
			int width,
			int height,
			int unlockedSlots
		) {
			if (source == null) {
				return;
			}
			foreach (KeyValuePair<ItemPosition, int> pair in source) {
				int slotIdx = CoordToUnlockedSlot(pair.Key.x, pair.Key.y, width, height, unlockedSlots);
				if (slotIdx >= 0) {
					destination[slotIdx] += pair.Value;
				}
			}
		}

		private static int GetOrAssignSymmetryGroup(
			Dictionary<string, int> symmetryGroupMap,
			string key,
			ref int nextGroupId
		) {
			if (symmetryGroupMap.TryGetValue(key, out int existingId)) {
				return existingId;
			}
			int assigned = nextGroupId++;
			symmetryGroupMap[key] = assigned;
			return assigned;
		}
	}
}
