namespace StockSharp.Breeze.Native;

static class BreezeSocketCodec
{
	public static string CreateConnect(string user, string token)
		=> "40" + JsonConvert.SerializeObject(new BreezeSocketAuth { User = user, Token = token });

	public static string CreateRoomRequest(BreezeRoomRequest request)
	{
		using var writer = new StringWriter(CultureInfo.InvariantCulture);
		using (var json = new JsonTextWriter(writer))
		{
			json.WriteStartArray();
			json.WriteValue(request.Event);
			json.WriteStartArray();
			foreach (var symbol in request.Symbols)
				json.WriteValue(symbol);
			json.WriteEndArray();
			json.WriteEndArray();
		}
		return "42" + writer;
	}

	public static string GetEvent(string message)
	{
		using var reader = CreateReader(message);
		ReadRequired(reader, JsonToken.StartArray);
		return ReadString(reader);
	}

	public static BreezeMarketTick ReadMarketTick(string message, Func<string, bool> isDerivative)
	{
		using var reader = CreatePayloadReader(message, "stock");
		ReadRequired(reader, JsonToken.StartArray);
		var tick = new BreezeMarketTick();
		var derivative = false;
		for (var index = 0; reader.Read() && reader.TokenType != JsonToken.EndArray; index++)
		{
			if (index == 0)
			{
				tick.InstrumentToken = ExtractToken(Convert.ToString(reader.Value, CultureInfo.InvariantCulture));
				derivative = isDerivative(tick.InstrumentToken);
				continue;
			}
			var value = ReadDecimal(reader);
			switch (index)
			{
				case 1: tick.OpenPrice = value; break;
				case 2: tick.LastPrice = value; break;
				case 3: tick.HighPrice = value; break;
				case 4: tick.LowPrice = value; break;
				case 5: tick.Change = value; break;
				case 6: tick.BidPrice = value; break;
				case 7: tick.BidVolume = value; break;
				case 8: tick.AskPrice = value; break;
				case 9: tick.AskVolume = value; break;
				case 10: tick.LastVolume = value; break;
				case 11: tick.AveragePrice = value; break;
				case 12 when derivative: tick.OpenInterest = value; break;
				case 13 when derivative: tick.OpenInterestChange = value; break;
				case 14 when derivative: tick.Volume = value; break;
				case 15 when derivative: tick.TotalBuyVolume = value; break;
				case 16 when derivative: tick.TotalSellVolume = value; break;
				case 19 when derivative: tick.LowerCircuit = value; break;
				case 20 when derivative: tick.UpperCircuit = value; break;
				case 21 when derivative: SetTradeTime(tick, value); break;
				case 22 when derivative: tick.ClosePrice = value; break;
				case 12: tick.Volume = value; break;
				case 13: tick.TotalBuyVolume = value; break;
				case 14: tick.TotalSellVolume = value; break;
				case 17: tick.LowerCircuit = value; break;
				case 18: tick.UpperCircuit = value; break;
				case 19: SetTradeTime(tick, value); break;
				case 20: tick.ClosePrice = value; break;
			}
		}
		if (tick.InstrumentToken.IsEmpty())
			throw new InvalidDataException("Breeze market update has no instrument token.");
		return tick;
	}

	public static BreezeDepthUpdate ReadDepth(string message)
	{
		using var reader = CreatePayloadReader(message, "stock");
		ReadRequired(reader, JsonToken.StartArray);
		var update = new BreezeDepthUpdate();
		for (var index = 0; reader.Read() && reader.TokenType != JsonToken.EndArray; index++)
		{
			switch (index)
			{
				case 0:
					update.InstrumentToken = ExtractToken(Convert.ToString(reader.Value, CultureInfo.InvariantCulture));
					break;
				case 1:
					if (ReadDecimal(reader) is decimal epoch && epoch > 0)
						update.ServerTime = DateTimeOffset.FromUnixTimeSeconds((long)epoch).UtcDateTime;
					break;
				case 2:
					ReadDepthLevels(reader, update);
					break;
			}
		}
		if (update.InstrumentToken.IsEmpty())
			throw new InvalidDataException("Breeze depth update has no instrument token.");
		return update;
	}

	public static bool IsDepth(string message)
	{
		using var reader = CreatePayloadReader(message, "stock");
		ReadRequired(reader, JsonToken.StartArray);
		if (!reader.Read())
			return false;
		var symbol = Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
		return symbol?.Contains(".2!", StringComparison.Ordinal) == true;
	}

	public static BreezeOrderUpdate ReadOrder(string message)
	{
		using var reader = CreatePayloadReader(message, "order");
		ReadRequired(reader, JsonToken.StartArray);
		var update = new BreezeOrderUpdate();
		var messageType = 0;
		for (var index = 0; reader.Read() && reader.TokenType != JsonToken.EndArray; index++)
		{
			if (index == 11)
			{
				messageType = ReadDecimal(reader)?.To<int>() ?? 0;
				continue;
			}
			if (messageType is 4 or 5)
				ReadEquityOrderField(reader, index, update);
			else if (messageType is 6 or 7)
				ReadDerivativeOrderField(reader, index, update);
		}
		return update;
	}

	public static BreezeStreamCandle ReadCandle(string message, string expectedEvent)
	{
		using var reader = CreatePayloadReader(message, expectedEvent);
		var payload = ReadCurrentString(reader);
		var values = payload.Split(',').Select(v => v.Trim()).ToArray();
		if (values.Length < 9)
			throw new InvalidDataException($"Breeze {expectedEvent} candle contains {values.Length} fields.");
		var candle = new BreezeStreamCandle { Event = expectedEvent, ExchangeCode = values[0], StockCode = values[1] };
		if (values.Length >= 13)
		{
			candle.ExpiryDate = values[2]; candle.StrikePrice = ParseDecimal(values[3]); candle.Right = values[4];
			SetPrices(candle, values, 5, true);
		}
		else if (values.Length >= 11)
		{
			candle.ExpiryDate = values[2];
			SetPrices(candle, values, 3, true);
		}
		else
		{
			SetPrices(candle, values, 2, false);
		}
		return candle;
	}

	private static void SetPrices(BreezeStreamCandle candle, string[] values, int offset, bool hasOpenInterest)
	{
		candle.Low = ParseDecimal(values[offset]);
		candle.High = ParseDecimal(values[offset + 1]);
		candle.Open = ParseDecimal(values[offset + 2]);
		candle.Close = ParseDecimal(values[offset + 3]);
		candle.Volume = ParseDecimal(values[offset + 4]);
		var dateIndex = offset + 5;
		if (hasOpenInterest)
		{
			candle.OpenInterest = ParseDecimal(values[dateIndex]);
			dateIndex++;
		}
		candle.Time = values[dateIndex].ParseBreezeTime() ?? DateTime.UtcNow;
	}

	private static void ReadEquityOrderField(JsonReader reader, int index, BreezeOrderUpdate update)
	{
		switch (index)
		{
			case 14: update.StockCode = ReadCurrentString(reader); break;
			case 15: update.Side = ReadCurrentString(reader).ToSide(); break;
			case 16: update.OrderType = ReadCurrentString(reader); break;
			case 17: update.Validity = ReadCurrentString(reader); break;
			case 18: update.Price = ReadDecimal(reader) ?? 0; break;
			case 19: update.Product = ParseProduct(ReadCurrentString(reader)); break;
			case 20: update.Status = ReadCurrentString(reader); break;
			case 21: update.OrderTime = ReadCurrentString(reader).ParseBreezeTime(); break;
			case 22: update.TradeTime = ReadCurrentString(reader).ParseBreezeTime(); break;
			case 23: update.OrderId = ReadCurrentString(reader); break;
			case 24: update.Quantity = ReadDecimal(reader) ?? 0; break;
			case 26: update.ExecutedQuantity = ReadDecimal(reader) ?? 0; break;
			case 27: update.CancelledQuantity = ReadDecimal(reader) ?? 0; break;
			case 30: update.TriggerPrice = ReadDecimal(reader) ?? 0; break;
			case 41: update.Message = ReadCurrentString(reader); break;
			case 42: update.AveragePrice = ReadDecimal(reader) ?? 0; break;
		}
	}

	private static void ReadDerivativeOrderField(JsonReader reader, int index, BreezeOrderUpdate update)
	{
		switch (index)
		{
			case 14: update.StockCode = ReadCurrentString(reader); break;
			case 15: update.Product = ParseProduct(ReadCurrentString(reader)); break;
			case 16: update.OptionType = ParseOption(ReadCurrentString(reader)); break;
			case 18: update.StrikePrice = ReadDecimal(reader); break;
			case 19:
				var expiry = ReadCurrentString(reader).ParseBreezeTime();
				update.ExpiryDate = expiry?.Date;
				break;
			case 21: update.Side = ReadCurrentString(reader).ToSide(); break;
			case 22: update.OrderType = ReadCurrentString(reader); break;
			case 23: update.Validity = ReadCurrentString(reader); break;
			case 24: update.Price = ReadDecimal(reader) ?? 0; break;
			case 25: update.Status = ReadCurrentString(reader); break;
			case 26: update.OrderId = ReadCurrentString(reader); break;
			case 27: update.Quantity = ReadDecimal(reader) ?? 0; break;
			case 28: update.ExecutedQuantity = ReadDecimal(reader) ?? 0; break;
			case 29: update.CancelledQuantity = ReadDecimal(reader) ?? 0; break;
			case 31: update.TriggerPrice = ReadDecimal(reader) ?? 0; break;
			case 39: update.AveragePrice = ReadDecimal(reader) ?? 0; break;
		}
	}

	private static void ReadDepthLevels(JsonReader reader, BreezeDepthUpdate update)
	{
		if (reader.TokenType != JsonToken.StartArray)
			throw new InvalidDataException("Breeze depth levels are not an array.");
		var bids = new List<BreezeDepthLevel>();
		var asks = new List<BreezeDepthLevel>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			if (reader.TokenType != JsonToken.StartArray)
				throw new InvalidDataException("Breeze depth level is not an array.");
			var bid = new BreezeDepthLevel();
			var ask = new BreezeDepthLevel();
			for (var index = 0; reader.Read() && reader.TokenType != JsonToken.EndArray; index++)
			{
				var value = ReadDecimal(reader);
				switch (index)
				{
					case 0: bid.Price = value ?? 0; break;
					case 1: bid.Volume = value ?? 0; break;
					case 2: bid.OrdersCount = value?.To<int>(); break;
					case 4: ask.Price = value ?? 0; break;
					case 5: ask.Volume = value ?? 0; break;
					case 6: ask.OrdersCount = value?.To<int>(); break;
				}
			}
			if (bid.Price > 0) bids.Add(bid);
			if (ask.Price > 0) asks.Add(ask);
		}
		update.Bids = [.. bids.OrderByDescending(l => l.Price)];
		update.Asks = [.. asks.OrderBy(l => l.Price)];
	}

	private static JsonTextReader CreateReader(string message)
	{
		if (message.IsEmpty() || !message.StartsWith("42", StringComparison.Ordinal))
			throw new InvalidDataException("Invalid Breeze Socket.IO event.");
		return new JsonTextReader(new StringReader(message[2..]));
	}

	private static JsonTextReader CreatePayloadReader(string message, string expectedEvent)
	{
		var reader = CreateReader(message);
		ReadRequired(reader, JsonToken.StartArray);
		var eventName = ReadString(reader);
		if (!eventName.EqualsIgnoreCase(expectedEvent))
		{
			reader.Close();
			throw new InvalidDataException($"Expected Breeze '{expectedEvent}' event, received '{eventName}'.");
		}
		if (!reader.Read())
		{
			reader.Close();
			throw new EndOfStreamException("Breeze Socket.IO event payload is missing.");
		}
		return reader;
	}

	private static void ReadRequired(JsonReader reader, JsonToken token)
	{
		if (!reader.Read() || reader.TokenType != token)
			throw new InvalidDataException($"Expected JSON token {token}, received {reader.TokenType}.");
	}

	private static string ReadString(JsonReader reader)
	{
		if (!reader.Read() || reader.TokenType != JsonToken.String)
			throw new InvalidDataException("Expected a JSON string.");
		return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
	}

	private static string ReadCurrentString(JsonReader reader)
		=> reader.TokenType is JsonToken.Null or JsonToken.Undefined ? null : Convert.ToString(reader.Value, CultureInfo.InvariantCulture);

	private static decimal? ReadDecimal(JsonReader reader)
	{
		if (reader.TokenType is JsonToken.Null or JsonToken.Undefined || reader.Value == null)
			return null;
		if (reader.Value is decimal value)
			return value;
		return decimal.TryParse(Convert.ToString(reader.Value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out value) ? value : null;
	}

	private static decimal ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;

	private static string ExtractToken(string value)
	{
		var index = value?.LastIndexOf('!') ?? -1;
		return index >= 0 ? value[(index + 1)..] : value;
	}

	private static BreezeProducts ParseProduct(string value)
		=> value?.ToUpperInvariant() switch { "F" => BreezeProducts.Futures, "O" => BreezeProducts.Options, "FUTURES" => BreezeProducts.Futures, "OPTIONS" => BreezeProducts.Options, _ => BreezeProducts.Cash };

	private static OptionTypes? ParseOption(string value)
		=> value?.ToUpperInvariant() switch { "C" or "CE" or "CALL" => OptionTypes.Call, "P" or "PE" or "PUT" => OptionTypes.Put, _ => null };

	private static void SetTradeTime(BreezeMarketTick tick, decimal? epoch)
	{
		if (epoch is not > 0)
			return;
		tick.LastTradeTime = DateTimeOffset.FromUnixTimeSeconds((long)epoch.Value).UtcDateTime;
		tick.ServerTime = tick.LastTradeTime.Value;
	}
}
