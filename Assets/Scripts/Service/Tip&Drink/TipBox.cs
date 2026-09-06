using UnityEngine;

public class TipBox : MonoBehaviour
{
    [SerializeField] private int tipAmount;
    [SerializeField] private Transform tipSpot;
    [SerializeField] private GameObject tipGetVfxPrefab;
    private TipButton tipButton;
    public Transform TipSpot => tipSpot;

    private void OnEnable()
    {
        tipAmount = 0;
        tipButton = GetComponentInChildren<TipButton>();
        tipButton.OnClicked += ApplyTip;
        tipButton.gameObject.SetActive(false);
    }

    public void AddTip(int tip)
    {
        tipAmount += tip;
        GameManager.Instance.Utility.Audio.PlaySFX(SFXType.Service_TipAdded);
        UpdateUI();
    }
    public void ApplyTip()
    {
        int collectedTipAmount = tipAmount;

        GameManager.Instance.StockManager.AddCurrency(collectedTipAmount);
        GameManager.Instance.Market.MarketData.TotalIncome += collectedTipAmount;
        GameManager.Instance.Service.ResultBuilder.RecordTip(collectedTipAmount);
        tipAmount = 0;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if(tipAmount <= 0)
        {
            tipButton.gameObject.SetActive(false);
            return;
        }
        else
        {
            if(!tipButton.gameObject.activeSelf)
                tipButton.gameObject.SetActive(true);

            tipButton.SetAmountText(tipAmount);

        }
    }

    public void VFXOn()
    {
        Instantiate(tipGetVfxPrefab, tipButton.transform.position, Quaternion.identity);
    }

    private void OnDisable()
    {
        if(tipAmount > 0)
        {
            ApplyTip();
        }
        tipButton.OnClicked -= ApplyTip;
    }
}
