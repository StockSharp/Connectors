namespace StockSharp.GmoCoin;

/// <summary>
/// GMO Coin stop, margin, and position-closing parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GmoCoinKey)]
public class GmoCoinOrderCondition : OrderCondition, IStopLossOrderCondition
{
    /// <summary>
    /// Stop activation price used by a native STOP order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TriggerKey,
        Description = LocalizedStrings.TriggerFieldKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 0)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Native margin position identifier for a partial or full close.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PositionKey,
        Description = LocalizedStrings.PositionKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    public long? PositionId
    {
        get => (long?)Parameters.TryGetValue(nameof(PositionId));
        set => Parameters[nameof(PositionId)] = value;
    }

    /// <summary>
    /// Whether the order closes an existing margin position.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ClosePositionKey,
        Description = LocalizedStrings.ClosePositionKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 2)]
    public bool IsClosePosition
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsClosePosition)) == true;
        set => Parameters[nameof(IsClosePosition)] = value;
    }

    /// <summary>
    /// Margin liquidation price for a new margin order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.LiquidationPriceKey,
        Description = LocalizedStrings.LiquidationPriceKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 3)]
    public decimal? LossCutPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(LossCutPrice));
        set => Parameters[nameof(LossCutPrice)] = value;
    }

    /// <summary>
    /// Whether GMO Coin may cancel older active orders before accepting this order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CancelKey,
        Description = LocalizedStrings.CancelKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 4)]
    public bool IsCancelBefore
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsCancelBefore)) == true;
        set => Parameters[nameof(IsCancelBefore)] = value;
    }

    decimal? IStopLossOrderCondition.ActivationPrice
    {
        get => TriggerPrice;
        set => TriggerPrice = value;
    }

    decimal? IStopLossOrderCondition.ClosePositionPrice
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
                    "GMO Coin does not document trailing-stop orders.");
        }
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new GmoCoinOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
