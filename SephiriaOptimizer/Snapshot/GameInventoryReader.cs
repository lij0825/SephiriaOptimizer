using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Snapshot {
	public static class GameInventoryReader {
		private static readonly string[] DefaultElementalCategories = new[] {
			"EMBER",
			"GLACIER",
			"MAGITECH",
			"STURDY",
			"MYSTIC"
		};

		private static readonly FieldInfo? WeaponAddonIsEnabledField = typeof(WeaponAddon).GetField(
			"isEnabled",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		private static readonly FieldInfo? BurnWeaponAddedField = typeof(Charm_BurnWeaponDamage).GetField(
			"added",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		public static InventorySnapshot CaptureSnapshot(
			PlayerAvatar avatar,
			GridInventory inventory,
			HashSet<int> pinnedSlotIndices,
			Dictionary<int, int> customEntityWeights,
			OptimizerUserSettings? userSettings = null
		) {
			OptimizerUserSettings settings = userSettings ?? new OptimizerUserSettings();
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

			PopulateBaseSlotModifiers(
				inventory,
				width,
				height,
				unlockedSlots,
				baseSlotLevel,
				baseSlotDisable,
				baseSlotIgnoreCriteria,
				baseSlotMultiplyLevel
			);

			TalentRuleSnapshot talentRules = CaptureTalentRules(avatar, inventory);
			PlayerCombatBaseline rawCombatBaseline = ExtractCombatBaseline(avatar, talentRules);

			Dictionary<string, int> categoryKeyToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			List<string> orderedCategoryKeys = new List<string>(InventorySnapshot.MaxSupportedCategories);
			for (int i = 0; i < DefaultElementalCategories.Length; i++) {
				RegisterCategoryKey(DefaultElementalCategories[i], categoryKeyToId, orderedCategoryKeys);
			}
			if (!string.IsNullOrEmpty(settings.PreferredChalcedonyCategory)) {
				RegisterCategoryKey(settings.PreferredChalcedonyCategory, categoryKeyToId, orderedCategoryKeys);
			}
			foreach (string targetKey in settings.TargetComboCategories) {
				RegisterCategoryKey(targetKey, categoryKeyToId, orderedCategoryKeys);
			}
			if (inventory.bonusComboCount != null) {
				foreach (KeyValuePair<string, int> bonusEntry in inventory.bonusComboCount) {
					if (bonusEntry.Value > 0) {
						RegisterCategoryKey(bonusEntry.Key, categoryKeyToId, orderedCategoryKeys);
					}
				}
			}

			int currentTabletCount = 0;
			for (int slotIdx = 0; slotIdx < unlockedSlots; slotIdx++) {
				sbyte xCoord = (sbyte)(slotIdx % width);
				sbyte yCoord = (sbyte)(slotIdx / width);
				NewItemOwnInstance itemInstance = inventory.FindItem(xCoord, yCoord);
				if (itemInstance == null || itemInstance.Entity == null) {
					continue;
				}
				if (itemInstance.Entity.type == EItemType.StoneTablet) {
					currentTabletCount++;
				}
				if (itemInstance.Entity.categories != null) {
					for (int c = 0; c < itemInstance.Entity.categories.Count; c++) {
						RegisterCategoryKey(
							itemInstance.Entity.categories[c],
							categoryKeyToId,
							orderedCategoryKeys
						);
					}
				}
				Charm_Basic? preScanCharm = ResolveSlotCharm(
					inventory,
					itemInstance,
					itemInstance.Entity,
					xCoord,
					yCoord
				);
				if (preScanCharm is Charm_3Elemental_ByRow elemCharm && elemCharm.lineCategory != null) {
					for (int c = 0; c < elemCharm.lineCategory.Length; c++) {
						RegisterCategoryKey(
							elemCharm.lineCategory[c],
							categoryKeyToId,
							orderedCategoryKeys
						);
					}
				}
			}

			int[] staticCategoryCounts = new int[orderedCategoryKeys.Count];
			HashSet<int> seenStaticEntityIds = new HashSet<int>();

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
					Charm_Basic? charm = ResolveSlotCharm(inventory, itemInstance, entity, xCoord, yCoord);
					int baseLevel = ReadEnchantLevel(itemInstance.InstanceID);
					int maxLevel = charm != null ? charm.maxLevel : 0;
					ArtifactConstraintType constraintType = MapConstraintType(
						charm != null ? charm.criteria : null
					);

					bool dualDisabled = avatar != null && entity.IsDisabledFor(avatar);
					WeaponControllerSimple? weaponCtrl = charm != null ? charm.WeaponController : null;
					if (weaponCtrl == null && avatar != null) {
						weaponCtrl = avatar.GetComponent<WeaponControllerSimple>();
					}
					bool weaponMismatch = charm != null
						&& charm.isWeaponRelatedCharm
						&& (weaponCtrl == null
							|| weaponCtrl.currentWeapon == null
							|| weaponCtrl.currentWeapon.weaponType != charm.relatedWeapon);
					bool isDisabledByWeaponOrDual = dualDisabled || weaponMismatch;

					bool isMagicBook = charm is Charm_Magic;
					bool wantsRightMagicBook = charm is Charm_AutoMagic
						|| charm is Charm_ReduceMPCost
						|| charm is Charm_RightSpellCooldownHelper;
					bool isAttackable = charm is IAttackableCharm attackable && attackable.IsAttackableCharm();

					bool hasDependency = charm is Charm_UpCharmDamage;
					sbyte depX = charm is Charm_UpCharmDamage upX ? upX.xOffset : (sbyte)0;
					sbyte depY = charm is Charm_UpCharmDamage upY ? upY.yOffset : (sbyte)0;
					bool hasDepCond = charm is Charm_UpCharmDamage upCond && upCond.hasDependencyCondition;
					int depMaxRarity = charm is Charm_UpCharmDamage upRar ? (int)upRar.maxRarity : 1;
					int[]? needleDmg = charm is Charm_UpCharmDamage upDmg && upDmg.damageBonusByLevel != null
						? (int[])upDmg.damageBonusByLevel.Clone()
						: null;
					int[]? needleDepDmg = charm is Charm_UpCharmDamage upDep
						&& upDep.dependencyDamageBonusByLevel != null
						? (int[])upDep.dependencyDamageBonusByLevel.Clone()
						: null;

					bool isElementalByRow = charm is Charm_3Elemental_ByRow;
					int[]? rowCategoryIds = null;
					int[]? rowElementalBonus = null;
					if (charm is Charm_3Elemental_ByRow rowCharm) {
						if (rowCharm.lineCategory != null && rowCharm.lineCategory.Length > 0) {
							rowCategoryIds = new int[rowCharm.lineCategory.Length];
							for (int r = 0; r < rowCharm.lineCategory.Length; r++) {
								rowCategoryIds[r] = categoryKeyToId.TryGetValue(rowCharm.lineCategory[r], out int cid)
									? cid
									: -1;
							}
						}
						if (rowCharm.addElementalStatByLevel != null) {
							rowElementalBonus = (int[])rowCharm.addElementalStatByLevel.Clone();
						}
					}

					bool isWhitePaper = charm is Charm_WhitePaper;
					int whitePaperMatch = charm is Charm_WhitePaper wpCharm ? Math.Max(1, wpCharm.match) : 2;

					bool isNearLevelDamage = charm is Charm_NearLevelDamage;
					float[]? crystalRates = charm is Charm_NearLevelDamage nearDmg
						&& nearDmg.allDamageBonusByLevel != null
						&& nearDmg.allDamageBonusByLevel.Length > 0
						? (float[])nearDmg.allDamageBonusByLevel.Clone()
						: null;
					bool isCompanionChaos = charm is Charm_CompanionChaos;
					bool isCompanion = charm is ICompanionCharm;
					bool isPlanetModule = charm is Charm_PlanetModule;
					bool isPlanet = charm is Charm_SummonGreenBat
						|| (entity.categories != null && entity.categories.Contains("PLANET"));
					bool isNearMagicBullet = charm is Charm_NearMagicBullet;
					bool isWoodenBox = charm is Charm_WoodenBox;
					bool isFireIce = charm is Charm_FireIce || charm is Charm_FireIceWeapon;

					string[] categories = entity.categories != null
						? entity.categories.ToArray()
						: Array.Empty<string>();

					uint staticCatMask = 0u;
					bool isDynamicCategoryProvider = isElementalByRow || hasDependency || isWhitePaper;
					if (!isDynamicCategoryProvider) {
						bool countStaticCombo = !talentRules.PreventOverlapItemCombo
							|| seenStaticEntityIds.Add(entity.id);
						for (int c = 0; c < categories.Length; c++) {
							if (categoryKeyToId.TryGetValue(categories[c], out int catId)
								&& catId >= 0
								&& catId < InventorySnapshot.MaxSupportedCategories) {
								staticCatMask |= 1u << catId;
								if (countStaticCombo) {
									staticCategoryCounts[catId]++;
								}
							}
						}
					}

					ArtifactPriorityTier defaultPriority = InferDefaultPriorityTier(
						entity,
						charm,
						isDisabledByWeaponOrDual
					);
					ArtifactPriorityTier priorityTier = settings.GetPriority(
						itemInstance.InstanceID,
						defaultPriority
					);
					if (settings.PreferredNeedleTargetInstanceId == itemInstance.InstanceID
						&& priorityTier < ArtifactPriorityTier.Core) {
						priorityTier = ArtifactPriorityTier.Core;
					}

					int weight = ComputeArtifactWeight(entity, charm, customEntityWeights);
					ExtractCharmCombatTables(
						avatar,
						entity,
						charm,
						isAttackable,
						hasDependency,
						isNearLevelDamage,
						isElementalByRow,
						maxLevel,
						priorityTier,
						rawCombatBaseline.AttacksPerSecond,
						currentTabletCount,
						out float[]? attackDamageByLevel,
						out float baseProcsPerSecond,
						out float[]? statBonusPercentByLevel,
						out float[]? finalDamagePercentByLevel,
						out CombatStatDelta[]? statDeltaByLevel,
						out byte charmScalingElementKind,
						out float[]? charmElementalPercentByLevel
					);

					string symmetryKey = $"C:{entity.id}:{baseLevel}:{maxLevel}:"
						+ $"{(int)constraintType}:{(int)priorityTier}:{staticCatMask}";
					if (settings.PreferredNeedleTargetInstanceId == itemInstance.InstanceID) {
						symmetryKey += $":NEEDLE_TARGET:{itemInstance.InstanceID}";
					}
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
						symmetryGroupId,
						priorityTier,
						staticCatMask,
						hasDepCond,
						depMaxRarity,
						needleDmg,
						needleDepDmg,
						rowCategoryIds,
						rowElementalBonus,
						whitePaperMatch,
						isNearLevelDamage,
						isCompanionChaos,
						isCompanion,
						isPlanetModule,
						isPlanet,
						isNearMagicBullet,
						isWoodenBox,
						isFireIce,
						isNearLevelDamage,
						crystalRates,
						attackDamageByLevel,
						baseProcsPerSecond,
						statBonusPercentByLevel,
						finalDamagePercentByLevel,
						statDeltaByLevel,
						charmScalingElementKind,
						charmElementalPercentByLevel
					));
				} else if (entity.type == EItemType.StoneTablet) {
					StoneTablet? tablet = ResolveSlotTablet(
						inventory,
						itemInstance,
						entity,
						xCoord,
						yCoord,
						out bool isLiveTabletInstance
					);
					int initialRotation = tablet != null && isLiveTabletInstance ? (tablet.rotation & 3) : 0;
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

			if (inventory.bonusComboCount != null) {
				foreach (KeyValuePair<string, int> bonusPair in inventory.bonusComboCount) {
					if (bonusPair.Value > 0
						&& categoryKeyToId.TryGetValue(bonusPair.Key, out int bonusCatId)
						&& (uint)bonusCatId < (uint)staticCategoryCounts.Length) {
						staticCategoryCounts[bonusCatId] += bonusPair.Value;
					}
				}
			}

			ComboCategoryInfo[] comboCategories = BuildComboCategoryInfos(
				orderedCategoryKeys,
				staticCategoryCounts,
				talentRules.ComboBonusDamageStat,
				settings
			);

			bool isFullHp = avatar != null && Math.Abs(avatar.hp - avatar.MaxHp) < 0.01f;
			int preferredChalcedonyId = -1;
			if (!string.IsNullOrEmpty(settings.PreferredChalcedonyCategory)
				&& categoryKeyToId.TryGetValue(settings.PreferredChalcedonyCategory, out int chalCatId)) {
				preferredChalcedonyId = chalCatId;
			}

			ulong[] mooreNeighborMasks = BuildMooreNeighborMasks(width, height, unlockedSlots);
			ReadMysticConfiguration(
				avatar,
				inventory,
				width,
				height,
				unlockedSlots,
				out int mysticSlot0,
				out int mysticSlot1,
				out int mysticTier1Threshold,
				out int mysticTier2Threshold,
				out int[] extraMysticTier2Slots
			);

			SlotItemSnapshot[] capturedItems = itemSnapshots.ToArray();
			PlayerCombatBaseline combatBaseline = DeductActiveInventoryStatBonuses(
				rawCombatBaseline,
				inventory,
				capturedItems,
				comboCategories,
				talentRules.UnlimitedComboStat,
				talentRules.ComboBonusDamageStat,
				width
			);

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
				capturedItems,
				comboCategories,
				talentRules,
				settings.PreferredNeedleTargetInstanceId,
				preferredChalcedonyId,
				mooreNeighborMasks,
				mysticSlot0,
				mysticSlot1,
				mysticTier1Threshold,
				mysticTier2Threshold,
				extraMysticTier2Slots,
				combatBaseline
			);
		}

		public static ulong[] BuildMooreNeighborMasks(int width, int height, int unlockedSlots) {
			return InventorySnapshot.ComputeMooreNeighborMasks(width, height, unlockedSlots);
		}

		private static PlayerCombatBaseline ExtractCombatBaseline(
			PlayerAvatar? avatar,
			TalentRuleSnapshot talentRules
		) {
			if (avatar == null) {
				return PlayerCombatBaseline.Default;
			}

			float basePhys = Math.Max(1f, avatar.GetCustomStat(ECustomStat.PhysicalDamage));
			float baseFire = Math.Max(1f, avatar.GetCustomStat(ECustomStat.FireDamage));
			float baseIce = Math.Max(1f, avatar.GetCustomStat(ECustomStat.IceDamage));
			float baseLight = Math.Max(1f, avatar.GetCustomStat(ECustomStat.LightningDamage));
			float baseAllDmg = avatar.GetCustomStat(ECustomStat.AllDamageBonus);
			float baseWeaponDmg = avatar.GetCustomStat(ECustomStat.WeaponDamageBonus);
			float baseBasicDmg = avatar.GetCustomStat(ECustomStat.BasicAttackDamageBonus);
			float baseFinalWeaponDmg = avatar.GetCustomStat(ECustomStat.FinalWeaponDamage);
			float baseCharmDmg = avatar.GetCustomStatUnsafe("CHARMDAMAGEBONUS");
			float baseAttackSpeed = avatar.GetCustomStat(ECustomStat.AttackSpeed);
			float baseCritChancePct = Math.Max(0f, avatar.GetCustomStat(ECustomStat.Critical) / 100f);
			float baseCritDmgRate = Math.Max(0f, avatar.GetCustomStat(ECustomStat.CriticalDamageBonus) + 50f);

			float weaponExtraCritChancePct = avatar.GetCustomStatUnsafe("WEAPONCRITICAL") / 100f;
			float weaponExtraCritDmgRate = avatar.GetCustomStatUnsafe("WEAPONCRITICALDAMAGE");
			float weaponCritAmpPct = Math.Max(0f, avatar.GetCustomStatUnsafe("WEAPONCRITICALDAMAGEAMPLIFY"));
			bool hasExecution = avatar.GetCustomStat(ECustomStat.EXECUTION) > 0;
			float mpSkillDmgPct = avatar.GetCustomStatUnsafe("MPSKILLDAMAGE");
			int lastBasicDmgPct = avatar.GetCustomStatUnsafe("LASTBASICATTACKDAMAGE");

			int fixedAsRaw = avatar.GetCustomStatUnsafe("FIXEDATTACKSPEED");
			float fixedAsMult = fixedAsRaw > 0 ? (fixedAsRaw / 100f) : 0f;

			int dashBonusPerCount = avatar.GetCustomStatUnsafe("WEAPONDAMAGEBONUSBYDASHCOUNT");
			int dashCount = avatar.GetCustomStat(ECustomStat.DashCount);
			float directAttackDashMult = Math.Max(0.1f, 1f + ((dashBonusPerCount * dashCount) / 100f));

			float globalPostMult = 1f;
			if (avatar.GetCustomStatUnsafe("GOLDHAND") > 0) {
				int leafPerPct = SafeGetConstValue("GOLDHANDLEAF", 100);
				if (leafPerPct > 0) {
					int goldPct = avatar.Money / leafPerPct;
					if (avatar.GetCustomStatUnsafe("GOLDHANDUNLIMIT") <= 0) {
						int goldCap = SafeGetConstValue("GOLDHANDMAX", 50);
						goldPct = Math.Min(goldPct, goldCap);
					}
					globalPostMult *= Math.Max(0.1f, 1f + (goldPct / 100f));
				}
			}
			if (avatar.GetCustomStatUnsafe("DEFENSETOATTACK") > 0) {
				int defenseStat = avatar.GetCustomStat(ECustomStat.DamageReduction);
				float drBonus = UnitAvatar.GetDamageReduction(1f, defenseStat);
				globalPostMult *= Math.Max(0.1f, 1f + drBonus);
			}

			WeaponElementalFormulaMode formulaMode = WeaponElementalFormulaMode.Highest;
			byte averageElementMask = 0x0F;
			float comboSwingMultiplier = 1.0f;
			float avgSwingRatioForAddons = 1.0f;
			float flatBonusDamagePerSwing = 0f;
			float baseSwingRate = 2.0f;
			float attackSpeedAmplify = 0f;
			float extraAttackSpeedBonus = 0f;
			int magazineCapacity = 0;
			float effectiveReloadSeconds = 0f;
			float additionalBasicAttackPercent = 0f;
			bool usesMpBasicAttack = false;
			bool hasElementalScaledSpread = false;
			int spreadBaseBullets = 1;
			int spreadStatThreshold = 20;
			int spreadStatPerExtraBullet = 10;
			int spreadMaxExtraBullets = 0;
			float addOnHitPhysicalPct = 0f;
			float addOnHitFirePct = 0f;
			float addOnHitIcePct = 0f;
			float addOnHitLightningPct = 0f;
			WeaponConditionalStatMode condMode = WeaponConditionalStatMode.None;
			WeaponConditionalStatTarget condTarget = WeaponConditionalStatTarget.None;
			float condThreshold = 0f;
			float condDamageAmpPercent = 0f;
			float burnRingFirePctPerSec = 0f;
			int critNeedleBaseCount = 0;
			float critNeedlePhysicalRatio = 0f;
			float nastrondDefaultDmg = 0f;
			float nastrondIcePct = 0f;
			float nastrondBonusMult = 1f;
			float nastrondProcsPerSec = 0f;

			WeaponControllerSimple? weaponCtrl = avatar.GetComponent<WeaponControllerSimple>();
			WeaponSimple? weapon = weaponCtrl != null ? weaponCtrl.currentWeapon : null;
			if (weapon != null) {
				attackSpeedAmplify = weapon.attackSpeedAmplify;
				if (weapon.AttackWeightPerSwing > 0.04f && weapon.AttackWeightPerSwing <= 4.0f) {
					baseSwingRate = Math.Clamp(1.0f / weapon.AttackWeightPerSwing, 0.4f, 16.0f);
				}

				if (weapon is WeaponSimple_Crossbow crossbow) {
					if (crossbow.fireIntervalTimer != null && crossbow.fireIntervalTimer.time > 0.02f) {
						baseSwingRate = Math.Clamp(1.0f / crossbow.fireIntervalTimer.time, 0.4f, 20.0f);
					}
					if (avatar.GetCustomStatUnsafe("GRENADEATTACK") > 0) {
						fixedAsMult = 1.0f;
					} else {
						if (avatar.GetCustomStatUnsafe("CROSSBOWMG") > 0) {
							extraAttackSpeedBonus += 0.5f;
						}
						if (avatar.GetCustomStatUnsafe("MOVESPEEDTOATTACKSPEED") > 0) {
							float moveBonus = avatar.moveSpeedMultiplier - 1f;
							if (moveBonus > 0f) {
								extraAttackSpeedBonus += moveBonus;
							}
						}
						int iceDecAs = avatar.GetCustomStatUnsafe("ICEBUFF_DECREASEATTACKSPEED");
						if (iceDecAs > 0) {
							extraAttackSpeedBonus -= iceDecAs / 100f;
						}
					}

					bool iceInfiniteAmmo = avatar.GetCustomStatUnsafe("ICECROSSBOWBUFF") > 0
						&& crossbow.infiniteAmmoDuringBuff;
					if (!iceInfiniteAmmo && crossbow.reloadTime > 0.02f) {
						int fixedAmmo = avatar.GetCustomStatUnsafe("FIXEDAMMO");
						int magCap = Math.Max(
							1,
							crossbow.defaultMagazineCapacity + avatar.GetCustomStatUnsafe("CROSSBOWAMMO")
						);
						int magCount = Math.Max(
							1,
							crossbow.defaultMagazineCount + avatar.GetCustomStatUnsafe("CROSSBOWADDITIONALMAGAZINE")
						);
						magazineCapacity = fixedAmmo > 0 ? fixedAmmo : (magCap * magCount);
						float reloadSpeedFactor = 1f + (avatar.GetCustomStatUnsafe("CROSSBOWRELOADSPEED") * 0.01f);
						if (avatar.GetCustomStatUnsafe("ICECROSSBOWBUFF") > 0
							&& avatar.GetCustomStatUnsafe("ICECROSSBOWFROSTRELIC") > 0) {
							reloadSpeedFactor += avatar.GetCustomStatUnsafe("CHARGINGCHARMBONUS") * 0.01f;
						}
						effectiveReloadSeconds = crossbow.reloadTime / Math.Max(0.2f, reloadSpeedFactor);
					}
				} else if (weapon is WeaponSimple_Staff staff) {
					if (staff.fireIntervalTimer != null && staff.fireIntervalTimer.time > 0.02f) {
						baseSwingRate = Math.Clamp(1.0f / staff.fireIntervalTimer.time, 0.4f, 20.0f);
					}
					if (staff.ammoCount > 0 && staff.reloadTimer != null && staff.reloadTimer.time > 0.02f) {
						magazineCapacity = staff.ammoCount;
						effectiveReloadSeconds = staff.reloadTimer.time;
					}
				}

				if (weapon is WeaponSimple_GreatSword greatSword
					&& (greatSword.useMPBasicAttack || greatSword.isTransformed)) {
					usesMpBasicAttack = true;
				}

				ExtractWeaponSwingMetrics(
					avatar,
					weapon,
					lastBasicDmgPct,
					out formulaMode,
					out averageElementMask,
					out comboSwingMultiplier,
					out avgSwingRatioForAddons,
					out additionalBasicAttackPercent,
					out hasElementalScaledSpread,
					out spreadBaseBullets,
					out spreadStatThreshold,
					out spreadStatPerExtraBullet,
					out spreadMaxExtraBullets
				);

				if (weapon.addons != null) {
					for (int i = 0; i < weapon.addons.Length; i++) {
						WeaponAddon addon = weapon.addons[i];
						if (!IsWeaponAddonActive(weapon, addon)) {
							continue;
						}

						if (addon is WeaponAddonCommon_AdditionalElementalDamage addElem) {
							int totalPct = Math.Max(
								0,
								addElem.additionalDamagePercent
								+ avatar.GetCustomStatUnsafe("ADDITIONALELEMENTALDAMAGEBONUS")
							);
							switch (addElem.statId) {
								case ECustomStat.PhysicalDamage: {
									addOnHitPhysicalPct += totalPct;
									break;
								}
								case ECustomStat.FireDamage: {
									addOnHitFirePct += totalPct;
									break;
								}
								case ECustomStat.IceDamage: {
									addOnHitIcePct += totalPct;
									break;
								}
								case ECustomStat.LightningDamage: {
									addOnHitLightningPct += totalPct;
									break;
								}
							}
						} else if (addon is WeaponAddonCommon_ConditionalStat condAddon) {
							WeaponConditionalStatMode parsedMode = MapConditionalMode(condAddon.condition);
							WeaponConditionalStatTarget parsedTarget = MapConditionalTarget(condAddon.sourceStatId);
							if (parsedTarget != WeaponConditionalStatTarget.None
								&& condMode == WeaponConditionalStatMode.None) {
								condMode = parsedMode;
								condTarget = parsedTarget;
								condThreshold = condAddon.sourceStatValue;
								condDamageAmpPercent = condAddon.addDamagePercent;
							} else if (EvaluateLiveStatCondition(
								avatar.GetCustomStatUnsafe(condAddon.sourceStatId),
								parsedMode,
								condAddon.sourceStatValue
							)) {
								globalPostMult *= Math.Max(0.1f, 1f + (condAddon.addDamagePercent / 100f));
							}
						} else if (addon is WeaponAddonCommon_BurnRing burnRing) {
							float tickSec = burnRing.burnRingTickTimer != null
								&& burnRing.burnRingTickTimer.time > 0.05f
								? burnRing.burnRingTickTimer.time
								: 0.5f;
							float burnSpeedMult = Math.Max(
								0.2f,
								1f + (avatar.GetCustomStatUnsafe("BURNSPEED") / 100f)
							);
							float burnDmgMult = Math.Max(
								0.1f,
								1f + (avatar.GetCustomStatUnsafe("BURNDAMAGE") / 100f)
							);
							burnRingFirePctPerSec += (burnRing.damagePercent * 0.01f)
								* (burnSpeedMult / tickSec)
								* burnDmgMult;
						} else if (addon is WeaponAddonGreatsword_FireNeedleBullet needleAddon) {
							critNeedleBaseCount = needleAddon.needleBulletCount;
							critNeedlePhysicalRatio = needleAddon.needleBulletDamageRatio;
						} else if (addon is WeaponAddonKatana_Nastrond nastrond) {
							nastrondDefaultDmg = nastrond.defaultDamage;
							nastrondIcePct = nastrond.damagePercent;
							nastrondBonusMult = Math.Max(
								0.1f,
								1f + (avatar.GetCustomStatUnsafe("FROSTRELICDAMAGE") / 100f)
							);
							float chargeTime = nastrond.chargingCharm != null
								&& nastrond.chargingCharm.defaultChargeTimer > 0.1f
								? nastrond.chargingCharm.defaultChargeTimer
								: 2.5f;
							float chargeSpeed = Math.Max(
								0.25f,
								1f + (avatar.GetCustomStatUnsafe("CHARGINGCHARMBONUS") * 0.01f)
							);
							nastrondProcsPerSec = Math.Clamp((chargeSpeed / chargeTime) * 0.65f, 0.1f, 4.0f);
						} else if (addon is WeaponAddonCommon_TransformAttackBonus transBonus) {
							if (weapon is WeaponSimple_GreatSword gsTrans && gsTrans.isTransformed) {
								float flatAdd = SafeGetConstValue(transBonus.transformAttackBonusDamageConstKey, 0);
								float defRatio = SafeGetConstValue(transBonus.relatedStatBonusConstKey, 0);
								flatAdd += avatar.GetCustomStatUnsafe(transBonus.relatedStatUnsafe) * defRatio / 100f;
								flatBonusDamagePerSwing += Math.Max(0f, flatAdd);
							}
						}
					}
				}
			}

			WeaponCombatProfile weaponProfile = new WeaponCombatProfile(
				formulaMode,
				averageElementMask,
				basePhys,
				baseFire,
				baseIce,
				baseLight,
				baseAllDmg,
				baseWeaponDmg,
				baseBasicDmg,
				baseFinalWeaponDmg,
				baseCharmDmg,
				baseAttackSpeed,
				baseCritChancePct,
				baseCritDmgRate,
				weaponExtraCritChancePct,
				weaponExtraCritDmgRate,
				weaponCritAmpPct,
				hasExecution,
				comboSwingMultiplier,
				avgSwingRatioForAddons,
				flatBonusDamagePerSwing,
				baseSwingRate,
				attackSpeedAmplify,
				fixedAsMult,
				extraAttackSpeedBonus,
				magazineCapacity,
				effectiveReloadSeconds,
				additionalBasicAttackPercent,
				usesMpBasicAttack,
				mpSkillDmgPct,
				directAttackDashMult,
				globalPostMult,
				hasElementalScaledSpread,
				spreadBaseBullets,
				spreadStatThreshold,
				spreadStatPerExtraBullet,
				spreadMaxExtraBullets,
				addOnHitPhysicalPct,
				addOnHitFirePct,
				addOnHitIcePct,
				addOnHitLightningPct,
				condMode,
				condTarget,
				condThreshold,
				condDamageAmpPercent,
				burnRingFirePctPerSec,
				critNeedleBaseCount,
				critNeedlePhysicalRatio,
				nastrondDefaultDmg,
				nastrondIcePct,
				nastrondBonusMult,
				nastrondProcsPerSec
			);
			return new PlayerCombatBaseline(in weaponProfile);
		}

		private static void ExtractWeaponSwingMetrics(
			PlayerAvatar avatar,
			WeaponSimple weapon,
			int lastBasicDmgPct,
			out WeaponElementalFormulaMode formulaMode,
			out byte averageElementMask,
			out float comboSwingMultiplier,
			out float avgSwingRatioForAddons,
			out float additionalBasicAttackPercent,
			out bool hasElementalScaledSpread,
			out int spreadBaseBullets,
			out int spreadStatThreshold,
			out int spreadStatPerExtraBullet,
			out int spreadMaxExtraBullets
		) {
			formulaMode = WeaponElementalFormulaMode.Highest;
			averageElementMask = 0x0F;
			comboSwingMultiplier = 1.0f;
			avgSwingRatioForAddons = 1.0f;
			additionalBasicAttackPercent = 0f;
			hasElementalScaledSpread = false;
			spreadBaseBullets = 1;
			spreadStatThreshold = 20;
			spreadStatPerExtraBullet = 10;
			spreadMaxExtraBullets = 0;

			bool forceAllSwingsFinal = weapon is WeaponSimple_Dagger dagger && dagger.basicAttackFinal;
			float lightningArrowProb = 0f;
			NewWeaponFireData? lightningArrowData = null;
			NewWeaponFireData? iceArrowOverride = null;
			if (weapon is WeaponSimple_Crossbow crossbow) {
				if (avatar.GetCustomStatUnsafe("ICECROSSBOWBUFF") > 0 && crossbow.iceArrow != null) {
					iceArrowOverride = crossbow.iceArrow;
				} else if (crossbow.lightningArrow != null) {
					lightningArrowProb = Math.Clamp(
						avatar.GetCustomStatUnsafe("LIGHTNINGCROSSBOW") / 100f,
						0f,
						1f
					);
					lightningArrowData = crossbow.lightningArrow;
				}
			}

			float doubleSpearProb = 0f;
			WeaponSimple_QuartterStaff? quarterStaff = weapon as WeaponSimple_QuartterStaff;
			if (quarterStaff != null
				&& quarterStaff.doubleBasicComboAttacks != null
				&& quarterStaff.doubleBasicComboAttacks.Length > 0) {
				float doublePct = (avatar.GetCustomStatUnsafe("STAFFMULTITHROWINGSPEARDOUBLE") / 100f)
					* (avatar.GetCustomStat(ECustomStat.Evasion) / 100f);
				doubleSpearProb = Math.Clamp(doublePct / 100f, 0f, 1f);
			}

			int swingCount = weapon.basicComboAttacks != null ? weapon.basicComboAttacks.Length : 0;
			if (quarterStaff != null
				&& quarterStaff.isNormalAttackRolling
				&& quarterStaff.rollingFireData != null) {
				swingCount = 1;
			} else if (iceArrowOverride != null && swingCount <= 0) {
				swingCount = 1;
			}

			if (swingCount <= 0) {
				if (weapon is WeaponSimple_SwordAndShield snsEmpty && snsEmpty.isAllElementalEnabled) {
					formulaMode = WeaponElementalFormulaMode.Highest;
				}
				return;
			}

			float sumSwingMult = 0f;
			float sumAddonMult = 0f;
			float sumAddBasicPct = 0f;
			int validSwings = 0;
			NewWeaponFireData? representativeFireData = null;

			for (int idx = 0; idx < swingCount; idx++) {
				NewWeaponFireData? swing = ResolvePrimaryComboSwing(
					weapon,
					quarterStaff,
					iceArrowOverride,
					idx
				);
				if (swing == null) {
					continue;
				}
				if (representativeFireData == null) {
					representativeFireData = swing;
				}

				bool isFinalSwing = forceAllSwingsFinal
					|| idx == weapon.finalComboIdx
					|| swing.forceFinalCombo;
				EvaluateSingleFireData(
					swing,
					isFinalSwing,
					lastBasicDmgPct,
					out float primarySwingMult,
					out float primaryAddonMult,
					ref hasElementalScaledSpread,
					ref spreadBaseBullets,
					ref spreadStatThreshold,
					ref spreadStatPerExtraBullet,
					ref spreadMaxExtraBullets
				);

				if (lightningArrowProb > 0f && lightningArrowData != null) {
					EvaluateSingleFireData(
						lightningArrowData,
						isFinalSwing,
						lastBasicDmgPct,
						out float lightSwingMult,
						out float lightAddonMult,
						ref hasElementalScaledSpread,
						ref spreadBaseBullets,
						ref spreadStatThreshold,
						ref spreadStatPerExtraBullet,
						ref spreadMaxExtraBullets
					);
					primarySwingMult = ((1f - lightningArrowProb) * primarySwingMult)
						+ (lightningArrowProb * lightSwingMult);
					primaryAddonMult = ((1f - lightningArrowProb) * primaryAddonMult)
						+ (lightningArrowProb * lightAddonMult);
				}

				if (doubleSpearProb > 0f
					&& quarterStaff != null
					&& quarterStaff.doubleBasicComboAttacks != null
					&& quarterStaff.doubleBasicComboAttacks.Length > 0) {
					int dblIdx = Math.Clamp(idx, 0, quarterStaff.doubleBasicComboAttacks.Length - 1);
					NewWeaponFireData? dblSwing = quarterStaff.doubleBasicComboAttacks[dblIdx];
					if (dblSwing != null) {
						EvaluateSingleFireData(
							dblSwing,
							isFinalSwing,
							lastBasicDmgPct,
							out float dblSwingMult,
							out float dblAddonMult,
							ref hasElementalScaledSpread,
							ref spreadBaseBullets,
							ref spreadStatThreshold,
							ref spreadStatPerExtraBullet,
							ref spreadMaxExtraBullets
						);
						primarySwingMult = ((1f - doubleSpearProb) * primarySwingMult)
							+ (doubleSpearProb * dblSwingMult);
						primaryAddonMult = ((1f - doubleSpearProb) * primaryAddonMult)
							+ (doubleSpearProb * dblAddonMult);
					}
				}

				sumSwingMult += primarySwingMult;
				sumAddonMult += primaryAddonMult;
				sumAddBasicPct += SafeGetAdditionalBasicAttackPercent(weapon, idx);
				validSwings++;
			}

			if (validSwings > 0) {
				comboSwingMultiplier = Math.Max(0.05f, sumSwingMult / validSwings);
				avgSwingRatioForAddons = Math.Max(0.05f, sumAddonMult / validSwings);
				additionalBasicAttackPercent = sumAddBasicPct / validSwings;
			}

			if (weapon is WeaponSimple_SwordAndShield sns && sns.isAllElementalEnabled) {
				formulaMode = WeaponElementalFormulaMode.Highest;
				averageElementMask = 0x0F;
			} else if (representativeFireData != null) {
				ParseFireDataElementalFormula(
					representativeFireData,
					out formulaMode,
					out averageElementMask
				);
			}
		}

		private static NewWeaponFireData? ResolvePrimaryComboSwing(
			WeaponSimple weapon,
			WeaponSimple_QuartterStaff? quarterStaff,
			NewWeaponFireData? iceArrowOverride,
			int idx
		) {
			if (quarterStaff != null
				&& quarterStaff.isNormalAttackRolling
				&& quarterStaff.rollingFireData != null) {
				return quarterStaff.rollingFireData;
			}
			if (iceArrowOverride != null) {
				return iceArrowOverride;
			}
			if (weapon is WeaponSimple_GreatSword greatSword
				&& greatSword.useMPBasicAttack
				&& greatSword.mpConsumedBasicAttack != null
				&& idx < greatSword.mpConsumedBasicAttack.Length
				&& greatSword.mpConsumedBasicAttack[idx] != null) {
				return greatSword.mpConsumedBasicAttack[idx];
			}
			if (weapon is WeaponSimple_Crossbow crossbow && crossbow.hasCompressedAmmo) {
				try {
					NewWeaponFireData compressed = crossbow.GetBasicAttack(idx, "COMPRESSEDAMMO");
					if (compressed != null) {
						return compressed;
					}
				} catch {
					// Fallback to default basic attack below.
				}
			}
			try {
				NewWeaponFireData resolved = weapon.GetBasicAttack(idx, string.Empty);
				if (resolved != null) {
					return resolved;
				}
			} catch {
				// Fallback to raw array below.
			}
			if (weapon.basicComboAttacks != null && idx >= 0 && idx < weapon.basicComboAttacks.Length) {
				return weapon.basicComboAttacks[idx];
			}
			return null;
		}

		private static void EvaluateSingleFireData(
			NewWeaponFireData swing,
			bool isFinalSwing,
			int lastBasicDmgPct,
			out float effectiveSwingMult,
			out float addonHitMult,
			ref bool hasElementalScaledSpread,
			ref int spreadBaseBullets,
			ref int spreadStatThreshold,
			ref int spreadStatPerExtraBullet,
			ref int spreadMaxExtraBullets
		) {
			float baseDmgMult = swing.damageMultiplier > 0.02f ? swing.damageMultiplier : 1.0f;
			float hitCount = 1.0f;

			if (swing is NewWeaponFireData_MeleeAttack meleeSwing && meleeSwing.projectilePrefab != null) {
				MeleeCollision? meleeCol = meleeSwing.projectilePrefab.GetComponent<MeleeCollision>();
				if (meleeCol != null && meleeCol.multiHit > 1) {
					hitCount = Math.Clamp(meleeCol.multiHit, 1, 12);
				}
			} else if (swing is NewWeaponFireData_BulletBurst burstSwing) {
				hitCount = Math.Clamp(burstSwing.burstRound, 1, 16);
			} else if (swing is NewWeaponFireData_BulletSpread spreadSwing) {
				int totalBullets = spreadSwing.betweenAngleDegrees > 0.01f
					? Math.Max(1, (int)Math.Ceiling(spreadSwing.spreadAngle / spreadSwing.betweenAngleDegrees))
					: 1;
				if (totalBullets <= 1 || spreadSwing.spreadAngle <= 50f) {
					hitCount = totalBullets;
				} else {
					float coneFraction = Math.Clamp(55f / spreadSwing.spreadAngle, 1f / totalBullets, 1f);
					hitCount = Math.Max(1f, totalBullets * coneFraction);
				}
				if (spreadSwing is NewWeaponFireData_BulletSpread_ElementalScaled scaledSpread) {
					hasElementalScaledSpread = true;
					spreadBaseBullets = Math.Max(1, totalBullets);
					spreadStatThreshold = Math.Max(0, scaledSpread.baseStatThreshold);
					spreadStatPerExtraBullet = Math.Max(1, scaledSpread.statPerExtraBullet);
					spreadMaxExtraBullets = Math.Max(0, scaledSpread.maxExtraBullets);
				}
			}

			float mpUsedFactor = swing.addDamagePerUsedMP > 0
				? (1f + ((2f * swing.addDamagePerUsedMP) / 100f))
				: 1f;
			float finalComboFactor = isFinalSwing && lastBasicDmgPct != 0
				? Math.Max(0.1f, 1f + (lastBasicDmgPct / 100f))
				: 1f;

			addonHitMult = baseDmgMult * hitCount;
			effectiveSwingMult = addonHitMult * mpUsedFactor * finalComboFactor;
		}

		private static void ParseFireDataElementalFormula(
			NewWeaponFireData fireData,
			out WeaponElementalFormulaMode formulaMode,
			out byte averageMask
		) {
			averageMask = 0x0F;
			string rawFormula = fireData.relatedStatFormula != null
				? fireData.relatedStatFormula.Trim().ToUpperInvariant()
				: string.Empty;

			if (string.IsNullOrEmpty(rawFormula)) {
				formulaMode = fireData.damageElementalType switch {
					EDamageElementalType.Physical => WeaponElementalFormulaMode.SinglePhysical,
					EDamageElementalType.Fire => WeaponElementalFormulaMode.SingleFire,
					EDamageElementalType.Ice => WeaponElementalFormulaMode.SingleIce,
					EDamageElementalType.Lightning => WeaponElementalFormulaMode.SingleLightning,
					_ => WeaponElementalFormulaMode.Highest
				};
				return;
			}

			if (rawFormula == "PHYSICALDAMAGE" || rawFormula.StartsWith("CHAOS_PHYSICAL", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.SinglePhysical;
				return;
			}
			if (rawFormula == "FIREDAMAGE" || rawFormula.StartsWith("CHAOS_FIRE", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.SingleFire;
				return;
			}
			if (rawFormula == "ICEDAMAGE" || rawFormula.StartsWith("CHAOS_ICE", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.SingleIce;
				return;
			}
			if (rawFormula == "LIGHTNINGDAMAGE"
				|| rawFormula.StartsWith("CHAOS_LIGHTNING", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.SingleLightning;
				return;
			}
			if (rawFormula.StartsWith("HIGHEST", StringComparison.Ordinal)
				|| rawFormula.StartsWith("CHAOS_HIGHEST", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.Highest;
				return;
			}
			if (rawFormula.StartsWith("LOWEST", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.Lowest;
				return;
			}
			if (rawFormula.StartsWith("AVERAGEALL", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.AverageMask;
				averageMask = 0x0F;
				return;
			}
			if (rawFormula.StartsWith("AVERAGE/", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.AverageMask;
				byte mask = 0;
				string listPart = rawFormula.Substring(8);
				string[] tokens = listPart.Split(',');
				for (int i = 0; i < tokens.Length; i++) {
					string tok = tokens[i].Trim();
					if (tok == "PHYSICALDAMAGE") {
						mask |= 0x01;
					} else if (tok == "FIREDAMAGE") {
						mask |= 0x02;
					} else if (tok == "ICEDAMAGE") {
						mask |= 0x04;
					} else if (tok == "LIGHTNINGDAMAGE") {
						mask |= 0x08;
					}
				}
				averageMask = mask != 0 ? mask : (byte)0x0F;
				return;
			}
			if (rawFormula.StartsWith("ONLY_1", StringComparison.Ordinal)) {
				formulaMode = WeaponElementalFormulaMode.OnlyOne;
				return;
			}

			formulaMode = WeaponElementalFormulaMode.Highest;
		}

		private static bool IsWeaponAddonActive(WeaponSimple weapon, WeaponAddon? addon) {
			if (addon == null || !addon.enabled || !addon.gameObject.activeInHierarchy) {
				return false;
			}
			if (weapon.isServer && WeaponAddonIsEnabledField != null) {
				object? val = WeaponAddonIsEnabledField.GetValue(addon);
				if (val is bool isEn) {
					return isEn;
				}
			}
			return true;
		}

		private static int SafeGetAdditionalBasicAttackPercent(WeaponSimple weapon, int idx) {
			try {
				return weapon.GetAdditionalBasicAttackDamagePercent(idx);
			} catch {
				return 0;
			}
		}

		private static int SafeGetConstValue(string key, int fallback) {
			if (string.IsNullOrEmpty(key)) {
				return fallback;
			}
			try {
				int val = KeywordDatabase.GetConstValue(key);
				return val != 0 ? val : fallback;
			} catch {
				return fallback;
			}
		}

		private static WeaponConditionalStatMode MapConditionalMode(
			WeaponAddonCommon_ConditionalStat.ECondition condition
		) {
			return condition switch {
				WeaponAddonCommon_ConditionalStat.ECondition.LOWER_THAN_OR_EQUAL =>
					WeaponConditionalStatMode.LessOrEqual,
				WeaponAddonCommon_ConditionalStat.ECondition.GREATER_THAN_OR_EQUAL =>
					WeaponConditionalStatMode.GreaterOrEqual,
				WeaponAddonCommon_ConditionalStat.ECondition.EQUAL =>
					WeaponConditionalStatMode.Equal,
				WeaponAddonCommon_ConditionalStat.ECondition.NOT_EQUAL =>
					WeaponConditionalStatMode.NotEqual,
				_ => WeaponConditionalStatMode.None
			};
		}

		private static WeaponConditionalStatTarget MapConditionalTarget(string? sourceStatId) {
			if (string.IsNullOrEmpty(sourceStatId)) {
				return WeaponConditionalStatTarget.None;
			}
			return sourceStatId.Trim().ToUpperInvariant() switch {
				"CRITICAL" => WeaponConditionalStatTarget.CriticalRaw,
				"PHYSICALDAMAGE" => WeaponConditionalStatTarget.PhysicalDamage,
				"FIREDAMAGE" => WeaponConditionalStatTarget.FireDamage,
				"ICEDAMAGE" => WeaponConditionalStatTarget.IceDamage,
				"LIGHTNINGDAMAGE" => WeaponConditionalStatTarget.LightningDamage,
				"ATTACKSPEED" => WeaponConditionalStatTarget.AttackSpeed,
				"ALLDAMAGEBONUS" => WeaponConditionalStatTarget.AllDamageBonus,
				_ => WeaponConditionalStatTarget.None
			};
		}

		private static bool EvaluateLiveStatCondition(
			int liveStatVal,
			WeaponConditionalStatMode mode,
			int threshold
		) {
			return mode switch {
				WeaponConditionalStatMode.LessOrEqual => liveStatVal <= threshold,
				WeaponConditionalStatMode.GreaterOrEqual => liveStatVal >= threshold,
				WeaponConditionalStatMode.Equal => liveStatVal == threshold,
				WeaponConditionalStatMode.NotEqual => liveStatVal != threshold,
				_ => false
			};
		}

		private static PlayerCombatBaseline DeductActiveInventoryStatBonuses(
			PlayerCombatBaseline rawBaseline,
			GridInventory inventory,
			SlotItemSnapshot[] items,
			ComboCategoryInfo[] comboCategories,
			int unlimitedComboStat,
			int comboBonusDamageStat,
			int width
		) {
			if (rawBaseline.HasDetailedWeaponProfile) {
				CombatStatDelta activeDelta = CombatStatDelta.Zero;
				int safeWidth = Math.Max(1, width);

				for (int i = 0; i < items.Length; i++) {
					SlotItemSnapshot item = items[i];
					if (item.Kind != SlotItemKind.Artifact || item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
						continue;
					}
					sbyte xCoord = (sbyte)(item.InitialSlotIndex % safeWidth);
					sbyte yCoord = (sbyte)(item.InitialSlotIndex / safeWidth);
					NewItemOwnInstance? inst = inventory.FindItem(xCoord, yCoord);
					Charm_Basic? liveCharm = inst != null ? inst.Charm : null;
					if (liveCharm == null && inventory.charms != null) {
						inventory.charms.TryGetValue(new ItemPosition(xCoord, yCoord), out liveCharm);
					}
					if (liveCharm == null || !liveCharm.IsEffectEnabled) {
						continue;
					}

					int effLv = Math.Clamp(liveCharm.limitedEffectEnabledLevel, 0, item.MaxLevel);
					if (liveCharm is Charm_StatusInstance || liveCharm is Charm_CriticalChanceIncreaseWithTablets) {
						activeDelta = activeDelta.Add(item.GetStatDeltaAtLevel(effLv));
					} else if (liveCharm is Charm_BurnWeaponDamage && BurnWeaponAddedField != null) {
						object? rawAdded = BurnWeaponAddedField.GetValue(liveCharm);
						if (rawAdded is int burnAdded && burnAdded != 0) {
							activeDelta = activeDelta.Add(new CombatStatDelta(finalWeaponDamage: burnAdded));
						}
					}

					if (item.IsElementalByRowCharm && item.RowCategoryIds.Length > 0) {
						int rowIdx = yCoord % item.RowCategoryIds.Length;
						int catId = item.RowCategoryIds[rowIdx];
						byte elemKind = (catId >= 0 && catId < comboCategories.Length)
							? comboCategories[catId].CategoryElementalKind
							: (byte)0;
						activeDelta = activeDelta.AddElementalByKind(elemKind, item.GetRowElementalBonus(effLv));
					}
				}

				int liveActiveComboTypes = 0;
				for (int c = 0; c < comboCategories.Length; c++) {
					ComboCategoryInfo cat = comboCategories[c];
					int liveCount = Math.Max(cat.StaticCount, inventory.GetSetEffectCount(cat.CategoryKey));
					activeDelta = activeDelta.Add(cat.EvaluateStatDelta(liveCount, unlimitedComboStat));
					if (cat.Breakpoints.Length > 0 && liveCount >= cat.Breakpoints[0]) {
						liveActiveComboTypes++;
					}
				}

				if (comboBonusDamageStat > 0 && liveActiveComboTypes > 0) {
					activeDelta = activeDelta.Add(
						new CombatStatDelta(allDamageBonus: comboBonusDamageStat * liveActiveComboTypes)
					);
				}

				WeaponCombatProfile deductedProfile = rawBaseline.WeaponProfile.WithDeductedInventoryDelta(
					in activeDelta
				);
				return new PlayerCombatBaseline(in deductedProfile);
			}

			double activeInventoryStatPct = 0.0;
			for (int i = 0; i < items.Length; i++) {
				SlotItemSnapshot item = items[i];
				if (item.Kind != SlotItemKind.Artifact || item.PriorityTier == ArtifactPriorityTier.Sacrifice) {
					continue;
				}
				sbyte xCoord = (sbyte)(item.InitialSlotIndex % Math.Max(1, width));
				sbyte yCoord = (sbyte)(item.InitialSlotIndex / Math.Max(1, width));
				NewItemOwnInstance? inst = inventory.FindItem(xCoord, yCoord);
				Charm_Basic? liveCharm = inst != null ? inst.Charm : null;
				if (liveCharm == null && inventory.charms != null) {
					inventory.charms.TryGetValue(new ItemPosition(xCoord, yCoord), out liveCharm);
				}
				if (liveCharm == null || !liveCharm.IsEffectEnabled) {
					continue;
				}
				int effLv = Math.Clamp(liveCharm.limitedEffectEnabledLevel, 0, item.MaxLevel);
				activeInventoryStatPct += item.GetStatBonusPercentAtLevel(effLv);
				if (item.IsElementalByRowCharm) {
					activeInventoryStatPct += item.GetRowElementalBonus(effLv);
				}
			}

			for (int c = 0; c < comboCategories.Length; c++) {
				ComboCategoryInfo cat = comboCategories[c];
				activeInventoryStatPct += cat.EvaluateStatBonusPercent(cat.StaticCount, unlimitedComboStat);
			}

			float unbuffedStatPercent = (float)Math.Max(
				0.0,
				rawBaseline.ElementalDamageBonusPercent - activeInventoryStatPct
			);
			return new PlayerCombatBaseline(
				rawBaseline.BaseAttackDamage,
				rawBaseline.AttacksPerSecond,
				rawBaseline.CritChancePercent,
				rawBaseline.CritDamageMultiplier,
				unbuffedStatPercent
			);
		}

		private static void ExtractCharmCombatTables(
			PlayerAvatar? avatar,
			ItemEntity entity,
			Charm_Basic? charm,
			bool isAttackable,
			bool hasDependency,
			bool isNearLevelDamage,
			bool isElementalByRow,
			int maxLevel,
			ArtifactPriorityTier priorityTier,
			float playerAttacksPerSecond,
			int currentTabletCount,
			out float[]? attackDamageByLevel,
			out float baseProcsPerSecond,
			out float[]? statBonusPercentByLevel,
			out float[]? finalDamagePercentByLevel,
			out CombatStatDelta[]? statDeltaByLevel,
			out byte charmScalingElementKind,
			out float[]? charmElementalPercentByLevel
		) {
			attackDamageByLevel = null;
			baseProcsPerSecond = 1f;
			statBonusPercentByLevel = null;
			finalDamagePercentByLevel = null;
			statDeltaByLevel = null;
			charmScalingElementKind = 0;
			charmElementalPercentByLevel = null;

			if (charm == null || priorityTier == ArtifactPriorityTier.Sacrifice) {
				return;
			}

			int levelCount = Math.Max(1, maxLevel + 1);

			if (charm is Charm_StatusInstance statusCharm
				&& statusCharm.stats != null
				&& statusCharm.stats.Length > 0) {
				float[] statTable = new float[levelCount];
				float[] finalDmgTable = new float[levelCount];
				CombatStatDelta[] deltaTable = new CombatStatDelta[levelCount];
				bool hasAnyStat = false;
				bool hasAnyFinal = false;

				for (int g = 0; g < statusCharm.stats.Length; g++) {
					Charm_StatusInstance.StatusGroup group = statusCharm.stats[g];
					if (group == null
						|| string.IsNullOrEmpty(group.statusID)
						|| group.valuesByLevel == null
						|| group.valuesByLevel.Length == 0) {
						continue;
					}
					for (int lv = 0; lv < levelCount; lv++) {
						int valIdx = Math.Min(lv, group.valuesByLevel.Length - 1);
						int rawVal = group.valuesByLevel[valIdx];
						CombatStatDelta delta = ConvertStatusIdToStatDelta(
							group.statusID,
							rawVal,
							out float pct,
							out bool isFinalAmp
						);
						if (delta.HasAnyNonZero) {
							deltaTable[lv] = deltaTable[lv].Add(in delta);
							hasAnyStat = true;
						}
						if (pct > 0f) {
							if (isFinalAmp) {
								finalDmgTable[lv] += pct;
								hasAnyFinal = true;
							} else {
								statTable[lv] += pct;
								hasAnyStat = true;
							}
						}
					}
				}

				statBonusPercentByLevel = hasAnyStat ? statTable : new float[levelCount];
				statDeltaByLevel = deltaTable;
				if (hasAnyFinal) {
					finalDamagePercentByLevel = finalDmgTable;
				}
			}

			Type charmType = charm.GetType();
			FieldInfo[] fields = charmType.GetFields(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
			);

			if (isAttackable) {
				baseProcsPerSecond = ExtractEffectiveProcsPerSecond(
					avatar,
					charm,
					fields,
					levelCount,
					playerAttacksPerSecond
				);
				ExtractAttackCharmDamageTable(
					avatar,
					entity,
					charm,
					fields,
					levelCount,
					out attackDamageByLevel,
					out charmScalingElementKind,
					out charmElementalPercentByLevel
				);
				return;
			}

			if (!hasDependency
				&& !isNearLevelDamage
				&& !isElementalByRow
				&& statBonusPercentByLevel == null) {
				ExtractReflectionStatBonusTable(
					charm,
					fields,
					levelCount,
					currentTabletCount,
					out float[] extractedStats,
					out CombatStatDelta[] extractedDeltas
				);
				statBonusPercentByLevel = extractedStats;
				statDeltaByLevel = extractedDeltas;
			}
		}

		private static float ExtractEffectiveProcsPerSecond(
			PlayerAvatar? avatar,
			Charm_Basic charm,
			FieldInfo[] fields,
			int levelCount,
			float playerAttacksPerSecond
		) {
			ChargingCharm? chargingCharm = charm.GetComponent<ChargingCharm>();
			if (chargingCharm != null && chargingCharm.defaultChargeTimer > 0.05f) {
				int chargeSpeedBonus = avatar != null
					? avatar.GetCustomStatUnsafe("CHARGINGCHARMBONUS")
					: 0;
				float speedMult = Math.Max(0.25f, 1f + (chargeSpeedBonus * 0.01f));
				return Math.Clamp(speedMult / chargingCharm.defaultChargeTimer, 0.1f, 6f);
			}

			if (charm is Charm_Magic magicCharm && magicCharm.ContainedMagic != null) {
				float magicCd = Math.Max(0.5f, magicCharm.ContainedMagic.cooldownTime);
				int cdSpeed = avatar != null ? avatar.GetCustomStat(ECustomStat.CooldownRecoverySpeed) : 0;
				float cdMult = Math.Max(0.25f, 1f + (cdSpeed * 0.01f));
				return Math.Clamp(cdMult / magicCd, 0.1f, 4f);
			}

			if (charm is Charm_DashDamage) {
				return 0.5f;
			}

			if (charm is Charm_FireBulletOnHit || charm is Charm_VenomSporePouch) {
				return 0.15f;
			}

			float detectedIcd = 0f;
			float detectedSearchDelay = 0f;
			float procChancePct = 0f;

			for (int i = 0; i < fields.Length; i++) {
				FieldInfo field = fields[i];
				string name = field.Name;
				Type fType = field.FieldType;

				if (fType == typeof(Timer)) {
					object? timerObj = field.GetValue(charm);
					if (timerObj is Timer timer && timer.time > 0.02f && timer.time <= 30f) {
						if (name.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0) {
							detectedSearchDelay = timer.time;
						} else if (detectedIcd <= 0f || timer.time < detectedIcd) {
							detectedIcd = timer.time;
						}
					}
					continue;
				}

				if (fType == typeof(float) || fType == typeof(int)) {
					object? rawVal = field.GetValue(charm);
					float val = rawVal is float fv ? fv : (rawVal is int iv ? iv : 0f);
					if (val <= 0f) {
						continue;
					}
					if (name.IndexOf("cooldown", StringComparison.OrdinalIgnoreCase) >= 0
						|| name.IndexOf("interval", StringComparison.OrdinalIgnoreCase) >= 0
						|| name.IndexOf("delay", StringComparison.OrdinalIgnoreCase) >= 0) {
						if (val >= 0.05f && val <= 30f && detectedIcd <= 0f) {
							detectedIcd = val;
						}
					} else if (name.IndexOf("chance", StringComparison.OrdinalIgnoreCase) >= 0
						&& val > 0f
						&& val <= 100f
						&& procChancePct <= 0f) {
						procChancePct = val;
					}
					continue;
				}

				if ((fType == typeof(float[]) || fType == typeof(int[]))
					&& name.IndexOf("chance", StringComparison.OrdinalIgnoreCase) >= 0) {
					float[]? chanceArr = ConvertNumericArrayToFloat(field.GetValue(charm));
					if (chanceArr != null && chanceArr.Length > 0) {
						int midIdx = Math.Clamp(levelCount / 2, 0, chanceArr.Length - 1);
						if (chanceArr[midIdx] > 0f && chanceArr[midIdx] <= 100f) {
							procChancePct = chanceArr[midIdx];
						}
					}
				}
			}

			float totalTimerCycle = detectedIcd + detectedSearchDelay;
			if (procChancePct > 0f && procChancePct < 100f) {
				float effectiveTriggerRate = Math.Max(0.15f, playerAttacksPerSecond * (procChancePct * 0.01f));
				float expectedCycle = totalTimerCycle + (1f / effectiveTriggerRate);
				return Math.Clamp(1f / Math.Max(0.15f, expectedCycle), 0.1f, 6f);
			}

			if (totalTimerCycle > 0.05f) {
				return Math.Clamp(1f / totalTimerCycle, 0.15f, 6f);
			}

			return Math.Clamp(playerAttacksPerSecond * 0.5f, 0.5f, 3.0f);
		}

		private static void ExtractAttackCharmDamageTable(
			PlayerAvatar? avatar,
			ItemEntity entity,
			Charm_Basic charm,
			FieldInfo[] fields,
			int levelCount,
			out float[] flatResult,
			out byte scalingElementKind,
			out float[]? elemPercentResult
		) {
			float[]? flatDamageTable = null;
			float[]? percentDamageTable = null;
			float[]? projectileCountTable = null;
			float scalarBaseDamage = 0f;
			scalingElementKind = InferCharmElementKind(avatar, entity, charm, fields);

			for (int i = 0; i < fields.Length; i++) {
				FieldInfo field = fields[i];
				string name = field.Name;
				Type fType = field.FieldType;

				if (IsExcludedNonAttackFieldName(name)) {
					continue;
				}

				if (fType == typeof(float) || fType == typeof(int)) {
					if (string.Equals(name, "defaultDamage", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(name, "bulletDamage", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(name, "baseDamage", StringComparison.OrdinalIgnoreCase)
						|| string.Equals(name, "damage", StringComparison.OrdinalIgnoreCase)) {
						object? rawScalar = field.GetValue(charm);
						float val = rawScalar is float fv ? fv : (rawScalar is int iv ? iv : 0f);
						if (val > 0f && scalarBaseDamage <= 0f) {
							scalarBaseDamage = val;
						}
					}
					continue;
				}

				if (fType != typeof(int[]) && fType != typeof(float[])) {
					continue;
				}

				float[]? arr = ConvertNumericArrayToFloat(field.GetValue(charm));
				if (arr == null || arr.Length == 0) {
					continue;
				}

				if (name.IndexOf("count", StringComparison.OrdinalIgnoreCase) >= 0) {
					if (projectileCountTable == null
						&& (name.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0
							|| name.IndexOf("bullet", StringComparison.OrdinalIgnoreCase) >= 0
							|| name.IndexOf("shot", StringComparison.OrdinalIgnoreCase) >= 0
							|| string.Equals(name, "countByLevel", StringComparison.OrdinalIgnoreCase))) {
						projectileCountTable = NormalizeTableToLength(arr, levelCount);
					}
					continue;
				}

				if (name.IndexOf("percent", StringComparison.OrdinalIgnoreCase) >= 0) {
					if (percentDamageTable == null
						&& (name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0
							|| name.IndexOf("stat", StringComparison.OrdinalIgnoreCase) >= 0)) {
						percentDamageTable = NormalizeTableToLength(arr, levelCount);
					}
					continue;
				}

				if (name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0
					|| name.IndexOf("dmg", StringComparison.OrdinalIgnoreCase) >= 0) {
					if (flatDamageTable == null) {
						flatDamageTable = NormalizeTableToLength(arr, levelCount);
					}
				}
			}

			flatResult = new float[levelCount];
			elemPercentResult = null;
			bool populated = false;

			if (flatDamageTable != null && percentDamageTable != null) {
				elemPercentResult = new float[levelCount];
				for (int lv = 0; lv < levelCount; lv++) {
					flatResult[lv] = Math.Max(0f, flatDamageTable[lv]);
					elemPercentResult[lv] = Math.Max(0f, percentDamageTable[lv]);
				}
				populated = true;
			} else if (flatDamageTable != null) {
				for (int lv = 0; lv < levelCount; lv++) {
					flatResult[lv] = Math.Max(0f, flatDamageTable[lv]);
				}
				populated = true;
			} else if (percentDamageTable != null) {
				elemPercentResult = new float[levelCount];
				for (int lv = 0; lv < levelCount; lv++) {
					flatResult[lv] = Math.Max(0f, scalarBaseDamage);
					elemPercentResult[lv] = Math.Max(0f, percentDamageTable[lv]);
				}
				populated = true;
			} else if (scalarBaseDamage > 0f) {
				for (int lv = 0; lv < levelCount; lv++) {
					flatResult[lv] = scalarBaseDamage;
				}
				populated = true;
			}

			if (avatar != null
				&& charm is IAttackableCharm attackableCharm
				&& !(charm is Charm_Magic)) {
				try {
					float liveSingleHitDmg = attackableCharm.GetDamage(avatar);
					if (liveSingleHitDmg > 0.5f) {
						int curLv = Math.Clamp(charm.limitedEffectEnabledLevel, 0, levelCount - 1);
						float liveElemStat = ReadAvatarElementalStat(avatar, scalingElementKind);
						if (populated) {
							float elemAtCur = elemPercentResult != null
								? (liveElemStat * (elemPercentResult[curLv] * 0.01f))
								: 0f;
							float estAtCur = flatResult[curLv] + elemAtCur;
							if (estAtCur > 0.5f) {
								float relicScale = Math.Clamp(liveSingleHitDmg / estAtCur, 0.25f, 5.0f);
								for (int lv = 0; lv < levelCount; lv++) {
									flatResult[lv] *= relicScale;
									if (elemPercentResult != null) {
										elemPercentResult[lv] *= relicScale;
									}
								}
							}
						} else {
							float baseScale = liveSingleHitDmg / (1f + (curLv * 0.35f));
							for (int lv = 0; lv < levelCount; lv++) {
								flatResult[lv] = baseScale * (1f + (lv * 0.35f));
							}
							populated = true;
						}
					}
				} catch {
					// Keep field-extracted tables if GetDamage requires runtime state not yet initialized.
				}
			}

			if (populated && projectileCountTable != null) {
				for (int lv = 0; lv < levelCount; lv++) {
					int count = Math.Clamp((int)Math.Round(projectileCountTable[lv]), 1, 16);
					flatResult[lv] *= count;
					if (elemPercentResult != null) {
						elemPercentResult[lv] *= count;
					}
				}
			}

			if (!populated) {
				for (int lv = 0; lv < levelCount; lv++) {
					flatResult[lv] = 20f + (lv * 10f);
				}
			}
		}

		private static byte InferCharmElementKind(
			PlayerAvatar? avatar,
			ItemEntity entity,
			Charm_Basic charm,
			FieldInfo[] fields
		) {
			if (charm is Charm_AirSlash) {
				bool useFlame = avatar != null && avatar.GetCustomStatUnsafe("FROSTRELICFLAME") > 0;
				return useFlame ? (byte)2 : (byte)3;
			}

			for (int i = 0; i < fields.Length; i++) {
				FieldInfo field = fields[i];
				if (field.FieldType == typeof(ECustomStat)) {
					object? rawStat = field.GetValue(charm);
					if (rawStat is ECustomStat customStat) {
						switch (customStat) {
							case ECustomStat.PhysicalDamage: {
								return 1;
							}
							case ECustomStat.FireDamage: {
								return 2;
							}
							case ECustomStat.IceDamage: {
								return 3;
							}
							case ECustomStat.LightningDamage: {
								return 4;
							}
						}
					}
				} else if (field.FieldType == typeof(EDamageElementalType)) {
					object? rawElem = field.GetValue(charm);
					if (rawElem is EDamageElementalType elemType) {
						switch (elemType) {
							case EDamageElementalType.Physical: {
								return 1;
							}
							case EDamageElementalType.Fire: {
								return 2;
							}
							case EDamageElementalType.Ice: {
								return 3;
							}
							case EDamageElementalType.Lightning: {
								return 4;
							}
						}
					}
				}
			}

			if (entity.categories != null) {
				for (int c = 0; c < entity.categories.Count; c++) {
					byte catElem = ComboCategoryInfo.ResolveCategoryElementalKind(entity.categories[c]);
					if (catElem != 0) {
						return catElem;
					}
				}
			}
			return 0;
		}

		private static float ReadAvatarElementalStat(PlayerAvatar avatar, byte elemKind) {
			int phys = avatar.GetCustomStat(ECustomStat.PhysicalDamage);
			int fire = avatar.GetCustomStat(ECustomStat.FireDamage);
			int ice = avatar.GetCustomStat(ECustomStat.IceDamage);
			int light = avatar.GetCustomStat(ECustomStat.LightningDamage);
			return elemKind switch {
				1 => Math.Max(1, phys),
				2 => Math.Max(1, fire),
				3 => Math.Max(1, ice),
				4 => Math.Max(1, light),
				_ => Math.Max(1, Math.Max(Math.Max(phys, fire), Math.Max(ice, light)))
			};
		}

		private static void ExtractReflectionStatBonusTable(
			Charm_Basic charm,
			FieldInfo[] fields,
			int levelCount,
			int currentTabletCount,
			out float[] statResult,
			out CombatStatDelta[] deltaResult
		) {
			statResult = new float[levelCount];
			deltaResult = new CombatStatDelta[levelCount];

			if (charm is Charm_BurnWeaponDamage burnWeapon && burnWeapon.addDamageByStackedBurn != null) {
				float[]? burnArr = ConvertNumericArrayToFloat(burnWeapon.addDamageByStackedBurn);
				if (burnArr != null && burnArr.Length > 0) {
					float[] norm = NormalizeTableToLength(burnArr, levelCount);
					for (int lv = 0; lv < levelCount; lv++) {
						float expectedFinalBonus = Math.Max(0f, norm[lv] * 3f);
						statResult[lv] = expectedFinalBonus;
						deltaResult[lv] = new CombatStatDelta(finalWeaponDamage: expectedFinalBonus);
					}
					return;
				}
			}

			if (charm is Charm_CriticalChanceIncreaseWithTablets tabletCritCharm
				&& tabletCritCharm.criticalBonusByLevel != null) {
				float[]? critArr = ConvertNumericArrayToFloat(tabletCritCharm.criticalBonusByLevel);
				if (critArr != null && critArr.Length > 0) {
					float[] norm = NormalizeTableToLength(critArr, levelCount);
					int tablets = Math.Max(0, currentTabletCount);
					for (int lv = 0; lv < levelCount; lv++) {
						float critPct = Math.Max(0f, (norm[lv] * tablets) / 100f);
						statResult[lv] = critPct;
						deltaResult[lv] = new CombatStatDelta(critChancePercent: critPct);
					}
					return;
				}
			}

			for (int i = 0; i < fields.Length; i++) {
				FieldInfo field = fields[i];
				string name = field.Name;
				Type fType = field.FieldType;
				if ((fType != typeof(int[]) && fType != typeof(float[]))
					|| IsExcludedNonAttackFieldName(name)) {
					continue;
				}
				if (name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0
					|| name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0
					|| name.IndexOf("crit", StringComparison.OrdinalIgnoreCase) >= 0
					|| name.IndexOf("speed", StringComparison.OrdinalIgnoreCase) >= 0
					|| name.IndexOf("bonus", StringComparison.OrdinalIgnoreCase) >= 0) {
					float[]? arr = ConvertNumericArrayToFloat(field.GetValue(charm));
					if (arr == null || arr.Length == 0) {
						continue;
					}
					float[] norm = NormalizeTableToLength(arr, levelCount);
					bool isCritChance = name.IndexOf("crit", StringComparison.OrdinalIgnoreCase) >= 0
						&& name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) < 0;
					bool isCritBasisPoints = isCritChance && norm[0] >= 50f;
					for (int lv = 0; lv < levelCount; lv++) {
						float val = Math.Max(0f, norm[lv]);
						float scaledVal = isCritBasisPoints ? (val * 0.01f) : val;
						statResult[lv] += scaledVal;
						CombatStatDelta fieldDelta = MapReflectedFieldToDelta(name, scaledVal, isCritChance);
						deltaResult[lv] = deltaResult[lv].Add(in fieldDelta);
					}
				}
			}
		}

		private static CombatStatDelta MapReflectedFieldToDelta(
			string fieldName,
			float scaledVal,
			bool isCritChance
		) {
			if (scaledVal <= 0f) {
				return CombatStatDelta.Zero;
			}
			if (isCritChance) {
				return new CombatStatDelta(critChancePercent: scaledVal);
			}
			if (fieldName.IndexOf("crit", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(critDamageBonus: scaledVal);
			}
			if (fieldName.IndexOf("speed", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(attackSpeed: scaledVal);
			}
			if (fieldName.IndexOf("physical", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(physicalDamage: scaledVal);
			}
			if (fieldName.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(fireDamage: scaledVal);
			}
			if (fieldName.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("frost", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(iceDamage: scaledVal);
			}
			if (fieldName.IndexOf("lightning", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("electric", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(lightningDamage: scaledVal);
			}
			if (fieldName.IndexOf("finalWeapon", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(finalWeaponDamage: scaledVal);
			}
			if (fieldName.IndexOf("basicAttack", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(basicAttackDamageBonus: scaledVal);
			}
			if (fieldName.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(weaponDamageBonus: scaledVal);
			}
			if (fieldName.IndexOf("charm", StringComparison.OrdinalIgnoreCase) >= 0) {
				return new CombatStatDelta(charmDamageBonus: scaledVal);
			}
			return new CombatStatDelta(allDamageBonus: scaledVal);
		}

		private static bool IsExcludedNonAttackFieldName(string fieldName) {
			return fieldName.IndexOf("cut", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("reduce", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("cost", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("taken", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("defense", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("threshold", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("dependency", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("allDamageBonus", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("addElementalStat", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("debuff", StringComparison.OrdinalIgnoreCase) >= 0
				|| fieldName.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static CombatStatDelta ConvertStatusIdToStatDelta(
			string statusId,
			int rawValue,
			out float legacyOffensivePercent,
			out bool isFinalDamageAmp
		) {
			legacyOffensivePercent = 0f;
			isFinalDamageAmp = false;
			if (rawValue == 0 || string.IsNullOrEmpty(statusId)) {
				return CombatStatDelta.Zero;
			}

			string id = statusId.Trim().ToUpperInvariant();
			if (id == "FINAL_DAMAGE" || id == "ALL_DAMAGE_AMP") {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
					isFinalDamageAmp = true;
				}
				return new CombatStatDelta(allDamageBonus: rawValue);
			}
			if (id == "ALLDAMAGEBONUS" || id.IndexOf("ALL_DAMAGE", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
					isFinalDamageAmp = id == "ALLDAMAGEBONUS";
				}
				return new CombatStatDelta(allDamageBonus: rawValue);
			}
			if (id == "CRITICAL"
				|| id == "CRIT"
				|| id == "CRITICALCHANCE"
				|| id == "WEAPONCRITICAL") {
				float critPct = rawValue / 100f;
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue >= 50 ? critPct : rawValue;
				}
				return new CombatStatDelta(critChancePercent: critPct);
			}
			if (id.IndexOf("PHYSICALDAMAGE", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(physicalDamage: rawValue);
			}
			if (id.IndexOf("FIREDAMAGE", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(fireDamage: rawValue);
			}
			if (id.IndexOf("ICEDAMAGE", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(iceDamage: rawValue);
			}
			if (id.IndexOf("LIGHTNINGDAMAGE", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(lightningDamage: rawValue);
			}
			if (id == "FINALWEAPONDAMAGE") {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(finalWeaponDamage: rawValue);
			}
			if (id.IndexOf("BASICATTACK", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(basicAttackDamageBonus: rawValue);
			}
			if (id.IndexOf("WEAPONDAMAGE", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(weaponDamageBonus: rawValue);
			}
			if (id.IndexOf("CHARMDAMAGE", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(charmDamageBonus: rawValue);
			}
			if (id.IndexOf("ATTACKSPEED", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(attackSpeed: rawValue);
			}
			if (id.IndexOf("CRITICALDAMAGE", StringComparison.Ordinal) >= 0) {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(critDamageBonus: rawValue);
			}
			if (id.IndexOf("MAGICDAMAGE", StringComparison.Ordinal) >= 0
				|| id.IndexOf("SKILLDAMAGE", StringComparison.Ordinal) >= 0
				|| id == "AP") {
				if (rawValue > 0) {
					legacyOffensivePercent = rawValue;
				}
				return new CombatStatDelta(allDamageBonus: rawValue * 0.5f);
			}
			return CombatStatDelta.Zero;
		}

		private static CombatStatDelta ParseComboStatusArrayToStatDelta(
			string[]? statusEntries,
			out float legacyPercentSum
		) {
			legacyPercentSum = 0f;
			if (statusEntries == null || statusEntries.Length == 0) {
				return CombatStatDelta.Zero;
			}
			CombatStatDelta total = CombatStatDelta.Zero;
			for (int i = 0; i < statusEntries.Length; i++) {
				CombatStatDelta single = ParseSingleComboStatusToStatDelta(statusEntries[i], out float pct);
				total = total.Add(in single);
				legacyPercentSum += pct;
			}
			return total;
		}

		private static CombatStatDelta ParseSingleComboStatusToStatDelta(
			string? statusEntry,
			out float legacyPercent
		) {
			legacyPercent = 0f;
			if (string.IsNullOrWhiteSpace(statusEntry)) {
				return CombatStatDelta.Zero;
			}
			string[] parts = statusEntry.Trim().Split('/');
			if (parts.Length < 2) {
				return CombatStatDelta.Zero;
			}
			if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val)) {
				return CombatStatDelta.Zero;
			}
			return ConvertStatusIdToStatDelta(parts[0], val, out legacyPercent, out _);
		}

		private static float[]? ConvertNumericArrayToFloat(object? rawArray) {
			if (rawArray is float[] floatArr && floatArr.Length > 0) {
				float[] copy = new float[floatArr.Length];
				Array.Copy(floatArr, copy, floatArr.Length);
				return copy;
			}
			if (rawArray is int[] intArr && intArr.Length > 0) {
				float[] copy = new float[intArr.Length];
				for (int i = 0; i < intArr.Length; i++) {
					copy[i] = intArr[i];
				}
				return copy;
			}
			return null;
		}

		private static float[] NormalizeTableToLength(float[] source, int targetLength) {
			float[] result = new float[targetLength];
			for (int i = 0; i < targetLength; i++) {
				int srcIdx = Math.Min(i, source.Length - 1);
				result[i] = source[srcIdx];
			}
			return result;
		}

		private readonly struct RawQueryMetadata {
			public sbyte X { get; }
			public sbyte Y { get; }
			public string Value { get; }

			public RawQueryMetadata(int x, int y, string value) {
				X = (sbyte)Math.Clamp(x, sbyte.MinValue, sbyte.MaxValue);
				Y = (sbyte)Math.Clamp(y, sbyte.MinValue, sbyte.MaxValue);
				Value = value;
			}
		}

		public static TabletPlacementRule[] BuildTabletPlacementRules(
			string conditionQuery,
			string effectQuery,
			int width,
			int height,
			int unlockedSlots
		) {
			int effectiveWidth = Math.Max(1, width);
			int effectiveHeight = unlockedSlots > 0
				? ((unlockedSlots + effectiveWidth - 1) / effectiveWidth)
				: Math.Max(1, height);

			TabletPlacementRule[] rules = new TabletPlacementRule[unlockedSlots * 4];
			for (int slotIdx = 0; slotIdx < unlockedSlots; slotIdx++) {
				int originX = slotIdx % effectiveWidth;
				int originY = slotIdx / effectiveWidth;

				for (int rot = 0; rot < 4; rot++) {
					List<RawQueryMetadata> rawConditions = ParseTabletQueryMetadata(
						conditionQuery,
						effectiveWidth,
						effectiveHeight,
						unlockedSlots,
						originX,
						originY,
						rot
					);

					PrecomputedCondition[] conditions = new PrecomputedCondition[rawConditions.Count];
					bool hasPlacedCondition = false;
					bool placedHit = false;
					for (int c = 0; c < rawConditions.Count; c++) {
						RawQueryMetadata rawCond = rawConditions[c];
						TabletConditionType condType = ParseConditionValue(rawCond.Value);
						if (condType == TabletConditionType.Placed) {
							hasPlacedCondition = true;
							if (rawCond.X == originX && rawCond.Y == originY) {
								placedHit = true;
							}
						}
						int targetSlot = CoordToUnlockedSlot(
							rawCond.X,
							rawCond.Y,
							effectiveWidth,
							effectiveHeight,
							unlockedSlots
						);
						conditions[c] = new PrecomputedCondition(
							rawCond.X,
							rawCond.Y,
							targetSlot,
							condType
						);
					}

					if (hasPlacedCondition && !placedHit) {
						rules[(slotIdx * 4) + rot] = new TabletPlacementRule(
							conditions,
							Array.Empty<PrecomputedEffect>()
						);
						continue;
					}

					List<RawQueryMetadata> rawEffects = ParseTabletQueryMetadata(
						effectQuery,
						effectiveWidth,
						effectiveHeight,
						unlockedSlots,
						originX,
						originY,
						rot
					);

					PrecomputedEffect[] effects = new PrecomputedEffect[rawEffects.Count];
					for (int e = 0; e < rawEffects.Count; e++) {
						RawQueryMetadata rawEff = rawEffects[e];
						int targetSlot = CoordToUnlockedSlot(
							rawEff.X,
							rawEff.Y,
							effectiveWidth,
							effectiveHeight,
							unlockedSlots
						);
						(TabletEffectType effType, int levelParam) = ParseEffectValue(rawEff.Value);
						effects[e] = new PrecomputedEffect(
							rawEff.X,
							rawEff.Y,
							targetSlot,
							effType,
							levelParam
						);
					}

					rules[(slotIdx * 4) + rot] = new TabletPlacementRule(conditions, effects);
				}
			}
			return rules;
		}

		private static List<RawQueryMetadata> ParseTabletQueryMetadata(
			string query,
			int width,
			int height,
			int storage,
			int originX,
			int originY,
			int rotation
		) {
			if (string.IsNullOrWhiteSpace(query)) {
				return new List<RawQueryMetadata>(0);
			}

			int rot = ((rotation % 4) + 4) % 4;
			List<RawQueryMetadata> list = new List<RawQueryMetadata>();
			string[] lines = query.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

			for (int i = 0; i < lines.Length; i++) {
				string line = lines[i].Trim();
				if (line.Length == 0) {
					continue;
				}

				if (line.StartsWith("Placed(", StringComparison.OrdinalIgnoreCase) && line.EndsWith(")")) {
					string area = line.Substring(7, line.Length - 8).Trim().ToUpperInvariant();
					AppendKeywordPositions(list, area, "PLACED", width, height, storage, originX, originY, rot);
					continue;
				}

				int colonIdx = line.IndexOf(':');
				if (colonIdx > 0) {
					string coordPart = line.Substring(0, colonIdx).Trim();
					string valPart = line.Substring(colonIdx + 1).Trim();
					string[] xy = coordPart.Split(',');
					if (xy.Length == 2
						&& int.TryParse(xy[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dx)
						&& int.TryParse(xy[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dy)) {
						(int rdx, int rdy) = RotateRelativeOffset(dx, dy, rot);
						list.Add(new RawQueryMetadata(originX + rdx, originY + rdy, valPart));
						continue;
					}
				}

				string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (tokens.Length < 2) {
					continue;
				}

				string keyword = tokens[0].ToUpperInvariant();
				if (keyword == "IDX" && tokens.Length >= 3) {
					if (int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)) {
						list.Add(new RawQueryMetadata(idx % width, idx / width, tokens[2]));
					}
					continue;
				}
				if (keyword == "RIDX" && tokens.Length >= 3) {
					if (int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ridx)) {
						int actualIdx = storage - 1 - ridx;
						list.Add(new RawQueryMetadata(actualIdx % width, actualIdx / width, tokens[2]));
					}
					continue;
				}

				AppendKeywordPositions(list, keyword, tokens[1], width, height, storage, originX, originY, rot);
			}

			return list;
		}

		private static void AppendKeywordPositions(
			List<RawQueryMetadata> list,
			string keyword,
			string val,
			int width,
			int height,
			int storage,
			int originX,
			int originY,
			int rot
		) {
			if (TryGetBaseOffsetForKeyword(keyword, out int baseDx, out int baseDy)) {
				(int rdx, int rdy) = RotateRelativeOffset(baseDx, baseDy, rot);
				list.Add(new RawQueryMetadata(originX + rdx, originY + rdy, val));
				return;
			}

			switch (keyword) {
				case "O": {
					list.Add(new RawQueryMetadata(originX, originY, val));
					break;
				}
				case "HORIZONTAL": {
					if ((rot & 1) == 0) {
						for (int x = 0; x < width; x++) {
							if (x != originX) {
								list.Add(new RawQueryMetadata(x, originY, val));
							}
						}
					} else {
						for (int y = 0; y < height; y++) {
							if (y != originY) {
								list.Add(new RawQueryMetadata(originX, y, val));
							}
						}
					}
					break;
				}
				case "VERTICAL": {
					if ((rot & 1) == 0) {
						for (int y = 0; y < height; y++) {
							if (y != originY) {
								list.Add(new RawQueryMetadata(originX, y, val));
							}
						}
					} else {
						for (int x = 0; x < width; x++) {
							if (x != originX) {
								list.Add(new RawQueryMetadata(x, originY, val));
							}
						}
					}
					break;
				}
				case "X_PLUS":
				case "X_MINUS":
				case "Y_PLUS":
				case "Y_MINUS": {
					AppendRotatedRayPositions(list, keyword, val, width, height, originX, originY, rot);
					break;
				}
				case "TOP":
				case "BOTTOM":
				case "LEFTEND":
				case "RIGHTEND": {
					AppendRotatedEdgePositions(list, keyword, val, width, height, storage, rot);
					break;
				}
				case "RIGHT_RISING":
				case "RIGHT_FALLING":
				case "LEFT_RISING":
				case "LEFT_FALLING": {
					AppendRotatedDiagonalPositions(list, keyword, val, width, height, originX, originY, rot);
					break;
				}
				case "CHECKERBOARD": {
					for (int x = 0; x < width; x++) {
						for (int y = 0; y < height; y++) {
							if ((x != originX || y != originY)
								&& ((y * width) + x) <= storage - 1
								&& ((x + y + originX + originY) % 2 == 0)) {
								list.Add(new RawQueryMetadata(x, y, val));
							}
						}
					}
					break;
				}
				case "CHECKERBOARD2": {
					for (int x = 0; x < width; x++) {
						for (int y = 0; y < height; y++) {
							if ((x != originX || y != originY)
								&& ((y * width) + x) <= storage - 1
								&& ((x + y + originX + originY) % 2 == 1)) {
								list.Add(new RawQueryMetadata(x, y, val));
							}
						}
					}
					break;
				}
				case "BORDER":
				case "OUTLINED": {
					for (int s = 0; s < storage; s++) {
						int x = s % width;
						int y = s / width;
						if (x <= 0 || y <= 0 || x >= width - 1 || s >= storage - width) {
							list.Add(new RawQueryMetadata(x, y, val));
						}
					}
					break;
				}
				case "INNER":
				case "INSIDE": {
					for (int s = 0; s < storage; s++) {
						int x = s % width;
						int y = s / width;
						if (x > 0 && y > 0 && x < width - 1 && (s + width + 1) <= (storage - 1)) {
							list.Add(new RawQueryMetadata(x, y, val));
						}
					}
					break;
				}
				case "CORNER": {
					for (int s = 0; s < storage; s++) {
						int x = s % width;
						int y = s / width;
						bool isTopLeft = x == 0 && y == 0;
						bool isTopRight = x == width - 1 && y == 0;
						bool isBottomLeft = x == 0 && (s + width) >= storage;
						bool isBottomRight = s == storage - 1 || (x == width - 1 && (s + width) >= storage);
						if (isTopLeft || isTopRight || isBottomLeft || isBottomRight) {
							list.Add(new RawQueryMetadata(x, y, val));
						}
					}
					break;
				}
			}
		}

		private static bool TryGetBaseOffsetForKeyword(string keyword, out int dx, out int dy) {
			switch (keyword) {
				case "LEFT": dx = -1; dy = 0; return true;
				case "LEFTLEFT": dx = -2; dy = 0; return true;
				case "LEFTLEFTLEFT": dx = -3; dy = 0; return true;
				case "LEFTLEFTLEFTLEFT": dx = -4; dy = 0; return true;
				case "RIGHT": dx = 1; dy = 0; return true;
				case "RIGHTRIGHT": dx = 2; dy = 0; return true;
				case "RIGHTRIGHTRIGHT": dx = 3; dy = 0; return true;
				case "RIGHTRIGHTRIGHTRIGHT": dx = 4; dy = 0; return true;
				case "UP": dx = 0; dy = -1; return true;
				case "UPUP": dx = 0; dy = -2; return true;
				case "UPUPUP": dx = 0; dy = -3; return true;
				case "UPUPUPUP": dx = 0; dy = -4; return true;
				case "DOWN": dx = 0; dy = 1; return true;
				case "DOWNDOWN": dx = 0; dy = 2; return true;
				case "DOWNDOWNDOWN": dx = 0; dy = 3; return true;
				case "DOWNDOWNDOWNDOWN": dx = 0; dy = 4; return true;
				case "KNIGHTUPLEFT": dx = -1; dy = -2; return true;
				case "KNIGHTUPRIGHT": dx = 2; dy = -1; return true;
				case "KNIGHTDOWNLEFT": dx = -2; dy = 1; return true;
				case "KNIGHTDOWNRIGHT": dx = 1; dy = 2; return true;
				case "KNIGHTUPLEFT_INVERT": dx = -2; dy = -1; return true;
				case "KNIGHTDOWNLEFT_INVERT": dx = -1; dy = 2; return true;
				case "KNIGHTDOWNRIGHT_INVERT": dx = 2; dy = 1; return true;
				case "KNIGHTUPRIGHT_INVERT": dx = 1; dy = -2; return true;
				case "DIAUPLEFT": dx = -1; dy = -1; return true;
				case "DIADOWNLEFT": dx = -1; dy = 1; return true;
				case "DIAUPRIGHT": dx = 1; dy = -1; return true;
				case "DIADOWNRIGHT": dx = 1; dy = 1; return true;
				default: dx = 0; dy = 0; return false;
			}
		}

		private static (int Dx, int Dy) RotateRelativeOffset(int dx, int dy, int rot) {
			return (rot & 3) switch {
				0 => (dx, dy),
				1 => (dy, -dx),
				2 => (-dx, -dy),
				_ => (-dy, dx)
			};
		}

		private static void AppendRotatedRayPositions(
			List<RawQueryMetadata> list,
			string keyword,
			string val,
			int width,
			int height,
			int originX,
			int originY,
			int rot
		) {
			int baseDir = keyword switch {
				"X_PLUS" => 0,
				"Y_MINUS" => 1,
				"X_MINUS" => 2,
				_ => 3
			};
			int effDir = (baseDir + rot) & 3;
			switch (effDir) {
				case 0: {
					for (int x = originX + 1; x < width; x++) {
						list.Add(new RawQueryMetadata(x, originY, val));
					}
					break;
				}
				case 1: {
					for (int y = 0; y < originY; y++) {
						list.Add(new RawQueryMetadata(originX, y, val));
					}
					break;
				}
				case 2: {
					for (int x = 0; x < originX; x++) {
						list.Add(new RawQueryMetadata(x, originY, val));
					}
					break;
				}
				case 3: {
					for (int y = originY; y < height; y++) {
						list.Add(new RawQueryMetadata(originX, y, val));
					}
					break;
				}
			}
		}

		private static void AppendRotatedEdgePositions(
			List<RawQueryMetadata> list,
			string keyword,
			string val,
			int width,
			int height,
			int storage,
			int rot
		) {
			int baseEdge = keyword switch {
				"TOP" => 0,
				"LEFTEND" => 1,
				"BOTTOM" => 2,
				_ => 3
			};
			int effEdge = (baseEdge + rot) & 3;
			switch (effEdge) {
				case 0: {
					for (int x = 0; x < width; x++) {
						list.Add(new RawQueryMetadata(x, 0, val));
					}
					break;
				}
				case 1: {
					for (int y = 0; y < height; y++) {
						list.Add(new RawQueryMetadata(0, y, val));
					}
					break;
				}
				case 2: {
					for (int i = 0; i < width; i++) {
						int idx = storage - i - 1;
						if (idx >= 0) {
							list.Add(new RawQueryMetadata(idx % width, idx / width, val));
						}
					}
					break;
				}
				case 3: {
					for (int y = 0; y < height; y++) {
						if (((y * width) + (width - 1)) <= storage - 1) {
							list.Add(new RawQueryMetadata(width - 1, y, val));
						}
					}
					break;
				}
			}
		}

		private static void AppendRotatedDiagonalPositions(
			List<RawQueryMetadata> list,
			string keyword,
			string val,
			int width,
			int height,
			int originX,
			int originY,
			int rot
		) {
			int baseDiag = keyword switch {
				"RIGHT_RISING" => 0,
				"LEFT_RISING" => 1,
				"LEFT_FALLING" => 2,
				_ => 3
			};
			int effDiag = (baseDiag + rot) & 3;
			(int stepX, int stepY) = effDiag switch {
				0 => (1, -1),
				1 => (-1, -1),
				2 => (-1, 1),
				_ => (1, 1)
			};

			int curX = originX + stepX;
			int curY = originY + stepY;
			while (curX >= 0 && curX < width && curY >= 0 && curY < height) {
				list.Add(new RawQueryMetadata(curX, curY, val));
				curX += stepX;
				curY += stepY;
			}
		}

		private static TabletConditionType ParseConditionValue(string value) {
			return value switch {
				"ITEM" => TabletConditionType.AnyItem,
				"CHARM" => TabletConditionType.OnlyCharm,
				"PLACED" => TabletConditionType.Placed,
				_ => TabletConditionType.None
			};
		}

		private static (TabletEffectType EffectType, int LevelParam) ParseEffectValue(string value) {
			if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delta)) {
				return (TabletEffectType.IncreaseConstLevel, delta);
			}
			if (value == "X") {
				return (TabletEffectType.Disable, 1);
			}
			if (value == "IGNORECRITERIA") {
				return (TabletEffectType.IgnoreCriteria, 1);
			}
			string[] parts = value.Split('/');
			if (parts.Length == 2
				&& parts[0] == "MUL"
				&& int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mul)) {
				return (TabletEffectType.MultiplyConstLevel, mul);
			}
			return (TabletEffectType.None, 0);
		}

		private static void PopulateBaseSlotModifiers(
			GridInventory inventory,
			int width,
			int height,
			int unlockedSlots,
			int[] baseSlotLevel,
			int[] baseSlotDisable,
			int[] baseSlotIgnoreCriteria,
			int[] baseSlotMultiplyLevel
		) {
			foreach (KeyValuePair<ItemPosition, int> entry in inventory.dungeonTempLevels) {
				int slotIdx = CoordToUnlockedSlot(entry.Key.x, entry.Key.y, width, height, unlockedSlots);
				if (slotIdx >= 0) {
					baseSlotLevel[slotIdx] += entry.Value;
				}
			}

			bool hasServerEngravings = inventory.fixedEngravingsOnServer != null
				&& inventory.fixedEngravingsOnServer.Count > 0;

			if (hasServerEngravings) {
				for (int i = 0; i < inventory.fixedEngravingsOnServer!.Count; i++) {
					FixedEngraving engraving = inventory.fixedEngravingsOnServer[i];
					if (engraving == null || engraving.createdByExternalSystem) {
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
		}

		private static TalentRuleSnapshot CaptureTalentRules(PlayerAvatar? avatar, GridInventory inventory) {
			int tabletEngravingCount = inventory.tabletEngravingCount;
			int uniquePairCount = inventory.uniquePairCount;
			int unlimitedCombo = avatar != null ? avatar.GetCustomStatUnsafe("UNLIMITEDCOMBO") : 0;
			int comboBonusDamage = avatar != null ? avatar.GetCustomStatUnsafe("COMBOBONUSDAMAGE") : 0;
			int arrangementBonus = avatar != null ? avatar.GetCustomStatUnsafe("ARRANGEMENTBONUS") : 0;
			bool disableDual = avatar != null && avatar.GetCustomStat("DISABLEDUALARTIFACT") >= 1;
			bool preventOverlap = DungeonManager.Instance != null
				&& DungeonManager.Instance.hardModeEnvironment.TryGetValue("OVERLAPITEMCOMBO", out int ovVal)
				&& ovVal > 0;

			int fireStat = avatar != null ? avatar.GetCustomStat(ECustomStat.FireDamage) : 0;
			int iceStat = avatar != null ? avatar.GetCustomStat(ECustomStat.IceDamage) : 0;
			int lightningStat = avatar != null ? avatar.GetCustomStat(ECustomStat.LightningDamage) : 0;
			int physicalStat = avatar != null ? avatar.GetCustomStat(ECustomStat.PhysicalDamage) : 0;

			List<ArrangementPattern> patterns = new List<ArrangementPattern>();
			if (arrangementBonus > 0) {
				HashSet<int> ownedEntityIds = new HashSet<int>();
				foreach (NewItemOwnInstance inst in inventory.inventoryMatrix.Values) {
					if (inst != null) {
						ownedEntityIds.Add(inst.EntityID);
					}
				}
				foreach (ArrangementBonusEntity bonusEntity in ItemDatabase.GetAllArrangementBonuses()) {
					if (bonusEntity == null || !bonusEntity.isEnabled || bonusEntity.arrangements == null) {
						continue;
					}
					if (bonusEntity.arrangements.Length == 0) {
						continue;
					}
					bool allOwned = true;
					ArrangementOffsetEntry[] entries = new ArrangementOffsetEntry[bonusEntity.arrangements.Length];
					for (int a = 0; a < bonusEntity.arrangements.Length; a++) {
						ArrangementBonusEntity.Arrangement arr = bonusEntity.arrangements[a];
						if (arr == null || arr.item == null || !ownedEntityIds.Contains(arr.item.id)) {
							allOwned = false;
							break;
						}
						entries[a] = new ArrangementOffsetEntry(arr.offset.x, arr.offset.y, arr.item.id);
					}
					if (allOwned) {
						patterns.Add(new ArrangementPattern(entries));
					}
				}
			}

			return new TalentRuleSnapshot(
				tabletEngravingCount,
				uniquePairCount,
				unlimitedCombo,
				comboBonusDamage,
				arrangementBonus,
				disableDual,
				preventOverlap,
				fireStat,
				iceStat,
				lightningStat,
				physicalStat,
				patterns.ToArray()
			);
		}

		private static ComboCategoryInfo[] BuildComboCategoryInfos(
			List<string> orderedCategoryKeys,
			int[] staticCategoryCounts,
			int comboBonusDamageStat,
			OptimizerUserSettings settings
		) {
			ComboCategoryInfo[] result = new ComboCategoryInfo[orderedCategoryKeys.Count];
			bool hasExplicitTargets = settings.TargetComboCategories.Count > 0;
			float talentBonusPerCategory = Math.Max(0, comboBonusDamageStat);

			for (int i = 0; i < orderedCategoryKeys.Count; i++) {
				string key = orderedCategoryKeys[i];
				string displayName = key;
				int[] breakpoints = new[] { 2, 4, 6, 8 };
				float[]? extractedBonuses = null;
				CombatStatDelta[]? extractedDeltas = null;

				ItemCategoryEntity catEntity = ItemDatabase.FindItemCategory(key);
				if (catEntity != null) {
					if (!string.IsNullOrEmpty(catEntity.Name)) {
						displayName = catEntity.Name;
					}
					if (catEntity.setStatus != null && catEntity.setStatus.Length > 0) {
						int len = catEntity.setStatus.Length;
						breakpoints = new int[len];
						extractedBonuses = new float[len];
						extractedDeltas = new CombatStatDelta[len];
						for (int b = 0; b < len; b++) {
							breakpoints[b] = catEntity.setStatus[b].itemCount;
							extractedDeltas[b] = ParseSingleComboStatusToStatDelta(
								catEntity.setStatus[b].status,
								out float parsedPct
							);
							extractedBonuses[b] = parsedPct;
						}
						SortComboBreakpoints(breakpoints, extractedBonuses, extractedDeltas);
					} else if (catEntity.comboEffectPrefab != null
						&& catEntity.comboEffectPrefab.TryGetComponent<ComboEffect_Mystic>(out var mysticPrefab)) {
						int firstTier = mysticPrefab.first > 0 ? mysticPrefab.first : 2;
						int secondTier = mysticPrefab.second > firstTier ? mysticPrefab.second : 4;
						breakpoints = new[] { firstTier, secondTier };
						extractedBonuses = new float[2];
						extractedDeltas = new CombatStatDelta[2];
					} else if (catEntity.comboEffectPrefab != null
						&& catEntity.comboEffectPrefab.TryGetComponent<ComboEffectBase>(out var comboPrefab)
						&& comboPrefab.addStatByCombo != null
						&& comboPrefab.addStatByCombo.Length > 0) {
						int len = comboPrefab.addStatByCombo.Length;
						breakpoints = new int[len];
						extractedBonuses = new float[len];
						extractedDeltas = new CombatStatDelta[len];
						for (int b = 0; b < len; b++) {
							breakpoints[b] = comboPrefab.addStatByCombo[b].comboCount;
							extractedDeltas[b] = ParseComboStatusArrayToStatDelta(
								comboPrefab.addStatByCombo[b].status,
								out float parsedPct
							);
							extractedBonuses[b] = parsedPct;
						}
						SortComboBreakpoints(breakpoints, extractedBonuses, extractedDeltas);
					}
				}

				if (talentBonusPerCategory > 0f && breakpoints.Length > 0) {
					if (extractedBonuses != null && extractedBonuses.Length > 0) {
						extractedBonuses[0] += talentBonusPerCategory;
					}
					if (extractedDeltas != null && extractedDeltas.Length > 0) {
						extractedDeltas[0] = extractedDeltas[0].Add(
							new CombatStatDelta(allDamageBonus: talentBonusPerCategory)
						);
					}
				}

				float[] breakpointBonuses = extractedBonuses ?? new float[breakpoints.Length];

				int staticCount = staticCategoryCounts[i];
				bool isUserTarget = hasExplicitTargets
					? settings.TargetComboCategories.Contains(key)
					: (staticCount >= 1);

				result[i] = new ComboCategoryInfo(
					i,
					key,
					displayName,
					staticCount,
					breakpoints,
					isUserTarget,
					breakpointBonuses,
					extractedDeltas
				);
			}

			return result;
		}

		private static void SortComboBreakpoints(
			int[] breakpoints,
			float[] bonuses,
			CombatStatDelta[] deltas
		) {
			for (int i = 1; i < breakpoints.Length; i++) {
				int keyBp = breakpoints[i];
				float keyBonus = bonuses[i];
				CombatStatDelta keyDelta = deltas[i];
				int j = i - 1;
				while (j >= 0 && breakpoints[j] > keyBp) {
					breakpoints[j + 1] = breakpoints[j];
					bonuses[j + 1] = bonuses[j];
					deltas[j + 1] = deltas[j];
					j--;
				}
				breakpoints[j + 1] = keyBp;
				bonuses[j + 1] = keyBonus;
				deltas[j + 1] = keyDelta;
			}
		}

		private static void RegisterCategoryKey(
			string? rawKey,
			Dictionary<string, int> categoryKeyToId,
			List<string> orderedCategoryKeys
		) {
			if (string.IsNullOrWhiteSpace(rawKey)) {
				return;
			}
			string key = rawKey.Trim();
			if (categoryKeyToId.ContainsKey(key)
				|| orderedCategoryKeys.Count >= InventorySnapshot.MaxSupportedCategories) {
				return;
			}
			int id = orderedCategoryKeys.Count;
			categoryKeyToId[key] = id;
			orderedCategoryKeys.Add(key);
		}

		private static ArtifactPriorityTier InferDefaultPriorityTier(
			ItemEntity entity,
			Charm_Basic? charm,
			bool isDisabledByWeaponOrDual
		) {
			if (isDisabledByWeaponOrDual) {
				return ArtifactPriorityTier.Sacrifice;
			}
			if (charm != null && charm.maxLevel == 0 && !(charm is Charm_WhitePaper)) {
				return ArtifactPriorityTier.Low;
			}
			if (entity.rarity >= EItemRarity.Legend) {
				return ArtifactPriorityTier.High;
			}
			if (charm != null && (charm is IAttackableCharm || charm.isUniqueEffect)) {
				return ArtifactPriorityTier.High;
			}
			return ArtifactPriorityTier.Normal;
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
				EItemRarity.Common => 3,
				EItemRarity.Uncommon => 4,
				EItemRarity.Rare => 6,
				EItemRarity.Legend => 9,
				EItemRarity.Eternal => 13,
				_ => 3
			};
			if (charm != null) {
				if (charm.isUniqueEffect) {
					rarityWeight += 2;
				}
				if (charm is IAttackableCharm attackable && attackable.IsAttackableCharm()) {
					rarityWeight += 3;
				}
				if (charm is Charm_UpCharmDamage || charm is Charm_NearLevelDamage) {
					rarityWeight += 3;
				}
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
			Dictionary<ItemPosition, int>? source,
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

		private static void ReadMysticConfiguration(
			PlayerAvatar? avatar,
			GridInventory inventory,
			int width,
			int height,
			int unlockedSlots,
			out int mysticSlot0,
			out int mysticSlot1,
			out int mysticTier1Threshold,
			out int mysticTier2Threshold,
			out int[] extraMysticTier2Slots
		) {
			mysticSlot0 = -1;
			mysticSlot1 = -1;
			mysticTier1Threshold = 2;
			mysticTier2Threshold = 4;
			extraMysticTier2Slots = Array.Empty<int>();

			if (unlockedSlots <= 0) {
				return;
			}

			int firstCount = 1;
			int secondCount = 1;
			ItemCategoryEntity mysticCat = ItemDatabase.FindItemCategory("MYSTIC");
			if (mysticCat != null
				&& mysticCat.comboEffectPrefab != null
				&& mysticCat.comboEffectPrefab.TryGetComponent<ComboEffect_Mystic>(out var mysticPrefab)) {
				mysticTier1Threshold = mysticPrefab.first > 0 ? mysticPrefab.first : 2;
				mysticTier2Threshold = mysticPrefab.second > mysticTier1Threshold ? mysticPrefab.second : 4;
				firstCount = Math.Max(1, mysticPrefab.firstEngravingCount);
				secondCount = Math.Max(1, mysticPrefab.secondEngravingCount);
			}

			int totalSlotsNeeded = firstCount + secondCount;
			List<int> resolvedSlots = new List<int>(totalSlotsNeeded);

			if (inventory.mysticPositions != null && inventory.mysticPositions.Count > 0) {
				int maxRead = Math.Min(totalSlotsNeeded, inventory.mysticPositions.Count);
				for (int i = 0; i < maxRead; i++) {
					ItemPosition pos = inventory.mysticPositions[i];
					int slotIdx = CoordToUnlockedSlot(pos.x, pos.y, width, height, unlockedSlots);
					if (slotIdx >= 0 && !resolvedSlots.Contains(slotIdx)) {
						resolvedSlots.Add(slotIdx);
					}
				}
			} else if (avatar != null) {
				for (int i = 0; i < 10 && resolvedSlots.Count < totalSlotsNeeded; i++) {
					int generatedIdx = new Random(avatar.RandomID + i).Next(0, unlockedSlots);
					if (generatedIdx >= 0 && generatedIdx < unlockedSlots && !resolvedSlots.Contains(generatedIdx)) {
						resolvedSlots.Add(generatedIdx);
					}
				}
			}

			if (resolvedSlots.Count >= 1) {
				mysticSlot0 = resolvedSlots[0];
			}
			if (resolvedSlots.Count >= 2) {
				mysticSlot1 = resolvedSlots[1];
			}
			if (resolvedSlots.Count > 2) {
				extraMysticTier2Slots = new int[resolvedSlots.Count - 2];
				for (int i = 2; i < resolvedSlots.Count; i++) {
					extraMysticTier2Slots[i - 2] = resolvedSlots[i];
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

		private static Charm_Basic? ResolveSlotCharm(
			GridInventory inventory,
			NewItemOwnInstance itemInstance,
			ItemEntity entity,
			sbyte xCoord,
			sbyte yCoord
		) {
			if (itemInstance.Charm != null) {
				return itemInstance.Charm;
			}
			if (inventory.charms != null
				&& inventory.charms.TryGetValue(new ItemPosition(xCoord, yCoord), out Charm_Basic? syncedCharm)
				&& syncedCharm != null) {
				return syncedCharm;
			}
			if (entity.resourcePrefab != null) {
				return entity.resourcePrefab.GetComponent<Charm_Basic>();
			}
			return null;
		}

		private static StoneTablet? ResolveSlotTablet(
			GridInventory inventory,
			NewItemOwnInstance itemInstance,
			ItemEntity entity,
			sbyte xCoord,
			sbyte yCoord,
			out bool isLiveInstance
		) {
			if (itemInstance.StoneTablet != null) {
				isLiveInstance = true;
				return itemInstance.StoneTablet;
			}
			if (inventory.stoneTablets != null
				&& inventory.stoneTablets.TryGetValue(
					new ItemPosition(xCoord, yCoord),
					out StoneTablet? syncedTablet
				)
				&& syncedTablet != null) {
				isLiveInstance = true;
				return syncedTablet;
			}
			isLiveInstance = false;
			if (entity.resourcePrefab != null) {
				return entity.resourcePrefab.GetComponent<StoneTablet>();
			}
			return null;
		}
	}
}
