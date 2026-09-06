using UnityEngine;
using UnityEngine.UI;

public class DrinkZone : MonoBehaviour
{
    [SerializeField] private DrinkFillButton drinkFillButton;
    [SerializeField] private Image drinkFillImg;
    [SerializeField] private Image backGroundImage;
    [SerializeField] private Transform drinkSpot;
    [SerializeField] private ServiceWarningMessage warningMessage;

    private float spendDrinkFillAmount = 1.0f;
    private GroceryAmount groceryAmount = new();
    private bool isFilling;

    public Transform DrinkSpot => drinkSpot;

    private void Awake()
    {
        if ((GameManager.Instance.Upgrade.RuntimeLevel.Get(FacilityType.Decor_2) < 1))
            Destroy(this.gameObject);

        drinkFillButton.gameObject.SetActive(false);
        backGroundImage.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        drinkFillButton.Filled += ApplyFill;
        GameManager.Instance.StockManager.SubscribeStockDataChange(OnStockDataChanged);
        UpdateDrinkSpendUI();
        OnStockDataChanged();
    }

    private void UpdateDrinkSpendUI()
    {
        drinkFillImg.fillAmount = spendDrinkFillAmount;
    }

    public bool CanSpendDrink()
    {
        return spendDrinkFillAmount >= 0.25f;
    }

    public void SpendDrink()
    {
        spendDrinkFillAmount -= 0.25f;
        UpdateDrinkSpendUI();
        if (spendDrinkFillAmount <= 0.0f && !isFilling && CanFillDrink())
        {
            FillDrink();
        }
    }

    private bool CanFillDrink()
    {
        groceryAmount.grocery = GroceryType.Grape;
        groceryAmount.amount = 1;
        if(GameManager.Instance.StockManager.CanConsumeGrocery(groceryAmount))
        {
            return true;
        }
        warningMessage.ShowMessage(MessageType.wNoDrinkGroccery);
        return false;
    }

    private void FillDrink()
    {
        groceryAmount.grocery = GroceryType.Grape;
        groceryAmount.amount = 1;
        // 재료 소비 중 발생하는 재고 변경 알림에서 중복 충전을 막는다.
        isFilling = true;
        if (!GameManager.Instance.StockManager.TryConsumeGrocery(groceryAmount))
        {
            isFilling = false;
            return;
        }
        drinkFillButton.gameObject.SetActive(true);
        backGroundImage.gameObject.SetActive(true);
    }

    private void OnStockDataChanged()
    {
        if (spendDrinkFillAmount <= 0.0f && !isFilling)
            FillDrink();
    }

    private void ApplyFill()
    {
        spendDrinkFillAmount = 1.0f;
        isFilling = false;
        drinkFillImg.fillAmount = spendDrinkFillAmount;
        backGroundImage.gameObject.SetActive(false);
        GameManager.Instance.Utility.Audio.PlaySFX(SFXType.Service_DrinkRefill);
    }

    private void OnDisable()
    {
        drinkFillButton.Filled -= ApplyFill;
        GameManager.Instance.StockManager.UnsubscribeStockDataChange(OnStockDataChanged);
    }
}
