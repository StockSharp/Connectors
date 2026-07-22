namespace StockSharp.Zoomex;

/// <summary>
/// Zoomex trigger direction.
/// </summary>
[DataContract]
public enum ZoomexTriggerDirections
{
    /// <summary>
    /// Trigger when the market rises to the trigger price.
    /// </summary>
    [EnumMember]
    Rise,

    /// <summary>
    /// Trigger when the market falls to the trigger price.
    /// </summary>
    [EnumMember]
    Fall,
}

/// <summary>
/// Zoomex trigger price source.
/// </summary>
[DataContract]
public enum ZoomexTriggerPriceTypes
{
    /// <summary>
    /// Last traded price.
    /// </summary>
    [EnumMember]
    LastPrice,

    /// <summary>
    /// Index price.
    /// </summary>
    [EnumMember]
    IndexPrice,

    /// <summary>
    /// Mark price.
    /// </summary>
    [EnumMember]
    MarkPrice,
}

/// <summary>
/// Zoomex futures position index.
/// </summary>
[DataContract]
public enum ZoomexPositionIndexes
{
    /// <summary>
    /// One-way mode.
    /// </summary>
    [EnumMember]
    OneWay,

    /// <summary>
    /// Buy leg in hedge mode.
    /// </summary>
    [EnumMember]
    HedgeBuy,

    /// <summary>
    /// Sell leg in hedge mode.
    /// </summary>
    [EnumMember]
    HedgeSell,
}

/// <summary>
/// Quantity unit for a Spot market order.
/// </summary>
[DataContract]
public enum ZoomexSpotMarketUnits
{
    /// <summary>
    /// Base asset quantity.
    /// </summary>
    [EnumMember]
    BaseCoin,

    /// <summary>
    /// Quote asset amount.
    /// </summary>
    [EnumMember]
    QuoteCoin,
}

/// <summary>
/// Zoomex-specific order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.ZoomexKey)]
public class ZoomexOrderCondition : OrderCondition
{
    /// <summary>
    /// Conditional order trigger price.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.StopPriceKey,
        Description = LocalizedStrings.StopPriceKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 0)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Trigger direction.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SideKey,
        Description = LocalizedStrings.SideKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    public ZoomexTriggerDirections TriggerDirection
    {
        get => (ZoomexTriggerDirections?)Parameters.TryGetValue(
            nameof(TriggerDirection)) ?? ZoomexTriggerDirections.Rise;
        set => Parameters[nameof(TriggerDirection)] = value;
    }

    /// <summary>
    /// Trigger price source.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TypeKey,
        Description = LocalizedStrings.TypeKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 2)]
    public ZoomexTriggerPriceTypes TriggerPriceType
    {
        get => (ZoomexTriggerPriceTypes?)Parameters.TryGetValue(
            nameof(TriggerPriceType)) ?? ZoomexTriggerPriceTypes.LastPrice;
        set => Parameters[nameof(TriggerPriceType)] = value;
    }

    /// <summary>
    /// Position index for one-way or hedge mode.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PositionKey,
        Description = LocalizedStrings.PositionKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 3)]
    public ZoomexPositionIndexes PositionIndex
    {
        get => (ZoomexPositionIndexes?)Parameters.TryGetValue(
            nameof(PositionIndex)) ?? ZoomexPositionIndexes.OneWay;
        set => Parameters[nameof(PositionIndex)] = value;
    }

    /// <summary>
    /// Reduce an existing futures position only.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PosConditionReduceOnlyKey,
        Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 4)]
    public bool IsReduceOnly
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
        set => Parameters[nameof(IsReduceOnly)] = value;
    }

    /// <summary>
    /// Close the position when the trigger fires.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ClosePositionKey,
        Description = LocalizedStrings.ClosePositionKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 5)]
    public bool IsCloseOnTrigger
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsCloseOnTrigger)) ??
            false;
        set => Parameters[nameof(IsCloseOnTrigger)] = value;
    }

    /// <summary>
    /// Quantity unit for a Spot market order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.VolumeKey,
        Description = LocalizedStrings.VolumeKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 6)]
    public ZoomexSpotMarketUnits MarketUnit
    {
        get => (ZoomexSpotMarketUnits?)Parameters.TryGetValue(
            nameof(MarketUnit)) ?? ZoomexSpotMarketUnits.BaseCoin;
        set => Parameters[nameof(MarketUnit)] = value;
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new ZoomexOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
