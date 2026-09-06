using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class CropCutter : MonoBehaviour
{
    private const float CutterOffset = 0.7f;
    private const float OverloadDamageBoostAmount = 0.3f;

    [SerializeField] private GridChunkHandler gridChunkHandler;
    [SerializeField] private CutterViewer cutterViewer;
    [SerializeField, Min(0f)] private float cuttingRange = 0.5f;
    [SerializeField, Min(0f)] private float rangeLerpSpeed = 8f;
    [SerializeField, Min(0f)] private float damage = 1f;
    [SerializeField, Min(0f)] private float damageDelay = 0.25f;
    [SerializeField, Range(0f, 1f)] private float cuttingMoveSpeedMultiplier = 0.35f;
    [SerializeField, Range(0f, 1f)] private float cuttingDistanceSafetyRatio = 0.75f;

    private readonly Dictionary<int, float> nextDamageTimes = new();
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
    public float MoveSpeedMultiplier =>
        IsCutting ? cuttingMoveSpeedMultiplier : 1f;
    public float CuttingSpeedLimit =>
        IsCutting ? cuttingSpeedLimit : float.PositiveInfinity;

    public void Initialize(GridChunkHandler handler)
    {
        gridChunkHandler = handler;
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
        if (GameManager.Instance?.Harvest?.IsRunning != true)
            return;

        if (gridChunkHandler == null)
        {
            return;
        }

        List<Transform> nearbyTransforms =
            gridChunkHandler.Registry.GetNearbyTransforms(
                transform.position,
                cuttingRange);
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

            foundTarget = true;
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
