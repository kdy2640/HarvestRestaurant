using UnityEngine;

public sealed class AnimalPatrolState : AnimalStateBase
{
    private Vector3 patrolTarget;

    public override void StateStart()
    {
        patrolTarget = Controller.Mover.GetRandomPatrolPosition();
        Controller.SetBlend(0.5f);
    }

    public override void StateEnd()
    {
    }

    private void Update()
    {
        if (!Controller.IsRunning)
            return;

        if (Controller.IsPlayerWithin(Controller.AnimalStat.DetectionRange))
        {
            Controller.SetState(AnimalStateType.Flee);
            return;
        }

        if (Controller.Mover.HasArrived(patrolTarget))
        {
            Controller.SetState(AnimalStateType.Eat);
            return;
        }

        Vector3 direction = patrolTarget - transform.position;
        direction.y = 0f;
        Controller.Mover.Move(direction, Controller.AnimalStat.PatrolSpeed);
    }
}
