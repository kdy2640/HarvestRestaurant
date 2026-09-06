using System;
using UnityEngine;

[Serializable]
public sealed class LevelMissionInfo
{
    public enum MissionMenuTarget
    {
        None = 0,
        MenuManagement = 1,
        FacilityManagement = 2,
        StaffManagement = 3,
        HarvestUpgrade = 4
    }

    private enum MissionConditionType
    {
        None = 0,
        Upgrade = 1,
        Income = 2,
        GroceryDelivery = 6,
        Festival = 7
    }

    private enum MissionRewardType
    {
        None = 0,
        Currency = 1,
        Grocery = 2
    }

    [SerializeField] private MissionConditionType conditionType;
    [SerializeReference] private MissionCondition condition;
    [SerializeField] private MissionRewardType rewardType;
    [SerializeReference] private MissionReward reward;
    [SerializeField] private string title;
    [SerializeField, TextArea] private string description;
    [SerializeField] private MissionMenuTarget menuTarget;

    public MissionCondition Condition => condition;
    public MissionReward Reward => reward;
    public string Title => title;
    public string Description => description;
    public MissionMenuTarget MenuTarget => menuTarget;

    internal void SyncCondition()
    {
        switch (conditionType)
        {
            case MissionConditionType.None:
                condition = null;
                break;

            case MissionConditionType.Upgrade:
                if (condition is not UpgradeMissionCondition)
                    condition = new UpgradeMissionCondition();

                ((UpgradeMissionCondition)condition).SyncSubConditions();
                break;

            case MissionConditionType.Income:
                if (condition is not IncomeMissionCondition)
                    condition = new IncomeMissionCondition();
                break;

            case MissionConditionType.GroceryDelivery:
                if (condition is not GroceryDeliveryMissionCondition)
                    condition = new GroceryDeliveryMissionCondition();
                break;

            case MissionConditionType.Festival:
                if (condition is not FestivalMissionCondition)
                    condition = new FestivalMissionCondition();
                break;
        }
    }

    internal void SyncReward()
    {
        switch (rewardType)
        {
            case MissionRewardType.None:
                reward = null;
                break;

            case MissionRewardType.Currency:
                if (reward is not MissionCurrencyReward)
                    reward = new MissionCurrencyReward();
                break;

            case MissionRewardType.Grocery:
                if (reward is not MissionGroceryReward)
                    reward = new MissionGroceryReward();
                break;
        }
    }
}
