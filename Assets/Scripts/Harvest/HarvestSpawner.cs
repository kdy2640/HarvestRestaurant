using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(GridChunkHandler))]
[RequireComponent(typeof(ItemSpawner))]
public sealed class HarvestSpawner : MonoBehaviour
{
    private const float StageBoundaryEpsilon = 0.01f;

    [SerializeField] private HarvestActor cropPrefab;
    [SerializeField] private Transform player;
    [SerializeField] private GroceryGainRoutine groceryGainRoutine;
    [SerializeField] private float spawnHeight;
    [SerializeField] private List<HarvestActor> spawnedCrops = new();

    private GridChunkHandler gridChunkHandler;
    private ItemSpawner itemSpawner;
    private HarvestEmployeeResolver employeeResolver;
    private HarvestManager harvestManager;
    private bool hasSpawnedPigs;
    private bool useTriggerDetection;
    private readonly List<HarvestActor> spawnedPigs = new();

    public void SetTriggerDetection(bool enabled)
    {
        useTriggerDetection = enabled;
        foreach (HarvestActor crop in spawnedCrops)
        {
            crop.SetTriggerDetection(enabled);
        }
    }

    private void Awake()
    {
        gridChunkHandler = GetComponent<GridChunkHandler>();
        itemSpawner = GetComponent<ItemSpawner>();
        employeeResolver = player.GetComponent<HarvestEmployeeResolver>();
    }

    private void Start()
    {
        Initialize();
        harvestManager = GameManager.Instance.Harvest;
        harvestManager.Events.Subscribe(
            HarvestEventType.LoopStarted,
            SpawnPig);
    }

    private void OnDestroy()
    {
        harvestManager.Events.Unsubscribe(
            HarvestEventType.LoopStarted,
            SpawnPig);
    }

    public void Initialize()
    {
        hasSpawnedPigs = false;
        ClearSpawnedCrops();

        gridChunkHandler.Streamer.BeginLoading(player, this, itemSpawner);
    }

    private void SpawnPig()
    {
        if (hasSpawnedPigs)
            return;

        int pigCount = GameManager.Instance.Upgrade.RuntimeLevel.Get(
            HarvestUpgradeType.GoldenPigRadar);

        if (pigCount <= 0)
            return;

        hasSpawnedPigs = true;

        Rect area = gridChunkHandler.Geometry.Area;
        float stageMinZ = StageDataDB.GetData(StageType.Stage_2).ZStart;
        float stageMaxZ = StageDataDB.GetData(StageType.Stage_3).ZStart
            - StageBoundaryEpsilon;

        for (int i = 0; i < pigCount; i++)
        {
            Vector2 localPosition = new(
                Random.Range(area.xMin, area.xMax),
                Random.Range(stageMinZ, stageMaxZ));

            HarvestActor spawnedPig = CreateCrop(
                HarvestType.Pig,
                StageType.Stage_2,
                localPosition,
                transform);
            spawnedPigs.Add(spawnedPig);
        }
    }

    public void SpawnCrop(
        HarvestType type,
        StageType stageType,
        Vector2 localPosition,
        Transform parent)
    {
        CreateCrop(type, stageType, localPosition, parent);
    }

    private void ClearSpawnedCrops()
    {
        foreach (HarvestActor crop in spawnedCrops)
        {
            gridChunkHandler.Registry.Unregister(crop.transform);
        }

        foreach (HarvestActor spawnedPig in spawnedPigs)
        {
            if (spawnedPig != null)
                Destroy(spawnedPig.gameObject);
        }

        spawnedPigs.Clear();
        spawnedCrops.Clear();
        gridChunkHandler.Streamer.Reset();
    }

    private HarvestActor CreateCrop(
        HarvestType type,
        StageType stageType,
        Vector2 localPosition,
        Transform parent)
    {
        Vector3 worldPosition = transform.TransformPoint(
            new Vector3(localPosition.x, spawnHeight, localPosition.y));
        HarvestActor crop = Instantiate(
            cropPrefab,
            worldPosition,
            transform.rotation,
            parent);

        crop.name = "Crop";
        crop.Init(
            type,
            stageType,
            player,
            gridChunkHandler,
            employeeResolver,
            groceryGainRoutine);
        crop.SetTriggerDetection(useTriggerDetection);
        spawnedCrops.Add(crop);
        return crop;
    }
}
