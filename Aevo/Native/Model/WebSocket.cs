namespace StockSharp.Aevo.Native.Model;

sealed class AevoSocketRequest
{
	[JsonProperty("op")]
	public string Operation { get; init; }

	[JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Data { get; init; }

	[JsonProperty("auth", NullValueHandling = NullValueHandling.Ignore)]
	public AevoSocketAuth Authentication { get; init; }
}

sealed class AevoSocketAuth
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }

	[JsonProperty("key")]
	public string Key { get; init; }
}

sealed class AevoSocketHeader
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }
}

sealed class AevoTickerEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("data")]
	public AevoTickerData Data { get; init; }
}

sealed class AevoTickerData
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("tickers")]
	public AevoTicker[] Tickers { get; init; }
}

sealed class AevoTicker
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; init; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; init; }

	[JsonProperty("instrument_type")]
	public AevoInstrumentTypes InstrumentType { get; init; }

	[JsonProperty("index_price")]
	public string IndexPrice { get; init; }

	[JsonProperty("open_interest")]
	public string OpenInterest { get; init; }

	[JsonProperty("funding_rate")]
	public string FundingRate { get; init; }

	[JsonProperty("next_funding_rate")]
	public string NextFundingRate { get; init; }

	[JsonProperty("mark")]
	public AevoPriceLevel Mark { get; init; }

	[JsonProperty("bid")]
	public AevoPriceLevel Bid { get; init; }

	[JsonProperty("ask")]
	public AevoPriceLevel Ask { get; init; }
}

sealed class AevoOrderBookEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("data")]
	public AevoOrderBook Data { get; init; }
}

sealed class AevoTradeEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("data")]
	public AevoTrade Data { get; init; }
}

sealed class AevoPositionsEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("data")]
	public AevoPositionsData Data { get; init; }
}

sealed class AevoPositionsData
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("positions")]
	public AevoPosition[] Positions { get; init; }
}

sealed class AevoOrdersEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("data")]
	public AevoOrdersData Data { get; init; }
}

sealed class AevoOrdersData
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("orders")]
	public AevoOrder[] Orders { get; init; }
}

sealed class AevoFillEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("data")]
	public AevoFillData Data { get; init; }
}

sealed class AevoFillData
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; init; }

	[JsonProperty("fill")]
	public AevoTrade Fill { get; init; }
}
