namespace StockSharp.ProBit.Native.Model;

sealed class ProBitResponse<TData>
{
	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}

sealed class ProBitErrorResponse
{
	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }
}

sealed class ProBitTokenRequest
{
	[JsonProperty("grant_type")]
	public string GrantType { get; set; } = "client_credentials";
}

sealed class ProBitTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("error_description")]
	public string ErrorDescription { get; set; }
}

sealed class ProBitMarket
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("base_currency_id")]
	public string BaseCurrencyId { get; set; }

	[JsonProperty("quote_currency_id")]
	public string QuoteCurrencyId { get; set; }

	[JsonProperty("min_price")]
	public string MinPrice { get; set; }

	[JsonProperty("max_price")]
	public string MaxPrice { get; set; }

	[JsonProperty("price_increment")]
	public string PriceIncrement { get; set; }

	[JsonProperty("min_quantity")]
	public string MinQuantity { get; set; }

	[JsonProperty("max_quantity")]
	public string MaxQuantity { get; set; }

	[JsonProperty("quantity_precision")]
	public int QuantityPrecision { get; set; }

	[JsonProperty("min_cost")]
	public string MinCost { get; set; }

	[JsonProperty("max_cost")]
	public string MaxCost { get; set; }

	[JsonProperty("cost_precision")]
	public int CostPrecision { get; set; }

	[JsonProperty("taker_fee_rate")]
	public string TakerFeeRate { get; set; }

	[JsonProperty("maker_fee_rate")]
	public string MakerFeeRate { get; set; }

	[JsonProperty("show_in_ui")]
	public bool ShowInUi { get; set; }

	[JsonProperty("closed")]
	public bool IsClosed { get; set; }
}

sealed class ProBitTicker
{
	[JsonProperty("last")]
	public string Last { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("change")]
	public string Change { get; set; }

	[JsonProperty("base_volume")]
	public string BaseVolume { get; set; }

	[JsonProperty("quote_volume")]
	public string QuoteVolume { get; set; }

	[JsonProperty("market_id")]
	public string MarketId { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }
}

sealed class ProBitBookLevel
{
	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }
}

sealed class ProBitTrade
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("market_id")]
	public string MarketId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("cost")]
	public string Cost { get; set; }

	[JsonProperty("fee_amount")]
	public string FeeAmount { get; set; }

	[JsonProperty("fee_currency_id")]
	public string FeeCurrencyId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("tick_direction")]
	public string TickDirection { get; set; }
}

sealed class ProBitCandle
{
	[JsonProperty("market_id")]
	public string MarketId { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("close")]
	public string Close { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("base_volume")]
	public string BaseVolume { get; set; }

	[JsonProperty("quote_volume")]
	public string QuoteVolume { get; set; }

	[JsonProperty("start_time")]
	public string StartTime { get; set; }

	[JsonProperty("end_time")]
	public string EndTime { get; set; }
}

sealed class ProBitBalance
{
	[JsonProperty("currency_id")]
	public string CurrencyId { get; set; }

	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("available")]
	public string Available { get; set; }
}

sealed class ProBitBalanceValue
{
	[JsonProperty("total")]
	public string Total { get; set; }

	[JsonProperty("available")]
	public string Available { get; set; }
}

sealed class ProBitOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("user_id")]
	public string UserId { get; set; }

	[JsonProperty("market_id")]
	public string MarketId { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("limit_price")]
	public string LimitPrice { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("filled_cost")]
	public string FilledCost { get; set; }

	[JsonProperty("filled_quantity")]
	public string FilledQuantity { get; set; }

	[JsonProperty("open_quantity")]
	public string OpenQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public string CancelledQuantity { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }
}

sealed class ProBitOrderRequest
{
	[JsonProperty("market_id")]
	public string MarketId { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("time_in_force")]
	public string TimeInForce { get; set; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("limit_price")]
	public string LimitPrice { get; set; }

	[JsonProperty("cost")]
	public string Cost { get; set; }
}

sealed class ProBitCancelOrderRequest
{
	[JsonProperty("market_id")]
	public string MarketId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }
}

sealed class ProBitWsCommand
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("interval")]
	public int? Interval { get; set; }

	[JsonProperty("market_id")]
	public string MarketId { get; set; }

	[JsonProperty("filter")]
	public string[] Filter { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }
}

class ProBitWsHeader
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("result")]
	public string Result { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("market_id")]
	public string MarketId { get; set; }

	[JsonProperty("errorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class ProBitWsMarketDataMessage : ProBitWsHeader
{
	[JsonProperty("ticker")]
	public ProBitTicker Ticker { get; set; }

	[JsonProperty("recent_trades")]
	public ProBitTrade[] RecentTrades { get; set; }

	[JsonProperty("order_books_l0")]
	public ProBitBookLevel[] OrderBooks { get; set; }
}

sealed class ProBitWsBalanceMessage : ProBitWsHeader
{
	[JsonProperty("data")]
	[JsonConverter(typeof(ProBitBalanceMapConverter))]
	public ProBitBalance[] Data { get; set; }
}

sealed class ProBitWsOrderMessage : ProBitWsHeader
{
	[JsonProperty("data")]
	public ProBitOrder[] Data { get; set; }
}

sealed class ProBitWsTradeMessage : ProBitWsHeader
{
	[JsonProperty("data")]
	public ProBitTrade[] Data { get; set; }
}

sealed class ProBitBalanceMapConverter : JsonConverter<ProBitBalance[]>
{
	public override ProBitBalance[] ReadJson(JsonReader reader, Type targetType,
		ProBitBalance[] existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		_ = targetType;
		_ = existingValue;
		_ = hasExistingValue;
		if (reader.TokenType == JsonToken.Null)
			return [];

		var balances = new List<ProBitBalance>();
		if (reader.TokenType == JsonToken.StartArray)
		{
			while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			{
				var balance = serializer.Deserialize<ProBitBalance>(reader);
				if (balance is not null)
					balances.Add(balance);
			}
			return [.. balances];
		}

		if (reader.TokenType != JsonToken.StartObject)
			throw new JsonSerializationException("ProBit balance update must be a JSON map or array.");

		while (reader.Read() && reader.TokenType != JsonToken.EndObject)
		{
			if (reader.TokenType != JsonToken.PropertyName)
				throw new JsonSerializationException("Invalid ProBit balance map property.");
			var currencyId = reader.Value?.ToString();
			if (!reader.Read())
				throw new JsonSerializationException("Unexpected end of ProBit balance update.");
			var value = serializer.Deserialize<ProBitBalanceValue>(reader);
			if (value is null || currencyId.IsEmpty())
				continue;
			balances.Add(new()
			{
				CurrencyId = currencyId,
				Total = value.Total,
				Available = value.Available,
			});
		}
		return [.. balances];
	}

	public override void WriteJson(JsonWriter writer, ProBitBalance[] value,
		JsonSerializer serializer)
		=> throw new NotSupportedException();

	public override bool CanWrite => false;
}
