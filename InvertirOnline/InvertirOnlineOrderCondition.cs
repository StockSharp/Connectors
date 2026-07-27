namespace StockSharp.InvertirOnline;

/// <summary>Additional conditions for an IOL order.</summary>
[DataContract]
[Serializable]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.IolOrderConditionKey,
    Description = LocalizedStrings.InvertirOnlineOrderParametersDescKey)]
public sealed class InvertirOnlineOrderCondition : OrderCondition
{
    /// <summary>Settlement term.</summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SettlementKey,
        Description = LocalizedStrings.IolSettlementTermDescKey,
        GroupName = LocalizedStrings.GeneralKey,
        Order = 0)]
    public InvertirOnlineSettlements Settlement
    {
        get => Parameters.TryGetValue(nameof(Settlement))
            ?.To<InvertirOnlineSettlements?>() ??
                InvertirOnlineSettlements.T1;
        set => Parameters[nameof(Settlement)] = value;
    }
}
