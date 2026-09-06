
using System;
using System.Collections.Generic;
using UnityEngine;



public class AIMove : MonoBehaviour
{
    public event Action OnArrived;

    private enum MoveState
    {
        ToStartWaypoint,
        FollowingPath,
        ToDestination,
        Arrived
    }

    private MoveState moveState;

    [SerializeField] private float moveSpeed;
    [SerializeField] private float rotateSpeed = 10.0f;
    [SerializeField] private PathManager pathManager;
    [SerializeField] private GraphManager graph;


    private Vector3 direction;

    private Transform destination;

    private List<Waypoint> currentPath;
    private int pathIndex;

    private Waypoint startWaypoint;

    private bool isMoveToNear;

    private void Awake()
    {
        if(pathManager == null)
        {
            pathManager = FindFirstObjectByType<PathManager>();
        }

        if(graph == null)
        {
            graph = FindFirstObjectByType<GraphManager>();
        }

        StopMove();
    }

    private void Update()
    {
        Move();
        Rotate();
    }

    private void Move()
    {
        if (isMoveToNear)
        {
            if (Vector3.Distance(transform.position, destination.transform.position) < 3.0f)
            {
                moveState = MoveState.Arrived;
                isMoveToNear = false;
                OnArrived?.Invoke();
            }
        }

        switch (moveState)
        {
            case MoveState.ToStartWaypoint:
                MoveToNearWayPoint();
                break;

            case MoveState.FollowingPath:
                FollowPath();
                break;

            case MoveState.ToDestination:
                MoveToDestination();
                break;

            case MoveState.Arrived:
                break;
        }

    }

    private void MoveToNearWayPoint()
    {
        if(currentPath == null)
        {
            moveState = MoveState.ToDestination;
            return;
        }

        SetDirection(transform.position, startWaypoint.transform.position);

        transform.position = Vector3.MoveTowards(
            transform.position,
            startWaypoint.transform.position,
            moveSpeed * Time.deltaTime
        );

        if (Vector3.Distance(transform.position, startWaypoint.transform.position) < 0.05f)
        {
            pathIndex = 0;
            moveState = MoveState.FollowingPath;
            return;
        }
    }

    private void FollowPath()
    {
        if (currentPath == null || pathIndex >= currentPath.Count)
        {
            moveState = MoveState.ToDestination;
            return;
        }

        Waypoint target = currentPath[pathIndex];


        SetDirection(transform.position, currentPath[pathIndex].transform.position);

        transform.position = Vector3.MoveTowards(
            transform.position,
            currentPath[pathIndex].transform.position,
            moveSpeed * Time.deltaTime );

        if (Vector3.Distance(transform.position, target.transform.position) < 0.05f)
        {
            pathIndex++;
        }

        

    }
    private void MoveToDestination()
    {
        SetDirection(transform.position, destination.position);
        transform.position = Vector3.MoveTowards(
            transform.position,
            destination.transform.position,
            moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, destination.transform.position) < 0.05f)
        {
            moveState = MoveState.Arrived;

            OnArrived?.Invoke();

            return;
        }
    }

    public void MoveTo(Transform target)
    {
        destination = target;

        startWaypoint = graph.GetClosestWaypoint(transform.position);

        if (Vector3.Distance(transform.position, startWaypoint.transform.position)
            > Vector3.Distance(transform.position, destination.transform.position))
        {
            moveState = MoveState.ToDestination;
            return;
        }

        currentPath = pathManager.GetPath(transform.position, destination.position);

        if (currentPath == null)
        {
            Debug.Log("Path not found : " + destination);
            
            return;
        }

        if (currentPath.Count == 0)
        {
            moveState = MoveState.ToDestination;
            return;
        }

        pathIndex = 0;

        startWaypoint = currentPath[0];

        moveState = MoveState.ToStartWaypoint;
    }

    public void StopMove()
    {
        moveState = MoveState.Arrived;
        currentPath = null;
    }

    private void OnDrawGizmos()
    {
        if (currentPath == null)
            return;

        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(
                currentPath[i].transform.position,
                currentPath[i + 1].transform.position
            );
        }
    }

    private void SetDirection(Vector3 startPos, Vector3 goalPos)
    {
        direction = (goalPos - startPos).normalized;
    }
    public void SetDirection(Vector3 destination)
    {
        SetDirection(transform.position, destination);
    }
    public void SetDirectionVector(Vector3 dir)
    {
        direction = Vector3.Normalize(dir);
    }

    private void Rotate()
    {
        if(direction == Vector3.zero)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotateSpeed * Time.deltaTime
        );
    }

    public void SetSpeed(float speed)
    {
        moveSpeed = speed;
    }

    public void MoveToNear(Transform target)
    {
        isMoveToNear = true;
        MoveTo(target);
    }
}
