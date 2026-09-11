using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(HPHandler))]
[RequireComponent(typeof(HarvestPresenter))]
[RequireComponent(typeof(AnimalStateController))]
public sealed class HarvestActor : MonoBehaviour
{
    [SerializeField] private HPHandler hpHandler;
    [SerializeField] private HarvestPresenter presenter;
    [SerializeField] private HarvestMover mover;
    [SerializeField] private AnimalStateController animalStateController;
    [SerializeField] private SphereCollider detectionCollider;

    private HarvestDataSO harvestDataSO;
    private HarvestEmployeeResolver employeeResolver;
    private GroceryGainRoutine groceryGainRoutine;
    private ChunkRegistry registry;
    private bool isInitialized;
    private bool isDying;
    private bool useTriggerDetection;

    public float CurrentHp => hpHandler.nowHp;
    public bool IsHarvestable => isInitialized && !isDying && isActiveAndEnabled;

    public void SetTriggerDetection(bool enabled)
    {
        useTriggerDetection = enabled;
        detectionCollider.enabled = enabled && IsHarvestable;
    }

    public void Init(
        HarvestType type,
        StageType stageType,
        Transform player,
        GridChunkHandler gridChunkHandler,
        HarvestEmployeeResolver resolver,
        GroceryGainRoutine gainRoutine)
    {
        registry = gridChunkHandler.Registry;
        employeeResolver = resolver;
        groceryGainRoutine = gainRoutine;
        harvestDataSO = HarvestDataDB.GetData(type);
        isDying = false;

        if (hpHandler == null)
        {
            hpHandler = GetComponent<HPHandler>();
        }

        if (presenter == null)
        {
            presenter = GetComponent<HarvestPresenter>();
        }

        hpHandler.SubscribeDying(OnDied); 
        hpHandler.Init(harvestDataSO.HP);

        GameObject solid = Instantiate(harvestDataSO.SolidPrefab, transform);
        presenter.Init(solid);
        isInitialized = true;

        if (harvestDataSO.IsMove)
        {
            if (mover == null)
                mover = GetComponent<HarvestMover>();

            if (mover == null)
            {
                Debug.LogError("[HarvestActor] HarvestMover is not assigned.", this);
                return;
            }

            mover.Init(
                stageType,
                gridChunkHandler,
                harvestDataSO.HarvestType == HarvestType.Pig);

            Animator animator = solid.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animalStateController.enabled = true;
            animalStateController.Init(
                player,
                mover,
                animator,
                harvestDataSO.AnimalStat,
                harvestDataSO.AnimatorController,
                harvestDataSO.HarvestType);
        }
        else
        {
            if (mover != null)
            {
                mover.enabled = false;
            }

            animalStateController.enabled = false;
        }

        if (gameObject.activeInHierarchy)
        {
            registry.ActiveActorCount++;
            if (harvestDataSO.IsMove)
                registry.ActiveMovingActorCount++;
            registry.Register(transform);
        }
    }

    private void OnEnable()
    {
        detectionCollider.enabled = useTriggerDetection && IsHarvestable;
        if (isInitialized && registry != null)
        {
            registry.ActiveActorCount++;
            if (harvestDataSO.IsMove)
                registry.ActiveMovingActorCount++;
            registry.Register(transform);
        }
    }

    private void OnDestroy()
    {
        if (hpHandler != null && presenter != null)
        { 
            hpHandler.UnSubscribeDying(OnDied);
        }
    }

    private void OnDisable()
    {
        detectionCollider.enabled = false;
        if (registry != null)
        {
            if (isInitialized)
            {
                registry.ActiveActorCount--;
                if (harvestDataSO.IsMove)
                    registry.ActiveMovingActorCount--;
            }
            registry.Unregister(transform);
        }
    }

    public void TakeDamage(float damage)
    {
        if (damage <= 0f || !gameObject.activeInHierarchy || isDying)
        {
            return;
        }

        if (harvestDataSO.IsMove)
        {
            animalStateController.PlayHit();
        }
        else
        {
            presenter.PlayHit();
        }

        hpHandler.TakeDamage(damage);
    }

    private void OnDied()
    {
        Vector3 gainPoint = transform.position;

        isDying = true;
        registry.HarvestedCount++;
        detectionCollider.enabled = false;
        registry.Unregister(transform);

        if (harvestDataSO.HarvestType == HarvestType.Pig)
        {
            GrantGoldenPigReward(gainPoint);
            GameManager.Instance.Utility.Audio.PlaySFX(
                SFXType.Harvest_GoldenPigCollected);
        }
        else
        {
            GameManager.Instance.StockManager.AddGrocery(harvestDataSO.Rewards);
            List<GroceryAmount> bonusRewards =
                employeeResolver.ResolveHarvested(harvestDataSO);
            groceryGainRoutine.Play(harvestDataSO.Rewards, gainPoint);

            if (bonusRewards.Count > 0)
            {
                groceryGainRoutine.Play(bonusRewards, gainPoint);
            }
        }

        if (harvestDataSO.IsMove)
        {
            animalStateController.SetState(AnimalStateType.Dead);
        }
        else
        {
            presenter.PlayDeath();
        }
    }

    private void GrantGoldenPigReward(Vector3 gainPoint)
    {
        int radarLevel = GameManager.Instance.Upgrade.RuntimeLevel.Get(
            HarvestUpgradeType.GoldenPigRadar);

        int firstAmount;
        int secondAmount;
        int thirdAmount;
        int goldAmount;

        switch (radarLevel)
        {
            case 1:
                firstAmount = 10;
                secondAmount = 5;
                thirdAmount = 3;
                goldAmount = 20;
                break;
            case 2:
                firstAmount = 20;
                secondAmount = 10;
                thirdAmount = 5;
                goldAmount = 50;
                break;
            case 3:
                firstAmount = 30;
                secondAmount = 15;
                thirdAmount = 10;
                goldAmount = 100;
                break;
            case 4:
                firstAmount = 50;
                secondAmount = 30;
                thirdAmount = 20;
                goldAmount = 500;
                break;
            default:
                Debug.LogError(
                    $"[HarvestActor] Invalid golden pig radar level: {radarLevel}",
                    this);
                return;
        }

        StageDataSO stageData = StageDataDB.GetData(
            (StageType)(radarLevel - 1));

        List<GroceryAmount> rewards = new()
        {
            new GroceryAmount(stageData.RewardList[0], firstAmount),
            new GroceryAmount(stageData.RewardList[1], secondAmount),
            new GroceryAmount(stageData.RewardList[2], thirdAmount)
        };

        GameManager.Instance.StockManager.AddGrocery(rewards);
        GameManager.Instance.StockManager.AddCurrency(goldAmount);
        groceryGainRoutine.PlayBundled(rewards, gainPoint);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        hpHandler = GetComponent<HPHandler>();
        presenter = GetComponent<HarvestPresenter>();
        mover = GetComponent<HarvestMover>();
        animalStateController = GetComponent<AnimalStateController>();
    }
#endif
}
