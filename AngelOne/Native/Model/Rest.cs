namespace StockSharp.AngelOne.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneResponse<T>
{
	[JsonProperty("status")]
	public bool Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorcode")]
	public string ErrorCode { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneLoginRequest
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("password")]
	public string Password { get; set; }

	[JsonProperty("totp")]
	public string Totp { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneLogoutRequest
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneSession
{
	[JsonProperty("jwtToken")]
	public string JwtToken { get; set; }

	[JsonProperty("refreshToken")]
	public string RefreshToken { get; set; }

	[JsonProperty("feedToken")]
	public string FeedToken { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneLogoutResult
{
}

sealed class AngelOneNoRequest
{
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneProfile
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("email")]
	public string Email { get; set; }

	[JsonProperty("mobileno")]
	public string Mobile { get; set; }

	[JsonProperty("exchanges")]
	public string[] Exchanges { get; set; }

	[JsonProperty("products")]
	public string[] Products { get; set; }

	[JsonProperty("broker")]
	public string Broker { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneInstrument
{
	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("strike")]
	public decimal Strike { get; set; }

	[JsonProperty("lotsize")]
	public decimal LotSize { get; set; }

	[JsonProperty("instrumenttype")]
	public string InstrumentType { get; set; }

	[JsonProperty("exch_seg")]
	public string Exchange { get; set; }

	[JsonProperty("tick_size")]
	public decimal TickSize { get; set; }

	[JsonProperty("freeze_qty")]
	public decimal FreezeQuantity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneFunds
{
	[JsonProperty("net")]
	public string Net { get; set; }

	[JsonProperty("availablecash")]
	public string AvailableCash { get; set; }

	[JsonProperty("availableintradaypayin")]
	public string AvailableIntradayPayIn { get; set; }

	[JsonProperty("availablelimitmargin")]
	public string AvailableLimitMargin { get; set; }

	[JsonProperty("collateral")]
	public string Collateral { get; set; }

	[JsonProperty("m2munrealized")]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("m2mrealized")]
	public string RealizedPnL { get; set; }

	[JsonProperty("utiliseddebits")]
	public string UtilizedDebits { get; set; }

	[JsonProperty("utilisedspan")]
	public string UtilizedSpan { get; set; }

	[JsonProperty("utilisedoptionpremium")]
	public string UtilizedOptionPremium { get; set; }

	[JsonProperty("utilisedholdingsales")]
	public string UtilizedHoldingSales { get; set; }

	[JsonProperty("utilisedexposure")]
	public string UtilizedExposure { get; set; }

	[JsonProperty("utilisedturnover")]
	public string UtilizedTurnover { get; set; }

	[JsonProperty("utilisedpayout")]
	public string UtilizedPayout { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOnePosition
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symboltoken")]
	public string SymbolToken { get; set; }

	[JsonProperty("producttype")]
	public string ProductType { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("symbolname")]
	public string SymbolName { get; set; }

	[JsonProperty("netqty")]
	public string NetQuantity { get; set; }

	[JsonProperty("netprice")]
	public string NetPrice { get; set; }

	[JsonProperty("avgnetprice")]
	public string AverageNetPrice { get; set; }

	[JsonProperty("totalbuyvalue")]
	public string TotalBuyValue { get; set; }

	[JsonProperty("totalsellvalue")]
	public string TotalSellValue { get; set; }

	[JsonProperty("buyqty")]
	public string BuyQuantity { get; set; }

	[JsonProperty("sellqty")]
	public string SellQuantity { get; set; }

	[JsonProperty("buyavgprice")]
	public string BuyAveragePrice { get; set; }

	[JsonProperty("sellavgprice")]
	public string SellAveragePrice { get; set; }

	[JsonProperty("lotsize")]
	public string LotSize { get; set; }

	[JsonProperty("instrumenttype")]
	public string InstrumentType { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneAllHoldings
{
	[JsonProperty("holdings")]
	public AngelOneHolding[] Holdings { get; set; }

	[JsonProperty("totalholding")]
	public AngelOneHoldingSummary Summary { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneHolding
{
	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("symboltoken")]
	public string SymbolToken { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("t1quantity")]
	public decimal T1Quantity { get; set; }

	[JsonProperty("realisedquantity")]
	public decimal RealizedQuantity { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("authorisedquantity")]
	public decimal AuthorizedQuantity { get; set; }

	[JsonProperty("averageprice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("ltp")]
	public decimal LastPrice { get; set; }

	[JsonProperty("close")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("profitandloss")]
	public decimal ProfitAndLoss { get; set; }

	[JsonProperty("pnlpercentage")]
	public decimal ProfitAndLossPercent { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneHoldingSummary
{
	[JsonProperty("totalholdingvalue")]
	public decimal HoldingValue { get; set; }

	[JsonProperty("totalinvvalue")]
	public decimal InvestmentValue { get; set; }

	[JsonProperty("totalprofitandloss")]
	public decimal ProfitAndLoss { get; set; }

	[JsonProperty("totalpnlpercentage")]
	public decimal ProfitAndLossPercent { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneOrder
{
	[JsonProperty("variety")]
	public string Variety { get; set; }

	[JsonProperty("ordertype")]
	public string OrderType { get; set; }

	[JsonProperty("ordertag")]
	public string OrderTag { get; set; }

	[JsonProperty("producttype")]
	public string ProductType { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("triggerprice")]
	public string TriggerPrice { get; set; }

	[JsonProperty("quantity")]
	public string Quantity { get; set; }

	[JsonProperty("disclosedquantity")]
	public string DisclosedQuantity { get; set; }

	[JsonProperty("duration")]
	public string Duration { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("transactiontype")]
	public string TransactionType { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symboltoken")]
	public string SymbolToken { get; set; }

	[JsonProperty("averageprice")]
	public string AveragePrice { get; set; }

	[JsonProperty("filledshares")]
	public string FilledShares { get; set; }

	[JsonProperty("unfilledshares")]
	public string UnfilledShares { get; set; }

	[JsonProperty("orderid")]
	public string OrderId { get; set; }

	[JsonProperty("uniqueorderid")]
	public string UniqueOrderId { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("orderstatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("updatetime")]
	public string UpdateTime { get; set; }

	[JsonProperty("exchtime")]
	public string ExchangeTime { get; set; }

	[JsonProperty("exchorderupdatetime")]
	public string ExchangeUpdateTime { get; set; }

	[JsonProperty("fillid")]
	public string FillId { get; set; }

	[JsonProperty("filltime")]
	public string FillTime { get; set; }

	[JsonProperty("parentorderid")]
	public string ParentOrderId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneTrade
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("producttype")]
	public string ProductType { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("symboltoken")]
	public string SymbolToken { get; set; }

	[JsonProperty("transactiontype")]
	public string TransactionType { get; set; }

	[JsonProperty("fillprice")]
	public string FillPrice { get; set; }

	[JsonProperty("fillsize")]
	public string FillSize { get; set; }

	[JsonProperty("orderid")]
	public string OrderId { get; set; }

	[JsonProperty("fillid")]
	public string FillId { get; set; }

	[JsonProperty("filltime")]
	public string FillTime { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneOrderRequest
{
	[JsonProperty("variety")]
	public string Variety { get; set; }

	[JsonProperty("orderid", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderId { get; set; }

	[JsonProperty("tradingsymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("symboltoken")]
	public string SymbolToken { get; set; }

	[JsonProperty("transactiontype", NullValueHandling = NullValueHandling.Ignore)]
	public string TransactionType { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("ordertype")]
	public string OrderType { get; set; }

	[JsonProperty("producttype")]
	public string ProductType { get; set; }

	[JsonProperty("duration")]
	public string Duration { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerprice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("disclosedquantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("squareoff")]
	public decimal SquareOff { get; set; }

	[JsonProperty("stoploss")]
	public decimal StopLoss { get; set; }

	[JsonProperty("trailingStopLoss")]
	public decimal TrailingStopLoss { get; set; }

	[JsonProperty("ordertag", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderTag { get; set; }

	[JsonProperty("scripconsent", NullValueHandling = NullValueHandling.Ignore)]
	public string ScripConsent { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneCancelOrderRequest
{
	[JsonProperty("variety")]
	public string Variety { get; set; }

	[JsonProperty("orderid")]
	public string OrderId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneOrderResult
{
	[JsonProperty("orderid")]
	public string OrderId { get; set; }

	[JsonProperty("uniqueorderid")]
	public string UniqueOrderId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class AngelOneCandleRequest
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symboltoken")]
	public string SymbolToken { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("fromdate")]
	public string From { get; set; }

	[JsonProperty("todate")]
	public string To { get; set; }
}

sealed class AngelOneCandle
{
	public DateTime Time { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}

sealed class AngelOneCandleArrayConverter : JsonConverter
{
	public override bool CanWrite => false;
	public override bool CanConvert(Type objectType) => objectType == typeof(AngelOneCandle[]);

	public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return Array.Empty<AngelOneCandle>();
		if (reader.TokenType != JsonToken.StartArray)
			throw new JsonSerializationException("Angel One candles must be a JSON array.");

		var candles = new List<AngelOneCandle>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			if (reader.TokenType != JsonToken.StartArray)
				throw new JsonSerializationException("An Angel One candle must be a JSON array.");

			ReadValue(reader);
			var candle = new AngelOneCandle
			{
				Time = DateTimeOffset.Parse(reader.Value?.ToString() ?? throw new JsonSerializationException("Angel One candle time is missing."), CultureInfo.InvariantCulture).UtcDateTime,
			};

			candle.Open = ReadDecimal(reader);
			candle.High = ReadDecimal(reader);
			candle.Low = ReadDecimal(reader);
			candle.Close = ReadDecimal(reader);
			candle.Volume = ReadDecimal(reader);

			while (reader.Read() && reader.TokenType != JsonToken.EndArray)
			{
			}

			candles.Add(candle);
		}

		return candles.ToArray();
	}

	private static decimal ReadDecimal(JsonReader reader)
	{
		ReadValue(reader);
		return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
	}

	private static void ReadValue(JsonReader reader)
	{
		if (!reader.Read())
			throw new JsonSerializationException("Unexpected end of an Angel One candle.");
	}

	public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		=> throw new InvalidOperationException("Angel One candle serialization is not used.");
}
