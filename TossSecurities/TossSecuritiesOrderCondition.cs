namespace StockSharp.TossSecurities;

/// <summary>
/// Toss Securities specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class TossSecuritiesOrderCondition : OrderCondition
{
    private const string _triggerPrice = "TriggerPrice";
    private const string _conditionalType = "ConditionalType";
    private const string _secondSide = "SecondSide";
    private const string _secondTriggerPrice = "SecondTriggerPrice";
    private const string _secondOrderPrice = "SecondOrderPrice";
    private const string _orderAmount = "OrderAmount";
    private const string _confirmHighValueOrder = "ConfirmHighValueOrder";
    private const string _atClose = "AtClose";

    /// <summary>Watched trigger price.</summary>
    [DataMember]
    public decimal? TriggerPrice
    {
        get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
        set => Parameters[_triggerPrice] = value;
    }

    /// <summary>Conditional-order relationship.</summary>
    [DataMember]
    public TossConditionalOrderTypes ConditionalType
    {
        get => Parameters.TryGetValue(_conditionalType)?
            .To<TossConditionalOrderTypes?>() ??
            TossConditionalOrderTypes.Single;
        set => Parameters[_conditionalType] = value;
    }

    /// <summary>Second condition side for OCO or OTO orders.</summary>
    [DataMember]
    public Sides? SecondSide
    {
        get => Parameters.TryGetValue(_secondSide)?.To<Sides?>();
        set => Parameters[_secondSide] = value;
    }

    /// <summary>Second condition trigger price.</summary>
    [DataMember]
    public decimal? SecondTriggerPrice
    {
        get => Parameters.TryGetValue(_secondTriggerPrice)?.To<decimal?>();
        set => Parameters[_secondTriggerPrice] = value;
    }

    /// <summary>Second condition limit-order price.</summary>
    [DataMember]
    public decimal? SecondOrderPrice
    {
        get => Parameters.TryGetValue(_secondOrderPrice)?.To<decimal?>();
        set => Parameters[_secondOrderPrice] = value;
    }

    /// <summary>
    /// Dollar amount for a US fractional market order. When specified,
    /// quantity is not sent.
    /// </summary>
    [DataMember]
    public decimal? OrderAmount
    {
        get => Parameters.TryGetValue(_orderAmount)?.To<decimal?>();
        set => Parameters[_orderAmount] = value;
    }

    /// <summary>
    /// Confirm that an order whose value is at least KRW 100 million is
    /// intentional.
    /// </summary>
    [DataMember]
    public bool ConfirmHighValueOrder
    {
        get => Parameters.TryGetValue(_confirmHighValueOrder)?.To<bool>() ==
            true;
        set => Parameters[_confirmHighValueOrder] = value;
    }

    /// <summary>
    /// Submit a US limit order as an at-the-close order.
    /// </summary>
    [DataMember]
    public bool AtClose
    {
        get => Parameters.TryGetValue(_atClose)?.To<bool>() == true;
        set => Parameters[_atClose] = value;
    }
}
