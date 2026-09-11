using UnityEngine;

public sealed class AnimalFleeState : AnimalStateBase
{
    public override void StateStart()
    {
        Controller.SetBlend(1f);
    }

    public override void StateEnd()
    {
    }

    private void Update()
    {
        if (!Controller.IsRunning)
            return;

        if (!Controller.IsPlayerWithin(Controller.AnimalStat.FleeDistance))
        {
            Controller.SetState(AnimalStateType.Patrol);
            return;
        }

        Vector3 direction = transform.position - Controller.Player.position;
        direction.y = 0f;
        Controller.Mover.Move(direction, Controller.AnimalStat.FleeSpeed);
    }
}
