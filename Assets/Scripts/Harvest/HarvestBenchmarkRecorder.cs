using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

// 씬의 Spawner.Start보다 먼저 모드와 생성 시드를 적용한다.
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class HarvestBenchmarkRecorder : MonoBehaviour
{
    [SerializeField] private HarvestStreamingBenchmark benchmark;
    [SerializeField] private GridChunkHandler grid;
    [SerializeField] private TractorController tractor;
    [SerializeField] private CropCutter[] cutters;

    [Serializable]
    public sealed class RunSettings
    {
        public bool streamingEnabled;
        public int seed;
        public float seconds;
        public int loadRadius;
        public string outputDirectory;
        public string codeVersion;
        public string unityVersion;
        public string environment;
        public string device;
        public string processor;
        public string graphics;
        public float fixedDeltaTime;
        public float timeScale;
        public int targetFrameRate;
        public int vSyncCount;
        public int width;
        public int height;
        public int quality;
        public bool profilerEnabled;
        public bool profileEditor;
        public int unloadRadius;
        public int maxLoadsPerFrame;
        public float chunkSize;
        public Vector3 startPosition;
        public Quaternion startRotation;
        public int initialActiveActors;
        public int initialActiveMovingActors;
        public int initialLoadedChunks;
        public int initialPendingChunks;
        public int totalMapChunks;
        public bool initialLoadComplete;
        public double preparationSeconds;
        public float[] cutterRanges;
        public GameSaveData initialGameState;
    }

    private struct FrameSample
    {
        public int frame, editorProfileFrame, fixedSteps, chunks, pending, loads, unloads;
        public int actors, moving, registered, cutters, detected, candidates, damageCalls, harvested;
        public double elapsed, frameMs, mainMs, playerMs, editorMs, physicsMs, queryMs, processMs, gcBytes;
        public Vector3 position;
    }

    private static RunSettings pendingRun;
    private RunSettings settings;
    private FrameSample[] frames;
    private FrameSample pendingFrame;
    private bool hasPendingFrame;
    private int frameCount;
    private int fixedSteps;
    private double startedAt;
    private double startedRealtime;
    private int startingHarvested;
    private int startingDamageCalls;
    private Recorder mainRecorder, playerRecorder, physicsRecorder, queryRecorder, processRecorder;
    private ProfilerRecorder gcRecorder;
    private int lastEditorProfileFrame = -1;
    private double preparationStartedAt;

    public bool IsRecording { get; private set; }
    public string Status { get; private set; } = "Idle";
    public int RecordedFrames => frameCount;

    public static void RunFromHub(bool streamingEnabled, int seed, float seconds, int loadRadius,
        string outputDirectory, string codeVersion)
    {
        if (GameManager.Instance.Scene.CurrentSceneType != SceneType.Hub || GameManager.Instance.Scene.IsChangingScene)
            throw new InvalidOperationException("Wait for the normal MainScene to HubScene startup to finish.");

        pendingRun = new RunSettings
        {
            streamingEnabled = streamingEnabled, seed = seed, seconds = seconds, loadRadius = loadRadius,
            outputDirectory = outputDirectory, codeVersion = codeVersion,
            initialGameState = new GameSaveData
            {
                upgrades = GameManager.Instance.Upgrade.CreateUpgradeSaveData(),
                tutorials = GameManager.Instance.Utility.Tutorial.CreateTutorialSaveData(),
                audio = GameManager.Instance.Utility.Audio.CreateAudioSaveData(),
                stock = GameManager.Instance.StockManager.CreateStockSaveData(),
                market = GameManager.Instance.Market.CreateMarketSaveData()
            }
        };
        GameManager.Instance.Scene.ChangeScene(SceneType.Harvest);
    }

    private void Awake()
    {
        if (pendingRun == null)
            return;

        settings = pendingRun;
        pendingRun = null;
        preparationStartedAt = Time.realtimeSinceStartupAsDouble;
        benchmark.Configure(settings.streamingEnabled, settings.seed, settings.loadRadius);
        tractor.SetBenchmarkInput(Vector2.zero);
        frames = new FrameSample[16384];
        Status = "WaitingForHarvest";
    }

    private void FixedUpdate()
    {
        if (IsRecording)
            fixedSteps++;
    }

    private void LateUpdate()
    {
        if (settings == null || Status == "Completed" || Status == "Interrupted")
            return;

        if (!IsRecording)
        {
            if (grid.Streamer.IsInitialLoadComplete && GameManager.Instance.Harvest.IsRunning)
                BeginRecording();
            return;
        }

        // Recorder 값은 직전 완료 프레임의 값이다. 그 프레임의 부하 스냅샷과 결합한다.
        if (hasPendingFrame)
        {
            pendingFrame.frameMs = Time.unscaledDeltaTime * 1000.0;
            pendingFrame.mainMs = ReadMilliseconds(mainRecorder);
            pendingFrame.playerMs = ReadMilliseconds(playerRecorder);
            pendingFrame.editorMs = ReadEditorLoopMilliseconds(out pendingFrame.editorProfileFrame);
            pendingFrame.physicsMs = ReadMilliseconds(physicsRecorder);
            pendingFrame.queryMs = ReadMilliseconds(queryRecorder);
            pendingFrame.processMs = ReadMilliseconds(processRecorder);
            pendingFrame.gcBytes = gcRecorder.Valid && gcRecorder.Count > 0
                ? gcRecorder.LastValue : double.NaN;
            frames[frameCount++] = pendingFrame;
        }

        if (Time.timeAsDouble - startedAt >= settings.seconds)
        {
            Finish("completed");
            return;
        }
        if (!GameManager.Instance.Harvest.IsRunning || frameCount == frames.Length
            || Time.realtimeSinceStartupAsDouble - startedRealtime >= 170.0)
        {
            Finish("interrupted");
            return;
        }

        int activeCutters = 0, detected = 0, candidates = 0, damageCalls = 0;
        foreach (CropCutter cutter in cutters)
        {
            damageCalls += cutter.DamageCallCount;
            if (!cutter.isActiveAndEnabled)
                continue;
            activeCutters++;
            detected += cutter.LastDetectedTargetCount;
            candidates += cutter.TriggerCandidateCount;
        }
        pendingFrame = new FrameSample
        {
            frame = Time.frameCount, elapsed = Time.timeAsDouble - startedAt,
            fixedSteps = fixedSteps, chunks = grid.Streamer.LoadedChunkCount,
            pending = grid.Streamer.PendingLoadCount, loads = grid.Streamer.LoadsThisFrame,
            unloads = grid.Streamer.UnloadsThisFrame, actors = grid.Registry.ActiveActorCount,
            moving = grid.Registry.ActiveMovingActorCount, registered = grid.Registry.RegisteredActorCount,
            cutters = activeCutters, detected = detected, candidates = candidates,
            damageCalls = damageCalls - startingDamageCalls,
            harvested = grid.Registry.HarvestedCount - startingHarvested, position = tractor.transform.position
        };
        fixedSteps = 0;
        hasPendingFrame = true;
    }

    private void BeginRecording()
    {
#if UNITY_EDITOR
        lastEditorProfileFrame = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
#endif
        settings.unityVersion = Application.unityVersion;
        settings.environment = Application.isEditor ? "Editor" : "Player";
        settings.device = SystemInfo.deviceModel;
        settings.processor = SystemInfo.processorType;
        settings.graphics = SystemInfo.graphicsDeviceName;
        settings.fixedDeltaTime = Time.fixedDeltaTime;
        settings.timeScale = Time.timeScale;
        settings.targetFrameRate = Application.targetFrameRate;
        settings.vSyncCount = QualitySettings.vSyncCount;
        settings.width = Screen.width;
        settings.height = Screen.height;
        settings.quality = QualitySettings.GetQualityLevel();
        settings.profilerEnabled = Profiler.enabled;
#if UNITY_EDITOR
        settings.profileEditor = UnityEditorInternal.ProfilerDriver.profileEditor;
#endif
        settings.unloadRadius = grid.Streamer.UnloadRadius;
        settings.maxLoadsPerFrame = grid.Streamer.MaxChunkLoadsPerFrame;
        settings.chunkSize = grid.Geometry.ChunkSize;
        settings.startPosition = tractor.transform.position;
        settings.startRotation = tractor.transform.rotation;
        settings.initialActiveActors = grid.Registry.ActiveActorCount;
        settings.initialActiveMovingActors = grid.Registry.ActiveMovingActorCount;
        settings.initialLoadedChunks = grid.Streamer.LoadedChunkCount;
        settings.initialPendingChunks = grid.Streamer.PendingLoadCount;
        Vector2Int mapMin = grid.Geometry.MinChunkCoordinate;
        Vector2Int mapMax = grid.Geometry.MaxChunkCoordinate;
        settings.totalMapChunks = (mapMax.x - mapMin.x + 1) * (mapMax.y - mapMin.y + 1);
        settings.initialLoadComplete = grid.Streamer.IsInitialLoadComplete;
        settings.preparationSeconds = Time.realtimeSinceStartupAsDouble - preparationStartedAt;
        settings.cutterRanges = new float[cutters.Length];
        for (int i = 0; i < cutters.Length; i++)
        {
            settings.cutterRanges[i] = cutters[i].Range;
            startingDamageCalls += cutters[i].DamageCallCount;
        }
        startingHarvested = grid.Registry.HarvestedCount;
        Directory.CreateDirectory(settings.outputDirectory);
        File.WriteAllText(Path.Combine(settings.outputDirectory, "settings.json"), JsonUtility.ToJson(settings, true));

        mainRecorder = Recorder.Get("Main Thread");
        playerRecorder = Recorder.Get("PlayerLoop");
        physicsRecorder = Recorder.Get("Physics.Simulate");
        queryRecorder = Recorder.Get("Harvest.ChunkQuery");
        processRecorder = Recorder.Get("Harvest.ProcessTargets");
        mainRecorder.enabled = playerRecorder.enabled = physicsRecorder.enabled = true;
        queryRecorder.enabled = processRecorder.enabled = true;
        gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        startedAt = Time.timeAsDouble;
        startedRealtime = Time.realtimeSinceStartupAsDouble;
        tractor.SetBenchmarkInput(Vector2.up);
        Status = "Recording";
        IsRecording = true;
    }

    private static double ReadMilliseconds(Recorder recorder)
    {
        return recorder.isValid ? recorder.elapsedNanoseconds / 1000000.0 : double.NaN;
    }

    private double ReadEditorLoopMilliseconds(out int profilerFrame)
    {
#if UNITY_EDITOR
        profilerFrame = UnityEditorInternal.ProfilerDriver.lastFrameIndex;
        if (profilerFrame <= lastEditorProfileFrame)
            return double.NaN;
        lastEditorProfileFrame = profilerFrame;
        using (var view = UnityEditorInternal.ProfilerDriver.GetRawFrameDataView(profilerFrame, 0))
        {
            if (!view.valid)
                return double.NaN;

            double milliseconds = 0;
            bool found = false;
            // Main Thread의 직계 샘플만 순회한다. 여러 EditorLoop 구간은 합산한다.
            for (int i = 1; i < view.sampleCount; i += view.GetSampleChildrenCountRecursive(i) + 1)
            {
                if (view.GetSampleName(i) != "EditorLoop")
                    continue;
                milliseconds += view.GetSampleTimeMs(i);
                found = true;
            }
            return found ? milliseconds : double.NaN;
        }
#else
        profilerFrame = -1;
        return double.NaN;
#endif
    }

    public void Abort()
    {
        if (IsRecording)
            Finish("interrupted");
    }

    private void Finish(string result)
    {
        IsRecording = false;
        Status = result == "completed" ? "Completed" : "Interrupted";
        tractor.ClearBenchmarkInput();
        mainRecorder.enabled = playerRecorder.enabled = physicsRecorder.enabled = false;
        queryRecorder.enabled = processRecorder.enabled = false;
        gcRecorder.Dispose();
        WriteResults(result);
    }

    private void OnDisable()
    {
        if (IsRecording)
            Finish("interrupted");
    }

    private static string Number(double value)
    {
        return double.IsNaN(value) ? "" : value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private void WriteResults(string result)
    {
        var csv = new StringBuilder(frameCount * 220);
        csv.AppendLine("frame,elapsed_s,phase,frame_ms,main_thread_ms,player_loop_ms,editor_loop_ms,editor_profile_frame,physics_simulate_ms,query_ms,process_ms,gc_bytes,fixed_steps,loaded_chunks,pending_chunks,loads,unloads,active_actors,active_moving_actors,registered_actors,active_cutters,detected_last_fixed_step,trigger_candidates,damage_calls,harvested,x,y,z");
        double distance = 0;
        Vector3 previousPosition = settings.startPosition;
        for (int i = 0; i < frameCount; i++)
        {
            FrameSample f = frames[i];
            string phase = f.elapsed < 1.0 ? "warmup"
                : f.pending > 0 || f.loads > 0 || f.unloads > 0 ? "streaming" : "steady";
            csv.Append(f.frame).Append(',').Append(Number(f.elapsed)).Append(',').Append(phase).Append(',')
                .Append(Number(f.frameMs)).Append(',').Append(Number(f.mainMs)).Append(',')
                .Append(Number(f.playerMs)).Append(',').Append(Number(f.editorMs)).Append(',')
                .Append(f.editorProfileFrame).Append(',')
                .Append(Number(f.physicsMs)).Append(',')
                .Append(Number(f.queryMs)).Append(',').Append(Number(f.processMs)).Append(',')
                .Append(Number(f.gcBytes)).Append(',').Append(f.fixedSteps).Append(',')
                .Append(f.chunks).Append(',').Append(f.pending).Append(',').Append(f.loads).Append(',')
                .Append(f.unloads).Append(',').Append(f.actors).Append(',').Append(f.moving).Append(',')
                .Append(f.registered).Append(',').Append(f.cutters).Append(',').Append(f.detected).Append(',')
                .Append(f.candidates).Append(',').Append(f.damageCalls).Append(',').Append(f.harvested).Append(',')
                .Append(Number(f.position.x)).Append(',').Append(Number(f.position.y)).Append(',')
                .Append(Number(f.position.z)).AppendLine();
            distance += Vector3.Distance(previousPosition, f.position);
            previousPosition = f.position;
        }
        File.WriteAllText(Path.Combine(settings.outputDirectory, "frames.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(settings.outputDirectory, "summary.csv"),
            "mode,status,frames,duration_s,distance_m,harvested\n"
            + (settings.streamingEnabled ? "StreamerOn" : "StreamerOff") + "," + result + "," + frameCount + ","
            + Number(frameCount > 0 ? frames[frameCount - 1].elapsed : 0) + "," + Number(distance) + ","
            + (frameCount > 0 ? frames[frameCount - 1].harvested : 0) + "\n");
        File.WriteAllText(Path.Combine(settings.outputDirectory, "report.md"),
            "# Harvest benchmark run\n\nStatus: " + result + "\n\nEnvironment: " + settings.environment
            + "\n\nMain Thread/PlayerLoop include engine waits and, in Editor, editor-related overhead. "
            + "Physics.Simulate is a marker duration, not the sum of all physics worker CPU time. "
            + "Missing metrics are blank. Detection counts describe the last fixed step, not damage counts. "
            + "Initial loading and scene preparation are excluded. Both modes use Harvest.ChunkQuery. "
            + "StreamerOff loads the whole map and never unloads chunks. "
            + "First measured second is labelled warmup; pending/loading/unloading frames are labelled streaming.\n");
        // 마지막에 기록한다. 외부 실행기는 이 파일이 생긴 뒤에만 Play를 종료한다.
        File.WriteAllText(Path.Combine(settings.outputDirectory, "complete.txt"), result);
    }
}
