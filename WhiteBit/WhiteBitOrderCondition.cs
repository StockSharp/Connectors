namespace StockSharp.WhiteBit;

/// <summary>
/// WhiteBIT conditional order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.WhiteBITKey)]
public class WhiteBitOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
    /// <summary>
    /// Trigger price.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.StopPriceKey,
        Description = LocalizedStrings.StopPriceDescKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 0)]
    public decimal? ActivationPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(ActivationPrice));
        set => Parameters[nameof(ActivationPrice)] = value;
    }

    /// <summary>
    /// Limit price used after activation. A null value selects a market trigger order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ClosingPriceKey,
        Description = LocalizedStrings.ClosingPriceKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    public decimal? ClosePositionPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(ClosePositionPrice));
        set => Parameters[nameof(ClosePositionPrice)] = value;
    }

    /// <summary>
    /// Position side for hedge-mode collateral accounts.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PositionKey,
        Description = LocalizedStrings.PositionKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 2)]
    public WhiteBitPositionSides PositionSide
    {
        get => (WhiteBitPositionSides?)Parameters.TryGetValue(nameof(PositionSide)) ?? WhiteBitPositionSides.Both;
        set => Parameters[nameof(PositionSide)] = value;
    }

    /// <summary>
    /// Reduce-only flag for collateral orders.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PosConditionReduceOnlyKey,
        Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 3)]
    public bool IsReduceOnly
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
        set => Parameters[nameof(IsReduceOnly)] = value;
    }

    /// <inheritdoc />
    [DataMember]
    public bool IsTrailing
    {
        get => false;
        set
        {
            if (value)
                throw new NotSupportedException("WhiteBIT trailing trigger orders are not supported.");
        }
    }

    decimal? IStopLossOrderCondition.ActivationPrice
    {
        get => ActivationPrice;
        set => ActivationPrice = value;
    }

    decimal? ITakeProfitOrderCondition.ActivationPrice
    {
        get => ActivationPrice;
        set => ActivationPrice = value;
    }

    decimal? IStopLossOrderCondition.ClosePositionPrice
    {
        get => ClosePositionPrice;
        set => ClosePositionPrice = value;
    }

    decimal? ITakeProfitOrderCondition.ClosePositionPrice
    {
        get => ClosePositionPrice;
        set => ClosePositionPrice = value;
    }
}
