using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HarvestEmployeeResolver : MonoBehaviour
{
    #region Fields

    private const float CutterRangeMultiplier = 1.5f;
    private const float CutterRangeBuffDuration = 5f;
    private const float CutterOverloadCoolDown = 10f;
    private const float ChargeDuration = 1.5f;
    private const float ChargeDamageMultiplier = 100f;
    private const float ChargeCoolDown = 8f;
    private const float YieldChancePerUpgrade = 0.05f;
    private const int HarvestsPerGoldBonus = 20;
    private const int GoldBonusAmount = 10;

    [SerializeField] private GameObject harvester1Sidecar;
    [SerializeField] private GameObject harvester2Sidecar;

    private readonly SkillBase[] skills = new SkillBase[2];
    private CropCutter harvester1Cutter;
    private CropCutter harvester2Cutter;
    private float harvester1OriginalRange;
    private float harvester2OriginalRange;
    private TractorController tractorController;
    private CropCutter tractorCutter;
    private Coroutine cutterRangeBuffCoroutine;
    private Coroutine chargeCoroutine;
    private float extraYieldChance;
    private float mainCropMeatChance;
    private float vegetableMeatChance;
    private float chargeCoolDownMultiplier;
    private float cutterOverloadCoolDownMultiplier;
    private int harvestedCropCount;
    private bool cutterOverloadUnlocked;
    private bool chargeUnlocked;
    private bool harvestGoldBonusEnabled;
    private bool isConfigured;
    private bool isGrindSFXPlaying;

    #endregion

    #region Skill Lifecycle

    private void Awake()
    {
        tractorController = GetComponent<TractorController>();
        tractorCutter = tractorController.Cutter;

        skills[0] = new SkillBase(CutterOverloadCoolDown, false);
        skills[1] = new SkillBase(ChargeCoolDown, false);

        skills[0].SetExecute(ExecuteCutterRangeBuff);
        skills[1].SetExecute(ExecuteCharge);
    }

    private void Start()
    {
        EnsureConfigured();

        harvester1Cutter = ResolveSidecar(
            EmployeeType.Harvester_1,
            harvester1Sidecar);
        harvester2Cutter = ResolveSidecar(
            EmployeeType.Harvester_2,
            harvester2Sidecar);

        if (harvester1Cutter != null)
        {
            harvester1OriginalRange = harvester1Cutter.Range;
        }

        if (harvester2Cutter != null)
        {
            harvester2OriginalRange = harvester2Cutter.Range;
        }

    }

    private void ResolveUpgradeEffects()
    {
        extraYieldChance = YieldChancePerUpgrade * (
            GetReachedUpgradeCount(EmployeeType.Harvester_1, 2, 4)
            + GetReachedUpgradeCount(EmployeeType.Harvester_2, 2, 5)
            + GetReachedUpgradeCount(EmployeeType.Harvester_3, 2, 5));

        cutterOverloadUnlocked = HasUpgrade(EmployeeType.Harvester_1, 3);
        cutterOverloadCoolDownMultiplier = HasUpgrade(EmployeeType.Harvester_2, 5)
            ? 0.5f
            : 1f;

        mainCropMeatChance =
            (HasUpgrade(EmployeeType.Harvester_1, 5) ? 0.10f : 0f)
            + (HasUpgrade(EmployeeType.Harvester_2, 3) ? 0.10f : 0f);
        vegetableMeatChance = HasUpgrade(EmployeeType.Harvester_2, 1)
            ? 0.10f
            : 0f;

        harvestGoldBonusEnabled = HasUpgrade(EmployeeType.Harvester_3, 1);
        chargeUnlocked = HasUpgrade(EmployeeType.Harvester_3, 3);
        chargeCoolDownMultiplier = HasUpgrade(EmployeeType.Harvester_3, 5)
            ? 0.5f
            : 1f;
    }

    private void ConfigureSkills()
    {
        skills[0].Configure(
            CutterOverloadCoolDown * cutterOverloadCoolDownMultiplier,
            cutterOverloadUnlocked);
        skills[1].Configure(
            ChargeCoolDown * chargeCoolDownMultiplier,
            chargeUnlocked);
    }

    private void EnsureConfigured()
    {
        if (isConfigured)
        {
            return;
        }

        ResolveUpgradeEffects();
        ConfigureSkills();
        isConfigured = true;
    }

    private bool HasUpgrade(EmployeeType employeeType, int requiredLevel)
    {
        return GameManager.Instance.Upgrade.RuntimeLevel.Get(employeeType)
            >= requiredLevel;
    }

    private int GetReachedUpgradeCount(
        EmployeeType employeeType,
        int firstLevel,
        int lastLevel)
    {
        int level = GameManager.Instance.Upgrade.RuntimeLevel.Get(employeeType);
        return Mathf.Clamp(level - firstLevel + 1, 0, lastLevel - firstLevel + 1);
    }

    private void Update()
    {
        for (int i = 0; i < skills.Length; i++)
        {
            skills[i].Tick();
        }

        UpdateGrindSFX();
    }

    private void UpdateGrindSFX()
    {
        bool shouldPlay = GameManager.Instance.Harvest.IsRunning
            && (tractorCutter.IsCutting
                || (harvester1Cutter != null
                    && harvester1Cutter.gameObject.activeInHierarchy
                    && harvester1Cutter.IsCutting)
                || (harvester2Cutter != null
                    && harvester2Cutter.gameObject.activeInHierarchy
                    && harvester2Cutter.IsCutting));

        if (isGrindSFXPlaying == shouldPlay)
            return;

        isGrindSFXPlaying = shouldPlay;

        if (shouldPlay)
        {
            GameManager.Instance.Utility.Audio.PlayLoopSFX(
                SFXType.Harvest_Grind);
        }
        else
        {
            GameManager.Instance.Utility.Audio.StopLoopSFX(
                SFXType.Harvest_Grind);
        }
    }

    public SkillBase GetSkill(int index)
    {
        if (index < 0 || index >= skills.Length)
        {
            return null;
        }

        EnsureConfigured();
        return skills[index];
    }

    #endregion

    #region Harvest Rewards

    public List<GroceryAmount> ResolveHarvested(HarvestDataSO harvestData)
    {
        List<GroceryAmount> bonusRewards = new();

        TryAddExtraYield(harvestData, bonusRewards);
        TryAddMeatBonus(harvestData, bonusRewards);

        if (!harvestData.IsMove)
        {
            ResolveHarvestGoldBonus();
        }

        return bonusRewards;
    }

    private void TryAddExtraYield(
        HarvestDataSO harvestData,
        List<GroceryAmount> bonusRewards)
    {
        if (Random.value >= extraYieldChance)
        {
            return;
        }

        for (int i = 0; i < harvestData.Rewards.Count; i++)
        {
            GroceryAmount reward = new(
                harvestData.Rewards[i].grocery,
                1);
            GameManager.Instance.StockManager.AddGrocery(reward);
            bonusRewards.Add(reward);
        }
    }

    private void TryAddMeatBonus(
        HarvestDataSO harvestData,
        List<GroceryAmount> bonusRewards)
    {
        float chance = IsMainCrop(harvestData.HarvestType)
            ? mainCropMeatChance
            : IsVegetable(harvestData.HarvestType)
                ? vegetableMeatChance
                : 0f;

        if (Random.value >= chance)
        {
            return;
        }

        int tier = GroceryDataDB.GetData(
            harvestData.Rewards[0].grocery).Tier;
        GroceryAmount reward = new(ResolveMeatReward(tier), 1);
        GameManager.Instance.StockManager.AddGrocery(reward);
        bonusRewards.Add(reward);
    }

    private void ResolveHarvestGoldBonus()
    {
        if (!harvestGoldBonusEnabled)
        {
            return;
        }

        harvestedCropCount++;

        if (harvestedCropCount < HarvestsPerGoldBonus)
        {
            return;
        }

        int bonusCount = harvestedCropCount / HarvestsPerGoldBonus;
        harvestedCropCount %= HarvestsPerGoldBonus;
        GameManager.Instance.StockManager.AddCurrency(
            GoldBonusAmount * bonusCount);
    }

    private static bool IsMainCrop(HarvestType harvestType)
    {
        return harvestType is HarvestType.Rice
            or HarvestType.Wheat
            or HarvestType.Potato
            or HarvestType.Corn;
    }

    private static bool IsVegetable(HarvestType harvestType)
    {
        return harvestType is HarvestType.Carrot
            or HarvestType.Onion
            or HarvestType.Cabbage
            or HarvestType.Tomato;
    }

    private static GroceryType ResolveMeatReward(int tier)
    {
        return tier switch
        {
            1 => GroceryType.Chicken,
            2 => GroceryType.Beef,
            3 => GroceryType.Lamb,
            _ => Random.Range(0, 3) switch
            {
                0 => GroceryType.Chicken,
                1 => GroceryType.Beef,
                _ => GroceryType.Lamb
            }
        };
    }

    #endregion

    #region Cutter Range Skill

    private void ExecuteCutterRangeBuff()
    {
        if (cutterRangeBuffCoroutine != null)
        {
            StopCoroutine(cutterRangeBuffCoroutine);
            tractorCutter.ApplyRangeBoost(1f - CutterRangeMultiplier);
            SetCutterTargetRanges(1f);
        }

        cutterRangeBuffCoroutine = StartCoroutine(CutterRangeBuff());
    }

    private IEnumerator CutterRangeBuff()
    {
        tractorCutter.ApplyRangeBoost(CutterRangeMultiplier - 1f);
        SetCutterTargetRanges(CutterRangeMultiplier);

        yield return new WaitForSeconds(CutterRangeBuffDuration);

        tractorCutter.ApplyRangeBoost(1f - CutterRangeMultiplier);
        SetCutterTargetRanges(1f);
        cutterRangeBuffCoroutine = null;
    }

    private void SetCutterTargetRanges(float multiplier)
    {
        bool isOverloadActive = multiplier > 1f;
        tractorCutter.SetOverloadActive(isOverloadActive);

        if (harvester1Cutter != null
            && (harvester1Cutter.gameObject.activeInHierarchy || !isOverloadActive))
        {
            harvester1Cutter.SetOverloadActive(isOverloadActive);
            harvester1Cutter.SetTargetRange(
                harvester1OriginalRange * multiplier);
        }

        if (harvester2Cutter != null
            && (harvester2Cutter.gameObject.activeInHierarchy || !isOverloadActive))
        {
            harvester2Cutter.SetOverloadActive(isOverloadActive);
            harvester2Cutter.SetTargetRange(
                harvester2OriginalRange * multiplier);
        }
    }

    #endregion

    #region Charge Skill

    private void ExecuteCharge()
    {
        if (tractorController == null || tractorCutter == null)
        {
            return;
        }

        chargeCoroutine = StartCoroutine(Charge());
    }

    private IEnumerator Charge()
    {
        tractorController.SetCharging(true);
        tractorCutter.SetDamageMultiplier(ChargeDamageMultiplier);
        harvester1Cutter?.SetDamageMultiplier(ChargeDamageMultiplier);
        harvester2Cutter?.SetDamageMultiplier(ChargeDamageMultiplier);

        yield return new WaitForSeconds(ChargeDuration);

        RestoreChargeState();
        chargeCoroutine = null;
    }

    private void OnDisable()
    {
        if (cutterRangeBuffCoroutine != null)
        {
            StopCoroutine(cutterRangeBuffCoroutine);
            tractorCutter.ApplyRangeBoost(1f - CutterRangeMultiplier);
            SetCutterTargetRanges(1f);
            cutterRangeBuffCoroutine = null;
        }

        if (isGrindSFXPlaying)
        {
            isGrindSFXPlaying = false;
            GameManager.Instance.Utility.Audio.StopLoopSFX(
                SFXType.Harvest_Grind);
        }

        if (chargeCoroutine != null)
        {
            StopCoroutine(chargeCoroutine);
            chargeCoroutine = null;
        }

        RestoreChargeState();
    }

    private void RestoreChargeState()
    {
        tractorController?.SetCharging(false);
        tractorCutter?.SetDamageMultiplier(1f);
        harvester1Cutter?.SetDamageMultiplier(1f);
        harvester2Cutter?.SetDamageMultiplier(1f);
    }

    #endregion

    #region Sidecar

    private CropCutter ResolveSidecar(
        EmployeeType employeeType,
        GameObject sidecar)
    { 

        int level = GameManager.Instance.Upgrade.RuntimeLevel.Get(employeeType);
        bool isUnlocked = level >= 1;
        sidecar.SetActive(isUnlocked);

        if (!isUnlocked)
        {
            return null;
        }

        CropCutter cutter = sidecar.GetComponentInChildren<CropCutter>(true);

        HarvestRuntimeStat harvestStat =
            GameManager.Instance?.Upgrade?.RuntimeStat?.Harvest;

        if (harvestStat != null)
        {
            cutter.ApplyUpgradeStats(
                harvestStat.Get(HarvestStatType.SawSize),
                harvestStat.Get(HarvestStatType.SawSpeed),
                harvestStat.Get(HarvestStatType.SawSharpness));
        }

        cutter.Initialize(tractorController.GridChunkHandler);
        tractorController.GridChunkHandler.Streamer.AddLoadingTarget(
            sidecar.transform);

        return cutter;
    }

    #endregion
}
