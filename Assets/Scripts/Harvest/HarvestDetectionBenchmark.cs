using UnityEngine;

[DisallowMultipleComponent]
public sealed class HarvestDetectionBenchmark : MonoBehaviour
{
    [SerializeField] private HarvestSpawner spawner;
    [SerializeField] private CropCutter[] cutters;
    [SerializeField, Tooltip("켜면 Trigger, 끄면 기존 Chunk 감지를 사용합니다. 실행 중 변경할 수 있습니다.")]
    private bool useTriggerDetection;

    private bool appliedTriggerDetection;

    public bool UsesTriggerDetection => appliedTriggerDetection;

    private void Awake()
    {
        SetTriggerDetection(useTriggerDetection);
    }

    private void Update()
    {
        if (useTriggerDetection != appliedTriggerDetection)
            SetTriggerDetection(useTriggerDetection);
    }

    public void SetTriggerDetection(bool enabled)
    {
        useTriggerDetection = enabled;
        if (!Application.isPlaying)
            return;

        spawner.SetTriggerDetection(enabled);
        foreach (CropCutter cutter in cutters)
            cutter.SetTriggerDetection(enabled);

        appliedTriggerDetection = enabled;
    }

    [ContextMenu("Detection/Use Chunk")]
    public void UseChunkDetection()
    {
        SetTriggerDetection(false);
    }

    [ContextMenu("Detection/Use Trigger")]
    public void UseTriggerDetection()
    {
        SetTriggerDetection(true);
    }
}
