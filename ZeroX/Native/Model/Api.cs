namespace StockSharp.ZeroX.Native.Model;

sealed class ZeroXQuoteResponse
{
	[JsonProperty("liquidityAvailable")]
	public bool IsLiquidityAvailable { get; init; }

	[JsonProperty("sellToken")]
	public string SellToken { get; init; }

	[JsonProperty("buyToken")]
	public string BuyToken { get; init; }

	[JsonProperty("sellAmount")]
	public string SellAmount { get; init; }

	[JsonProperty("buyAmount")]
	public string BuyAmount { get; init; }

	[JsonProperty("allowanceTarget")]
	public string AllowanceTarget { get; init; }

	[JsonProperty("issues")]
	public ZeroXIssues Issues { get; init; }

	[JsonProperty("transaction")]
	public ZeroXTransactionData Transaction { get; init; }

	[JsonProperty("zid")]
	public string RequestId { get; init; }
}

sealed class ZeroXIssues
{
	[JsonProperty("allowance")]
	public ZeroXAllowanceIssue Allowance { get; init; }

	[JsonProperty("balance")]
	public ZeroXBalanceIssue Balance { get; init; }

	[JsonProperty("simulationIncomplete")]
	public bool IsSimulationIncomplete { get; init; }

	[JsonProperty("invalidSourcesPassed")]
	public string[] InvalidSources { get; init; }
}

sealed class ZeroXAllowanceIssue
{
	[JsonProperty("actual")]
	public string Actual { get; init; }

	[JsonProperty("spender")]
	public string Spender { get; init; }
}

sealed class ZeroXBalanceIssue
{
	[JsonProperty("token")]
	public string Token { get; init; }

	[JsonProperty("actual")]
	public string Actual { get; init; }

	[JsonProperty("expected")]
	public string Expected { get; init; }
}

sealed class ZeroXTransactionData
{
	[JsonProperty("to")]
	public string To { get; init; }

	[JsonProperty("data")]
	public string Data { get; init; }

	[JsonProperty("value")]
	public string Value { get; init; }

	[JsonProperty("gasPrice")]
	public string GasPrice { get; init; }

	[JsonProperty("gas")]
	public string Gas { get; init; }
}

sealed class ZeroXApiError
{
	[JsonProperty("name")]
	public string Name { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("code")]
	public string Code { get; init; }

	[JsonProperty("reason")]
	public string Reason { get; init; }

	[JsonProperty("validationErrors")]
	public ZeroXValidationError[] ValidationErrors { get; init; }
}

sealed class ZeroXValidationError
{
	[JsonProperty("field")]
	public string Field { get; init; }

	[JsonProperty("reason")]
	public string Reason { get; init; }
}
