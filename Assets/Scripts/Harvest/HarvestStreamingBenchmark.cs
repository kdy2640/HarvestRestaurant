using UnityEngine;

[DefaultExecutionOrder(-250)]
[DisallowMultipleComponent]
public sealed class HarvestStreamingBenchmark : MonoBehaviour
{
    [SerializeField] private GridChunkHandler grid;
    [SerializeField] private HarvestSpawner spawner;
    [SerializeField] private CropCutter[] cutters;
    [SerializeField, Tooltip("Play 전에 설정합니다. OFF는 전체 맵을 생성하고 청크를 비활성화하지 않습니다.")]
    private bool useStreaming = true;
    [SerializeField] private int seed = 12345;
    [SerializeField, Min(0)] private int loadRadius = 10;

    public bool UsesStreaming => useStreaming;

    private void Awake()
    {
        Configure(useStreaming, seed, loadRadius);
    }

    // 자동 실행기는 Spawner.Start 이전에 호출한다. 실행 중 모드 전환은 하지 않는다.
    public void Configure(bool enableStreaming, int generationSeed, int radius)
    {
        useStreaming = enableStreaming;
        seed = generationSeed;
        loadRadius = radius;
        Random.InitState(seed);
        grid.Streamer.ConfigureBenchmark(useStreaming, seed, loadRadius);
        spawner.SetTriggerDetection(false);
        foreach (CropCutter cutter in cutters)
            cutter.SetTriggerDetection(false);
    }
}
