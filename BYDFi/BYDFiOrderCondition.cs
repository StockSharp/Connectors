namespace StockSharp.BYDFi;

/// <summary>
/// BYDFi conditional order kind.
/// </summary>
[DataContract]
public enum BYDFiTriggerKinds
{
    /// <summary>
    /// Stop-loss order.
    /// </summary>
    [EnumMember]
    StopLoss,

    /// <summary>
    /// Take-profit order.
    /// </summary>
    [EnumMember]
    TakeProfit,

    /// <summary>
    /// Trailing stop order.
    /// </summary>
    [EnumMember]
    TrailingStop,
}

/// <summary>
/// BYDFi trigger price source.
/// </summary>
[DataContract]
public enum BYDFiTriggerPriceTypes
{
    /// <summary>
    /// Last contract price.
    /// </summary>
    [EnumMember]
    ContractPrice,

    /// <summary>
    /// Mark price.
    /// </summary>
    [EnumMember]
    MarkPrice,
}

/// <summary>
/// BYDFi position side.
/// </summary>
[DataContract]
public enum BYDFiPositionSide
{
    /// <summary>
    /// One-way mode.
    /// </summary>
    [EnumMember]
    Both,

    /// <summary>
    /// Long leg in hedge mode.
    /// </summary>
    [EnumMember]
    Long,

    /// <summary>
    /// Short leg in hedge mode.
    /// </summary>
    [EnumMember]
    Short,
}

/// <summary>
/// BYDFi futures order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.BYDFiKey)]
public class BYDFiOrderCondition : OrderCondition
{
    /// <summary>
    /// Trigger price for stop and take-profit orders.
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
    /// Conditional order kind.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TypeKey,
        Description = LocalizedStrings.TypeKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    public BYDFiTriggerKinds TriggerKind
    {
        get => (BYDFiTriggerKinds?)Parameters.TryGetValue(
            nameof(TriggerKind)) ?? BYDFiTriggerKinds.StopLoss;
        set => Parameters[nameof(TriggerKind)] = value;
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
    public BYDFiTriggerPriceTypes TriggerPriceType
    {
        get => (BYDFiTriggerPriceTypes?)Parameters.TryGetValue(
            nameof(TriggerPriceType)) ??
            BYDFiTriggerPriceTypes.ContractPrice;
        set => Parameters[nameof(TriggerPriceType)] = value;
    }

    /// <summary>
    /// Position side for one-way or hedge mode.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PositionKey,
        Description = LocalizedStrings.PositionKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 3)]
    public BYDFiPositionSide PositionSide
    {
        get => (BYDFiPositionSide?)Parameters.TryGetValue(
            nameof(PositionSide)) ?? BYDFiPositionSide.Both;
        set => Parameters[nameof(PositionSide)] = value;
    }

    /// <summary>
    /// Reduce an existing position only.
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
    /// Close the whole position when the trigger fires.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ClosePositionKey,
        Description = LocalizedStrings.ClosePositionKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 5)]
    public bool IsClosePosition
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsClosePosition)) ??
            false;
        set => Parameters[nameof(IsClosePosition)] = value;
    }

    /// <summary>
    /// Trailing-stop activation price.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PriceKey,
        Description = LocalizedStrings.PriceKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 6)]
    public decimal? ActivationPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(ActivationPrice));
        set => Parameters[nameof(ActivationPrice)] = value;
    }

    /// <summary>
    /// Trailing-stop callback rate in percent, from 0.1 to 5.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PercentKey,
        Description = LocalizedStrings.PercentKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 7)]
    public decimal? CallbackRate
    {
        get => (decimal?)Parameters.TryGetValue(nameof(CallbackRate));
        set => Parameters[nameof(CallbackRate)] = value;
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new BYDFiOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
