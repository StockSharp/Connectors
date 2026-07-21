namespace StockSharp.Copper.Native.Model;

sealed class CopperCurrenciesResponse
{
	[JsonProperty("currencies")]
	public CopperCurrency[] Currencies { get; set; } = [];
}

sealed class CopperCurrency
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("mainCurrency")]
	public string MainCurrency { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("networkName")]
	public string NetworkName { get; set; }

	[JsonProperty("protocol")]
	public string Protocol { get; set; }

	[JsonProperty("decimal")]
	public string Decimals { get; set; }

	[JsonProperty("fiat")]
	public bool IsFiat { get; set; }

	[JsonProperty("stableCoin")]
	public bool IsStableCoin { get; set; }

	[JsonProperty("crossChainNetworks")]
	public string[] CrossChainNetworks { get; set; } = [];
}

sealed class CopperPortfoliosResponse
{
	[JsonProperty("portfolios")]
	public CopperPortfolio[] Portfolios { get; set; } = [];
}

sealed class CopperPortfolio
{
	[JsonProperty("portfolioId")]
	public string Id { get; set; }

	[JsonProperty("portfolioName")]
	public string Name { get; set; }

	[JsonProperty("portfolioDescription")]
	public string Description { get; set; }

	[JsonProperty("portfolioType")]
	[JsonConverter(typeof(CopperEnumConverter<CopperPortfolioTypes>))]
	public CopperPortfolioTypes Type { get; set; }

	[JsonProperty("isActive")]
	public bool IsActive { get; set; }

	[JsonProperty("createdAt")]
	public string CreatedAt { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }
}

sealed class CopperWalletsResponse
{
	[JsonProperty("wallets")]
	public CopperWallet[] Wallets { get; set; } = [];
}

sealed class CopperWallet
{
	[JsonProperty("walletId")]
	public string Id { get; set; }

	[JsonProperty("portfolioId")]
	public string PortfolioId { get; set; }

	[JsonProperty("portfolioType")]
	[JsonConverter(typeof(CopperEnumConverter<CopperPortfolioTypes>))]
	public CopperPortfolioTypes PortfolioType { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("mainCurrency")]
	public string MainCurrency { get; set; }

	[JsonProperty("available")]
	public string Available { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("locked")]
	public string Locked { get; set; }

	[JsonProperty("reserve")]
	public string Reserve { get; set; }

	[JsonProperty("stakeBalance")]
	public string StakeBalance { get; set; }

	[JsonProperty("totalBalance")]
	public string TotalBalance { get; set; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; set; }
}

sealed class CopperClearLoopPortfoliosResponse
{
	[JsonProperty("portfolios")]
	public CopperClearLoopPortfolio[] Portfolios { get; set; } = [];
}

sealed class CopperClearLoopPortfolio
{
	[JsonProperty("portfolioId")]
	public string PortfolioId { get; set; }

	[JsonProperty("clientAccountId")]
	public string ClientAccountId { get; set; }

	[JsonProperty("delegationsEnabled")]
	public bool IsDelegationEnabled { get; set; }

	[JsonProperty("undelegationsEnabled")]
	public bool IsUndelegationEnabled { get; set; }

	[JsonProperty("disabledDelegationsReason")]
	public string DisabledDelegationReason { get; set; }

	[JsonProperty("disabledUndelegationsReason")]
	public string DisabledUndelegationReason { get; set; }
}

sealed class CopperClearLoopBalancesResponse
{
	[JsonProperty("balances")]
	public CopperClearLoopBalance[] Balances { get; set; } = [];
}

sealed class CopperClearLoopBalance
{
	[JsonProperty("portfolioId")]
	public string PortfolioId { get; set; }

	[JsonProperty("clientAccountId")]
	public string ClientAccountId { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("mainCurrency")]
	public string MainCurrency { get; set; }

	[JsonProperty("balance")]
	public string Balance { get; set; }

	[JsonProperty("amount")]
	public string DelegatedAvailable { get; set; }

	[JsonProperty("available")]
	public string AvailableToUndelegate { get; set; }

	[JsonProperty("reserve")]
	public string Reserve { get; set; }

	[JsonProperty("totalAvailableToUndelegate")]
	public string TotalAvailableToUndelegate { get; set; }

	[JsonProperty("exchangeId")]
	public string ExchangeId { get; set; }
}
