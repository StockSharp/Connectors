namespace StockSharp.LsegRealTime.Native;

internal enum LsegSubscriptionKinds
{
	MarketPrice,
	MarketByPrice,
}

internal sealed class LsegClientConfiguration
{
	public LsegAuthenticationModes AuthenticationMode { get; init; }
	public string Address { get; init; }
	public string StandbyAddress { get; init; }
	public bool IsHotStandby { get; init; }
	public string Login { get; init; }
	public SecureString Password { get; init; }
	public string ClientId { get; init; }
	public SecureString Secret { get; init; }
	public string ApplicationId { get; init; }
	public string Position { get; init; }
	public string Service { get; init; }
	public string Region { get; init; }
	public string Scope { get; init; }
	public string AuthUrl { get; init; }
	public string DiscoveryUrl { get; init; }
}

internal sealed class LsegEndpoint
{
	public string Host { get; init; }
	public int Port { get; init; }
	public string[] Locations { get; init; } = [];

	public Uri ToWebSocketUri()
		=> new($"wss://{Host}:{Port}/WebSocket");
}

internal sealed class LsegTokenRequest
{
	public string GrantType { get; init; }
	public string UserName { get; init; }
	public string Password { get; init; }
	public string ClientId { get; init; }
	public string ClientSecret { get; init; }
	public string Scope { get; init; }
	public string RefreshToken { get; init; }
	public bool IsExclusiveSignOn { get; init; }

	public string ToFormBody()
	{
		var values = new List<string>();
		Add(values, "grant_type", GrantType);
		Add(values, "username", UserName);
		Add(values, "password", Password);
		Add(values, "client_id", ClientId);
		Add(values, "client_secret", ClientSecret);
		Add(values, "scope", Scope);
		Add(values, "refresh_token", RefreshToken);
		if (IsExclusiveSignOn)
			Add(values, "takeExclusiveSignOnControl", "True");
		return string.Join('&', values);
	}

	private static void Add(List<string> values, string name, string value)
	{
		if (!value.IsEmpty())
			values.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
	}
}

internal sealed class LsegTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }
}

internal sealed class LsegServiceDiscoveryResponse
{
	[JsonProperty("services")]
	public LsegDiscoveredService[] Services { get; set; } = [];
}

internal sealed class LsegDiscoveredService
{
	[JsonProperty("endpoint")]
	public string Endpoint { get; set; }

	[JsonProperty("port")]
	public int Port { get; set; }

	[JsonProperty("location")]
	public string[] Locations { get; set; } = [];
}

internal sealed class LsegLoginRequest
{
	[JsonProperty("ID")]
	public long Id { get; set; } = 1;

	public string Domain { get; set; } = "Login";
	public LsegLoginKey Key { get; set; }
	public bool? Refresh { get; set; }
}

internal sealed class LsegLoginKey
{
	public string Name { get; set; }
	public string NameType { get; set; }
	public LsegLoginElements Elements { get; set; }
}

internal sealed class LsegLoginElements
{
	public string ApplicationId { get; set; }
	public string Position { get; set; }
	public string AuthenticationToken { get; set; }
}

internal sealed class LsegItemRequest
{
	[JsonProperty("ID")]
	public long Id { get; set; }

	public string Domain { get; set; }
	public LsegItemKey Key { get; set; }
	public bool Streaming { get; set; }
	public bool KeyInUpdates { get; set; } = true;
	public string[] View { get; set; }
}

internal sealed class LsegItemKey
{
	public string Name { get; set; }
	public string Service { get; set; }
}

internal sealed class LsegCloseRequest
{
	[JsonProperty("ID")]
	public long Id { get; set; }

	public string Type { get; set; } = "Close";
}

internal sealed class LsegPongMessage
{
	public string Type { get; set; } = "Pong";
}

internal sealed class LsegSourceRequest
{
	[JsonProperty("ID")]
	public long Id { get; set; } = 2;

	public string Domain { get; set; } = "Source";
	public LsegSourceKey Key { get; set; } = new();
}

internal sealed class LsegSourceKey
{
	public int Filter { get; set; } = 3;
}

internal sealed class LsegWireMessage
{
	[JsonProperty("ID")]
	public long Id { get; set; }

	public string Type { get; set; }
	public string Domain { get; set; }
	public string UpdateType { get; set; }
	public string Text { get; set; }
	public long? SeqNumber { get; set; }
	public int? PartNum { get; set; }
	public bool? Complete { get; set; }
	public bool? ClearCache { get; set; }
	public LsegWireKey Key { get; set; }
	public LsegWireState State { get; set; }
	public LsegWireFields Fields { get; set; }
	public LsegWireMap Map { get; set; }
}

internal sealed class LsegWireKey
{
	public string Name { get; set; }
	public string Service { get; set; }
	public int? Filter { get; set; }
}

internal sealed class LsegWireState
{
	public string Stream { get; set; }
	public string Data { get; set; }
	public string Code { get; set; }
	public string Text { get; set; }

	public bool IsOpenAndOk
		=> Stream.EqualsIgnoreCase("Open") && Data.EqualsIgnoreCase("Ok");
}

internal sealed class LsegWireFields
{
	[JsonProperty("BID")]
	public decimal? Bid { get; set; }

	[JsonProperty("ASK")]
	public decimal? Ask { get; set; }

	[JsonProperty("BIDSIZE")]
	public decimal? BidSize { get; set; }

	[JsonProperty("ASKSIZE")]
	public decimal? AskSize { get; set; }

	[JsonProperty("TRDPRC_1")]
	public decimal? TradePrice { get; set; }

	[JsonProperty("TRDVOL_1")]
	public decimal? TradeVolume { get; set; }

	[JsonProperty("OPEN_PRC")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("HIGH_1")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("LOW_1")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("HST_CLOSE")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("ACVOL_1")]
	public decimal? Volume { get; set; }

	[JsonProperty("OPEN_INT")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("VWAP")]
	public decimal? Vwap { get; set; }

	[JsonProperty("NETCHNG_1")]
	public decimal? Change { get; set; }

	[JsonProperty("PCTCHNG")]
	public decimal? ChangePercent { get; set; }

	[JsonProperty("QUOTIM_MS")]
	public long? QuoteTimeMilliseconds { get; set; }

	[JsonProperty("TRDTIM_MS")]
	public long? TradeTimeMilliseconds { get; set; }

	[JsonProperty("TRADE_DATE")]
	public string TradeDate { get; set; }

	[JsonProperty("DSPLY_NAME")]
	public string DisplayName { get; set; }

	[JsonProperty("CURRENCY")]
	public string Currency { get; set; }

	[JsonProperty("RDN_EXCHID")]
	public string Exchange { get; set; }

	[JsonProperty("RDN_EXCHD2")]
	public string Exchange2 { get; set; }

	[JsonProperty("LOT_SIZE_A")]
	public decimal? LotSize { get; set; }

	[JsonProperty("RECORDTYPE")]
	public string RecordType { get; set; }

	[JsonProperty("ORDER_PRC")]
	public decimal? OrderPrice { get; set; }

	[JsonProperty("ORDER_SIDE")]
	public string OrderSide { get; set; }

	[JsonProperty("ACC_SIZE")]
	public decimal? AccumulatedSize { get; set; }

	[JsonProperty("NO_ORD")]
	public int? OrdersCount { get; set; }

	[JsonProperty("LV_DATE")]
	public string LevelDate { get; set; }

	[JsonProperty("LV_TIM_MS")]
	public long? LevelTimeMilliseconds { get; set; }
}

internal sealed class LsegWireMap
{
	public string KeyType { get; set; }
	public int? CountHint { get; set; }
	public LsegWireSummary Summary { get; set; }
	public LsegWireMapEntry[] Entries { get; set; } = [];
}

internal sealed class LsegWireSummary
{
	public LsegWireFields Fields { get; set; }
}

internal sealed class LsegWireMapEntry
{
	public string Action { get; set; }
	public string Key { get; set; }
	public LsegWireFields Fields { get; set; }
	public LsegWireFilterList FilterList { get; set; }
}

internal sealed class LsegWireFilterList
{
	public LsegWireFilterEntry[] Entries { get; set; } = [];
}

internal sealed class LsegWireFilterEntry
{
	[JsonProperty("ID")]
	public int Id { get; set; }

	public string Action { get; set; }
	public LsegDirectoryElements Elements { get; set; }
}

internal sealed class LsegDirectoryElements
{
	public string Name { get; set; }
	public int? ServiceState { get; set; }
	public int? AcceptingRequests { get; set; }
	public int[] Capabilities { get; set; } = [];
	public string[] DictionariesProvided { get; set; } = [];
	public string[] DictionariesUsed { get; set; } = [];
}

internal sealed class LsegSubscription
{
	public long StreamId { get; init; }
	public long ExternalId { get; init; }
	public string Ric { get; init; }
	public LsegSubscriptionKinds Kind { get; init; }
	public long LastSequence { get; set; }
	public int IsRecovering;
}

internal sealed class LsegMarketPriceUpdate
{
	public long SubscriptionId { get; init; }
	public string Ric { get; init; }
	public bool IsRefresh { get; init; }
	public string UpdateType { get; init; }
	public long Sequence { get; init; }
	public long EventId { get; init; }
	public DateTime ReceivedTime { get; init; }
	public LsegWireState State { get; init; }
	public LsegWireFields Fields { get; init; }
}

internal sealed class LsegDepthUpdate
{
	public long SubscriptionId { get; init; }
	public string Ric { get; init; }
	public bool IsRefresh { get; init; }
	public bool IsComplete { get; init; }
	public bool IsClearCache { get; init; }
	public int? PartNumber { get; init; }
	public long Sequence { get; init; }
	public DateTime ReceivedTime { get; init; }
	public LsegWireState State { get; init; }
	public LsegWireMapEntry[] Entries { get; init; } = [];
}

internal sealed class LsegSecuritySnapshot
{
	public string Ric { get; init; }
	public DateTime ReceivedTime { get; init; }
	public LsegWireFields Fields { get; init; }
	public LsegWireState State { get; init; }
}
