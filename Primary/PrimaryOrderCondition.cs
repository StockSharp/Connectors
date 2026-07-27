namespace StockSharp.Primary;

/// <summary>Primary-specific order settings.</summary>
[DataContract]
[Serializable]
public class PrimaryOrderCondition : OrderCondition
{
    private const string _cancelPrevious = "CancelPrevious";
    private const string _iceberg = "Iceberg";
    private const string _displayVolume = "DisplayVolume";

    /// <summary>
    /// Cancel active orders for the same account, instrument, and side
    /// before entering this order.
    /// </summary>
    [DataMember]
    public bool CancelPrevious
    {
        get => Parameters.TryGetValue(_cancelPrevious)?.To<bool?>() ?? false;
        set => Parameters[_cancelPrevious] = value;
    }

    /// <summary>Submit an iceberg order.</summary>
    [DataMember]
    public bool Iceberg
    {
        get => Parameters.TryGetValue(_iceberg)?.To<bool?>() ?? false;
        set => Parameters[_iceberg] = value;
    }

    /// <summary>Quantity disclosed by an iceberg order.</summary>
    [DataMember]
    public decimal? DisplayVolume
    {
        get => Parameters.TryGetValue(_displayVolume)?.To<decimal?>();
        set => Parameters[_displayVolume] = value;
    }
}
