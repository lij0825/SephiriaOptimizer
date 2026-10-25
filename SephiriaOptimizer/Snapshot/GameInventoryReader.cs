using System;
using System.Collections.Generic;
using System.Globalization;
using SephiriaOptimizer.Models;

namespace SephiriaOptimizer.Snapshot {
	public static class GameInventoryReader {
		private static readonly string[] DefaultElementalCategories = new[] {
			"EMBER",
			"GLACIER",
			"MAGITECH",
			"STURDY"
		};

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

			for (int slotIdx = 0; slotIdx < unlockedSlots; slotIdx++) {
				sbyte xCoord = (sbyte)(slotIdx % width);
				sbyte yCoord = (sbyte)(slotIdx / width);
				NewItemOwnInstance itemInstance = inventory.FindItem(xCoord, yCoord);
				if (itemInstance == null || itemInstance.Entity == null) {
					continue;
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
				if (itemInstance.Charm is Charm_3Elemental_ByRow elemCharm && elemCharm.lineCategory != null) {
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
						isFireIce
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

			ComboCategoryInfo[] comboCategories = BuildComboCategoryInfos(
				orderedCategoryKeys,
				staticCategoryCounts,
				settings
			);

			bool isFullHp = avatar != null && Math.Abs(avatar.hp - avatar.MaxHp) < 0.01f;
			int preferredChalcedonyId = -1;
			if (!string.IsNullOrEmpty(settings.PreferredChalcedonyCategory)
				&& categoryKeyToId.TryGetValue(settings.PreferredChalcedonyCategory, out int chalCatId)) {
				preferredChalcedonyId = chalCatId;
			}

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
				itemSnapshots.ToArray(),
				comboCategories,
				talentRules,
				settings.PreferredNeedleTargetInstanceId,
				preferredChalcedonyId
			);
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
			OptimizerUserSettings settings
		) {
			ComboCategoryInfo[] result = new ComboCategoryInfo[orderedCategoryKeys.Count];
			bool hasExplicitTargets = settings.TargetComboCategories.Count > 0;

			for (int i = 0; i < orderedCategoryKeys.Count; i++) {
				string key = orderedCategoryKeys[i];
				string displayName = key;
				int[] breakpoints = new[] { 2, 4, 6, 8 };

				ItemCategoryEntity catEntity = ItemDatabase.FindItemCategory(key);
				if (catEntity != null) {
					if (!string.IsNullOrEmpty(catEntity.Name)) {
						displayName = catEntity.Name;
					}
					if (catEntity.setStatus != null && catEntity.setStatus.Length > 0) {
						breakpoints = new int[catEntity.setStatus.Length];
						for (int b = 0; b < catEntity.setStatus.Length; b++) {
							breakpoints[b] = catEntity.setStatus[b].itemCount;
						}
						Array.Sort(breakpoints);
					} else if (catEntity.comboEffectPrefab != null
						&& catEntity.comboEffectPrefab.TryGetComponent<ComboEffectBase>(out var comboPrefab)
						&& comboPrefab.addStatByCombo != null
						&& comboPrefab.addStatByCombo.Length > 0) {
						breakpoints = new int[comboPrefab.addStatByCombo.Length];
						for (int b = 0; b < comboPrefab.addStatByCombo.Length; b++) {
							breakpoints[b] = comboPrefab.addStatByCombo[b].comboCount;
						}
						Array.Sort(breakpoints);
					}
				}

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
					isUserTarget
				);
			}

			return result;
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
