using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SephiriaOptimizer.Models {
	public enum WeaponElementalFormulaMode : byte {
		LegacyGeneric = 0,
		SinglePhysical = 1,
		SingleFire = 2,
		SingleIce = 3,
		SingleLightning = 4,
		Highest = 5,
		Lowest = 6,
		AverageMask = 7,
		OnlyOne = 8
	}

	public enum WeaponConditionalStatMode : byte {
		None = 0,
		LessOrEqual = 1,
		GreaterOrEqual = 2,
		Equal = 3,
		NotEqual = 4
	}

	public enum WeaponConditionalStatTarget : byte {
		None = 0,
		CriticalRaw = 1,
		PhysicalDamage = 2,
		FireDamage = 3,
		IceDamage = 4,
		LightningDamage = 5,
		AttackSpeed = 6,
		AllDamageBonus = 7,
		AlwaysActive = 8
	}

	public readonly struct CombatStatDelta {
		public static readonly CombatStatDelta Zero = default;

		public float PhysicalDamage { get; }
		public float FireDamage { get; }
		public float IceDamage { get; }
		public float LightningDamage { get; }
		public float AllDamageBonus { get; }
		public float WeaponDamageBonus { get; }
		public float BasicAttackDamageBonus { get; }
		public float FinalWeaponDamage { get; }
		public float CharmDamageBonus { get; }
		public float AttackSpeed { get; }
		public float CritChancePercent { get; }
		public float CritDamageBonus { get; }

		public CombatStatDelta(
			float physicalDamage = 0f,
			float fireDamage = 0f,
			float iceDamage = 0f,
			float lightningDamage = 0f,
			float allDamageBonus = 0f,
			float weaponDamageBonus = 0f,
			float basicAttackDamageBonus = 0f,
			float finalWeaponDamage = 0f,
			float charmDamageBonus = 0f,
			float attackSpeed = 0f,
			float critChancePercent = 0f,
			float critDamageBonus = 0f
		) {
			PhysicalDamage = physicalDamage;
			FireDamage = fireDamage;
			IceDamage = iceDamage;
			LightningDamage = lightningDamage;
			AllDamageBonus = allDamageBonus;
			WeaponDamageBonus = weaponDamageBonus;
			BasicAttackDamageBonus = basicAttackDamageBonus;
			FinalWeaponDamage = finalWeaponDamage;
			CharmDamageBonus = charmDamageBonus;
			AttackSpeed = attackSpeed;
			CritChancePercent = critChancePercent;
			CritDamageBonus = critDamageBonus;
		}

		public bool HasAnyNonZero =>
			PhysicalDamage != 0f
			|| FireDamage != 0f
			|| IceDamage != 0f
			|| LightningDamage != 0f
			|| AllDamageBonus != 0f
			|| WeaponDamageBonus != 0f
			|| BasicAttackDamageBonus != 0f
			|| FinalWeaponDamage != 0f
			|| CharmDamageBonus != 0f
			|| AttackSpeed != 0f
			|| CritChancePercent != 0f
			|| CritDamageBonus != 0f;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public CombatStatDelta Add(in CombatStatDelta other) {
			return new CombatStatDelta(
				PhysicalDamage + other.PhysicalDamage,
				FireDamage + other.FireDamage,
				IceDamage + other.IceDamage,
				LightningDamage + other.LightningDamage,
				AllDamageBonus + other.AllDamageBonus,
				WeaponDamageBonus + other.WeaponDamageBonus,
				BasicAttackDamageBonus + other.BasicAttackDamageBonus,
				FinalWeaponDamage + other.FinalWeaponDamage,
				CharmDamageBonus + other.CharmDamageBonus,
				AttackSpeed + other.AttackSpeed,
				CritChancePercent + other.CritChancePercent,
				CritDamageBonus + other.CritDamageBonus
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public CombatStatDelta AddElementalByKind(byte elementalKind, float amount) {
			if (amount == 0f) {
				return this;
			}
			return elementalKind switch {
				1 => new CombatStatDelta(
					PhysicalDamage + amount,
					FireDamage,
					IceDamage,
					LightningDamage,
					AllDamageBonus,
					WeaponDamageBonus,
					BasicAttackDamageBonus,
					FinalWeaponDamage,
					CharmDamageBonus,
					AttackSpeed,
					CritChancePercent,
					CritDamageBonus
				),
				2 => new CombatStatDelta(
					PhysicalDamage,
					FireDamage + amount,
					IceDamage,
					LightningDamage,
					AllDamageBonus,
					WeaponDamageBonus,
					BasicAttackDamageBonus,
					FinalWeaponDamage,
					CharmDamageBonus,
					AttackSpeed,
					CritChancePercent,
					CritDamageBonus
				),
				3 => new CombatStatDelta(
					PhysicalDamage,
					FireDamage,
					IceDamage + amount,
					LightningDamage,
					AllDamageBonus,
					WeaponDamageBonus,
					BasicAttackDamageBonus,
					FinalWeaponDamage,
					CharmDamageBonus,
					AttackSpeed,
					CritChancePercent,
					CritDamageBonus
				),
				4 => new CombatStatDelta(
					PhysicalDamage,
					FireDamage,
					IceDamage,
					LightningDamage + amount,
					AllDamageBonus,
					WeaponDamageBonus,
					BasicAttackDamageBonus,
					FinalWeaponDamage,
					CharmDamageBonus,
					AttackSpeed,
					CritChancePercent,
					CritDamageBonus
				),
				_ => new CombatStatDelta(
					PhysicalDamage,
					FireDamage,
					IceDamage,
					LightningDamage,
					AllDamageBonus + amount,
					WeaponDamageBonus,
					BasicAttackDamageBonus,
					FinalWeaponDamage,
					CharmDamageBonus,
					AttackSpeed,
					CritChancePercent,
					CritDamageBonus
				)
			};
		}
	}

	public readonly struct WeaponCombatEvaluation {
		public double WeaponAndAddonRawDps { get; }
		public double WeaponAndAddonEffectiveDps { get; }
		public double LivePhysicalStat { get; }
		public double LiveFireStat { get; }
		public double LiveIceStat { get; }
		public double LiveLightningStat { get; }
		public double HighestLiveElementalStat { get; }
		public double LiveCharmDamageBonusPercent { get; }
		public double CharmPostMultiplier { get; }

		public WeaponCombatEvaluation(
			double weaponAndAddonRawDps,
			double weaponAndAddonEffectiveDps,
			double livePhysicalStat,
			double liveFireStat,
			double liveIceStat,
			double liveLightningStat,
			double highestLiveElementalStat,
			double liveCharmDamageBonusPercent,
			double charmPostMultiplier
		) {
			WeaponAndAddonRawDps = weaponAndAddonRawDps;
			WeaponAndAddonEffectiveDps = weaponAndAddonEffectiveDps;
			LivePhysicalStat = livePhysicalStat;
			LiveFireStat = liveFireStat;
			LiveIceStat = liveIceStat;
			LiveLightningStat = liveLightningStat;
			HighestLiveElementalStat = highestLiveElementalStat;
			LiveCharmDamageBonusPercent = liveCharmDamageBonusPercent;
			CharmPostMultiplier = charmPostMultiplier;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public double ResolveElementalStat(byte elementalKind) {
			return elementalKind switch {
				1 => LivePhysicalStat,
				2 => LiveFireStat,
				3 => LiveIceStat,
				4 => LiveLightningStat,
				_ => HighestLiveElementalStat
			};
		}
	}

	public readonly struct WeaponCombatProfile {
		public WeaponElementalFormulaMode FormulaMode { get; }
		public byte AverageElementMask { get; }
		public float BasePhysicalStat { get; }
		public float BaseFireStat { get; }
		public float BaseIceStat { get; }
		public float BaseLightningStat { get; }
		public float BaseAllDamageBonus { get; }
		public float BaseWeaponDamageBonus { get; }
		public float BaseBasicAttackDamageBonus { get; }
		public float BaseFinalWeaponDamage { get; }
		public float BaseCharmDamageBonus { get; }
		public float BaseAttackSpeedStat { get; }
		public float BaseCritChancePercent { get; }
		public float BaseCritDamageRate { get; }
		public float WeaponExtraCritChancePercent { get; }
		public float WeaponExtraCritDamageRate { get; }
		public float WeaponCritDamageAmplifyPercent { get; }
		public bool HasExecution { get; }
		public float ComboSwingMultiplier { get; }
		public float AvgSwingRatioForAddons { get; }
		public float FlatBonusDamagePerSwing { get; }
		public float BaseSwingRate { get; }
		public float AttackSpeedAmplify { get; }
		public float FixedAttackSpeedMultiplier { get; }
		public float ExtraAttackSpeedBonus { get; }
		public int MagazineCapacity { get; }
		public float EffectiveReloadSeconds { get; }
		public float AdditionalBasicAttackPercent { get; }
		public bool UsesMpBasicAttack { get; }
		public float MpSkillDamagePercent { get; }
		public float DirectAttackDashMultiplier { get; }
		public float GlobalPostMultiplier { get; }
		public bool HasElementalScaledSpread { get; }
		public int SpreadBaseBullets { get; }
		public int SpreadStatThreshold { get; }
		public int SpreadStatPerExtraBullet { get; }
		public int SpreadMaxExtraBullets { get; }
		public float AddOnHitPhysicalPct { get; }
		public float AddOnHitFirePct { get; }
		public float AddOnHitIcePct { get; }
		public float AddOnHitLightningPct { get; }
		public WeaponConditionalStatMode CondMode { get; }
		public WeaponConditionalStatTarget CondTarget { get; }
		public float CondThreshold { get; }
		public float CondDamageAmpPercent { get; }
		public float BurnRingFirePercentPerSec { get; }
		public int CritNeedleBaseCount { get; }
		public float CritNeedlePhysicalRatio { get; }
		public float NastrondDefaultDamage { get; }
		public float NastrondIcePercent { get; }
		public float NastrondBonusMultiplier { get; }
		public float NastrondProcsPerSec { get; }

		public WeaponCombatProfile(
			WeaponElementalFormulaMode formulaMode,
			byte averageElementMask = 0x0F,
			float basePhysicalStat = 20f,
			float baseFireStat = 20f,
			float baseIceStat = 20f,
			float baseLightningStat = 20f,
			float baseAllDamageBonus = 0f,
			float baseWeaponDamageBonus = 0f,
			float baseBasicAttackDamageBonus = 0f,
			float baseFinalWeaponDamage = 0f,
			float baseCharmDamageBonus = 0f,
			float baseAttackSpeedStat = 0f,
			float baseCritChancePercent = 10f,
			float baseCritDamageRate = 50f,
			float weaponExtraCritChancePercent = 0f,
			float weaponExtraCritDamageRate = 0f,
			float weaponCritDamageAmplifyPercent = 0f,
			bool hasExecution = false,
			float comboSwingMultiplier = 1.0f,
			float avgSwingRatioForAddons = 1.0f,
			float flatBonusDamagePerSwing = 0f,
			float baseSwingRate = 2.0f,
			float attackSpeedAmplify = 0f,
			float fixedAttackSpeedMultiplier = 0f,
			float extraAttackSpeedBonus = 0f,
			int magazineCapacity = 0,
			float effectiveReloadSeconds = 0f,
			float additionalBasicAttackPercent = 0f,
			bool usesMpBasicAttack = false,
			float mpSkillDamagePercent = 0f,
			float directAttackDashMultiplier = 1.0f,
			float globalPostMultiplier = 1.0f,
			bool hasElementalScaledSpread = false,
			int spreadBaseBullets = 1,
			int spreadStatThreshold = 20,
			int spreadStatPerExtraBullet = 10,
			int spreadMaxExtraBullets = 0,
			float addOnHitPhysicalPct = 0f,
			float addOnHitFirePct = 0f,
			float addOnHitIcePct = 0f,
			float addOnHitLightningPct = 0f,
			WeaponConditionalStatMode condMode = WeaponConditionalStatMode.None,
			WeaponConditionalStatTarget condTarget = WeaponConditionalStatTarget.None,
			float condThreshold = 0f,
			float condDamageAmpPercent = 0f,
			float burnRingFirePercentPerSec = 0f,
			int critNeedleBaseCount = 0,
			float critNeedlePhysicalRatio = 0f,
			float nastrondDefaultDamage = 0f,
			float nastrondIcePercent = 0f,
			float nastrondBonusMultiplier = 1f,
			float nastrondProcsPerSec = 0f
		) {
			FormulaMode = formulaMode;
			AverageElementMask = averageElementMask == 0 ? (byte)0x0F : averageElementMask;
			BasePhysicalStat = Math.Max(1f, basePhysicalStat);
			BaseFireStat = Math.Max(1f, baseFireStat);
			BaseIceStat = Math.Max(1f, baseIceStat);
			BaseLightningStat = Math.Max(1f, baseLightningStat);
			BaseAllDamageBonus = baseAllDamageBonus;
			BaseWeaponDamageBonus = baseWeaponDamageBonus;
			BaseBasicAttackDamageBonus = baseBasicAttackDamageBonus;
			BaseFinalWeaponDamage = baseFinalWeaponDamage;
			BaseCharmDamageBonus = baseCharmDamageBonus;
			BaseAttackSpeedStat = baseAttackSpeedStat;
			BaseCritChancePercent = Math.Max(0f, baseCritChancePercent);
			BaseCritDamageRate = Math.Max(0f, baseCritDamageRate);
			WeaponExtraCritChancePercent = weaponExtraCritChancePercent;
			WeaponExtraCritDamageRate = weaponExtraCritDamageRate;
			WeaponCritDamageAmplifyPercent = Math.Max(0f, weaponCritDamageAmplifyPercent);
			HasExecution = hasExecution;
			ComboSwingMultiplier = comboSwingMultiplier > 0.01f ? comboSwingMultiplier : 1.0f;
			AvgSwingRatioForAddons = avgSwingRatioForAddons > 0.01f ? avgSwingRatioForAddons : 1.0f;
			FlatBonusDamagePerSwing = Math.Max(0f, flatBonusDamagePerSwing);
			BaseSwingRate = Math.Clamp(baseSwingRate, 0.2f, 20.0f);
			AttackSpeedAmplify = attackSpeedAmplify;
			FixedAttackSpeedMultiplier = Math.Max(0f, fixedAttackSpeedMultiplier);
			ExtraAttackSpeedBonus = extraAttackSpeedBonus;
			MagazineCapacity = Math.Max(0, magazineCapacity);
			EffectiveReloadSeconds = Math.Max(0f, effectiveReloadSeconds);
			AdditionalBasicAttackPercent = additionalBasicAttackPercent;
			UsesMpBasicAttack = usesMpBasicAttack;
			MpSkillDamagePercent = mpSkillDamagePercent;
			DirectAttackDashMultiplier = Math.Max(0.1f, directAttackDashMultiplier);
			GlobalPostMultiplier = Math.Max(0.1f, globalPostMultiplier);
			HasElementalScaledSpread = hasElementalScaledSpread;
			SpreadBaseBullets = Math.Max(1, spreadBaseBullets);
			SpreadStatThreshold = Math.Max(0, spreadStatThreshold);
			SpreadStatPerExtraBullet = Math.Max(1, spreadStatPerExtraBullet);
			SpreadMaxExtraBullets = Math.Max(0, spreadMaxExtraBullets);
			AddOnHitPhysicalPct = Math.Max(0f, addOnHitPhysicalPct);
			AddOnHitFirePct = Math.Max(0f, addOnHitFirePct);
			AddOnHitIcePct = Math.Max(0f, addOnHitIcePct);
			AddOnHitLightningPct = Math.Max(0f, addOnHitLightningPct);
			CondMode = condMode;
			CondTarget = condTarget;
			CondThreshold = condThreshold;
			CondDamageAmpPercent = condDamageAmpPercent;
			BurnRingFirePercentPerSec = Math.Max(0f, burnRingFirePercentPerSec);
			CritNeedleBaseCount = Math.Max(0, critNeedleBaseCount);
			CritNeedlePhysicalRatio = Math.Max(0f, critNeedlePhysicalRatio);
			NastrondDefaultDamage = Math.Max(0f, nastrondDefaultDamage);
			NastrondIcePercent = Math.Max(0f, nastrondIcePercent);
			NastrondBonusMultiplier = Math.Max(0.1f, nastrondBonusMultiplier);
			NastrondProcsPerSec = Math.Max(0f, nastrondProcsPerSec);
		}

		public WeaponCombatProfile WithDeductedInventoryDelta(in CombatStatDelta activeDelta) {
			return new WeaponCombatProfile(
				FormulaMode,
				AverageElementMask,
				Math.Max(1f, BasePhysicalStat - activeDelta.PhysicalDamage),
				Math.Max(1f, BaseFireStat - activeDelta.FireDamage),
				Math.Max(1f, BaseIceStat - activeDelta.IceDamage),
				Math.Max(1f, BaseLightningStat - activeDelta.LightningDamage),
				BaseAllDamageBonus - activeDelta.AllDamageBonus,
				BaseWeaponDamageBonus - activeDelta.WeaponDamageBonus,
				BaseBasicAttackDamageBonus - activeDelta.BasicAttackDamageBonus,
				BaseFinalWeaponDamage - activeDelta.FinalWeaponDamage,
				BaseCharmDamageBonus - activeDelta.CharmDamageBonus,
				BaseAttackSpeedStat - activeDelta.AttackSpeed,
				Math.Max(0f, BaseCritChancePercent - activeDelta.CritChancePercent),
				Math.Max(0f, BaseCritDamageRate - activeDelta.CritDamageBonus),
				WeaponExtraCritChancePercent,
				WeaponExtraCritDamageRate,
				WeaponCritDamageAmplifyPercent,
				HasExecution,
				ComboSwingMultiplier,
				AvgSwingRatioForAddons,
				FlatBonusDamagePerSwing,
				BaseSwingRate,
				AttackSpeedAmplify,
				FixedAttackSpeedMultiplier,
				ExtraAttackSpeedBonus,
				MagazineCapacity,
				EffectiveReloadSeconds,
				AdditionalBasicAttackPercent,
				UsesMpBasicAttack,
				MpSkillDamagePercent,
				DirectAttackDashMultiplier,
				GlobalPostMultiplier,
				HasElementalScaledSpread,
				SpreadBaseBullets,
				SpreadStatThreshold,
				SpreadStatPerExtraBullet,
				SpreadMaxExtraBullets,
				AddOnHitPhysicalPct,
				AddOnHitFirePct,
				AddOnHitIcePct,
				AddOnHitLightningPct,
				CondMode,
				CondTarget,
				CondThreshold,
				CondDamageAmpPercent,
				BurnRingFirePercentPerSec,
				CritNeedleBaseCount,
				CritNeedlePhysicalRatio,
				NastrondDefaultDamage,
				NastrondIcePercent,
				NastrondBonusMultiplier,
				NastrondProcsPerSec
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public WeaponCombatEvaluation Evaluate(in CombatStatDelta delta, double charmFinalDamageMultiplier) {
			double livePhys = Math.Max(1.0, BasePhysicalStat + delta.PhysicalDamage);
			double liveFire = Math.Max(1.0, BaseFireStat + delta.FireDamage);
			double liveIce = Math.Max(1.0, BaseIceStat + delta.IceDamage);
			double liveLight = Math.Max(1.0, BaseLightningStat + delta.LightningDamage);
			double highestElem = Math.Max(Math.Max(livePhys, liveFire), Math.Max(liveIce, liveLight));
			double lowestElem = Math.Min(Math.Min(livePhys, liveFire), Math.Min(liveIce, liveLight));

			double relatedStat = FormulaMode switch {
				WeaponElementalFormulaMode.SinglePhysical => livePhys,
				WeaponElementalFormulaMode.SingleFire => liveFire,
				WeaponElementalFormulaMode.SingleIce => liveIce,
				WeaponElementalFormulaMode.SingleLightning => liveLight,
				WeaponElementalFormulaMode.Highest => highestElem,
				WeaponElementalFormulaMode.Lowest => lowestElem,
				WeaponElementalFormulaMode.AverageMask => ComputeMaskedAverage(
					AverageElementMask,
					livePhys,
					liveFire,
					liveIce,
					liveLight
				),
				WeaponElementalFormulaMode.OnlyOne => 1.0,
				_ => highestElem
			};

			double spreadScaleRatio = 1.0;
			if (HasElementalScaledSpread && SpreadStatPerExtraBullet > 0) {
				int excessSum = Math.Max(0, (int)Math.Floor(livePhys) - SpreadStatThreshold)
					+ Math.Max(0, (int)Math.Floor(liveFire) - SpreadStatThreshold)
					+ Math.Max(0, (int)Math.Floor(liveIce) - SpreadStatThreshold)
					+ Math.Max(0, (int)Math.Floor(liveLight) - SpreadStatThreshold);
				int extraBullets = excessSum / SpreadStatPerExtraBullet;
				if (SpreadMaxExtraBullets > 0 && extraBullets > SpreadMaxExtraBullets) {
					extraBullets = SpreadMaxExtraBullets;
				}
				int totalBullets = Math.Max(1, SpreadBaseBullets + extraBullets);
				spreadScaleRatio = (double)totalBullets / Math.Max(1, SpreadBaseBullets);
			}

			double liveWeaponBonus = BaseWeaponDamageBonus + delta.WeaponDamageBonus;
			double liveBasicBonus = BaseBasicAttackDamageBonus + delta.BasicAttackDamageBonus;
			double liveFinalWeaponBonus = BaseFinalWeaponDamage + delta.FinalWeaponDamage;
			double liveAllDamageBonus = BaseAllDamageBonus + delta.AllDamageBonus;
			double liveCharmBonus = BaseCharmDamageBonus + delta.CharmDamageBonus;
			double liveAttackSpeed = BaseAttackSpeedStat + delta.AttackSpeed;
			double liveCritChance = Math.Max(0.0, BaseCritChancePercent + delta.CritChancePercent);
			double liveCritDamageRate = Math.Max(0.0, BaseCritDamageRate + delta.CritDamageBonus);

			double weaponAddonStatChain = Math.Max(0.1, 1.0 + (liveWeaponBonus * 0.01))
				* Math.Max(0.1, 1.0 + (liveBasicBonus * 0.01))
				* Math.Max(0.1, 1.0 + (liveFinalWeaponBonus * 0.01));
			double weaponSwingChain = weaponAddonStatChain
				* Math.Max(0.1, 1.0 + (AdditionalBasicAttackPercent * 0.01));
			if (UsesMpBasicAttack) {
				weaponSwingChain *= Math.Max(0.1, 1.0 + (MpSkillDamagePercent * 0.01));
			}

			double swingDirectDamage = (relatedStat * ComboSwingMultiplier * spreadScaleRatio * weaponSwingChain)
				+ FlatBonusDamagePerSwing;

			double animSpeedMult;
			if (FixedAttackSpeedMultiplier > 0f) {
				animSpeedMult = FixedAttackSpeedMultiplier;
			} else {
				double amplifiedAs = (liveAttackSpeed * (1.0 + AttackSpeedAmplify) * 0.01) + ExtraAttackSpeedBonus;
				animSpeedMult = Math.Clamp(1.0 + amplifiedAs, 0.25, 8.0);
			}

			double rawSwingsPerSec = Math.Clamp(BaseSwingRate * animSpeedMult, 0.2, 24.0);
			double sustainedSwingsPerSec = rawSwingsPerSec;
			if (MagazineCapacity > 0 && EffectiveReloadSeconds > 0.01f) {
				double firePhaseSeconds = MagazineCapacity / rawSwingsPerSec;
				sustainedSwingsPerSec = MagazineCapacity / (firePhaseSeconds + EffectiveReloadSeconds);
			}

			double weaponCritChance = Math.Max(0.0, liveCritChance + WeaponExtraCritChancePercent);
			double weaponCritRate = Math.Max(0.0, liveCritDamageRate + WeaponExtraCritDamageRate);
			if (WeaponCritDamageAmplifyPercent > 0f) {
				weaponCritRate += Math.Floor(weaponCritRate * (WeaponCritDamageAmplifyPercent * 0.01));
			}

			double weaponCritMultiplier = ComputeExpectedCritMultiplier(
				weaponCritChance,
				weaponCritRate,
				HasExecution
			);
			double generalCritMultiplier = ComputeExpectedCritMultiplier(
				liveCritChance,
				liveCritDamageRate,
				HasExecution
			);

			double conditionalMultiplier = EvaluateConditionalStatMultiplier(
				liveCritChance,
				livePhys,
				liveFire,
				liveIce,
				liveLight,
				liveAttackSpeed,
				liveAllDamageBonus
			);

			double allDamageFactor = Math.Max(0.1, 1.0 + (liveAllDamageBonus * 0.01));
			double sharedPostMultiplier = allDamageFactor
				* GlobalPostMultiplier
				* conditionalMultiplier
				* charmFinalDamageMultiplier;

			double directWeaponRawDps = swingDirectDamage * sustainedSwingsPerSec;
			double directWeaponEffectiveDps = directWeaponRawDps
				* DirectAttackDashMultiplier
				* sharedPostMultiplier
				* weaponCritMultiplier;

			double addonOnHitPerSwing = (
				(livePhys * AddOnHitPhysicalPct)
				+ (liveFire * AddOnHitFirePct)
				+ (liveIce * AddOnHitIcePct)
				+ (liveLight * AddOnHitLightningPct)
			) * 0.01 * AvgSwingRatioForAddons * weaponAddonStatChain;
			double addonOnHitRawDps = addonOnHitPerSwing * sustainedSwingsPerSec;

			double burnRingRawDps = 0.0;
			if (BurnRingFirePercentPerSec > 0f) {
				burnRingRawDps = liveFire
					* BurnRingFirePercentPerSec
					* Math.Max(0.1, 1.0 + (liveWeaponBonus * 0.01))
					* Math.Max(0.1, 1.0 + (liveFinalWeaponBonus * 0.01));
			}

			double critNeedleRawDps = 0.0;
			if (CritNeedleBaseCount > 0 && CritNeedlePhysicalRatio > 0f) {
				double critTriggerProb = Math.Clamp(weaponCritChance * 0.01, 0.0, 1.0);
				int extraNeedles = Math.Max(0, (int)((weaponCritRate - 50.0) / 20.0));
				int totalNeedles = CritNeedleBaseCount + extraNeedles;
				critNeedleRawDps = sustainedSwingsPerSec
					* critTriggerProb
					* totalNeedles
					* livePhys
					* CritNeedlePhysicalRatio
					* weaponAddonStatChain;
			}

			double nastrondRawDps = 0.0;
			if (NastrondProcsPerSec > 0f) {
				double nastrondHit = (NastrondDefaultDamage + (liveIce * (NastrondIcePercent * 0.01)))
					* NastrondBonusMultiplier;
				nastrondRawDps = nastrondHit * NastrondProcsPerSec;
			}

			double totalAddonRawDps = addonOnHitRawDps + burnRingRawDps + critNeedleRawDps + nastrondRawDps;
			double totalAddonEffectiveDps = totalAddonRawDps * sharedPostMultiplier * generalCritMultiplier;

			return new WeaponCombatEvaluation(
				directWeaponRawDps + totalAddonRawDps,
				directWeaponEffectiveDps + totalAddonEffectiveDps,
				livePhys,
				liveFire,
				liveIce,
				liveLight,
				highestElem,
				liveCharmBonus,
				sharedPostMultiplier * generalCritMultiplier
			);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double ComputeExpectedCritMultiplier(
			double critChancePercent,
			double critDamageRate,
			bool hasExecution
		) {
			double bonusRatio = Math.Max(0.0, critDamageRate * 0.01);
			if (hasExecution && critChancePercent > 100.0) {
				double execProb = Math.Clamp((critChancePercent - 100.0) * 0.01, 0.0, 1.0);
				return 1.0 + (bonusRatio * (1.0 + execProb));
			}
			double critProb = Math.Clamp(critChancePercent * 0.01, 0.0, 1.0);
			return 1.0 + (critProb * bonusRatio);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static double ComputeMaskedAverage(
			byte mask,
			double phys,
			double fire,
			double ice,
			double light
		) {
			double sum = 0.0;
			int count = 0;
			if ((mask & 0x01) != 0) {
				sum += phys;
				count++;
			}
			if ((mask & 0x02) != 0) {
				sum += fire;
				count++;
			}
			if ((mask & 0x04) != 0) {
				sum += ice;
				count++;
			}
			if ((mask & 0x08) != 0) {
				sum += light;
				count++;
			}
			return count > 0 ? (sum / count) : ((phys + fire + ice + light) * 0.25);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private double EvaluateConditionalStatMultiplier(
			double liveCritChance,
			double livePhys,
			double liveFire,
			double liveIce,
			double liveLight,
			double liveAttackSpeed,
			double liveAllDamageBonus
		) {
			if (CondMode == WeaponConditionalStatMode.None || CondDamageAmpPercent == 0f) {
				return 1.0;
			}
			if (CondTarget == WeaponConditionalStatTarget.AlwaysActive) {
				return Math.Max(0.1, 1.0 + (CondDamageAmpPercent * 0.01));
			}

			double currentVal = CondTarget switch {
				WeaponConditionalStatTarget.CriticalRaw => Math.Round(liveCritChance * 100.0),
				WeaponConditionalStatTarget.PhysicalDamage => livePhys,
				WeaponConditionalStatTarget.FireDamage => liveFire,
				WeaponConditionalStatTarget.IceDamage => liveIce,
				WeaponConditionalStatTarget.LightningDamage => liveLight,
				WeaponConditionalStatTarget.AttackSpeed => liveAttackSpeed,
				WeaponConditionalStatTarget.AllDamageBonus => liveAllDamageBonus,
				_ => 0.0
			};

			bool met = CondMode switch {
				WeaponConditionalStatMode.LessOrEqual => currentVal <= CondThreshold + 1e-4,
				WeaponConditionalStatMode.GreaterOrEqual => currentVal >= CondThreshold - 1e-4,
				WeaponConditionalStatMode.Equal => Math.Abs(currentVal - CondThreshold) <= 0.5,
				WeaponConditionalStatMode.NotEqual => Math.Abs(currentVal - CondThreshold) > 0.5,
				_ => false
			};

			return met ? Math.Max(0.1, 1.0 + (CondDamageAmpPercent * 0.01)) : 1.0;
		}
	}

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
		public bool HasDetailedWeaponProfile { get; }
		public WeaponCombatProfile WeaponProfile { get; }

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
			HasDetailedWeaponProfile = false;
			WeaponProfile = default;

			double critFactor = ComputeCritExpectedMultiplier();
			double elemFactor = 1.0 + (ElementalDamageBonusPercent * 0.01);
			BaseWeaponDps = (float)(BaseAttackDamage * AttacksPerSecond * critFactor * elemFactor);
		}

		public PlayerCombatBaseline(in WeaponCombatProfile weaponProfile) {
			HasDetailedWeaponProfile = true;
			WeaponProfile = weaponProfile;

			WeaponCombatEvaluation baseEval = weaponProfile.Evaluate(in CombatStatDelta.Zero, 1.0);
			double approxSwings = Math.Max(0.2, weaponProfile.BaseSwingRate);
			if (weaponProfile.FixedAttackSpeedMultiplier > 0f) {
				approxSwings = Math.Clamp(
					weaponProfile.BaseSwingRate * weaponProfile.FixedAttackSpeedMultiplier,
					0.2,
					24.0
				);
			} else {
				double asMult = 1.0
					+ ((weaponProfile.BaseAttackSpeedStat * (1.0 + weaponProfile.AttackSpeedAmplify)) * 0.01)
					+ weaponProfile.ExtraAttackSpeedBonus;
				approxSwings = Math.Clamp(weaponProfile.BaseSwingRate * Math.Clamp(asMult, 0.25, 8.0), 0.2, 24.0);
			}
			if (weaponProfile.MagazineCapacity > 0 && weaponProfile.EffectiveReloadSeconds > 0.01f) {
				double fireSec = weaponProfile.MagazineCapacity / approxSwings;
				approxSwings = weaponProfile.MagazineCapacity / (fireSec + weaponProfile.EffectiveReloadSeconds);
			}

			AttacksPerSecond = (float)approxSwings;
			BaseAttackDamage = (float)Math.Max(1.0, baseEval.WeaponAndAddonRawDps / Math.Max(0.2, approxSwings));
			CritChancePercent = Math.Clamp(
				weaponProfile.BaseCritChancePercent + weaponProfile.WeaponExtraCritChancePercent,
				0f,
				100f
			);
			CritDamageMultiplier = Math.Max(
				1.0f,
				1.0f + ((weaponProfile.BaseCritDamageRate + weaponProfile.WeaponExtraCritDamageRate) * 0.01f)
			);
			ElementalDamageBonusPercent = Math.Max(0f, weaponProfile.BaseAllDamageBonus);
			BaseWeaponDps = (float)Math.Max(1.0, baseEval.WeaponAndAddonEffectiveDps);
		}

		public double ComputeBaseWeaponRawDps() {
			if (HasDetailedWeaponProfile) {
				WeaponCombatEvaluation eval = WeaponProfile.Evaluate(in CombatStatDelta.Zero, 1.0);
				return eval.WeaponAndAddonRawDps > 1.0 ? eval.WeaponAndAddonRawDps : 20.0;
			}
			double rawDps = (double)BaseAttackDamage * AttacksPerSecond;
			return rawDps > 1.0 ? rawDps : 100.0;
		}

		public double ComputeCritExpectedMultiplier() {
			if (HasDetailedWeaponProfile) {
				return WeaponCombatProfile.ComputeExpectedCritMultiplier(
					WeaponProfile.BaseCritChancePercent,
					WeaponProfile.BaseCritDamageRate,
					WeaponProfile.HasExecution
				);
			}
			double clampedCritChance = Math.Clamp(CritChancePercent * 0.01, 0.0, 1.0);
			double effectiveCritDamage = Math.Max(1.0, (double)CritDamageMultiplier);
			return 1.0 + (clampedCritChance * (effectiveCritDamage - 1.0));
		}
	}

	public sealed class ComboCategoryInfo {
		public int CategoryId { get; }
		public string CategoryKey { get; }
		public string DisplayName { get; }
		public int StaticCount { get; }
		public int[] Breakpoints { get; }
		public float[] BreakpointStatBonuses { get; }
		public CombatStatDelta[] BreakpointStatDeltas { get; }
		public byte CategoryElementalKind { get; }
		public int HighestBreakpoint { get; }
		public bool IsUserTarget { get; }

		public ComboCategoryInfo(
			int categoryId,
			string categoryKey,
			string displayName,
			int staticCount,
			int[] breakpoints,
			bool isUserTarget,
			float[]? breakpointStatBonuses = null,
			CombatStatDelta[]? breakpointStatDeltas = null
		) {
			CategoryId = categoryId;
			CategoryKey = categoryKey ?? string.Empty;
			DisplayName = string.IsNullOrEmpty(displayName) ? CategoryKey : displayName;
			StaticCount = Math.Max(0, staticCount);
			Breakpoints = breakpoints ?? Array.Empty<int>();
			BreakpointStatBonuses = breakpointStatBonuses ?? Array.Empty<float>();
			CategoryElementalKind = ResolveCategoryElementalKind(CategoryKey);
			BreakpointStatDeltas = breakpointStatDeltas
				?? BuildDefaultBreakpointDeltas(Breakpoints, BreakpointStatBonuses, CategoryElementalKind);
			int maxBp = 0;
			for (int i = 0; i < Breakpoints.Length; i++) {
				if (Breakpoints[i] > maxBp) {
					maxBp = Breakpoints[i];
				}
			}
			HighestBreakpoint = maxBp;
			IsUserTarget = isUserTarget;
		}

		public static byte ResolveCategoryElementalKind(string? categoryKey) {
			if (string.IsNullOrEmpty(categoryKey)) {
				return 0;
			}
			if (string.Equals(categoryKey, "STURDY", StringComparison.OrdinalIgnoreCase)) {
				return 1;
			}
			if (string.Equals(categoryKey, "EMBER", StringComparison.OrdinalIgnoreCase)) {
				return 2;
			}
			if (string.Equals(categoryKey, "GLACIER", StringComparison.OrdinalIgnoreCase)) {
				return 3;
			}
			if (string.Equals(categoryKey, "MAGITECH", StringComparison.OrdinalIgnoreCase)) {
				return 4;
			}
			return 0;
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
						totalBonus += 6.0 + (b * 3.0);
					}
				}
			}

			if (unlimitedComboStat > 0 && HighestBreakpoint > 0 && totalCount > HighestBreakpoint) {
				int surplus = totalCount - HighestBreakpoint;
				totalBonus += surplus * Math.Clamp(unlimitedComboStat, 1, 4) * 3.5;
			}

			return totalBonus;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public CombatStatDelta EvaluateStatDelta(int totalCount, int unlimitedComboStat = 0) {
			if (totalCount <= 0) {
				return CombatStatDelta.Zero;
			}

			CombatStatDelta totalDelta = CombatStatDelta.Zero;
			int bpLen = Breakpoints.Length;
			for (int b = 0; b < bpLen; b++) {
				if (totalCount >= Breakpoints[b] && b < BreakpointStatDeltas.Length) {
					totalDelta = totalDelta.Add(in BreakpointStatDeltas[b]);
				}
			}

			if (unlimitedComboStat > 0 && HighestBreakpoint > 0 && totalCount > HighestBreakpoint) {
				int surplus = totalCount - HighestBreakpoint;
				float surplusBonus = surplus * Math.Clamp(unlimitedComboStat, 1, 4) * 2.0f;
				totalDelta = totalDelta.AddElementalByKind(CategoryElementalKind, surplusBonus);
			}

			return totalDelta;
		}

		private static CombatStatDelta[] BuildDefaultBreakpointDeltas(
			int[] breakpoints,
			float[] breakpointStatBonuses,
			byte elementalKind
		) {
			if (breakpoints.Length == 0) {
				return Array.Empty<CombatStatDelta>();
			}
			CombatStatDelta[] deltas = new CombatStatDelta[breakpoints.Length];
			for (int b = 0; b < breakpoints.Length; b++) {
				float fallbackVal = (b < breakpointStatBonuses.Length && breakpointStatBonuses[b] > 0f)
					? breakpointStatBonuses[b]
					: (elementalKind != 0 ? (2f + (b * 2f)) : (4f + (b * 2f)));
				deltas[b] = CombatStatDelta.Zero.AddElementalByKind(elementalKind, fallbackVal);
			}
			return deltas;
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
