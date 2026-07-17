namespace StockSharp.FivePaisa;

/// <summary>5paisa-specific order condition.</summary>
[DataContract]
[Serializable]
public class FivePaisaOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _isAfterMarket = "IsAfterMarket";

	/// <summary>Order product.</summary>
	[DataMember]
	public FivePaisaProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<FivePaisaProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Stop-loss trigger price.</summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
		set => Parameters[_triggerPrice] = value;
	}

	/// <summary>Quantity disclosed to the market.</summary>
	[DataMember]
	public decimal? DisclosedVolume
	{
		get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
		set => Parameters[_disclosedVolume] = value;
	}

	/// <summary>Whether the order is submitted as an after-market order.</summary>
	[DataMember]
	public bool IsAfterMarket
	{
		get => Parameters.TryGetValue(_isAfterMarket)?.To<bool>() == true;
		set => Parameters[_isAfterMarket] = value;
	}
}
