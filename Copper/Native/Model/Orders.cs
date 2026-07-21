namespace StockSharp.Copper.Native.Model;

sealed class CopperCreateOrderRequest
{
	[JsonProperty("externalOrderId")]
	public string ExternalOrderId { get; init; }

	[JsonProperty("orderType")]
	[JsonConverter(typeof(CopperEnumConverter<CopperOrderTypes>))]
	public CopperOrderTypes OrderType { get; init; } =
		CopperOrderTypes.Withdraw;

	[JsonProperty("portfolioId")]
	public string PortfolioId { get; init; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; init; }

	[JsonProperty("mainCurrency")]
	public string MainCurrency { get; init; }

	[JsonProperty("amount")]
	public string Amount { get; init; }

	[JsonProperty("toAddress")]
	public string ToAddress { get; init; }

	[JsonProperty("toCryptoAddressId")]
	public string ToCryptoAddressId { get; init; }

	[JsonProperty("toPortfolioId")]
	public string ToPortfolioId { get; init; }

	[JsonProperty("memo")]
	public string Memo { get; init; }

	[JsonProperty("feeLevel", DefaultValueHandling =
		DefaultValueHandling.Ignore)]
	[JsonConverter(typeof(CopperEnumConverter<CopperFeeLevels>))]
	public CopperFeeLevels FeeLevel { get; init; }

	[JsonProperty("includeFeeInWithdraw")]
	public bool? IsFeeIncluded { get; init; }

	[JsonProperty("description")]
	public string Description { get; init; }
}

sealed class CopperCancelOrderRequest
{
	[JsonProperty("reason")]
	public string Reason { get; init; }
}

sealed class CopperOrdersResponse
{
	[JsonProperty("orders")]
	public CopperOrder[] Orders { get; set; } = [];
}

sealed class CopperOrder
{
	[JsonProperty("orderId")]
	public string Id { get; set; }

	[JsonProperty("externalOrderId")]
	public string ExternalOrderId { get; set; }

	[JsonProperty("status")]
	[JsonConverter(typeof(CopperEnumConverter<CopperOrderStatuses>))]
	public CopperOrderStatuses Status { get; set; }

	[JsonProperty("orderType")]
	[JsonConverter(typeof(CopperEnumConverter<CopperOrderTypes>))]
	public CopperOrderTypes Type { get; set; }

	[JsonProperty("portfolioId")]
	public string PortfolioId { get; set; }

	[JsonProperty("portfolioType")]
	[JsonConverter(typeof(CopperEnumConverter<CopperPortfolioTypes>))]
	public CopperPortfolioTypes PortfolioType { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("mainCurrency")]
	public string MainCurrency { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }

	[JsonProperty("terminatedAt")]
	public string TerminatedAt { get; set; }

	[JsonProperty("extra")]
	public CopperOrderExtra Extra { get; set; }
}

sealed class CopperOrderExtra
{
	[JsonProperty("clearLoop")]
	public bool IsClearLoop { get; set; }

	[JsonProperty("clientAccountId")]
	public string ClientAccountId { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("memo")]
	public string Memo { get; set; }

	[JsonProperty("toAddress")]
	public string ToAddress { get; set; }

	[JsonProperty("toCryptoAddressId")]
	public string ToCryptoAddressId { get; set; }

	[JsonProperty("toPortfolioId")]
	public string ToPortfolioId { get; set; }

	[JsonProperty("transactionId")]
	public string TransactionId { get; set; }

	[JsonProperty("withdrawFee")]
	public string WithdrawFee { get; set; }

	[JsonProperty("includeFeeInWithdraw")]
	public bool? IsFeeIncluded { get; set; }
}
