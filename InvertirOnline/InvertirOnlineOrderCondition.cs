namespace StockSharp.InvertirOnline;

/// <summary>Additional conditions for an IOL order.</summary>
[DataContract]
[Serializable]
[Display(
    Name = "IOL order condition",
    Description = "InvertirOnline order parameters.")]
public sealed class InvertirOnlineOrderCondition : OrderCondition
{
    /// <summary>Settlement term.</summary>
    [DataMember]
    [Display(
        Name = "Settlement",
        Description = "IOL settlement term.",
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
