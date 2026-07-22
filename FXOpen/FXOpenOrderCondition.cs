namespace StockSharp.FXOpen;

/// <summary>FXOpen TickTrader-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.FXOpenKey)]
public sealed class FXOpenOrderCondition : BaseWithdrawOrderCondition,
    IStopLossOrderCondition, ITakeProfitOrderCondition
{
    /// <summary>Activation price for stop and stop-limit pending orders.</summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ActivationPriceKey,
        Description = LocalizedStrings.StopPriceDescKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 0)]
    public decimal? StopPrice
    {
        get => Parameters.TryGetValue(nameof(StopPrice))?.To<decimal?>();
        set => Parameters[nameof(StopPrice)] = value;
    }

    /// <summary>Protective stop-loss price.</summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.StopLossKey,
        Description = LocalizedStrings.StopPriceDescKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    public decimal? StopLoss
    {
        get => Parameters.TryGetValue(nameof(StopLoss))?.To<decimal?>();
        set => Parameters[nameof(StopLoss)] = value;
    }

    /// <summary>Protective take-profit price.</summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TakeProfitKey,
        Description = LocalizedStrings.TakeProfitDescKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 2)]
    public decimal? TakeProfit
    {
        get => Parameters.TryGetValue(nameof(TakeProfit))?.To<decimal?>();
        set => Parameters[nameof(TakeProfit)] = value;
    }

    /// <summary>Native trade identifier used for a full or partial position close.</summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PositionKey,
        Description = LocalizedStrings.IdentifierKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 3)]
    public long? PositionId
    {
        get => Parameters.TryGetValue(nameof(PositionId))?.To<long?>();
        set => Parameters[nameof(PositionId)] = value;
    }

    /// <summary>Opposite native position identifier for a close-by operation.</summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PositionKey,
        Description = LocalizedStrings.IdentifierKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 4)]
    public long? CloseByPositionId
    {
        get => Parameters.TryGetValue(nameof(CloseByPositionId))?.To<long?>();
        set => Parameters[nameof(CloseByPositionId)] = value;
    }

    /// <summary>Maximum allowed slippage for market-with-slippage execution.</summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SlippageKey,
        Description = LocalizedStrings.SlippageSizeKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 5)]
    public decimal? Slippage
    {
        get => Parameters.TryGetValue(nameof(Slippage))?.To<decimal?>();
        set => Parameters[nameof(Slippage)] = value;
    }

    /// <summary>Comment stored with the native trade.</summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.CommentKey,
        Description = LocalizedStrings.OrderConditionDescKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 6)]
    public string Comment
    {
        get => Parameters.TryGetValue(nameof(Comment))?.To<string>();
        set => Parameters[nameof(Comment)] = value;
    }

    decimal? IStopLossOrderCondition.ClosePositionPrice
    {
        get => null;
        set { }
    }

    decimal? IStopLossOrderCondition.ActivationPrice
    {
        get => StopLoss;
        set => StopLoss = value;
    }

    bool IStopLossOrderCondition.IsTrailing
    {
        get => false;
        set
        {
            if (value)
                throw new NotSupportedException("FXOpen Web API does not expose trailing stops.");
        }
    }

    decimal? ITakeProfitOrderCondition.ClosePositionPrice
    {
        get => null;
        set { }
    }

    decimal? ITakeProfitOrderCondition.ActivationPrice
    {
        get => TakeProfit;
        set => TakeProfit = value;
    }
}
