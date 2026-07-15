namespace StockSharp.Fyers;

/// <summary>FYERS-specific order condition.</summary>
[DataContract]
[Serializable]
public class FyersOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _isAfterMarket = "IsAfterMarket";
	private const string _isSliceOrder = "IsSliceOrder";
	private const string _stopLoss = "StopLoss";
	private const string _takeProfit = "TakeProfit";
	private const string _isGtt = "IsGtt";
	private const string _secondPrice = "SecondPrice";
	private const string _secondTriggerPrice = "SecondTriggerPrice";
	private const string _secondVolume = "SecondVolume";

	/// <summary>Order product.</summary>
	[DataMember]
	public FyersProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<FyersProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Stop or GTT trigger price.</summary>
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

	/// <summary>Whether the order is submitted as an after-market order.</summary>
	[DataMember]
	public bool IsAfterMarket
	{
		get => Parameters.TryGetValue(_isAfterMarket)?.To<bool>() == true;
		set => Parameters[_isAfterMarket] = value;
	}

	/// <summary>Whether FYERS may slice the order according to exchange freeze limits.</summary>
	[DataMember]
	public bool IsSliceOrder
	{
		get => Parameters.TryGetValue(_isSliceOrder)?.To<bool>() == true;
		set => Parameters[_isSliceOrder] = value;
	}

	/// <summary>Cover or bracket stop-loss value.</summary>
	[DataMember]
	public decimal? StopLoss
	{
		get => Parameters.TryGetValue(_stopLoss)?.To<decimal?>();
		set => Parameters[_stopLoss] = value;
	}

	/// <summary>Bracket take-profit value.</summary>
	[DataMember]
	public decimal? TakeProfit
	{
		get => Parameters.TryGetValue(_takeProfit)?.To<decimal?>();
		set => Parameters[_takeProfit] = value;
	}

	/// <summary>Use the Good Till Triggered API.</summary>
	[DataMember]
	public bool IsGtt
	{
		get => Parameters.TryGetValue(_isGtt)?.To<bool>() == true;
		set => Parameters[_isGtt] = value;
	}

	/// <summary>Second-leg limit price for an OCO GTT order.</summary>
	[DataMember]
	public decimal? SecondPrice
	{
		get => Parameters.TryGetValue(_secondPrice)?.To<decimal?>();
		set => Parameters[_secondPrice] = value;
	}

	/// <summary>Second-leg trigger price for an OCO GTT order.</summary>
	[DataMember]
	public decimal? SecondTriggerPrice
	{
		get => Parameters.TryGetValue(_secondTriggerPrice)?.To<decimal?>();
		set => Parameters[_secondTriggerPrice] = value;
	}

	/// <summary>Second-leg volume for an OCO GTT order.</summary>
	[DataMember]
	public decimal? SecondVolume
	{
		get => Parameters.TryGetValue(_secondVolume)?.To<decimal?>();
		set => Parameters[_secondVolume] = value;
	}
}
