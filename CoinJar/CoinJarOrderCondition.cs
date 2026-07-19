namespace StockSharp.CoinJar;

/// <summary>
/// CoinJar Exchange order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.CoinJarKey)]
public class CoinJarOrderCondition : OrderCondition, IStopLossOrderCondition
{
    /// <inheritdoc />
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.StopPriceKey,
        Description = LocalizedStrings.StopPriceDescKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 0)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Whether the order is auction-only.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.OrderTypeKey,
        Description = LocalizedStrings.OrderTypeKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 1)]
    public bool IsAuctionOnly
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsAuctionOnly)) ?? false;
        set => Parameters[nameof(IsAuctionOnly)] = value;
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
                    "CoinJar does not document trailing-stop orders.");
        }
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new CoinJarOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
