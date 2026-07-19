namespace StockSharp.Orca.Native.Model;

sealed class OrcaApiResponse<TResult>
{
	[JsonProperty("data")]
	public TResult Data { get; init; }

	[JsonProperty("meta")]
	public OrcaApiMeta Meta { get; init; }
}

sealed class OrcaApiMeta
{
	[JsonProperty("cursor")]
	public OrcaApiCursor Cursor { get; init; }
}

sealed class OrcaApiCursor
{
	[JsonProperty("next")]
	public string Next { get; init; }

	[JsonProperty("previous")]
	public string Previous { get; init; }
}

sealed class OrcaApiPool
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("whirlpoolsConfig")]
	public string WhirlpoolsConfig { get; init; }

	[JsonProperty("tickSpacing")]
	public int TickSpacing { get; init; }

	[JsonProperty("feeRate")]
	public int FeeRate { get; init; }

	[JsonProperty("liquidity")]
	public string Liquidity { get; init; }

	[JsonProperty("sqrtPrice")]
	public string SqrtPrice { get; init; }

	[JsonProperty("tickCurrentIndex")]
	public int TickCurrentIndex { get; init; }

	[JsonProperty("tokenMintA")]
	public string TokenMintA { get; init; }

	[JsonProperty("tokenVaultA")]
	public string TokenVaultA { get; init; }

	[JsonProperty("tokenMintB")]
	public string TokenMintB { get; init; }

	[JsonProperty("tokenVaultB")]
	public string TokenVaultB { get; init; }

	[JsonProperty("hasWarning")]
	public bool IsWarning { get; init; }

	[JsonProperty("adaptiveFeeEnabled")]
	public bool IsAdaptiveFeeEnabled { get; init; }

	[JsonProperty("tokenA")]
	public OrcaApiToken TokenA { get; init; }

	[JsonProperty("tokenB")]
	public OrcaApiToken TokenB { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("tvlUsdc")]
	public string TvlUsdc { get; init; }

	[JsonProperty("tradeEnableTimestamp")]
	public string TradeEnableTimestamp { get; init; }
}

sealed class OrcaApiToken
{
	[JsonProperty("address")]
	public string Address { get; init; }

	[JsonProperty("programId")]
	public string ProgramId { get; init; }

	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("decimals")]
	public int Decimals { get; init; }

	[JsonProperty("tags")]
	public string[] Tags { get; init; } = [];
}

sealed class OrcaApiError
{
	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}
