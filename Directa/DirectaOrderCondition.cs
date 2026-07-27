namespace StockSharp.Directa;

/// <summary>
/// Directa stop-order condition.
/// </summary>
[Serializable]
[DataContract]
public sealed class DirectaOrderCondition :
    OrderCondition, IStopLossOrderCondition
{
    /// <summary>
    /// Stop trigger price.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.StopPriceKey,
        Description = LocalizedStrings.StopPriceDescKey,
        GroupName = LocalizedStrings.GeneralKey)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(
            nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    decimal? IStopLossOrderCondition.ClosePositionPrice
    {
        get;
        set;
    }

    decimal? IStopLossOrderCondition.ActivationPrice
    {
        get => TriggerPrice;
        set => TriggerPrice = value;
    }

    bool IStopLossOrderCondition.IsTrailing
    {
        get => false;
        set
        {
            if (value)
            {
                throw new NotSupportedException(
                    "Directa Darwin API does not support trailing stops.");
            }
        }
    }
}
