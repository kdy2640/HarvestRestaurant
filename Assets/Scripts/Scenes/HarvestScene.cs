using System.Collections;
using UnityEngine;

public class HarvestScene : SceneBase
{
    public override SceneType SceneType => SceneType.Harvest;
    public override string SceneName => "HarvestScene";

    public override IEnumerator PrepareBeforeReveal()
    {
        GameManager.Instance.Harvest.PrepareReveal();
        yield return null;
        GridChunkHandler grid = Object.FindFirstObjectByType<GridChunkHandler>();
        while (!grid.Streamer.IsInitialLoadComplete)
            yield return null;
    }

    public override IEnumerator Enter()
    {
        HarvestPreStart preStart = Object.FindFirstObjectByType<HarvestPreStart>();

        if (preStart == null)
        {
            Debug.LogError("HarvestPreStart가 씬에 없습니다.");
            yield break;
        }

        yield return preStart.Run();
        GameManager.Instance.Harvest.StartLoop();
    }

    public override IEnumerator Exit()
    { 
        GameManager.Instance.Market.MoveToNextPhase();
        yield return null;
    }
}
