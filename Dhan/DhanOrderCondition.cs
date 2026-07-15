namespace StockSharp.Dhan;

/// <summary>Dhan specific order condition.</summary>
[DataContract]
[Serializable]
public class DhanOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _afterMarket = "AfterMarket";
	private const string _afterMarketTime = "AfterMarketTime";
	private const string _bracketProfit = "BracketProfit";
	private const string _bracketStopLoss = "BracketStopLoss";
	private const string _leg = "Leg";
	private const string _isForever = "IsForever";
	private const string _foreverFlag = "ForeverFlag";
	private const string _secondPrice = "SecondPrice";
	private const string _secondTriggerPrice = "SecondTriggerPrice";
	private const string _secondVolume = "SecondVolume";

	/// <summary>Order product.</summary>
	[DataMember]
	public DhanProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<DhanProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Stop or Forever trigger price.</summary>
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

	/// <summary>After-market execution time.</summary>
	[DataMember]
	public DhanAfterMarketTimes AfterMarketTime
	{
		get => Parameters.TryGetValue(_afterMarketTime)?.To<DhanAfterMarketTimes>() ?? DhanAfterMarketTimes.Open;
		set => Parameters[_afterMarketTime] = value;
	}

	/// <summary>Bracket-order profit distance.</summary>
	[DataMember]
	public decimal? BracketProfit
	{
		get => Parameters.TryGetValue(_bracketProfit)?.To<decimal?>();
		set => Parameters[_bracketProfit] = value;
	}

	/// <summary>Bracket-order stop-loss distance.</summary>
	[DataMember]
	public decimal? BracketStopLoss
	{
		get => Parameters.TryGetValue(_bracketStopLoss)?.To<decimal?>();
		set => Parameters[_bracketStopLoss] = value;
	}

	/// <summary>Order leg selected for modification.</summary>
	[DataMember]
	public DhanOrderLegs? Leg
	{
		get => Parameters.TryGetValue(_leg)?.To<DhanOrderLegs?>();
		set => Parameters[_leg] = value;
	}

	/// <summary>Use the Forever/Good Till Triggered order API.</summary>
	[DataMember]
	public bool IsForever
	{
		get => Parameters.TryGetValue(_isForever)?.To<bool>() == true;
		set => Parameters[_isForever] = value;
	}

	/// <summary>Forever order structure.</summary>
	[DataMember]
	public DhanForeverOrderFlags ForeverFlag
	{
		get => Parameters.TryGetValue(_foreverFlag)?.To<DhanForeverOrderFlags>() ?? DhanForeverOrderFlags.Single;
		set => Parameters[_foreverFlag] = value;
	}

	/// <summary>Second-leg limit price for a Forever OCO order.</summary>
	[DataMember]
	public decimal? SecondPrice
	{
		get => Parameters.TryGetValue(_secondPrice)?.To<decimal?>();
		set => Parameters[_secondPrice] = value;
	}

	/// <summary>Second-leg trigger price for a Forever OCO order.</summary>
	[DataMember]
	public decimal? SecondTriggerPrice
	{
		get => Parameters.TryGetValue(_secondTriggerPrice)?.To<decimal?>();
		set => Parameters[_secondTriggerPrice] = value;
	}

	/// <summary>Second-leg volume for a Forever OCO order.</summary>
	[DataMember]
	public decimal? SecondVolume
	{
		get => Parameters.TryGetValue(_secondVolume)?.To<decimal?>();
		set => Parameters[_secondVolume] = value;
	}
}
