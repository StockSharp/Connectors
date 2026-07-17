namespace StockSharp.FubonNeo;

/// <summary>Fubon securities trading sessions.</summary>
[DataContract]
[Serializable]
public enum FubonNeoStockMarketTypes
{
	/// <summary>Regular board lot.</summary>
	[EnumMember]
	Common,
	/// <summary>Fixed-price session.</summary>
	[EnumMember]
	Fixing,
	/// <summary>Post-market odd lot.</summary>
	[EnumMember]
	Odd,
	/// <summary>Intraday odd lot.</summary>
	[EnumMember]
	IntradayOdd,
	/// <summary>Emerging stocks.</summary>
	[EnumMember]
	Emerging,
	/// <summary>Emerging-stock odd lot.</summary>
	[EnumMember]
	EmergingOdd,
}

/// <summary>Fubon securities order types.</summary>
[DataContract]
[Serializable]
public enum FubonNeoStockOrderTypes
{
	/// <summary>Cash stock.</summary>
	[EnumMember]
	Stock,
	/// <summary>Margin purchase.</summary>
	[EnumMember]
	Margin,
	/// <summary>Short sale.</summary>
	[EnumMember]
	Short,
	/// <summary>Securities borrowing and lending.</summary>
	[EnumMember]
	Sbl,
	/// <summary>Day trade.</summary>
	[EnumMember]
	DayTrade,
}

/// <summary>Fubon futures/options position effects.</summary>
[DataContract]
[Serializable]
public enum FubonNeoFuturesOrderTypes
{
	/// <summary>Let Fubon determine the position effect.</summary>
	[EnumMember]
	Auto,
	/// <summary>Open a new position.</summary>
	[EnumMember]
	New,
	/// <summary>Close an existing position.</summary>
	[EnumMember]
	Close,
	/// <summary>Futures day trade.</summary>
	[EnumMember]
	DayTrade,
}

/// <summary>Fubon native price types.</summary>
[DataContract]
[Serializable]
public enum FubonNeoPriceTypes
{
	/// <summary>Derive from the StockSharp order type.</summary>
	[EnumMember]
	Auto,
	/// <summary>Limit price.</summary>
	[EnumMember]
	Limit,
	/// <summary>Market price.</summary>
	[EnumMember]
	Market,
	/// <summary>Price at the daily upper limit.</summary>
	[EnumMember]
	LimitUp,
	/// <summary>Price at the daily lower limit.</summary>
	[EnumMember]
	LimitDown,
	/// <summary>Range-market order for futures/options.</summary>
	[EnumMember]
	RangeMarket,
	/// <summary>Reference price.</summary>
	[EnumMember]
	Reference,
}

/// <summary>Fubon-specific order parameters.</summary>
[DataContract]
[Serializable]
public class FubonNeoOrderCondition : OrderCondition
{
	private const string _stockMarketType = "StockMarketType";
	private const string _stockOrderType = "StockOrderType";
	private const string _futuresOrderType = "FuturesOrderType";
	private const string _priceType = "PriceType";
	private const string _isAfterHours = "IsAfterHours";
	private const string _userTag = "UserTag";

	/// <summary>Securities trading session.</summary>
	[DataMember]
	public FubonNeoStockMarketTypes StockMarketType
	{
		get => Parameters.TryGetValue(_stockMarketType)?.To<FubonNeoStockMarketTypes>() ?? FubonNeoStockMarketTypes.Common;
		set => Parameters[_stockMarketType] = value;
	}

	/// <summary>Securities financing/order type.</summary>
	[DataMember]
	public FubonNeoStockOrderTypes StockOrderType
	{
		get => Parameters.TryGetValue(_stockOrderType)?.To<FubonNeoStockOrderTypes>() ?? FubonNeoStockOrderTypes.Stock;
		set => Parameters[_stockOrderType] = value;
	}

	/// <summary>Futures/options position effect.</summary>
	[DataMember]
	public FubonNeoFuturesOrderTypes FuturesOrderType
	{
		get => Parameters.TryGetValue(_futuresOrderType)?.To<FubonNeoFuturesOrderTypes>() ?? FubonNeoFuturesOrderTypes.Auto;
		set => Parameters[_futuresOrderType] = value;
	}

	/// <summary>Native price type.</summary>
	[DataMember]
	public FubonNeoPriceTypes PriceType
	{
		get => Parameters.TryGetValue(_priceType)?.To<FubonNeoPriceTypes>() ?? FubonNeoPriceTypes.Auto;
		set => Parameters[_priceType] = value;
	}

	/// <summary>Use the futures/options after-hours session.</summary>
	[DataMember]
	public bool IsAfterHours
	{
		get => Parameters.TryGetValue(_isAfterHours)?.To<bool>() == true;
		set => Parameters[_isAfterHours] = value;
	}

	/// <summary>Alphanumeric Fubon user-defined value, up to ten characters.</summary>
	[DataMember]
	public string UserTag
	{
		get => Parameters.TryGetValue(_userTag)?.ToString();
		set => Parameters[_userTag] = value;
	}
}
