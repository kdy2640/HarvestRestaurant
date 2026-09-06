using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PathManager : MonoBehaviour
{
    [SerializeField] private GraphManager graph;
    private Dictionary<(Waypoint, Waypoint), List<Waypoint>> pathTable
        = new();
    private AStarPathFinder finder;


  

    private void OnEnable()
    {
        graph.WaypointSetDone += Initialize;
    }

    public void Initialize()
    {
        finder = new AStarPathFinder(graph);

        foreach (Waypoint start in graph.AllWaypoints)
        {
            foreach (Waypoint goal in graph.AllWaypoints)
            {
                if (start == goal)
                    continue;

                List<Waypoint> path = finder.FindPath(start, goal);

                pathTable.Add((start, goal), path);

            }
        }
    }


    public List<Waypoint> GetPath(Waypoint start, Waypoint goal)
    {
        if (pathTable.TryGetValue((start, goal), out var path))
        {
            if(path == null)
            {
                Debug.Log("Path Null Inside");
                Debug.Log(start + "-" + goal + " : GetPath (Waypoint)Fail");
                Debug.Log("PathTableCount : " + pathTable.Count);
            }

            return path;
        }
        Debug.Log(start + "-" + goal + " : GetPath (Waypoint)Fail");
        Debug.Log("PathTableCount : " + pathTable.Count);
        
        return null;
    }

    public List<Waypoint> GetPath(Vector3 startPos, Vector3 goalPos)
    {
        Waypoint start = graph.GetClosestWaypoint(startPos);
        Waypoint goal = graph.GetClosestWaypoint(goalPos);

        if (start == goal)
        {
            return new List<Waypoint>();
        }

        return GetPath(start, goal);
    }

    private void OnDisable()
    {
        graph.WaypointSetDone -= Initialize;
    }
}
