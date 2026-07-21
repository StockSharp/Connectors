namespace StockSharp.CoinGecko.Native.Model;

sealed class CoinGeckoSocketRequest
{
	[JsonProperty("command")]
	public CoinGeckoSocketCommands Command { get; set; }

	[JsonProperty("identifier")]
	public string Identifier { get; set; }

	[JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
	public string Data { get; set; }
}

sealed class CoinGeckoSocketIdentifier
{
	[JsonProperty("channel")]
	public CoinGeckoSocketChannels Channel { get; set; }
}

sealed class CoinGeckoCoinStreamRequest
{
	[JsonProperty("coin_id")]
	public string[] CoinIds { get; set; }

	[JsonProperty("vs_currencies", NullValueHandling = NullValueHandling.Ignore)]
	public string[] QuoteCurrencies { get; set; }

	[JsonProperty("action")]
	public CoinGeckoSocketActions Action { get; set; }
}

sealed class CoinGeckoOnchainTokenStreamRequest
{
	[JsonProperty("network_id:token_addresses")]
	public string[] Tokens { get; set; }

	[JsonProperty("action")]
	public CoinGeckoSocketActions Action { get; set; }
}

sealed class CoinGeckoOnchainPoolStreamRequest
{
	[JsonProperty("network_id:pool_addresses")]
	public string[] Pools { get; set; }

	[JsonProperty("action")]
	public CoinGeckoSocketActions Action { get; set; }
}

sealed class CoinGeckoOnchainOhlcvStreamRequest
{
	[JsonProperty("network_id:pool_addresses")]
	public string[] Pools { get; set; }

	[JsonProperty("interval")]
	public CoinGeckoSocketIntervals Interval { get; set; }

	[JsonProperty("token")]
	public CoinGeckoOnchainTokens Token { get; set; }

	[JsonProperty("action")]
	public CoinGeckoSocketActions Action { get; set; }
}

sealed class CoinGeckoSocketRoute
{
	[JsonProperty("c")]
	public CoinGeckoSocketChannelCodes? Channel { get; set; }

	[JsonProperty("ch")]
	public CoinGeckoSocketChannelCodes? OhlcvChannel { get; set; }

	[JsonProperty("i")]
	public string CoinOrInterval { get; set; }

	[JsonProperty("n")]
	public string Network { get; set; }

	[JsonProperty("ta")]
	public string TokenAddress { get; set; }

	[JsonProperty("pa")]
	public string PoolAddress { get; set; }

	[JsonProperty("tx")]
	public string TransactionHash { get; set; }

	[JsonProperty("type")]
	public CoinGeckoSocketMessageTypes? MessageType { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class CoinGeckoCoinPriceUpdate
{
	[JsonProperty("c")]
	public CoinGeckoSocketChannelCodes Channel { get; set; }

	[JsonProperty("i")]
	public string CoinId { get; set; }

	[JsonProperty("vs")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("p")]
	public decimal? Price { get; set; }

	[JsonProperty("pp")]
	public decimal? PriceChangePercentage24Hours { get; set; }

	[JsonProperty("m")]
	public decimal? MarketCap { get; set; }

	[JsonProperty("v")]
	public decimal? Volume24Hours { get; set; }

	[JsonProperty("t")]
	public decimal? Timestamp { get; set; }
}

sealed class CoinGeckoOnchainPriceUpdate
{
	[JsonProperty("c")]
	public CoinGeckoSocketChannelCodes Channel { get; set; }

	[JsonProperty("n")]
	public string Network { get; set; }

	[JsonProperty("ta")]
	public string TokenAddress { get; set; }

	[JsonProperty("p")]
	public decimal? PriceUsd { get; set; }

	[JsonProperty("pp")]
	public decimal? PriceChangePercentage24Hours { get; set; }

	[JsonProperty("m")]
	public decimal? MarketCapUsd { get; set; }

	[JsonProperty("v")]
	public decimal? VolumeUsd24Hours { get; set; }

	[JsonProperty("t")]
	public decimal? Timestamp { get; set; }
}

sealed class CoinGeckoOnchainTradeUpdate
{
	[JsonProperty("c")]
	public CoinGeckoSocketChannelCodes Channel { get; set; }

	[JsonProperty("n")]
	public string Network { get; set; }

	[JsonProperty("pa")]
	public string PoolAddress { get; set; }

	[JsonProperty("tx")]
	public string TransactionHash { get; set; }

	[JsonProperty("ty")]
	public CoinGeckoSocketTradeSides Side { get; set; }

	[JsonProperty("to")]
	public decimal? TokenAmount { get; set; }

	[JsonProperty("toq")]
	public decimal? QuoteTokenAmount { get; set; }

	[JsonProperty("vo")]
	public decimal? VolumeUsd { get; set; }

	[JsonProperty("pc")]
	public decimal? PriceNative { get; set; }

	[JsonProperty("pu")]
	public decimal? PriceUsd { get; set; }

	[JsonProperty("t")]
	public decimal? Timestamp { get; set; }
}

sealed class CoinGeckoOnchainOhlcvUpdate
{
	[JsonProperty("ch")]
	public CoinGeckoSocketChannelCodes Channel { get; set; }

	[JsonProperty("n")]
	public string Network { get; set; }

	[JsonProperty("pa")]
	public string PoolAddress { get; set; }

	[JsonProperty("to")]
	public CoinGeckoOnchainTokens Token { get; set; }

	[JsonProperty("i")]
	public CoinGeckoSocketIntervals Interval { get; set; }

	[JsonProperty("o")]
	public decimal? Open { get; set; }

	[JsonProperty("h")]
	public decimal? High { get; set; }

	[JsonProperty("l")]
	public decimal? Low { get; set; }

	[JsonProperty("c")]
	public decimal? Close { get; set; }

	[JsonProperty("v")]
	public decimal? Volume { get; set; }

	[JsonProperty("t")]
	public decimal? Timestamp { get; set; }
}
