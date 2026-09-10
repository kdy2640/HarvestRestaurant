using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CropCutter : MonoBehaviour
{
    private const float CutterOffset = 0.7f;
    private const float OverloadDamageBoostAmount = 0.3f;
    private static readonly ProfilerMarker ChunkQueryMarker = new("Harvest.ChunkQuery");
    private static readonly ProfilerMarker TriggerQueryMarker = new("Harvest.TriggerTargets");
    private static readonly ProfilerMarker ProcessTargetsMarker = new("Harvest.ProcessTargets");

    [SerializeField] private GridChunkHandler gridChunkHandler;
    [SerializeField] private CutterViewer cutterViewer;
    [SerializeField] private BoxCollider detectionTrigger;
    [SerializeField, Min(0.01f)] private float triggerHeight = 20f;
    [SerializeField, Min(0f)] private float cuttingRange = 0.5f;
    [SerializeField, Min(0f)] private float rangeLerpSpeed = 8f;
    [SerializeField, Min(0f)] private float damage = 1f;
    [SerializeField, Min(0f)] private float damageDelay = 0.25f;
    [SerializeField, Range(0f, 1f)] private float cuttingMoveSpeedMultiplier = 0.35f;
    [SerializeField, Range(0f, 1f)] private float cuttingDistanceSafetyRatio = 0.75f;

    private readonly Dictionary<int, float> nextDamageTimes = new();
    private readonly List<Transform> triggerTargets = new();
    private bool useTriggerDetection;
    private float cuttingUntilTime;
    private float cuttingSpeedLimit = float.PositiveInfinity;
    private float damageMultiplier = 1f;
    private float baseRange;
    private float baseDamage;
    private float rangeBoostAmount;
    private float damageBoostAmount;
    private bool isOverloadActive;

    public bool IsCutting => Time.time <= cuttingUntilTime;
    public float Range => cuttingRange;
    public float TargetRange { get; private set; }
    public bool UsesTriggerDetection => useTriggerDetection;
    public int TriggerCandidateCount => triggerTargets.Count;
    public int LastDetectedTargetCount { get; private set; }
    public float MoveSpeedMultiplier =>
        IsCutting ? cuttingMoveSpeedMultiplier : 1f;
    public float CuttingSpeedLimit =>
        IsCutting ? cuttingSpeedLimit : float.PositiveInfinity;

    public void Initialize(GridChunkHandler handler)
    {
        gridChunkHandler = handler;
    }

    public void SetTriggerDetection(bool enabled)
    {
        if (useTriggerDetection == enabled)
            return;

        useTriggerDetection = enabled;
        triggerTargets.Clear();
        LastDetectedTargetCount = 0;
        detectionTrigger.enabled = false;
        UpdateTriggerSize();
        detectionTrigger.enabled = enabled && isActiveAndEnabled;
    }

    private void OnEnable()
    {
        detectionTrigger.enabled = useTriggerDetection;
    }

    private void OnDisable()
    {
        detectionTrigger.enabled = false;
        triggerTargets.Clear();
        LastDetectedTargetCount = 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!useTriggerDetection || !other.CompareTag("Harvestable"))
            return;

        if (!triggerTargets.Contains(other.transform))
            triggerTargets.Add(other.transform);
    }

    private void OnTriggerExit(Collider other)
    {
        if (useTriggerDetection)
            triggerTargets.Remove(other.transform);
    }

    private void UpdateTriggerSize()
    {
        // 높이 차이로 후보를 놓치지 않는 박스. 최종 판정은 기존 XZ 원을 사용한다.
        Vector3 scale = transform.lossyScale;
        detectionTrigger.size = new Vector3(
            cuttingRange * 2f / Mathf.Abs(scale.x),
            triggerHeight / Mathf.Abs(scale.y),
            cuttingRange * 2f / Mathf.Abs(scale.z));
    }

    public void ApplyUpgradeStats(
        float range,
        float attacksPerSecond,
        float baseDamage)
    {
        baseRange = Mathf.Max(0f, range);
        TargetRange = baseRange * (1f + rangeBoostAmount);
        ApplyRange(TargetRange);

        damageDelay = attacksPerSecond > 0f
            ? 1f / attacksPerSecond
            : float.MaxValue;
        this.baseDamage = Mathf.Max(0f, baseDamage);
        damage = this.baseDamage * (1f + damageBoostAmount);
    }

    private void Awake()
    {
        baseRange = cuttingRange;
        baseDamage = damage;
        TargetRange = cuttingRange;
        ApplyRange(cuttingRange);
    }

    private void Update()
    {
        if (Mathf.Approximately(cuttingRange, TargetRange))
        {
            return;
        }

        float nextRange = Mathf.Lerp(
            cuttingRange,
            TargetRange,
            rangeLerpSpeed * Time.deltaTime);

        if (Mathf.Abs(nextRange - TargetRange) < 0.001f)
        {
            nextRange = TargetRange;
        }

        ApplyRange(nextRange);
    }

    private void OnValidate()
    {
        cuttingRange = Mathf.Max(0f, cuttingRange);
        rangeLerpSpeed = Mathf.Max(0f, rangeLerpSpeed);
        cuttingDistanceSafetyRatio = Mathf.Clamp01(cuttingDistanceSafetyRatio);
        TargetRange = cuttingRange;

        ApplyRange(cuttingRange);
    }

    public void SetTargetRange(float range)
    {
        TargetRange = Mathf.Max(0f, range);
    }

    public void SetDamageMultiplier(float multiplier)
    {
        damageMultiplier = Mathf.Max(0f, multiplier);
    }

    public void ApplyRangeBoost(float amount)
    {
        rangeBoostAmount += amount;
        SetTargetRange(baseRange * (1f + rangeBoostAmount));
    }

    public void ApplyDamageBoost(float amount)
    {
        damageBoostAmount += amount;
        damage = baseDamage * (1f + damageBoostAmount);
        RefreshBuffVisual();
    }

    public void SetOverloadActive(bool isActive)
    {
        if (isOverloadActive == isActive)
            return;

        isOverloadActive = isActive;
        ApplyDamageBoost(isActive ? OverloadDamageBoostAmount : -OverloadDamageBoostAmount);
    }

    private void RefreshBuffVisual()
    {
        cutterViewer.SetBuffTint(isOverloadActive || damageBoostAmount > 0f);
    }

    private void ApplyRange(float range)
    {
        cuttingRange = Mathf.Max(0f, range);

        Vector3 localPosition = transform.localPosition;
        localPosition.z = CutterOffset + cuttingRange;
        transform.localPosition = localPosition;

        cutterViewer?.SetRange(cuttingRange);
        if (Application.isPlaying && useTriggerDetection)
            UpdateTriggerSize();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            transform.position,
            Mathf.Max(0f, cuttingRange));
    }

    private void FixedUpdate()
    {
        LastDetectedTargetCount = 0;
        if (GameManager.Instance?.Harvest?.IsRunning != true)
            return;

        if (gridChunkHandler == null)
        {
            return;
        }

        List<Transform> nearbyTransforms;
        if (useTriggerDetection)
        {
            using (TriggerQueryMarker.Auto())
            {
                // 비활성화/파괴 시 Exit 콜백 없이 남은 참조도 정리한다.
                for (int i = triggerTargets.Count - 1; i >= 0; i--)
                {
                    Transform target = triggerTargets[i];
                    if (target == null || !target.gameObject.activeInHierarchy)
                        triggerTargets.RemoveAt(i);
                }
                nearbyTransforms = triggerTargets;
            }
        }
        else
        {
            using (ChunkQueryMarker.Auto())
            {
                nearbyTransforms = gridChunkHandler.Registry.GetNearbyTransforms(
                    transform.position,
                    cuttingRange);
            }
        }

        using (ProcessTargetsMarker.Auto())
            ProcessTargets(nearbyTransforms);
    }

    private void ProcessTargets(List<Transform> nearbyTransforms)
    {
        bool wasCutting = IsCutting;
        bool foundTarget = false;
        float frameSpeedLimit = float.PositiveInfinity;

        foreach (Transform target in nearbyTransforms)
        {
            if (target == null || !target.CompareTag("Harvestable"))
            {
                continue;
            }

            HarvestActor crop = target.GetComponent<HarvestActor>();

            if (crop == null)
            {
                continue;
            }

            if (useTriggerDetection)
            {
                if (!crop.IsHarvestable)
                    continue;

                Vector3 offset = target.position - transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude > cuttingRange * cuttingRange)
                    continue;
            }

            foundTarget = true;
            LastDetectedTargetCount++;
            cuttingUntilTime = Time.time + Time.fixedDeltaTime * 2f;

            float effectiveDamage = damage * damageMultiplier;
            int requiredHitCount = effectiveDamage > 0f
                ? Mathf.CeilToInt(crop.CurrentHp / effectiveDamage)
                : int.MaxValue;
            float expectedCuttingTime = requiredHitCount == int.MaxValue
                ? float.PositiveInfinity
                : requiredHitCount * Mathf.Max(0f, damageDelay);
            float effectiveCuttingDistance =
                cuttingRange * 2f * cuttingDistanceSafetyRatio;
            float targetSpeedLimit = expectedCuttingTime > 0f
                ? effectiveCuttingDistance / expectedCuttingTime
                : float.PositiveInfinity;

            frameSpeedLimit = Mathf.Min(
                frameSpeedLimit,
                targetSpeedLimit);

            int cropId = crop.GetInstanceID();

            if (nextDamageTimes.TryGetValue(cropId, out float nextDamageTime)
                && Time.time < nextDamageTime)
            {
                continue;
            }

            crop.TakeDamage(damage * damageMultiplier);
            nextDamageTimes[cropId] =
                Time.time + Mathf.Max(0f, damageDelay);
        }

        if (foundTarget)
        {
            if (!wasCutting)
            {
                cuttingSpeedLimit = frameSpeedLimit;
            }
            else
            {
                cuttingSpeedLimit = Mathf.Min(
                    cuttingSpeedLimit,
                    frameSpeedLimit);
            }
        }
    }
}
