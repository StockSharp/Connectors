namespace StockSharp.Paxos.Native.Model;

sealed class PaxosProfile
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("nickname")]
	public string Nickname { get; set; }

	[JsonProperty("type")]
	public PaxosProfileTypes Type { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }
}

sealed class PaxosProfileBalance
{
	[JsonProperty("asset")]
	public string Asset { get; set; }

	[JsonProperty("available")]
	public string Available { get; set; }

	[JsonProperty("trading")]
	public string Trading { get; set; }
}

sealed class PaxosCreateOrderRequest
{
	[JsonProperty("ref_id")]
	public string RefId { get; init; }

	[JsonProperty("side")]
	public PaxosSides Side { get; init; }

	[JsonProperty("market")]
	public string Market { get; init; }

	[JsonProperty("type")]
	public PaxosOrderTypes Type { get; init; }

	[JsonProperty("base_amount")]
	public string BaseAmount { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("quote_amount")]
	public string QuoteAmount { get; init; }

	[JsonProperty("time_in_force")]
	public PaxosTimeInForces TimeInForce { get; init; }

	[JsonProperty("expiration_date")]
	public string ExpirationDate { get; init; }

	[JsonProperty("identity_id")]
	public string IdentityId { get; init; }

	[JsonProperty("identity_account_id")]
	public string IdentityAccountId { get; init; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; init; }

	[JsonProperty("recipient_profile_id")]
	public string RecipientProfileId { get; init; }
}

sealed class PaxosOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("profile_id")]
	public string ProfileId { get; set; }

	[JsonProperty("ref_id")]
	public string RefId { get; set; }

	[JsonProperty("status")]
	public PaxosOrderStatuses Status { get; set; }

	[JsonProperty("side")]
	public PaxosSides Side { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("type")]
	public PaxosOrderTypes Type { get; set; }

	[JsonProperty("base_amount")]
	public string BaseAmount { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quote_amount")]
	public string QuoteAmount { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("modified_at")]
	public string ModifiedAt { get; set; }

	[JsonProperty("amount_filled")]
	public string AmountFilled { get; set; }

	[JsonProperty("volume_weighted_average_price")]
	public string VolumeWeightedAveragePrice { get; set; }

	[JsonProperty("time_in_force")]
	public PaxosTimeInForces TimeInForce { get; set; }

	[JsonProperty("expiration_date")]
	public string ExpirationDate { get; set; }

	[JsonProperty("identity_id")]
	public string IdentityId { get; set; }

	[JsonProperty("identity_account_id")]
	public string IdentityAccountId { get; set; }

	[JsonProperty("stop_price")]
	public string StopPrice { get; set; }

	[JsonProperty("recipient_profile_id")]
	public string RecipientProfileId { get; set; }

	[JsonProperty("is_triggered")]
	public bool IsTriggered { get; set; }
}

sealed class PaxosPrivateExecution
{
	[JsonProperty("execution_id")]
	public string ExecutionId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("executed_at")]
	public string ExecutedAt { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("side")]
	public PaxosSides Side { get; set; }

	[JsonProperty("amount")]
	public string Amount { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("commission")]
	public string Commission { get; set; }

	[JsonProperty("commission_asset")]
	public string CommissionAsset { get; set; }

	[JsonProperty("rebate")]
	public string Rebate { get; set; }

	[JsonProperty("rebate_asset")]
	public string RebateAsset { get; set; }
}
