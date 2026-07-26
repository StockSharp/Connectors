namespace StockSharp.HitBtc.Native.Model;

class ApiError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	public override string ToString()
		=> Description.IsEmpty() ? $"{Code}: {Message}" : $"{Code}: {Message}. {Description}";
}

class ErrorEnvelope
{
	[JsonProperty("error")]
	public ApiError Error { get; set; }
}

class WithdrawalResponse
{
	[JsonProperty("id")]
	public string Id { get; set; }
}

class WsHeader
{
	[JsonProperty("ch")]
	public string Channel { get; set; }

	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("error")]
	public ApiError Error { get; set; }
}

class WsResponse<T>
{
	[JsonProperty("result")]
	public T Result { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("error")]
	public ApiError Error { get; set; }
}

class WsNotification<T>
{
	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("params")]
	public T Params { get; set; }
}

class WsFeed<T>
{
	[JsonProperty("ch")]
	public string Channel { get; set; }

	[JsonProperty("data")]
	public Dictionary<string, T> Data { get; set; }

	[JsonProperty("snapshot")]
	public Dictionary<string, T> Snapshot { get; set; }

	[JsonProperty("update")]
	public Dictionary<string, T> Update { get; set; }
}

class WsCommand<T>
{
	[JsonProperty("method")]
	public string Method { get; set; }

	[JsonProperty("ch")]
	public string Channel { get; set; }

	[JsonProperty("params")]
	public T Params { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }
}

sealed class EmptyRequest
{
	public static EmptyRequest Instance { get; } = new();

	private EmptyRequest()
	{
	}
}

class SubscriptionRequest
{
	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("limit")]
	public int? Limit { get; set; }
}

class LoginRequest
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("api_key")]
	public string ApiKey { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("window")]
	public int Window { get; set; }

	[JsonProperty("signature")]
	public string Signature { get; set; }
}

class NewOrderRequest
{
	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("stop_price")]
	public decimal? StopPrice { get; set; }

	[JsonProperty("expire_time")]
	public DateTime? ExpireTime { get; set; }
}

class CancelOrderRequest
{
	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }
}

class ReplaceOrderRequest
{
	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("new_client_order_id")]
	public string NewClientOrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }
}

class BalanceSubscriptionRequest
{
	[JsonProperty("mode")]
	public string Mode { get; set; }
}
