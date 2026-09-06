using System;
using System.Collections.Generic;
using UnityEngine;

public class CustomerSpawner : MonoBehaviour
{
    [SerializeField] private CustomerStateManager customerPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private TableManager tableManager;
    [SerializeField] private DishRequestQueue requestQueue;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private TipBox tipBox;
    [SerializeField] private CustomerIntervalCalculater intervalCalculater = new();
    [SerializeField] private RuntimeAnimatorController controller;
    [SerializeField] private Dirty dirtyPrefab;
    [SerializeField] private Combo combo;
    [SerializeField] private DrinkZone drinkZone;
    [SerializeField] private ServiceWarningMessage warningMessage;


    [Header("랜덤 동물 범위")]
    [SerializeField] List<GameObject> animalPrefabs = new();
    [SerializeField] float animalSize;

    
    private float spawnTimer;
    private float nextInitialSpawnInterval;
    private int initialSpawnRemaining;
    private int targetCustomerCount;
    private int spawnedCustomerCount;
    private float completedLifecycleProgress;
    private readonly List<CustomerStateManager> activeCustomers = new();
    private bool serviceStarted;
    private bool serviceEnd;
    public event Action CustomerSpawned;
    public int SpawnCount => Mathf.Max(0, targetCustomerCount - spawnedCustomerCount);
    public float Progress
    {
        get
        {
            if (targetCustomerCount <= 0)
                return 1f;

            float totalProgress = completedLifecycleProgress;

            for (int i = 0; i < activeCustomers.Count; i++)
                totalProgress += activeCustomers[i].LifecycleProgress;

            return Mathf.Clamp01(totalProgress / targetCustomerCount);
        }
    }

    private void Awake()
    {
        if (exitPoint == null)
        {
            exitPoint = transform;
        }
        if (tableManager == null)
        {
            tableManager = FindFirstObjectByType<TableManager>();
        }
        if(requestQueue == null)
        {
            requestQueue = FindFirstObjectByType<DishRequestQueue>();
        }
    }

    private void OnEnable()
    {
        GameManager.Instance.Service.Events.Subscribe(ServiceEventType.BeforeLoopStarted, PrepareService);
        GameManager.Instance.Service.Events.Subscribe(ServiceEventType.LoopStarted, ServiceStart);
        GameManager.Instance.Service.Events.Subscribe(ServiceEventType.LoopEnded, ServiceEnd);
    }

    private void Start()
    {
        

        
    }

    private void Update()
    {
        ServiceManager service = GameManager.Instance.Service;
        ServiceProgress progress = service.Progress;
        progress.SetValue(Progress);

        if (!serviceStarted
            || serviceEnd
            || service.IsPause
            || spawnedCustomerCount >= targetCustomerCount)
            return;

        intervalCalculater.Tick(Time.deltaTime);

        if (initialSpawnRemaining > 0)
        {
            RunSpawnTimer(nextInitialSpawnInterval);
            return;
        }

        int usableSeatCount = tableManager.UsableSeatCount;

        if (!intervalCalculater.TryGetInterval(
            tableManager.WaitingCount,
            usableSeatCount,
            activeCustomers.Count,
            out float arrivalInterval))
        {
            return;
        }

        RunSpawnTimer(arrivalInterval);
    }

    private void RunSpawnTimer(float interval)
    {
        spawnTimer += Time.deltaTime;

        if (spawnTimer < interval)
            return;

        SpawnCustomer();
        spawnTimer = 0f;

        if (initialSpawnRemaining > 0)
        {
            initialSpawnRemaining--;

            if (initialSpawnRemaining > 0)
                nextInitialSpawnInterval = intervalCalculater.GetInitialInterval();
        }
    }

    private void SpawnCustomer()
    {
        CustomerStateManager customer =
            Instantiate(customerPrefab, spawnPoint.position, Quaternion.identity);

        GameObject animal =
            Instantiate(animalPrefabs[UnityEngine.Random.Range(0, animalPrefabs.Count)], customer.transform);

        customer.Initialize(exitPoint, tableManager, tipBox, requestQueue, dirtyPrefab, combo, drinkZone, warningMessage);
        animal.transform.localScale = Vector3.one * animalSize;
        animal.transform.localPosition += Vector3.down * (1 - animalSize) * 2;
        
        Animator animator = animal.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        customer.SetAnimator(animator);

        customer.ProcessingCompleted += CustomerProcessed;
        customer.LifecycleFinished += CustomerFinished;

        activeCustomers.Add(customer);
        spawnedCustomerCount++;
        GameManager.Instance.Service.Progress.SetValue(Progress);
        GameManager.Instance.Service.ResultBuilder.RecordCustomer();
        CustomerSpawned?.Invoke();
    }

    private void PrepareService()
    {
        targetCustomerCount = Mathf.RoundToInt(
            GameManager.Instance.Upgrade.RuntimeStat.Service
                .Get(ServiceStatType.CustomerCount));
        spawnedCustomerCount = 0;
        completedLifecycleProgress = 0f;
        activeCustomers.Clear();
        intervalCalculater.Reset();
        GameManager.Instance.Service.Progress.SetValue(Progress);
    }

    private void ServiceStart()
    {
        serviceStarted = true;
        serviceEnd = false;
        spawnTimer = 0f;
        nextInitialSpawnInterval = intervalCalculater.InitialDelay;
        initialSpawnRemaining = Mathf.Min(
            targetCustomerCount,
            intervalCalculater.CalculateInitialCustomerCount(tableManager.UsableSeatCount));

        if (targetCustomerCount == 0)
            GameManager.Instance.Service.EndLoop();
    }

    private void CustomerProcessed()
    {
        intervalCalculater.RecordProcessed();
    }

    private void CustomerFinished(CustomerStateManager customer)
    {
        completedLifecycleProgress += customer.LifecycleProgress;
        activeCustomers.Remove(customer);
        customer.ProcessingCompleted -= CustomerProcessed;
        customer.LifecycleFinished -= CustomerFinished;
        GameManager.Instance.Service.Progress.SetValue(Progress);

        if (spawnedCustomerCount >= targetCustomerCount
            && activeCustomers.Count == 0)
        {
            GameManager.Instance.Service.EndLoop();
        }
    }
    
    private void ServiceEnd()
    {
        serviceStarted = false;
        serviceEnd = true;
    }

    private void OnDisable()
    {
        GameManager.Instance.Service.Events.Unsubscribe(ServiceEventType.BeforeLoopStarted, PrepareService);
        GameManager.Instance.Service.Events.Unsubscribe(ServiceEventType.LoopStarted, ServiceStart);
        GameManager.Instance.Service.Events.Unsubscribe(ServiceEventType.LoopEnded, ServiceEnd);
    }
}
