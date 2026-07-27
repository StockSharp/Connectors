namespace StockSharp.Nuvama;

/// <summary>Nuvama-specific order condition.</summary>
[DataContract]
[Serializable]
public class NuvamaOrderCondition : OrderCondition
{
    private const string _product = "Product";
    private const string _triggerPrice = "TriggerPrice";
    private const string _disclosedVolume = "DisclosedVolume";
    private const string _remark = "Remark";

    /// <summary>Order product.</summary>
    [DataMember]
    public NuvamaProducts? Product
    {
        get => Parameters.TryGetValue(_product)?.To<NuvamaProducts?>();
        set => Parameters[_product] = value;
    }

    /// <summary>Stop-order trigger price.</summary>
    [DataMember]
    public decimal? TriggerPrice
    {
        get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
        set => Parameters[_triggerPrice] = value;
    }

    /// <summary>Quantity disclosed to the exchange.</summary>
    [DataMember]
    public decimal? DisclosedVolume
    {
        get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
        set => Parameters[_disclosedVolume] = value;
    }

    /// <summary>Order remark.</summary>
    [DataMember]
    public string Remark
    {
        get => Parameters.TryGetValue(_remark)?.ToString();
        set => Parameters[_remark] = value;
    }
}
