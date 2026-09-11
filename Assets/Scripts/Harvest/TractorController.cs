using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class TractorController : MonoBehaviour
{
    // 잠긴 스테이지 경계보다 5m 앞에서 멈추기 위한 의도적인 버퍼다.
    private const float StageBoundaryBuffer = 5f;
    private const float FieldItemEffectDuration = 4f;

    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string moveActionPath = "Player/Move";
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float rotationLerpSpeed = 120f;
    [SerializeField, Min(0f)] private float chargeSpeedMultiplier = 3f;
    [SerializeField] private CropCutter cropCutter;
    [SerializeField] private GridChunkHandler gridChunkHandler;

    private Rigidbody body;
    private InputAction moveAction;
    private CropCutter[] cropCutters;
    private Vector2 moveInput;
    private bool useBenchmarkInput;
    private Vector2 benchmarkInput;

    public void SetBenchmarkInput(Vector2 input)
    {
        useBenchmarkInput = true;
        benchmarkInput = input;
    }

    public void ClearBenchmarkInput()
    {
        useBenchmarkInput = false;
        moveInput = Vector2.zero;
    }
    private bool isCharging;
    private bool isStageBoundaryBlocked;
    private bool isEngineSFXPlaying;
    private float speedBoostAmount;

    public CropCutter Cutter =>
        cropCutter ??= GetComponentInChildren<CropCutter>(true);
    public GridChunkHandler GridChunkHandler => gridChunkHandler;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        cropCutter ??= GetComponentInChildren<CropCutter>(true);
        cropCutters = GetComponentsInChildren<CropCutter>(true);

        if (inputActions == null)
        {
            Debug.LogError("[TractorController] Input Action Asset is not assigned.", this);
            return;
        }

        moveAction = inputActions.FindAction(moveActionPath, true);
    }

    private void OnEnable()
    {
        moveAction?.Enable();
    }

    private void Start()
    {
        HarvestRuntimeStat harvestStat =
            GameManager.Instance?.Upgrade?.RuntimeStat?.Harvest;

        if (harvestStat == null)
        {
            return;
        }

        moveSpeed = harvestStat.Get(HarvestStatType.TruckSpeed);
        cropCutter?.ApplyUpgradeStats(
            harvestStat.Get(HarvestStatType.SawSize),
            harvestStat.Get(HarvestStatType.SawSpeed),
            harvestStat.Get(HarvestStatType.SawSharpness));
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        moveInput = Vector2.zero;
        UpdateEngineSFX(false);
    }

    private void Update()
    {
        if (GameManager.Instance?.Harvest?.IsRunning != true)
        {
            moveInput = Vector2.zero;
            return;
        }

        if (moveAction == null)
        {
            return;
        }

        moveInput = useBenchmarkInput
            ? benchmarkInput
            : Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
    }

    private void FixedUpdate()
    {
        if (GameManager.Instance?.Harvest?.IsRunning != true)
        {
            UpdateEngineSFX(false);
            return;
        }

        float throttle = isCharging ? 1f : moveInput.y;
        float steering = isCharging ? 0f : moveInput.x;
        bool isMoving = Mathf.Abs(throttle) > 0.0001f;

        if (!isMoving && Mathf.Abs(steering) <= 0.0001f)
        {
            UpdateEngineSFX(false);
            return;
        }

        float currentMoveSpeed =
            moveSpeed * (1f + speedBoostAmount);

        if (isCharging)
        {
            currentMoveSpeed *= chargeSpeedMultiplier;
        }
        else
        {
            for (int i = 0; i < cropCutters.Length; i++)
            {
                CropCutter cutter = cropCutters[i];

                if (!cutter.isActiveAndEnabled
                    || !cutter.gameObject.activeInHierarchy)
                {
                    continue;
                }

                currentMoveSpeed = Mathf.Min(
                    currentMoveSpeed,
                    moveSpeed
                    * (1f + speedBoostAmount)
                    * cutter.MoveSpeedMultiplier);
                currentMoveSpeed = Mathf.Min(
                    currentMoveSpeed,
                    cutter.CuttingSpeedLimit);
            }
        }

        float steeringAngle =
            steering
            * (isMoving ? throttle : 1f)
            * rotationLerpSpeed
            * Time.fixedDeltaTime;
        Quaternion nextRotation =
            body.rotation * Quaternion.Euler(0f, steeringAngle, 0f);
        body.MoveRotation(nextRotation);

        bool didMove = false;

        if (isMoving && currentMoveSpeed > 0f)
        {
            Vector3 forward = nextRotation * Vector3.forward;
            Vector3 nextPosition =
                body.position
                + forward
                * (throttle
                    * currentMoveSpeed
                    * Time.fixedDeltaTime);

            nextPosition = ClampToStageBoundary(nextPosition);
            didMove = (nextPosition - body.position).sqrMagnitude > 0.0001f;

            body.MovePosition(
                nextPosition);
        }

        UpdateEngineSFX(didMove);
    }

    private void UpdateEngineSFX(bool shouldPlay)
    {
        if (isEngineSFXPlaying == shouldPlay)
            return;

        isEngineSFXPlaying = shouldPlay;

        if (shouldPlay)
        {
            GameManager.Instance.Utility.Audio.PlayLoopSFX(
                SFXType.Harvest_TractorEngine);
        }
        else
        {
            GameManager.Instance.Utility.Audio.StopLoopSFX(
                SFXType.Harvest_TractorEngine);
        }
    }

    public void SetCharging(bool value)
    {
        isCharging = value;
    }

    public void ApplySpeedBoost(float amount)
    {
        speedBoostAmount += amount;
        StartCoroutine(RemoveSpeedBoostAfterDuration(amount));
    }

    public void ApplyRangeBoost(float amount)
    {
        cropCutter.ApplyRangeBoost(amount);
        StartCoroutine(RemoveRangeBoostAfterDuration(amount));
    }

    public void ApplyDamageBoost(float amount)
    {
        cropCutter.ApplyDamageBoost(amount);
        StartCoroutine(RemoveDamageBoostAfterDuration(amount));
    }

    private IEnumerator RemoveSpeedBoostAfterDuration(float amount)
    {
        yield return new WaitForSeconds(FieldItemEffectDuration);
        speedBoostAmount -= amount;
    }

    private IEnumerator RemoveRangeBoostAfterDuration(float amount)
    {
        yield return new WaitForSeconds(FieldItemEffectDuration);
        cropCutter.ApplyRangeBoost(-amount);
    }

    private IEnumerator RemoveDamageBoostAfterDuration(float amount)
    {
        yield return new WaitForSeconds(FieldItemEffectDuration);
        cropCutter.ApplyDamageBoost(-amount);
    }

    private Vector3 ClampToStageBoundary(Vector3 worldPosition)
    {
        if (gridChunkHandler == null)
        {
            return worldPosition;
        }

        int stageLevel = GameManager.Instance.Upgrade.RuntimeLevel.Get(
            HarvestUpgradeType.StageLevel);
        int nextStageIndex = stageLevel;

        if (nextStageIndex >= (int)StageType.Count)
        {
            isStageBoundaryBlocked = false;
            return worldPosition;
        }

        if (!StageDataDB.TryGetData(
                (StageType)nextStageIndex,
                out StageDataSO nextStageData))
        {
            isStageBoundaryBlocked = false;
            return worldPosition;
        }

        float boundaryLocalZ =
            nextStageData.ZStart - StageBoundaryBuffer;
        Vector3 localPosition =
            gridChunkHandler.transform.InverseTransformPoint(worldPosition);
        bool isBlocked = localPosition.z > boundaryLocalZ;

        if (isBlocked)
        {
            localPosition.z = boundaryLocalZ;
            worldPosition =
                gridChunkHandler.transform.TransformPoint(localPosition);

            if (!isStageBoundaryBlocked)
            {
                GameManager.Instance?.Utility?.Toast?.Show(
                    "아직 해금되지 않은 구역입니다.");
            }
        }

        isStageBoundaryBlocked = isBlocked;
        return worldPosition;
    }
}
