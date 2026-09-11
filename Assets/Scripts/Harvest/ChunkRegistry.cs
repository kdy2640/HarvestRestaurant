using System.Collections.Generic;
using UnityEngine;

public sealed class ChunkRegistry
{
    private readonly Dictionary<Vector2Int, GridChunk> chunks = new();
    private readonly Dictionary<Transform, Vector2Int> registeredChunks = new();

    private GridGeometry geometry;

    public int ActiveActorCount { get; internal set; }
    public int ActiveMovingActorCount { get; internal set; }
    public int HarvestedCount { get; internal set; }
    public int RegisteredActorCount => registeredChunks.Count;

    private sealed class GridChunk
    {
        public readonly List<Transform> Transforms = new();
    }

    // Actor 좌표 계산에 사용할 Geometry를 연결한다.
    public void Initialize(GridGeometry gridGeometry)
    {
        geometry = gridGeometry;
    }

    // Actor를 현재 위치에 해당하는 검색용 청크에 등록한다.
    public void Register(Transform target)
    {
        if (registeredChunks.ContainsKey(target))
        {
            TryUpdateChunk(target, out _);
            return;
        }

        Vector2Int coordinate = geometry.GetChunkCoordinate(target.position);
        GetOrCreateChunk(coordinate).Transforms.Add(target);
        registeredChunks.Add(target, coordinate);
    }

    // 이동한 Actor의 검색용 청크를 갱신하고 변경된 좌표를 반환한다.
    public bool TryUpdateChunk(
        Transform target,
        out Vector2Int nextCoordinate)
    {
        if (!registeredChunks.TryGetValue(
                target,
                out Vector2Int currentCoordinate))
        {
            Register(target);
            nextCoordinate = currentCoordinate;
            return false;
        }

        nextCoordinate = geometry.GetChunkCoordinate(target.position);

        if (currentCoordinate == nextCoordinate)
        {
            return false;
        }

        RemoveFromChunk(target, currentCoordinate);
        GetOrCreateChunk(nextCoordinate).Transforms.Add(target);
        registeredChunks[target] = nextCoordinate;
        return true;
    }

    // Actor를 등록된 검색용 청크에서 제거한다.
    public void Unregister(Transform target)
    {
        if (!registeredChunks.TryGetValue(target, out Vector2Int coordinate))
        {
            return;
        }

        RemoveFromChunk(target, coordinate);
        registeredChunks.Remove(target);
    }

    // 지정한 위치와 범위 안에 등록된 Actor를 조회한다.
    public List<Transform> GetNearbyTransforms(
        Vector3 worldPosition,
        float range)
    {
        List<Transform> results = new();
        geometry.GetChunkSearchRange(
            worldPosition,
            range,
            out Vector2Int minCoordinate,
            out Vector2Int maxCoordinate);
        float sqrRange = range * range;

        for (int z = minCoordinate.y; z <= maxCoordinate.y; z++)
        {
            for (int x = minCoordinate.x; x <= maxCoordinate.x; x++)
            {
                Vector2Int coordinate = new(x, z);

                if (!chunks.TryGetValue(coordinate, out GridChunk chunk))
                {
                    continue;
                }

                foreach (Transform target in chunk.Transforms)
                {
                    if (geometry.GetSqrDistanceXZ(
                            target.position,
                            worldPosition) <= sqrRange)
                    {
                        results.Add(target);
                    }
                }
            }
        }

        return results;
    }

    // Actor 검색에 사용하는 좌표별 청크 목록을 반환하거나 생성한다.
    private GridChunk GetOrCreateChunk(Vector2Int coordinate)
    {
        if (!chunks.TryGetValue(coordinate, out GridChunk chunk))
        {
            chunk = new GridChunk();
            chunks.Add(coordinate, chunk);
        }

        return chunk;
    }

    // 검색용 청크에서 Actor를 빼고 빈 청크 목록을 정리한다.
    private void RemoveFromChunk(Transform target, Vector2Int coordinate)
    {
        if (!chunks.TryGetValue(coordinate, out GridChunk chunk))
        {
            return;
        }

        chunk.Transforms.Remove(target);

        if (chunk.Transforms.Count == 0)
        {
            chunks.Remove(coordinate);
        }
    }
}
