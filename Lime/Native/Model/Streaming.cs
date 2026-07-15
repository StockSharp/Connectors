namespace StockSharp.Lime.Native.Model;

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
sealed class LimeFeedAction
{
	public LimeFeedActions Action { get; set; }
	public string Account { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeFeedHeader
{
	[JsonProperty("t")]
	public LimeFeedTypes Type { get; set; }
	public string Code { get; set; }
	public string Description { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeBalanceFeed
{
	[JsonProperty("t")]
	public LimeFeedTypes Type { get; set; }
	public LimeAccount[] Data { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimePositionFeed
{
	[JsonProperty("t")]
	public LimeFeedTypes Type { get; set; }
	public LimeAccountPositions[] Data { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeAccountPositions
{
	public string Account { get; set; }
	public LimePosition[] Positions { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeOrderFeed
{
	[JsonProperty("t")]
	public LimeFeedTypes Type { get; set; }
	public LimeOrder[] Data { get; set; }
}

[JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
sealed class LimeTradeFeed
{
	[JsonProperty("t")]
	public LimeFeedTypes Type { get; set; }
	public LimeTrade[] Data { get; set; }
}
