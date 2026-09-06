using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UI_MarketVisualPanel : MonoBehaviour
{
    [SerializeField] private Image[] levelSlots;
    [SerializeField] private GameObject salesProgressPanel;
    [SerializeField] private Slider salesSlider;
    [SerializeField] private TMP_Text salesAmountText;
    [SerializeField] private GameObject missionSlotContainer;
    [SerializeField] private Image[] questSlots;
    [SerializeField] private Sprite completedMissionSprite;
    [SerializeField] private Sprite currentMissionSprite;
    [SerializeField] private Sprite remainingMissionSprite;
    [SerializeField] private RectTransform currentMissionIndicator;
    [SerializeField] private float currentMissionSlotOffsetY = 15f;
    [SerializeField] private TMP_Text marketDescriptionText;
    [SerializeField] private TMP_Text marketDescriptionShadowText;
    [SerializeField] private TMP_Text missionTitleText;
    [SerializeField] private TMP_Text missionDescriptionText;
    [SerializeField] private Slider missionSlider;
    [SerializeField] private TMP_Text missionAmountText;
    [SerializeField] private Button rewardButton;
    [SerializeField] private GameObject completeButton;
    [SerializeField] private GameObject missionRewardPanel;
    [SerializeField] private Image missionRewardIcon;
    [SerializeField] private TMP_Text missionRewardAmountText;
    [SerializeField] private TMP_Text missionRewardAmountShadowText;
    [SerializeField] private Sprite currencyRewardSprite;
    [SerializeField] private Button promoteButton;
    [SerializeField] private Color inactiveColor = Color.gray;
    [SerializeField] private Color activeColor = Color.green;

    private MarketManager marketManager;
    private UpgradeManager upgradeManager;
    private HubCanvasController owner;

    public void Init(HubCanvasController owner)
    {
        this.owner = owner;
    }

    private void OnEnable()
    {
        rewardButton?.onClick.AddListener(ClaimCurrentMissionReward);
        promoteButton?.onClick.AddListener(Promote);

        if (GameManager.Instance == null)
            return;

        marketManager = GameManager.Instance.Market;
        upgradeManager = GameManager.Instance.Upgrade;

        marketManager?.SubscribeMarketDataChanged(Refresh);
        upgradeManager?.SubscribeUpgradeChanged(Refresh);
        Refresh();
    }

    private void OnDisable()
    {
        rewardButton?.onClick.RemoveListener(ClaimCurrentMissionReward);
        promoteButton?.onClick.RemoveListener(Promote);

        marketManager?.UnsubscribeMarketDataChanged(Refresh);
        upgradeManager?.UnsubscribeUpgradeChanged(Refresh);

        marketManager = null;
        upgradeManager = null;
    }

    public void Refresh()
    {
        if (GameManager.Instance == null || GameManager.Instance.Market == null)
            return;

        MarketManager market = GameManager.Instance.Market;
        MarketData marketData = market.MarketData;
        LevelData levelData = market.LevelData;

        int levelSlotCount = levelSlots?.Length ?? 0;
        int activeLevelCount = Mathf.Clamp(marketData.CurrentLevel, 0, levelSlotCount);

        for (int i = 0; i < levelSlotCount; i++)
            SetSlotColor(levelSlots[i], i < activeLevelCount);

        LevelMissionGroupSO missionGroup = market.LevelMissionProgress.MissionGroup;
        marketDescriptionText.text = missionGroup.MarketDescription;
        marketDescriptionShadowText.text = missionGroup.MarketDescription;

        bool isMaxLevel = marketData.CurrentLevel >= MarketManager.MaxMarketLevel;
        bool areAllMissionsCompleted = market.LevelMissionProgress.AreAllMissionsClaimed;
        bool isFinalLevelComplete = isMaxLevel
            && areAllMissionsCompleted
            && levelData.IncomeGoal > 0
            && marketData.TotalIncome >= levelData.IncomeGoal;
        bool canPromote = !isMaxLevel && market.CanPromote;
        bool showRewardButton = !isFinalLevelComplete
            && !areAllMissionsCompleted
            && !canPromote;
        bool showCompleteButton = areAllMissionsCompleted
            && !canPromote;

        if (rewardButton != null)
        {
            rewardButton.gameObject.SetActive(showRewardButton);
            rewardButton.interactable = market.LevelMissionProgress.CanClaimCurrentReward;
            completeButton.SetActive(showCompleteButton);
        }

        if (promoteButton != null)
        {
            promoteButton.gameObject.SetActive(canPromote);
            promoteButton.interactable = canPromote;
        }

        if (salesProgressPanel != null)
            salesProgressPanel.SetActive(true);

        if (missionSlotContainer != null)
            missionSlotContainer.SetActive(true);

        if (isFinalLevelComplete)
        {
            int finalMissionCount = Mathf.Clamp(
                missionGroup.Missions.Count,
                0,
                questSlots?.Length ?? 0);

            for (int i = 0; i < (questSlots?.Length ?? 0); i++)
            {
                Image questSlot = questSlots[i];

                if (questSlot == null)
                    continue;

                bool isVisible = i < finalMissionCount;
                questSlot.gameObject.SetActive(isVisible);

                if (!isVisible)
                    continue;

                questSlot.sprite = completedMissionSprite;
                questSlot.color = Color.white;

                Vector2 slotPosition = questSlot.rectTransform.anchoredPosition;
                slotPosition.y = 0f;
                questSlot.rectTransform.anchoredPosition = slotPosition;
            }

            if (currentMissionIndicator != null)
                currentMissionIndicator.gameObject.SetActive(false);

            if (salesSlider != null)
            {
                salesSlider.minValue = 0f;
                salesSlider.maxValue = 1f;
                salesSlider.value = 1f;
            }

            if (salesAmountText != null)
                salesAmountText.text = $"{marketData.TotalIncome:N0} / {levelData.IncomeGoal:N0}";

            if (missionRewardPanel != null)
                missionRewardPanel.SetActive(false);

            if (missionAmountText != null)
                missionAmountText.text = string.Empty;

            if (missionSlider != null)
            {
                missionSlider.minValue = 0f;
                missionSlider.maxValue = 1f;
                missionSlider.value = 1f;
                missionSlider.interactable = false;
            }

            if (missionTitleText != null)
                missionTitleText.text = "게임클리어!";

            return;
        }

        int incomeGoal = Mathf.Max(1, levelData.IncomeGoal);
        int totalIncome = Mathf.Clamp(marketData.TotalIncome, 0, incomeGoal);

        if (salesSlider != null)
        {
            salesSlider.minValue = 0f;
            salesSlider.maxValue = incomeGoal;
            salesSlider.value = totalIncome;
        }

        if (salesAmountText != null)
            salesAmountText.text = $"{totalIncome:N0} / {incomeGoal:N0}";

        int questSlotCount = questSlots?.Length ?? 0;
        int missionCount = Mathf.Clamp(
            missionGroup == null || missionGroup.Missions == null
                ? 0
                : missionGroup.Missions.Count,
            0,
            questSlotCount);
        int currentStage = Mathf.Clamp(
            market.LevelMissionProgress.CurrentStage,
            0,
            missionCount);

        for (int i = 0; i < questSlotCount; i++)
        {
            Image questSlot = questSlots[i];

            if (questSlot == null)
                continue;

            bool isVisible = i < missionCount;
            questSlot.gameObject.SetActive(isVisible);

            if (!isVisible)
                continue;

            if (i < currentStage && completedMissionSprite != null)
                questSlot.sprite = completedMissionSprite;
            else if (i == currentStage && currentMissionSprite != null)
                questSlot.sprite = currentMissionSprite;
            else if (remainingMissionSprite != null)
                questSlot.sprite = remainingMissionSprite;

            questSlot.color = Color.white;

            Vector2 slotPosition = questSlot.rectTransform.anchoredPosition;
            slotPosition.y = i == currentStage ? currentMissionSlotOffsetY : 0f;
            questSlot.rectTransform.anchoredPosition = slotPosition;
        }

        bool hasCurrentMissionSlot = currentStage < missionCount;

        if (currentMissionIndicator != null)
        {
            currentMissionIndicator.gameObject.SetActive(hasCurrentMissionSlot);

            if (hasCurrentMissionSlot && questSlots[currentStage] != null)
            {
                RectTransform currentSlot = questSlots[currentStage].rectTransform;
                Vector3 indicatorPosition = currentMissionIndicator.position;
                indicatorPosition.x = currentSlot.TransformPoint(currentSlot.rect.center).x;
                currentMissionIndicator.position = indicatorPosition;
            }
        }

        LevelMissionInfo currentMission = market.LevelMissionProgress.CurrentMission;

        if (currentMission != null)
        {
            if (rewardButton != null)
            {
                switch (currentMission.Reward)
                {
                    case MissionCurrencyReward currencyReward:
                        missionRewardPanel.SetActive(true);
                        missionRewardIcon.sprite = currencyRewardSprite;
                        missionRewardAmountText.text = $"x{currencyReward.Amount:N0}";
                        missionRewardAmountShadowText.text = $"x{currencyReward.Amount:N0}";
                        break;

                    case MissionGroceryReward groceryReward:
                        missionRewardPanel.SetActive(true);
                        missionRewardIcon.sprite = GroceryDataDB.GetData(groceryReward.Grocery).Icon;
                        missionRewardAmountText.text = $"x{groceryReward.Amount:N0}";
                        missionRewardAmountShadowText.text = $"x{groceryReward.Amount:N0}";
                        break;

                    default:
                        missionRewardPanel.SetActive(false);
                        break;
                }
            }

            string progress = currentMission.Condition?.ToString() ?? string.Empty;

            if (missionTitleText != null)
                missionTitleText.text = currentMission.Title; 

            if (missionAmountText != null)
                missionAmountText.text = progress;

            if (missionSlider != null)
            {
                int currentValue = 0;
                int targetValue = 1;
                string[] progressValues = progress.Split('/');

                if (progressValues.Length == 2
                    && int.TryParse(progressValues[0].Replace(",", string.Empty).Trim(), out int parsedCurrentValue)
                    && int.TryParse(progressValues[1].Replace(",", string.Empty).Trim(), out int parsedTargetValue)
                    && parsedTargetValue > 0)
                {
                    currentValue = parsedCurrentValue;
                    targetValue = parsedTargetValue;
                }

                missionSlider.minValue = 0f;
                missionSlider.maxValue = targetValue;
                missionSlider.value = Mathf.Clamp(currentValue, 0, targetValue);
                missionSlider.wholeNumbers = true;
                missionSlider.interactable = false;
            }

            return;
        }

        if (rewardButton != null)
            missionRewardPanel.SetActive(false);

        if (missionSlider != null)
        {
            missionSlider.minValue = 0f;
            missionSlider.maxValue = 1f;
            missionSlider.value = 1f;
            missionSlider.wholeNumbers = true;
            missionSlider.interactable = false;
        }

        if (missionAmountText != null)
            missionAmountText.text = string.Empty;

        if (missionTitleText != null)
        {
            if (!areAllMissionsCompleted)
                missionTitleText.text = "승급 미션이 없어요";
            else if (showCompleteButton)
                missionTitleText.text = "누적 매출액 달성 필요";
            else if (canPromote)
                missionTitleText.text = "승급 가능";
            else
                missionTitleText.text = "승급 미션이 없어요";
        }
         
    }

    public void ClaimCurrentMissionReward()
    {
        if (marketManager == null || !marketManager.TryClaimCurrentMissionReward())
            return;

        GameManager.Instance.Utility.Audio.PlaySFX(SFXType.Hub_GetReward);
    }

    public void Promote()
    {
        if (marketManager == null || !marketManager.TryPromote())
            return;

        GameManager.Instance.Utility.Audio.PlaySFX(SFXType.Hub_Rankup);
        owner.RequestStateChange(HubCanvasController.HubCanvasState.RankUpPanel);
    }

    private void SetSlotColor(Image slot, bool isActive)
    {
        if (slot != null)
            slot.color = isActive ? activeColor : inactiveColor;
    }
}
