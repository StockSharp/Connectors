namespace StockSharp.Etoro.Native.Model;

sealed class EtoroInstrumentSearchResponse
{
	[JsonProperty("page")]
	public int Page { get; set; }

	[JsonProperty("pageSize")]
	public int PageSize { get; set; }

	[JsonProperty("totalItems")]
	public int TotalItems { get; set; }

	[JsonProperty("items")]
	public EtoroInstrument[] Items { get; set; }
}

sealed class EtoroInstrumentDisplaysResponse
{
	[JsonProperty("instrumentDisplayDatas")]
	public EtoroInstrumentDisplay[] Items { get; set; }
}

sealed class EtoroInstrumentDisplay
{
	[JsonProperty("instrumentID")]
	public int InstrumentId { get; set; }

	[JsonProperty("instrumentDisplayName")]
	public string DisplayName { get; set; }

	[JsonProperty("instrumentTypeID")]
	public int InstrumentTypeId { get; set; }

	[JsonProperty("exchangeID")]
	public int ExchangeId { get; set; }

	[JsonProperty("symbolFull")]
	public string SymbolFull { get; set; }
}

sealed class EtoroInstrument
{
	[JsonProperty("instrumentId")]
	public int InstrumentId { get; set; }

	[JsonProperty("displayname")]
	public string DisplayName { get; set; }

	[JsonProperty("instrumentTypeID")]
	public int InstrumentTypeId { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("exchangeID")]
	public int ExchangeId { get; set; }

	[JsonProperty("isOpen")]
	public bool IsOpen { get; set; }

	[JsonProperty("internalSymbolFull")]
	public string InternalSymbolFull { get; set; }

	[JsonProperty("internalExchangeName")]
	public string InternalExchangeName { get; set; }

	[JsonProperty("internalAssetClassName")]
	public string InternalAssetClassName { get; set; }

	[JsonProperty("isDelisted")]
	public bool IsDelisted { get; set; }

	[JsonProperty("isCurrentlyTradable")]
	public bool IsCurrentlyTradable { get; set; }

	[JsonProperty("isExchangeOpen")]
	public bool IsExchangeOpen { get; set; }

	[JsonProperty("isBuyEnabled")]
	public bool IsBuyEnabled { get; set; }

	[JsonProperty("currentRate")]
	public decimal? CurrentRate { get; set; }
}

sealed class EtoroLiveRatesResponse
{
	[JsonProperty("rates")]
	public EtoroRate[] Rates { get; set; }
}

sealed class EtoroRate
{
	[JsonProperty("instrumentID")]
	public int InstrumentId { get; set; }

	[JsonProperty("Ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("Bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("LastExecution")]
	public decimal? LastExecution { get; set; }

	[JsonProperty("Date")]
	public DateTime Date { get; set; }

	[JsonProperty("PriceRateID")]
	public long PriceRateId { get; set; }
}

sealed class EtoroCandlesResponse
{
	[JsonProperty("interval")]
	public EtoroCandleIntervals Interval { get; set; }

	[JsonProperty("candles")]
	public EtoroCandleGroup[] Candles { get; set; }
}

sealed class EtoroCandleGroup
{
	[JsonProperty("instrumentId")]
	public int InstrumentId { get; set; }

	[JsonProperty("candles")]
	public EtoroCandle[] Candles { get; set; }
}

sealed class EtoroCandle
{
	[JsonProperty("instrumentID")]
	public int InstrumentId { get; set; }

	[JsonProperty("fromDate")]
	public DateTime FromDate { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }
}

[DataContract]
enum EtoroCandleDirections
{
	[EnumMember(Value = "asc")]
	Asc,

	[EnumMember(Value = "desc")]
	Desc,
}

[DataContract]
enum EtoroCandleIntervals
{
	[EnumMember]
	OneMinute,

	[EnumMember]
	FiveMinutes,

	[EnumMember]
	TenMinutes,

	[EnumMember]
	FifteenMinutes,

	[EnumMember]
	ThirtyMinutes,

	[EnumMember]
	OneHour,

	[EnumMember]
	FourHours,

	[EnumMember]
	OneDay,

	[EnumMember]
	OneWeek,
}
