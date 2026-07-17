namespace StockSharp.Yuanta;

/// <summary>Yuanta securities trading sessions.</summary>
[DataContract]
[Serializable]
public enum YuantaStockMarketTypes
{
	/// <summary>Regular board lot.</summary>
	[EnumMember]
	Regular,
	/// <summary>After-hours odd lot.</summary>
	[EnumMember]
	OddLot,
	/// <summary>Intraday odd lot.</summary>
	[EnumMember]
	IntradayOddLot,
	/// <summary>After-hours fixed-price session.</summary>
	[EnumMember]
	AfterHours,
}

/// <summary>Yuanta securities financing types.</summary>
[DataContract]
[Serializable]
public enum YuantaStockOrderTypes
{
	/// <summary>Cash securities order.</summary>
	[EnumMember]
	Cash,
	/// <summary>Margin purchase.</summary>
	[EnumMember]
	Margin,
	/// <summary>Short sale.</summary>
	[EnumMember]
	Short,
	/// <summary>Strategy securities borrowing sale.</summary>
	[EnumMember]
	StrategyBorrowed,
	/// <summary>Hedging securities borrowing sale.</summary>
	[EnumMember]
	HedgeBorrowed,
	/// <summary>Cash day-trading control order.</summary>
	[EnumMember]
	DayTrade,
}

/// <summary>Yuanta futures/options position effects.</summary>
[DataContract]
[Serializable]
public enum YuantaFuturesPositionEffects
{
	/// <summary>Let Yuanta select the position effect.</summary>
	[EnumMember]
	Auto,
	/// <summary>Open a new position.</summary>
	[EnumMember]
	Open,
	/// <summary>Close an existing position.</summary>
	[EnumMember]
	Close,
}

/// <summary>Yuanta futures/options price types.</summary>
[DataContract]
[Serializable]
public enum YuantaFuturesPriceTypes
{
	/// <summary>Derive the native type from the StockSharp order type.</summary>
	[EnumMember]
	Auto,
	/// <summary>Market order.</summary>
	[EnumMember]
	Market,
	/// <summary>Limit order.</summary>
	[EnumMember]
	Limit,
	/// <summary>Range-market order.</summary>
	[EnumMember]
	RangeMarket,
}

/// <summary>Yuanta-specific order parameters.</summary>
[DataContract]
[Serializable]
public class YuantaOrderCondition : OrderCondition
{
	private const string _stockMarketType = "StockMarketType";
	private const string _stockOrderType = "StockOrderType";
	private const string _positionEffect = "PositionEffect";
	private const string _futuresPriceType = "FuturesPriceType";
	private const string _orderSymbol = "OrderSymbol";
	private const string _settlementMonth = "SettlementMonth";
	private const string _optionType = "OptionType";
	private const string _strikePrice = "StrikePrice";
	private const string _isDayTrade = "IsDayTrade";
	private const string _isPreOrder = "IsPreOrder";
	private const string _userTag = "UserTag";

	/// <summary>Securities trading session.</summary>
	[DataMember]
	public YuantaStockMarketTypes StockMarketType
	{
		get => Parameters.TryGetValue(_stockMarketType)?.To<YuantaStockMarketTypes>() ?? YuantaStockMarketTypes.Regular;
		set => Parameters[_stockMarketType] = value;
	}

	/// <summary>Securities financing type.</summary>
	[DataMember]
	public YuantaStockOrderTypes StockOrderType
	{
		get => Parameters.TryGetValue(_stockOrderType)?.To<YuantaStockOrderTypes>() ?? YuantaStockOrderTypes.Cash;
		set => Parameters[_stockOrderType] = value;
	}

	/// <summary>Futures/options position effect.</summary>
	[DataMember]
	public YuantaFuturesPositionEffects PositionEffect
	{
		get => Parameters.TryGetValue(_positionEffect)?.To<YuantaFuturesPositionEffects>() ?? YuantaFuturesPositionEffects.Auto;
		set => Parameters[_positionEffect] = value;
	}

	/// <summary>Native futures/options price type.</summary>
	[DataMember]
	public YuantaFuturesPriceTypes FuturesPriceType
	{
		get => Parameters.TryGetValue(_futuresPriceType)?.To<YuantaFuturesPriceTypes>() ?? YuantaFuturesPriceTypes.Auto;
		set => Parameters[_futuresPriceType] = value;
	}

	/// <summary>Order-routing code from Yuanta FunctionList.xlsx when it differs from the quote symbol.</summary>
	[DataMember]
	public string OrderSymbol
	{
		get => Parameters.TryGetValue(_orderSymbol)?.ToString();
		set => Parameters[_orderSymbol] = value;
	}

	/// <summary>Futures/options contract month in YYYYMM format.</summary>
	[DataMember]
	public int SettlementMonth
	{
		get => Parameters.TryGetValue(_settlementMonth)?.To<int>() ?? 0;
		set => Parameters[_settlementMonth] = value;
	}

	/// <summary>Option side for an options order.</summary>
	[DataMember]
	public OptionTypes? OptionType
	{
		get => Parameters.TryGetValue(_optionType)?.To<OptionTypes?>();
		set => Parameters[_optionType] = value;
	}

	/// <summary>Option strike price.</summary>
	[DataMember]
	public decimal StrikePrice
	{
		get => Parameters.TryGetValue(_strikePrice)?.To<decimal>() ?? 0;
		set => Parameters[_strikePrice] = value;
	}

	/// <summary>Submit a futures/options day-trade order.</summary>
	[DataMember]
	public bool IsDayTrade
	{
		get => Parameters.TryGetValue(_isDayTrade)?.To<bool>() == true;
		set => Parameters[_isDayTrade] = value;
	}

	/// <summary>Submit a pre-order/reservation order.</summary>
	[DataMember]
	public bool IsPreOrder
	{
		get => Parameters.TryGetValue(_isPreOrder)?.To<bool>() == true;
		set => Parameters[_isPreOrder] = value;
	}

	/// <summary>User-defined alphanumeric value, up to 32 characters.</summary>
	[DataMember]
	public string UserTag
	{
		get => Parameters.TryGetValue(_userTag)?.ToString();
		set => Parameters[_userTag] = value;
	}
}
