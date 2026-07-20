namespace StockSharp.Osmosis.Native.Model;

enum OsmosisSwapKinds
{
	ExactInput,
	ExactOutput,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OsmosisBroadcastModes
{
	[EnumMember(Value = "BROADCAST_MODE_SYNC")]
	Sync,
}

sealed class OsmosisToken
{
	public string Denomination { get; init; }
	public string Symbol { get; init; }
	public string Name { get; init; }
	public int Decimals { get; init; }
}

sealed class OsmosisMarket
{
	public OsmosisToken BaseToken { get; init; }
	public OsmosisToken QuoteToken { get; init; }
	public string SecurityCode { get; init; }
}

sealed class OsmosisMarketDefinition
{
	public string BaseDenomination { get; init; }
	public string QuoteDenomination { get; init; }
	public string SecurityCode { get; init; }
}

sealed class OsmosisQuote
{
	public OsmosisSwapKinds Kind { get; init; }
	public BigInteger InputAmount { get; init; }
	public BigInteger OutputAmount { get; init; }
	public OsmosisQuotePool[] Pools { get; init; }
}

sealed class OsmosisSwapEvent
{
	public string TransactionHash { get; init; }
	public long Height { get; init; }
	public ulong PoolId { get; init; }
	public int MessageIndex { get; init; }
	public OsmosisCoin Input { get; init; }
	public OsmosisCoin Output { get; init; }
}

sealed class OsmosisCoin
{
	public string Denomination { get; init; }
	public BigInteger Amount { get; init; }
}

sealed class OsmosisApiException : InvalidOperationException
{
	public OsmosisApiException(HttpStatusCode statusCode, int? code,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public OsmosisApiException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}
