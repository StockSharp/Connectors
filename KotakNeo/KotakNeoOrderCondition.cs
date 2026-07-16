namespace StockSharp.KotakNeo;

/// <summary>Kotak Neo specific order condition.</summary>
[DataContract]
[Serializable]
public class KotakNeoOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _afterMarket = "AfterMarket";
	private const string _marketProtection = "MarketProtection";
	private const string _tag = "Tag";
	private const string _squareOffValue = "SquareOffValue";
	private const string _stopLossValue = "StopLossValue";
	private const string _trailingStopLossValue = "TrailingStopLossValue";

	/// <summary>Order product.</summary>
	[DataMember]
	public KotakNeoProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<KotakNeoProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
		set => Parameters[_triggerPrice] = value;
	}

	/// <summary>Publicly disclosed volume.</summary>
	[DataMember]
	public decimal? DisclosedVolume
	{
		get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
		set => Parameters[_disclosedVolume] = value;
	}

	/// <summary>Whether the order is placed after market hours.</summary>
	[DataMember]
	public bool AfterMarket
	{
		get => Parameters.TryGetValue(_afterMarket)?.To<bool>() == true;
		set => Parameters[_afterMarket] = value;
	}

	/// <summary>Market protection percentage.</summary>
	[DataMember]
	public decimal? MarketProtection
	{
		get => Parameters.TryGetValue(_marketProtection)?.To<decimal?>();
		set => Parameters[_marketProtection] = value;
	}

	/// <summary>Client-defined order tag.</summary>
	[DataMember]
	public string Tag
	{
		get => Parameters.TryGetValue(_tag)?.ToString();
		set => Parameters[_tag] = value;
	}

	/// <summary>Bracket-order square-off value.</summary>
	[DataMember]
	public decimal? SquareOffValue
	{
		get => Parameters.TryGetValue(_squareOffValue)?.To<decimal?>();
		set => Parameters[_squareOffValue] = value;
	}

	/// <summary>Bracket-order stop-loss value.</summary>
	[DataMember]
	public decimal? StopLossValue
	{
		get => Parameters.TryGetValue(_stopLossValue)?.To<decimal?>();
		set => Parameters[_stopLossValue] = value;
	}

	/// <summary>Bracket-order trailing stop-loss value.</summary>
	[DataMember]
	public decimal? TrailingStopLossValue
	{
		get => Parameters.TryGetValue(_trailingStopLossValue)?.To<decimal?>();
		set => Parameters[_trailingStopLossValue] = value;
	}
}
