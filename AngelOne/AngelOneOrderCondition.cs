namespace StockSharp.AngelOne;

/// <summary>
/// Angel One specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class AngelOneOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _variety = "Variety";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _squareOff = "SquareOff";
	private const string _stopLoss = "StopLoss";
	private const string _trailingStopLoss = "TrailingStopLoss";
	private const string _scripConsent = "ScripConsent";

	/// <summary>Order product.</summary>
	[DataMember]
	public AngelOneProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<AngelOneProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Order variety.</summary>
	[DataMember]
	public AngelOneOrderVarieties? Variety
	{
		get => Parameters.TryGetValue(_variety)?.To<AngelOneOrderVarieties?>();
		set => Parameters[_variety] = value;
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

	/// <summary>Bracket-order square-off distance.</summary>
	[DataMember]
	public decimal? SquareOff
	{
		get => Parameters.TryGetValue(_squareOff)?.To<decimal?>();
		set => Parameters[_squareOff] = value;
	}

	/// <summary>Bracket-order stop-loss distance.</summary>
	[DataMember]
	public decimal? StopLoss
	{
		get => Parameters.TryGetValue(_stopLoss)?.To<decimal?>();
		set => Parameters[_stopLoss] = value;
	}

	/// <summary>Trailing stop-loss distance.</summary>
	[DataMember]
	public decimal? TrailingStopLoss
	{
		get => Parameters.TryGetValue(_trailingStopLoss)?.To<decimal?>();
		set => Parameters[_trailingStopLoss] = value;
	}

	/// <summary>Consent for cash instruments under surveillance.</summary>
	[DataMember]
	public bool ScripConsent
	{
		get => Parameters.TryGetValue(_scripConsent)?.To<bool>() == true;
		set => Parameters[_scripConsent] = value;
	}
}
