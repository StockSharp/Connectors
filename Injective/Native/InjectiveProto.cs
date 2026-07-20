namespace StockSharp.Injective.Native;

static class InjectiveProto
{
	public static byte[] MarketStreamRequest(string marketId)
		=> Serialize(output => WriteString(output, 1,
			marketId.ThrowIfEmpty(nameof(marketId))));

	public static byte[] TradeStreamRequest(string marketId,
		string subaccountId = null)
		=> Serialize(output =>
		{
			WriteString(output, 1, marketId);
			WriteString(output, 4, subaccountId);
		});

	public static byte[] OrderStreamRequest(string marketId,
		string subaccountId, bool isDerivative)
		=> Serialize(output =>
		{
			WriteString(output, 1, marketId);
			WriteString(output, 3,
				subaccountId.ThrowIfEmpty(nameof(subaccountId)));
			WriteBool(output, isDerivative ? 11 : 9, true);
		});

	public static byte[] PositionStreamRequest(string subaccountId,
		string marketId = null)
		=> Serialize(output =>
		{
			WriteString(output, 1,
				subaccountId.ThrowIfEmpty(nameof(subaccountId)));
			WriteString(output, 2, marketId);
		});

	public static byte[] PortfolioStreamRequest(string address,
		string subaccountId)
		=> Serialize(output =>
		{
			WriteString(output, 1, address.ThrowIfEmpty(nameof(address)));
			WriteString(output, 2, subaccountId);
		});

	public static InjectiveDepthUpdate ParseDepth(byte[] value)
	{
		var result = new InjectiveDepthUpdate();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1:
					result.Orderbook = ParseOrderBook(input.ReadBytes()
						.ToByteArray());
					return true;
				case 4:
					result.MarketId = input.ReadString();
					return true;
				default:
					return false;
			}
		});
		if (result.MarketId.IsEmpty() || result.Orderbook is null)
			throw new InvalidDataException(
				"Injective returned a malformed order-book stream message.");
		return result;
	}

	public static InjectiveTrade ParseTrade(byte[] value, bool isDerivative)
	{
		InjectiveTrade trade = null;
		Read(value, (field, input) =>
		{
			if (field != 1)
				return false;
			trade = isDerivative
				? ParseDerivativeTrade(input.ReadBytes().ToByteArray())
				: ParseSpotTrade(input.ReadBytes().ToByteArray());
			return true;
		});
		return trade ?? throw new InvalidDataException(
			"Injective returned a malformed trade stream message.");
	}

	public static InjectiveOrder ParseOrder(byte[] value, bool isDerivative)
	{
		InjectiveOrder order = null;
		Read(value, (field, input) =>
		{
			if (field != 1)
				return false;
			order = isDerivative
				? ParseDerivativeOrder(input.ReadBytes().ToByteArray())
				: ParseSpotOrder(input.ReadBytes().ToByteArray());
			return true;
		});
		return order ?? throw new InvalidDataException(
			"Injective returned a malformed order stream message.");
	}

	public static InjectivePosition ParsePosition(byte[] value)
	{
		InjectivePosition position = null;
		Read(value, (field, input) =>
		{
			if (field != 1)
				return false;
			position = ParsePositionValue(input.ReadBytes().ToByteArray());
			return true;
		});
		return position ?? throw new InvalidDataException(
			"Injective returned a malformed position stream message.");
	}

	public static InjectiveOraclePrice ParseOraclePrice(byte[] value)
	{
		var price = new InjectiveOraclePrice();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1:
					price.Price = input.ReadString();
					return true;
				case 2:
					price.Timestamp = input.ReadInt64();
					return true;
				case 3:
					price.MarketId = input.ReadString();
					return true;
				default:
					return false;
			}
		});
		if (price.MarketId.IsEmpty() || price.Price.IsEmpty())
			throw new InvalidDataException(
				"Injective returned a malformed oracle stream message.");
		return price;
	}

	public static InjectivePortfolioUpdate ParsePortfolioUpdate(byte[] value)
	{
		var update = new InjectivePortfolioUpdate();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1:
					update.Type = input.ReadString();
					return true;
				case 2:
					update.Denom = input.ReadString();
					return true;
				case 3:
					update.Amount = input.ReadString();
					return true;
				case 4:
					update.SubaccountId = input.ReadString();
					return true;
				case 5:
					update.Timestamp = input.ReadInt64();
					return true;
				default:
					return false;
			}
		});
		return update;
	}

	private static InjectiveOrderBook ParseOrderBook(byte[] value)
	{
		var buys = new List<InjectivePriceLevel>();
		var sells = new List<InjectivePriceLevel>();
		var result = new InjectiveOrderBook();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1:
					buys.Add(ParsePriceLevel(input.ReadBytes().ToByteArray()));
					return true;
				case 2:
					sells.Add(ParsePriceLevel(input.ReadBytes().ToByteArray()));
					return true;
				case 3:
					result.Sequence = input.ReadUInt64();
					return true;
				case 4:
					result.Timestamp = input.ReadInt64();
					return true;
				case 5:
					result.Height = input.ReadInt64();
					return true;
				default:
					return false;
			}
		});
		result.Buys = [.. buys];
		result.Sells = [.. sells];
		return result;
	}

	private static InjectivePriceLevel ParsePriceLevel(byte[] value)
	{
		var result = new InjectivePriceLevel();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1:
					result.Price = input.ReadString();
					return true;
				case 2:
					result.Quantity = input.ReadString();
					return true;
				case 3:
					result.Timestamp = input.ReadInt64();
					return true;
				default:
					return false;
			}
		});
		return result;
	}

	private static InjectiveTrade ParseSpotTrade(byte[] value)
	{
		var result = new InjectiveTrade();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1: result.OrderHash = input.ReadString(); return true;
				case 2: result.SubaccountId = input.ReadString(); return true;
				case 3: result.MarketId = input.ReadString(); return true;
				case 4: result.TradeExecutionType = input.ReadString(); return true;
				case 5: result.TradeDirection = input.ReadString(); return true;
				case 6:
					result.Price = ParsePriceLevel(input.ReadBytes().ToByteArray());
					return true;
				case 7: result.Fee = input.ReadString(); return true;
				case 8: result.ExecutedAt = input.ReadInt64(); return true;
				case 9: result.FeeRecipient = input.ReadString(); return true;
				case 10: result.TradeId = input.ReadString(); return true;
				case 11: result.ExecutionSide = input.ReadString(); return true;
				case 12: result.Cid = input.ReadString(); return true;
				default: return false;
			}
		});
		return result;
	}

	private static InjectiveTrade ParseDerivativeTrade(byte[] value)
	{
		var result = new InjectiveTrade();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1: result.OrderHash = input.ReadString(); return true;
				case 2: result.SubaccountId = input.ReadString(); return true;
				case 3: result.MarketId = input.ReadString(); return true;
				case 4: result.TradeExecutionType = input.ReadString(); return true;
				case 5: result.IsLiquidation = input.ReadBool(); return true;
				case 6:
					result.PositionDelta = ParsePositionDelta(input.ReadBytes()
						.ToByteArray());
					return true;
				case 7: result.Payout = input.ReadString(); return true;
				case 8: result.Fee = input.ReadString(); return true;
				case 9: result.ExecutedAt = input.ReadInt64(); return true;
				case 10: result.FeeRecipient = input.ReadString(); return true;
				case 11: result.TradeId = input.ReadString(); return true;
				case 12: result.ExecutionSide = input.ReadString(); return true;
				case 13: result.Cid = input.ReadString(); return true;
				case 14: result.Pnl = input.ReadString(); return true;
				default: return false;
			}
		});
		return result;
	}

	private static InjectivePositionDelta ParsePositionDelta(byte[] value)
	{
		var result = new InjectivePositionDelta();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1: result.TradeDirection = input.ReadString(); return true;
				case 2: result.ExecutionPrice = input.ReadString(); return true;
				case 3: result.ExecutionQuantity = input.ReadString(); return true;
				case 4: result.ExecutionMargin = input.ReadString(); return true;
				default: return false;
			}
		});
		return result;
	}

	private static InjectiveOrder ParseSpotOrder(byte[] value)
	{
		var result = new InjectiveOrder();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1: result.OrderHash = input.ReadString(); return true;
				case 2:
					result.OrderSide = result.Direction = input.ReadString();
					return true;
				case 3: result.MarketId = input.ReadString(); return true;
				case 4: result.SubaccountId = input.ReadString(); return true;
				case 5: result.Price = input.ReadString(); return true;
				case 6: result.Quantity = input.ReadString(); return true;
				case 7: result.UnfilledQuantity = input.ReadString(); return true;
				case 8: result.TriggerPrice = input.ReadString(); return true;
				case 9: result.FeeRecipient = input.ReadString(); return true;
				case 10: result.State = input.ReadString(); return true;
				case 11: result.CreatedAt = input.ReadInt64(); return true;
				case 12: result.UpdatedAt = input.ReadInt64(); return true;
				case 13: result.TxHash = input.ReadString(); return true;
				case 14: result.Cid = input.ReadString(); return true;
				default: return false;
			}
		});
		return result;
	}

	private static InjectiveOrder ParseDerivativeOrder(byte[] value)
	{
		var result = new InjectiveOrder();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1: result.OrderHash = input.ReadString(); return true;
				case 2:
					result.OrderSide = result.Direction = input.ReadString();
					return true;
				case 3: result.MarketId = input.ReadString(); return true;
				case 4: result.SubaccountId = input.ReadString(); return true;
				case 5: result.IsReduceOnly = input.ReadBool(); return true;
				case 6: result.Margin = input.ReadString(); return true;
				case 7: result.Price = input.ReadString(); return true;
				case 8: result.Quantity = input.ReadString(); return true;
				case 9: result.UnfilledQuantity = input.ReadString(); return true;
				case 10: result.TriggerPrice = input.ReadString(); return true;
				case 11: result.FeeRecipient = input.ReadString(); return true;
				case 12: result.State = input.ReadString(); return true;
				case 13: result.CreatedAt = input.ReadInt64(); return true;
				case 14: result.UpdatedAt = input.ReadInt64(); return true;
				case 15: result.OrderNumber = input.ReadInt64(); return true;
				case 16: result.OrderType = input.ReadString(); return true;
				case 17: result.IsConditional = input.ReadBool(); return true;
				case 18: result.TriggerAt = input.ReadUInt64(); return true;
				case 19: result.PlacedOrderHash = input.ReadString(); return true;
				case 20: result.ExecutionType = input.ReadString(); return true;
				case 21: result.TxHash = input.ReadString(); return true;
				case 22: result.Cid = input.ReadString(); return true;
				case 23: result.AccountAddress = input.ReadString(); return true;
				default: return false;
			}
		});
		return result;
	}

	private static InjectivePosition ParsePositionValue(byte[] value)
	{
		var result = new InjectivePosition();
		Read(value, (field, input) =>
		{
			switch (field)
			{
				case 1: result.Ticker = input.ReadString(); return true;
				case 2: result.MarketId = input.ReadString(); return true;
				case 3: result.SubaccountId = input.ReadString(); return true;
				case 4: result.Direction = input.ReadString(); return true;
				case 5: result.Quantity = input.ReadString(); return true;
				case 6: result.EntryPrice = input.ReadString(); return true;
				case 7: result.Margin = input.ReadString(); return true;
				case 8: result.LiquidationPrice = input.ReadString(); return true;
				case 9: result.MarkPrice = input.ReadString(); return true;
				case 11: result.UpdatedAt = input.ReadInt64(); return true;
				case 12: result.Denom = input.ReadString(); return true;
				case 13: result.FundingLast = input.ReadString(); return true;
				case 14: result.FundingSum = input.ReadString(); return true;
				case 15:
					result.CumulativeFundingEntry = input.ReadString();
					return true;
				case 16:
					result.EffectiveCumulativeFundingEntry = input.ReadString();
					return true;
				case 17: result.Upnl = input.ReadString(); return true;
				default: return false;
			}
		});
		return result;
	}

	private static byte[] Serialize(Action<CodedOutputStream> write)
	{
		using var stream = new MemoryStream();
		using (var output = new CodedOutputStream(stream, true))
		{
			write(output);
			output.Flush();
		}
		return stream.ToArray();
	}

	private static void WriteString(CodedOutputStream output, int field,
		string value)
	{
		if (value.IsEmpty())
			return;
		output.WriteTag(field, WireFormat.WireType.LengthDelimited);
		output.WriteString(value);
	}

	private static void WriteBool(CodedOutputStream output, int field,
		bool value)
	{
		if (!value)
			return;
		output.WriteTag(field, WireFormat.WireType.Varint);
		output.WriteBool(value);
	}

	private static void Read(byte[] value,
		Func<int, CodedInputStream, bool> readField)
	{
		if (value is not { Length: > 0 })
			throw new InvalidDataException(
				"Injective returned an empty protobuf message.");
		using var input = new CodedInputStream(value);
		while (!input.IsAtEnd)
		{
			var tag = input.ReadTag();
			if (tag == 0)
				break;
			if (!readField(WireFormat.GetTagFieldNumber(tag), input))
				input.SkipLastField();
		}
	}
}
