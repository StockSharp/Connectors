namespace StockSharp.CapitalFutures;

/// <summary>Capital Futures position effects.</summary>
[DataContract]
[Serializable]
public enum CapitalFuturesPositionEffects
{
	/// <summary>Open a position.</summary>
	[EnumMember]
	Open,
	/// <summary>Close a position.</summary>
	[EnumMember]
	Close,
	/// <summary>Let Capital Futures determine open or close.</summary>
	[EnumMember]
	Auto,
}

/// <summary>Capital Futures market price modes.</summary>
[DataContract]
[Serializable]
public enum CapitalFuturesPriceTypes
{
	/// <summary>Use the StockSharp order type.</summary>
	[EnumMember]
	Auto,
	/// <summary>Exchange market order (M).</summary>
	[EnumMember]
	Market,
	/// <summary>Market-with-protection order (P).</summary>
	[EnumMember]
	MarketWithProtection,
}

/// <summary>Additional domestic futures/options order parameters.</summary>
[Serializable]
[DataContract]
public class CapitalFuturesOrderCondition : OrderCondition
{
	private const string _positionEffect = "PositionEffect";
	private const string _priceType = "PriceType";
	private const string _isDayTrade = "IsDayTrade";
	private const string _isPreOrder = "IsPreOrder";

	/// <summary>Position effect.</summary>
	[DataMember]
	public CapitalFuturesPositionEffects PositionEffect
	{
		get => Parameters.TryGetValue(_positionEffect)?.To<CapitalFuturesPositionEffects>()
			?? CapitalFuturesPositionEffects.Auto;
		set => Parameters[_positionEffect] = value;
	}

	/// <summary>Market price mode.</summary>
	[DataMember]
	public CapitalFuturesPriceTypes PriceType
	{
		get => Parameters.TryGetValue(_priceType)?.To<CapitalFuturesPriceTypes>()
			?? CapitalFuturesPriceTypes.Auto;
		set => Parameters[_priceType] = value;
	}

	/// <summary>Whether the order is marked as a day trade.</summary>
	[DataMember]
	public bool IsDayTrade
	{
		get => Parameters.TryGetValue(_isDayTrade)?.To<bool>() == true;
		set => Parameters[_isDayTrade] = value;
	}

	/// <summary>Whether the order is submitted to the T-session pre-order queue.</summary>
	[DataMember]
	public bool IsPreOrder
	{
		get => Parameters.TryGetValue(_isPreOrder)?.To<bool>() == true;
		set => Parameters[_isPreOrder] = value;
	}
}
