namespace StockSharp.Shioaji;

/// <summary>Shioaji stock order lots.</summary>
[DataContract]
[Serializable]
public enum ShioajiStockOrderLots
{
	/// <summary>Regular board lot.</summary>
	[EnumMember]
	Common,

	/// <summary>After-hours odd lot.</summary>
	[EnumMember]
	Odd,

	/// <summary>Intraday odd lot.</summary>
	[EnumMember]
	IntradayOdd,

	/// <summary>After-hours fixed-price order.</summary>
	[EnumMember]
	Fixing,
}

/// <summary>Shioaji stock financing conditions.</summary>
[DataContract]
[Serializable]
public enum ShioajiStockOrderConditions
{
	/// <summary>Cash.</summary>
	[EnumMember]
	Cash,

	/// <summary>Netting/day trade.</summary>
	[EnumMember]
	Netting,

	/// <summary>Margin purchase.</summary>
	[EnumMember]
	MarginTrading,

	/// <summary>Short sale.</summary>
	[EnumMember]
	ShortSelling,

	/// <summary>Emerging-market stock.</summary>
	[EnumMember]
	Emerging,
}

/// <summary>Shioaji futures open/close types.</summary>
[DataContract]
[Serializable]
public enum ShioajiFuturesOpenCloseTypes
{
	/// <summary>Let the broker determine open or close.</summary>
	[EnumMember]
	Auto,

	/// <summary>Open a new position.</summary>
	[EnumMember]
	New,

	/// <summary>Close an existing position.</summary>
	[EnumMember]
	Cover,

	/// <summary>Day-trade position.</summary>
	[EnumMember]
	DayTrade,
}

/// <summary>Shioaji native price types.</summary>
[DataContract]
[Serializable]
public enum ShioajiPriceTypes
{
	/// <summary>Use the StockSharp order type.</summary>
	[EnumMember]
	Auto,

	/// <summary>Limit.</summary>
	[EnumMember]
	Limit,

	/// <summary>Market.</summary>
	[EnumMember]
	Market,

	/// <summary>Range market.</summary>
	[EnumMember]
	RangeMarket,
}

/// <summary>Shioaji-specific order condition.</summary>
[DataContract]
[Serializable]
public class ShioajiOrderCondition : OrderCondition
{
	private const string _stockOrderLot = "StockOrderLot";
	private const string _stockOrderCondition = "StockOrderCondition";
	private const string _futuresOpenCloseType = "FuturesOpenCloseType";
	private const string _priceType = "PriceType";
	private const string _customField = "CustomField";

	/// <summary>Stock lot type.</summary>
	[DataMember]
	public ShioajiStockOrderLots? StockOrderLot
	{
		get => Parameters.TryGetValue(_stockOrderLot)?.To<ShioajiStockOrderLots?>();
		set => Parameters[_stockOrderLot] = value;
	}

	/// <summary>Stock financing condition.</summary>
	[DataMember]
	public ShioajiStockOrderConditions? StockOrderCondition
	{
		get => Parameters.TryGetValue(_stockOrderCondition)?.To<ShioajiStockOrderConditions?>();
		set => Parameters[_stockOrderCondition] = value;
	}

	/// <summary>Futures open/close type.</summary>
	[DataMember]
	public ShioajiFuturesOpenCloseTypes? FuturesOpenCloseType
	{
		get => Parameters.TryGetValue(_futuresOpenCloseType)?.To<ShioajiFuturesOpenCloseTypes?>();
		set => Parameters[_futuresOpenCloseType] = value;
	}

	/// <summary>Native price type override.</summary>
	[DataMember]
	public ShioajiPriceTypes? PriceType
	{
		get => Parameters.TryGetValue(_priceType)?.To<ShioajiPriceTypes?>();
		set => Parameters[_priceType] = value;
	}

	/// <summary>Broker memo, up to six characters.</summary>
	[DataMember]
	public string CustomField
	{
		get => Parameters.TryGetValue(_customField)?.ToString();
		set => Parameters[_customField] = value;
	}
}
