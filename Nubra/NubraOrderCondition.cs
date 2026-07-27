namespace StockSharp.Nubra;

/// <summary>Nubra-specific order condition.</summary>
[DataContract]
[Serializable]
public class NubraOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _triggerPrice = "TriggerPrice";
	private const string _strategyTag = "StrategyTag";

	/// <summary>Order delivery product.</summary>
	[DataMember]
	public NubraProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<NubraProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Stop-entry trigger price.</summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
		set => Parameters[_triggerPrice] = value;
	}

	/// <summary>Optional Nubra strategy tag.</summary>
	[DataMember]
	public string StrategyTag
	{
		get => Parameters.TryGetValue(_strategyTag)?.ToString();
		set => Parameters[_strategyTag] = value;
	}
}
