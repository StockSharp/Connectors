namespace StockSharp.ApexOmni.Native.Model;

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniWebSocketRequest
{
	[JsonProperty("op", Required = Required.Always)]
	public ApexOmniWebSocketOperations Operation { get; set; }

	[JsonProperty("args", Required = Required.Always)]
	public string[] Arguments { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniWebSocketHeader
{
	[JsonProperty("op")]
	public ApexOmniWebSocketOperations? Operation { get; set; }

	[JsonProperty("args")]
	public string[] Arguments { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("type")]
	public ApexOmniWebSocketTypes? Type { get; set; }

	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("ret_msg")]
	public string ReturnMessage { get; set; }

	[JsonProperty("request")]
	public ApexOmniWebSocketRequest Request { get; set; }

}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniWebSocketFeed<TData>
{
	[JsonProperty("topic", Required = Required.Always)]
	public string Topic { get; set; }

	[JsonProperty("type")]
	public ApexOmniWebSocketTypes Type { get; set; }

	[JsonProperty("data", Required = Required.Always)]
	public TData Data { get; set; }

	[JsonProperty("cs")]
	public long Checksum { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniPrivateLogin
{
	[JsonProperty("type", Required = Required.Always)]
	public ApexOmniPrivateLoginTypes Type { get; set; } =
		ApexOmniPrivateLoginTypes.Login;

	[JsonProperty("topics", Required = Required.Always)]
	public string[] Topics { get; set; }

	[JsonProperty("httpMethod", Required = Required.Always)]
	public ApexOmniHttpMethods HttpMethod { get; set; } =
		ApexOmniHttpMethods.Get;

	[JsonProperty("requestPath", Required = Required.Always)]
	public string RequestPath { get; set; } = "/ws/accounts";

	[JsonProperty("apiKey", Required = Required.Always)]
	public string ApiKey { get; set; }

	[JsonProperty("passphrase", Required = Required.Always)]
	public string Passphrase { get; set; }

	[JsonProperty("timestamp", Required = Required.Always)]
	public long Timestamp { get; set; }

	[JsonProperty("signature", Required = Required.Always)]
	public string Signature { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniPrivateContents
{
	[JsonProperty("contractAccounts")]
	public ApexOmniContractAccount[] ContractAccounts { get; set; }

	[JsonProperty("accounts")]
	public ApexOmniContractAccount[] Accounts { get; set; }

	[JsonProperty("contractWallets")]
	public ApexOmniWallet[] ContractWallets { get; set; }

	[JsonProperty("spotWallets")]
	public ApexOmniWallet[] SpotWallets { get; set; }

	[JsonProperty("wallets")]
	public ApexOmniWallet[] Wallets { get; set; }

	[JsonProperty("orders")]
	public ApexOmniOrder[] Orders { get; set; }

	[JsonProperty("positions")]
	public ApexOmniPosition[] Positions { get; set; }

	[JsonProperty("fills")]
	public ApexOmniFill[] Fills { get; set; }
}

[JsonObject(MemberSerialization.OptIn)]
sealed class ApexOmniPrivateFeed
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("type")]
	public ApexOmniWebSocketTypes Type { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("contents")]
	public ApexOmniPrivateContents Contents { get; set; }

	[JsonProperty("contractAccounts")]
	public ApexOmniContractAccount[] ContractAccounts { get; set; }

	[JsonProperty("accounts")]
	public ApexOmniContractAccount[] Accounts { get; set; }

	[JsonProperty("contractWallets")]
	public ApexOmniWallet[] ContractWallets { get; set; }

	[JsonProperty("spotWallets")]
	public ApexOmniWallet[] SpotWallets { get; set; }

	[JsonProperty("wallets")]
	public ApexOmniWallet[] Wallets { get; set; }

	[JsonProperty("orders")]
	public ApexOmniOrder[] Orders { get; set; }

	[JsonProperty("positions")]
	public ApexOmniPosition[] Positions { get; set; }

	[JsonProperty("fills")]
	public ApexOmniFill[] Fills { get; set; }

	public ApexOmniPrivateContents GetContents()
		=> Contents ?? new()
		{
			ContractAccounts = ContractAccounts,
			Accounts = Accounts,
			ContractWallets = ContractWallets,
			SpotWallets = SpotWallets,
			Wallets = Wallets,
			Orders = Orders,
			Positions = Positions,
			Fills = Fills,
		};

	public bool HasData()
		=> Contents is not null || ContractAccounts is not null ||
			Accounts is not null ||
			ContractWallets is not null || SpotWallets is not null ||
			Wallets is not null ||
			Orders is not null || Positions is not null || Fills is not null;
}
