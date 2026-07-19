namespace StockSharp.BTCMarkets;

/// <summary>
/// BTC Markets trigger and execution parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.BTCMarketsKey)]
public class BTCMarketsOrderCondition : OrderCondition, IStopLossOrderCondition,
    ITakeProfitOrderCondition
{
    /// <summary>
    /// Price that activates a stop or take-profit order.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TriggerKey,
        Description = LocalizedStrings.TriggerFieldKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 0)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Use a take-profit trigger instead of a stop trigger.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TakeProfitKey,
        Description = LocalizedStrings.TakeProfitKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 1)]
    public bool IsTakeProfit
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsTakeProfit)) ?? false;
        set => Parameters[nameof(IsTakeProfit)] = value;
    }

    /// <summary>
    /// Desired quote-currency outcome for a market order.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TargetKey,
        Description = LocalizedStrings.TargetKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 2)]
    public decimal? TargetAmount
    {
        get => (decimal?)Parameters.TryGetValue(nameof(TargetAmount));
        set => Parameters[nameof(TargetAmount)] = value;
    }

    /// <summary>
    /// Prevent the new order from trading against an existing own order.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ModeKey,
        Description = LocalizedStrings.ModeKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 3)]
    public bool IsSelfTradePrevented
    {
        get => (bool?)Parameters.TryGetValue(
            nameof(IsSelfTradePrevented)) ?? false;
        set => Parameters[nameof(IsSelfTradePrevented)] = value;
    }

    decimal? IStopLossOrderCondition.ActivationPrice
    {
        get => IsTakeProfit ? null : TriggerPrice;
        set
        {
            IsTakeProfit = false;
            TriggerPrice = value;
        }
    }

    decimal? ITakeProfitOrderCondition.ActivationPrice
    {
        get => IsTakeProfit ? TriggerPrice : null;
        set
        {
            IsTakeProfit = true;
            TriggerPrice = value;
        }
    }

    decimal? IStopLossOrderCondition.ClosePositionPrice
    {
        get => null;
        set { }
    }

    decimal? ITakeProfitOrderCondition.ClosePositionPrice
    {
        get => null;
        set { }
    }

    bool IStopLossOrderCondition.IsTrailing
    {
        get => false;
        set
        {
            if (value)
                throw new NotSupportedException(
                    "BTC Markets does not document trailing-stop orders.");
        }
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new BTCMarketsOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
