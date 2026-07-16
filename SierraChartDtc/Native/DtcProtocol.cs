namespace StockSharp.SierraChartDtc.Native;

using System.Buffers.Binary;
using System.Text;

internal static class DtcProtocol
{
	public const int CurrentVersion = 8;
	public const int MaxMessageLength = ushort.MaxValue;

	public static byte[] Encode(DtcMessage message)
		=> message switch
		{
			DtcEncodingRequest value => Encode(value),
			DtcLogonRequest value => Encode(value),
			DtcHeartbeat value => Encode(value),
			DtcLogoff value => Encode(value),
			DtcMarketDataRequest value => Encode(value),
			DtcMarketDepthRequest value => Encode(value),
			DtcSymbolsForExchangeRequest value => Encode(value),
			DtcSymbolSearchRequest value => Encode(value),
			DtcSecurityDefinitionRequest value => Encode(value),
			DtcHistoricalPriceRequest value => Encode(value),
			DtcSubmitOrder value => Encode(value),
			DtcReplaceOrder value => Encode(value),
			DtcCancelOrder value => Encode(value),
			DtcOpenOrdersRequest value => Encode(value),
			DtcHistoricalFillsRequest value => Encode(value),
			DtcCurrentPositionsRequest value => Encode(value),
			DtcTradeAccountsRequest value => Encode(value),
			DtcAccountBalanceRequest value => Encode(value),
			_ => throw new NotSupportedException($"Encoding DTC message type {message.Type} is not supported."),
		};

	public static DtcMessage Decode(byte[] data)
	{
		if (data == null || data.Length < 4)
			throw new InvalidDataException("A DTC message must contain the four-byte header.");

		var declaredSize = BinaryPrimitives.ReadUInt16LittleEndian(data);
		if (declaredSize != data.Length)
			throw new InvalidDataException($"DTC message size {declaredSize} does not match the received {data.Length} bytes.");

		var type = (DtcMessageTypes)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
		return type switch
		{
			DtcMessageTypes.EncodingResponse => DecodeEncodingResponse(data),
			DtcMessageTypes.LogonResponse => DecodeLogonResponse(data),
			DtcMessageTypes.Heartbeat => DecodeHeartbeat(data),
			DtcMessageTypes.Logoff => DecodeLogoff(data),
			DtcMessageTypes.MarketDataFeedStatus => DecodeMarketDataFeedStatus(data),
			DtcMessageTypes.MarketDataFeedSymbolStatus => DecodeMarketDataFeedSymbolStatus(data),
			DtcMessageTypes.TradingSymbolStatus => DecodeTradingSymbolStatus(data),
			DtcMessageTypes.MarketDataSnapshot => DecodeMarketDataSnapshot(data),
			DtcMessageTypes.MarketDataUpdateTrade or
			DtcMessageTypes.MarketDataUpdateTradeCompact or
			DtcMessageTypes.MarketDataUpdateLastTradeSnapshot or
			DtcMessageTypes.MarketDataUpdateTradeWithUnbundledIndicator or
			DtcMessageTypes.MarketDataUpdateTradeWithUnbundledIndicator2 or
			DtcMessageTypes.MarketDataUpdateTradeNoTimestamp or
			DtcMessageTypes.MarketDataUpdateTradeV2 => DecodeTrade(data, type),
			DtcMessageTypes.MarketDataUpdateBidAsk or
			DtcMessageTypes.MarketDataUpdateBidAskCompact or
			DtcMessageTypes.MarketDataUpdateBidAskNoTimestamp or
			DtcMessageTypes.MarketDataUpdateBidAskFloatWithMicroseconds or
			DtcMessageTypes.MarketDataUpdateBidAskV2 => DecodeBidAsk(data, type),
			DtcMessageTypes.MarketDataUpdateSessionOpen or
			DtcMessageTypes.MarketDataUpdateSessionHigh or
			DtcMessageTypes.MarketDataUpdateSessionLow or
			DtcMessageTypes.MarketDataUpdateSessionSettlement or
			DtcMessageTypes.MarketDataUpdateSessionVolume or
			DtcMessageTypes.MarketDataUpdateOpenInterest or
			DtcMessageTypes.MarketDataUpdateSessionNumTrades or
			DtcMessageTypes.MarketDataUpdateTradingSessionDate => DecodeSession(data, type),
			DtcMessageTypes.MarketDepthSnapshotLevel or
			DtcMessageTypes.MarketDepthSnapshotLevelFloat or
			DtcMessageTypes.MarketDepthUpdateLevel or
			DtcMessageTypes.MarketDepthUpdateLevelFloatWithMilliseconds or
			DtcMessageTypes.MarketDepthUpdateLevelNoTimestamp => DecodeDepth(data, type),
			DtcMessageTypes.SecurityDefinitionResponse or
			DtcMessageTypes.SecurityDefinitionResponseV2 => DecodeSecurityDefinition(data, type),
			DtcMessageTypes.HistoricalPriceDataResponseHeader => DecodeHistoricalHeader(data),
			DtcMessageTypes.HistoricalPriceDataRecordResponse => DecodeHistoricalRecord(data),
			DtcMessageTypes.HistoricalPriceDataTickRecordResponse => DecodeHistoricalTick(data),
			DtcMessageTypes.HistoricalPriceDataResponseTrailer => DecodeHistoricalTrailer(data),
			DtcMessageTypes.OrderUpdate => DecodeOrderUpdate(data),
			DtcMessageTypes.HistoricalOrderFillResponse => DecodeHistoricalFill(data),
			DtcMessageTypes.PositionUpdate => DecodePosition(data),
			DtcMessageTypes.TradeAccountResponse => DecodeTradeAccount(data),
			DtcMessageTypes.AccountBalanceUpdate => DecodeAccountBalance(data),
			DtcMessageTypes.MarketDataReject or
			DtcMessageTypes.MarketDepthReject or
			DtcMessageTypes.SecurityDefinitionReject or
			DtcMessageTypes.HistoricalPriceDataReject or
			DtcMessageTypes.OpenOrdersReject or
			DtcMessageTypes.HistoricalOrderFillsReject or
			DtcMessageTypes.CurrentPositionsReject or
			DtcMessageTypes.AccountBalanceReject => DecodeReject(data, type),
			_ => new DtcUnknownMessage(type),
		};
	}

	private static byte[] Encode(DtcEncodingRequest message)
	{
		var writer = new DtcWriter(message.Type);
		writer.WriteInt32(message.ProtocolVersion);
		writer.WriteInt32((int)message.Encoding);
		writer.WriteAscii("DTC", 4);
		return writer.Complete();
	}

	private static byte[] Encode(DtcLogonRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.ProtocolVersion);
		writer.WriteString(message.UserName);
		writer.WriteString(message.Password);
		writer.WriteString(message.GeneralText);
		writer.WriteInt32(message.Integer1);
		writer.WriteInt32(message.Integer2);
		writer.WriteInt32(message.HeartbeatIntervalSeconds);
		writer.WriteInt32(0);
		writer.WriteString(message.TradeAccount);
		writer.WriteString(message.HardwareIdentifier);
		writer.WriteString(message.ClientName);
		writer.WriteInt32(message.MarketDataTransmissionInterval);
		return writer.Complete();
	}

	private static byte[] Encode(DtcHeartbeat message)
	{
		var writer = new DtcWriter(message.Type);
		writer.WriteUInt32(message.DroppedMessages);
		writer.WriteInt64(ToUnixSeconds(message.CurrentTime));
		return writer.Complete();
	}

	private static byte[] Encode(DtcLogoff message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteString(message.Reason);
		writer.WriteByte(message.IsReconnectDisabled ? (byte)1 : (byte)0);
		return writer.Complete();
	}

	private static byte[] Encode(DtcMarketDataRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32((int)message.Action);
		writer.WriteUInt32(message.SymbolId);
		writer.WriteString(message.Symbol);
		writer.WriteString(message.Exchange);
		writer.WriteUInt32(message.UpdateIntervalMilliseconds);
		return writer.Complete();
	}

	private static byte[] Encode(DtcMarketDepthRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32((int)message.Action);
		writer.WriteUInt32(message.SymbolId);
		writer.WriteString(message.Symbol);
		writer.WriteString(message.Exchange);
		writer.WriteInt32(message.Levels);
		return writer.Complete();
	}

	private static byte[] Encode(DtcSymbolsForExchangeRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		writer.WriteString(message.Exchange);
		writer.WriteInt32((int)message.SecurityType);
		writer.WriteInt32((int)message.Action);
		writer.WriteString(message.Symbol);
		return writer.Complete();
	}

	private static byte[] Encode(DtcSymbolSearchRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		writer.WriteString(message.SearchText);
		writer.WriteString(message.Exchange);
		writer.WriteInt32((int)message.SecurityType);
		writer.WriteInt32((int)message.SearchType);
		return writer.Complete();
	}

	private static byte[] Encode(DtcSecurityDefinitionRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		writer.WriteString(message.Symbol);
		writer.WriteString(message.Exchange);
		return writer.Complete();
	}

	private static byte[] Encode(DtcHistoricalPriceRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		writer.WriteString(message.Symbol);
		writer.WriteString(message.Exchange);
		writer.WriteInt32(message.IntervalSeconds);
		writer.WriteInt64(message.From == null ? 0 : ToUnixSeconds(message.From.Value));
		writer.WriteInt64(message.To == null ? 0 : ToUnixSeconds(message.To.Value));
		writer.WriteUInt32(message.MaxDays);
		writer.WriteByte(0);
		writer.WriteByte(message.IsDividendAdjusted ? (byte)1 : (byte)0);
		writer.WriteUInt16(0);
		writer.WriteByte(0);
		return writer.Complete();
	}

	private static byte[] Encode(DtcSubmitOrder message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteString(message.Symbol);
		writer.WriteString(message.Exchange);
		writer.WriteString(message.TradeAccount);
		writer.WriteString(message.ClientOrderId);
		writer.WriteInt32((int)message.OrderType);
		writer.WriteInt32((int)message.Side);
		writer.WriteDouble(message.Price1 is { } price1 ? (double)price1 : double.MaxValue);
		writer.WriteDouble(message.Price2 is { } price2 ? (double)price2 : double.MaxValue);
		writer.WriteDouble((double)message.Quantity);
		writer.WriteInt32((int)message.TimeInForce);
		writer.WriteInt64(message.GoodTillTime == null ? 0 : ToUnixSeconds(message.GoodTillTime.Value));
		writer.WriteByte(message.IsAutomated ? (byte)1 : (byte)0);
		writer.WriteByte(message.IsParent ? (byte)1 : (byte)0);
		writer.WriteString(message.FreeFormText);
		writer.WriteInt32((int)message.OpenOrClose);
		writer.WriteDouble((double)message.MaxShowQuantity);
		writer.WriteString(null);
		writer.WriteString(null);
		writer.WriteDouble((double)message.IntendedPositionQuantity);
		return writer.Complete();
	}

	private static byte[] Encode(DtcReplaceOrder message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteString(message.ServerOrderId);
		writer.WriteString(message.ClientOrderId);
		writer.WriteDouble(message.Price1 is { } price1 ? (double)price1 : double.MaxValue);
		writer.WriteDouble(message.Price2 is { } price2 ? (double)price2 : double.MaxValue);
		writer.WriteDouble((double)message.Quantity);
		writer.WriteSByte(message.IsPrice1Set ? (sbyte)1 : (sbyte)0);
		writer.WriteSByte(message.IsPrice2Set ? (sbyte)1 : (sbyte)0);
		writer.WriteInt32(0);
		writer.WriteInt32((int)message.TimeInForce);
		writer.WriteInt64(message.GoodTillTime == null ? 0 : ToUnixSeconds(message.GoodTillTime.Value));
		writer.WriteByte(0);
		writer.WriteString(message.TradeAccount);
		writer.WriteString(null);
		writer.WriteString(null);
		return writer.Complete();
	}

	private static byte[] Encode(DtcCancelOrder message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteString(message.ServerOrderId);
		writer.WriteString(message.ClientOrderId);
		writer.WriteString(message.TradeAccount);
		return writer.Complete();
	}

	private static byte[] Encode(DtcOpenOrdersRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		writer.WriteInt32(message.IsAllOrders ? 1 : 0);
		writer.WriteString(message.ServerOrderId);
		writer.WriteString(message.TradeAccount);
		return writer.Complete();
	}

	private static byte[] Encode(DtcHistoricalFillsRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		writer.WriteString(message.ServerOrderId);
		writer.WriteInt32(message.Days);
		writer.WriteString(message.TradeAccount);
		writer.WriteInt64(message.From == null ? 0 : ToUnixSeconds(message.From.Value));
		return writer.Complete();
	}

	private static byte[] Encode(DtcCurrentPositionsRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		writer.WriteString(message.TradeAccount);
		return writer.Complete();
	}

	private static byte[] Encode(DtcTradeAccountsRequest message)
	{
		var writer = new DtcWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		return writer.Complete();
	}

	private static byte[] Encode(DtcAccountBalanceRequest message)
	{
		var writer = new DtcVlsWriter(message.Type);
		writer.WriteInt32(message.RequestId);
		writer.WriteString(message.TradeAccount);
		return writer.Complete();
	}

	private static DtcEncodingResponse DecodeEncodingResponse(byte[] data)
	{
		var reader = new DtcReader(data);
		return new()
		{
			ProtocolVersion = reader.ReadInt32(),
			Encoding = (DtcEncodings)reader.ReadInt32(),
			ProtocolType = reader.ReadAscii(4),
		};
	}

	private static DtcLogonResponse DecodeLogonResponse(byte[] data)
	{
		var reader = new DtcReader(data, true);
		var result = new DtcLogonResponse
		{
			ProtocolVersion = reader.ReadInt32(),
			Result = (DtcLogonStatuses)reader.ReadInt32(),
			ResultText = reader.ReadString(),
			ReconnectAddress = reader.ReadString(),
			Integer1 = reader.ReadInt32(),
			ServerName = reader.ReadString(),
			IsMarketDepthBestBidAsk = reader.ReadByte() != 0,
			IsTradingSupported = reader.ReadByte() != 0,
			IsOcoSupported = reader.ReadByte() != 0,
			IsCancelReplaceSupported = reader.ReadByte() != 0,
			SymbolExchangeDelimiter = reader.ReadString(),
			IsSecurityDefinitionsSupported = reader.ReadByte() != 0,
			IsHistoricalPriceDataSupported = reader.ReadByte() != 0,
			IsResubscribeRequired = reader.ReadByte() != 0,
			IsMarketDepthSupported = reader.ReadByte() != 0,
			IsOneHistoricalRequestPerConnection = reader.ReadByte() != 0,
			IsBracketOrdersSupported = reader.ReadByte() != 0,
		};
		reader.ReadByte();
		result.IsMultiplePositionsSupported = reader.ReadByte() != 0;
		result.IsMarketDataSupported = reader.ReadByte() != 0;
		return result;
	}

	private static DtcHeartbeat DecodeHeartbeat(byte[] data)
	{
		var reader = new DtcReader(data);
		return new()
		{
			DroppedMessages = reader.ReadUInt32(),
			CurrentTime = FromUnixSeconds(reader.ReadInt64()) ?? DateTime.UnixEpoch,
		};
	}

	private static DtcLogoff DecodeLogoff(byte[] data)
	{
		var reader = new DtcReader(data, true);
		return new()
		{
			Reason = reader.ReadString(),
			IsReconnectDisabled = reader.ReadByte() != 0,
		};
	}

	private static DtcMarketDataFeedStatus DecodeMarketDataFeedStatus(byte[] data)
	{
		var reader = new DtcReader(data);
		return new()
		{
			Status = (DtcMarketDataFeedStatuses)reader.ReadInt32(),
		};
	}

	private static DtcMarketDataFeedSymbolStatus DecodeMarketDataFeedSymbolStatus(byte[] data)
	{
		var reader = new DtcReader(data);
		return new()
		{
			SymbolId = reader.ReadUInt32(),
			Status = (DtcMarketDataFeedStatuses)reader.ReadInt32(),
		};
	}

	private static DtcTradingSymbolStatus DecodeTradingSymbolStatus(byte[] data)
	{
		var reader = new DtcReader(data);
		return new()
		{
			SymbolId = reader.ReadUInt32(),
			Status = (DtcTradingStatuses)reader.ReadSByte(),
		};
	}

	private static DtcMarketDataSnapshot DecodeMarketDataSnapshot(byte[] data)
	{
		var reader = new DtcReader(data);
		return new()
		{
			SymbolId = reader.ReadUInt32(),
			SettlementPrice = reader.ReadNullableDoubleDecimal(),
			OpenPrice = reader.ReadNullableDoubleDecimal(),
			HighPrice = reader.ReadNullableDoubleDecimal(),
			LowPrice = reader.ReadNullableDoubleDecimal(),
			Volume = reader.ReadNullableDoubleDecimal(),
			TradesCount = reader.ReadNullableUInt32(),
			OpenInterest = reader.ReadNullableUInt32(),
			BidPrice = reader.ReadNullableDoubleDecimal(),
			AskPrice = reader.ReadNullableDoubleDecimal(),
			AskVolume = reader.ReadNullableDoubleDecimal(),
			BidVolume = reader.ReadNullableDoubleDecimal(),
			LastPrice = reader.ReadNullableDoubleDecimal(),
			LastVolume = reader.ReadNullableDoubleDecimal(),
			LastTime = FromUnixFractionalSeconds(reader.ReadDouble()),
			BidAskTime = FromUnixFractionalSeconds(reader.ReadDouble()),
			SettlementTime = FromUnixSeconds(reader.ReadUInt32()),
			TradingSessionDate = FromUnixSeconds(reader.ReadUInt32()),
			TradingStatus = (DtcTradingStatuses)reader.ReadSByte(),
		};
	}

	private static DtcTradeUpdate DecodeTrade(byte[] data, DtcMessageTypes type)
	{
		var result = new DtcTradeUpdate(type);
		switch (type)
		{
			case DtcMessageTypes.MarketDataUpdateTrade:
			{
				var reader = new DtcReader(data);
				result.SymbolId = reader.ReadUInt32();
				result.AtBidOrAsk = (DtcAtBidOrAsks)reader.ReadUInt16();
				result.Price = reader.ReadDoubleDecimal();
				result.Volume = reader.ReadDoubleDecimal();
				result.Time = FromUnixFractionalSeconds(reader.ReadDouble());
				break;
			}
			case DtcMessageTypes.MarketDataUpdateTradeCompact:
			{
				var reader = new DtcReader(data);
				result.Price = reader.ReadSingleDecimal();
				result.Volume = reader.ReadSingleDecimal();
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				result.SymbolId = reader.ReadUInt32();
				result.AtBidOrAsk = (DtcAtBidOrAsks)reader.ReadUInt16();
				break;
			}
			case DtcMessageTypes.MarketDataUpdateLastTradeSnapshot:
			{
				var reader = new DtcReader(data);
				result.SymbolId = reader.ReadUInt32();
				result.Price = reader.ReadDoubleDecimal();
				result.Volume = reader.ReadDoubleDecimal();
				result.Time = FromUnixFractionalSeconds(reader.ReadDouble());
				break;
			}
			case DtcMessageTypes.MarketDataUpdateTradeWithUnbundledIndicator:
			{
				var reader = new DtcReader(data);
				result.SymbolId = reader.ReadUInt32();
				result.AtBidOrAsk = (DtcAtBidOrAsks)reader.ReadByte();
				result.UnbundledIndicator = reader.ReadByte();
				result.TradeCondition = reader.ReadByte();
				reader.ReadByte();
				reader.ReadUInt32();
				result.Price = reader.ReadDoubleDecimal();
				result.Volume = reader.ReadUInt32();
				reader.ReadUInt32();
				result.Time = FromUnixFractionalSeconds(reader.ReadDouble());
				break;
			}
			case DtcMessageTypes.MarketDataUpdateTradeWithUnbundledIndicator2:
			{
				var reader = new DtcReader(data, false, true);
				result.SymbolId = reader.ReadUInt32();
				result.Price = reader.ReadSingleDecimal();
				result.Volume = reader.ReadUInt32();
				result.Time = FromUnixMicroseconds(reader.ReadInt64());
				result.AtBidOrAsk = (DtcAtBidOrAsks)reader.ReadByte();
				result.UnbundledIndicator = reader.ReadByte();
				result.TradeCondition = reader.ReadByte();
				break;
			}
			case DtcMessageTypes.MarketDataUpdateTradeNoTimestamp:
			{
				var reader = new DtcReader(data, false, true);
				result.SymbolId = reader.ReadUInt32();
				result.Price = reader.ReadSingleDecimal();
				result.Volume = reader.ReadUInt32();
				result.AtBidOrAsk = (DtcAtBidOrAsks)reader.ReadByte();
				result.UnbundledIndicator = reader.ReadByte();
				result.TradeCondition = reader.ReadByte();
				break;
			}
			case DtcMessageTypes.MarketDataUpdateTradeV2:
			{
				var reader = new DtcReader(data);
				result.SymbolId = reader.ReadUInt32();
				result.Price = reader.ReadDoubleDecimal();
				result.Volume = reader.ReadDoubleDecimal();
				result.Time = FromUnixMicroseconds(reader.ReadInt64());
				result.AtBidOrAsk = (DtcAtBidOrAsks)reader.ReadByte();
				result.UnbundledIndicator = reader.ReadByte();
				result.TradeCondition = reader.ReadByte();
				break;
			}
		}
		return result;
	}

	private static DtcBidAskUpdate DecodeBidAsk(byte[] data, DtcMessageTypes type)
	{
		var result = new DtcBidAskUpdate(type);
		switch (type)
		{
			case DtcMessageTypes.MarketDataUpdateBidAsk:
			{
				var reader = new DtcReader(data);
				result.SymbolId = reader.ReadUInt32();
				result.BidPrice = reader.ReadNullableDoubleDecimal();
				result.BidVolume = reader.ReadNullableSingleDecimal(false);
				result.AskPrice = reader.ReadNullableDoubleDecimal();
				result.AskVolume = reader.ReadNullableSingleDecimal(false);
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
			}
			case DtcMessageTypes.MarketDataUpdateBidAskCompact:
			{
				var reader = new DtcReader(data);
				result.BidPrice = reader.ReadNullableSingleDecimal();
				result.BidVolume = reader.ReadNullableSingleDecimal(false);
				result.AskPrice = reader.ReadNullableSingleDecimal();
				result.AskVolume = reader.ReadNullableSingleDecimal(false);
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				result.SymbolId = reader.ReadUInt32();
				break;
			}
			case DtcMessageTypes.MarketDataUpdateBidAskFloatWithMicroseconds:
			{
				var reader = new DtcReader(data, false, true);
				result.SymbolId = reader.ReadUInt32();
				result.BidPrice = reader.ReadNullableSingleDecimal();
				result.BidVolume = reader.ReadNullableSingleDecimal(false);
				result.AskPrice = reader.ReadNullableSingleDecimal();
				result.AskVolume = reader.ReadNullableSingleDecimal(false);
				result.Time = FromUnixMicroseconds(reader.ReadInt64());
				break;
			}
			case DtcMessageTypes.MarketDataUpdateBidAskNoTimestamp:
			{
				var reader = new DtcReader(data, false, true);
				result.SymbolId = reader.ReadUInt32();
				result.BidPrice = reader.ReadNullableSingleDecimal();
				result.BidVolume = reader.ReadUInt32();
				result.AskPrice = reader.ReadNullableSingleDecimal();
				result.AskVolume = reader.ReadUInt32();
				break;
			}
			case DtcMessageTypes.MarketDataUpdateBidAskV2:
			{
				var reader = new DtcReader(data);
				result.SymbolId = reader.ReadUInt32();
				result.BidPrice = reader.ReadNullableDoubleDecimal();
				result.BidVolume = reader.ReadNullableDoubleDecimal(false);
				result.AskPrice = reader.ReadNullableDoubleDecimal();
				result.AskVolume = reader.ReadNullableDoubleDecimal(false);
				result.Time = FromUnixMicroseconds(reader.ReadInt64());
				break;
			}
		}
		return result;
	}

	private static DtcSessionUpdate DecodeSession(byte[] data, DtcMessageTypes type)
	{
		var reader = new DtcReader(data);
		var result = new DtcSessionUpdate(type) { SymbolId = reader.ReadUInt32() };
		switch (type)
		{
			case DtcMessageTypes.MarketDataUpdateSessionOpen:
				result.Field = DtcSessionUpdateFields.Open;
				result.Value = reader.ReadDoubleDecimal();
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
			case DtcMessageTypes.MarketDataUpdateSessionHigh:
				result.Field = DtcSessionUpdateFields.High;
				result.Value = reader.ReadDoubleDecimal();
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
			case DtcMessageTypes.MarketDataUpdateSessionLow:
				result.Field = DtcSessionUpdateFields.Low;
				result.Value = reader.ReadDoubleDecimal();
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
			case DtcMessageTypes.MarketDataUpdateSessionSettlement:
				result.Field = DtcSessionUpdateFields.Settlement;
				result.Value = reader.ReadDoubleDecimal();
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
			case DtcMessageTypes.MarketDataUpdateSessionVolume:
				result.Field = DtcSessionUpdateFields.Volume;
				result.Value = reader.ReadDoubleDecimal();
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
			case DtcMessageTypes.MarketDataUpdateOpenInterest:
				result.Field = DtcSessionUpdateFields.OpenInterest;
				result.Value = reader.ReadUInt32();
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
			case DtcMessageTypes.MarketDataUpdateSessionNumTrades:
				result.Field = DtcSessionUpdateFields.TradesCount;
				result.Value = reader.ReadUInt32();
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
			case DtcMessageTypes.MarketDataUpdateTradingSessionDate:
				result.Field = DtcSessionUpdateFields.TradingSessionDate;
				result.Time = FromUnixSeconds(reader.ReadUInt32());
				break;
		}
		return result;
	}

	private static DtcDepthUpdate DecodeDepth(byte[] data, DtcMessageTypes type)
	{
		var result = new DtcDepthUpdate(type);
		switch (type)
		{
			case DtcMessageTypes.MarketDepthSnapshotLevel:
			{
				var reader = new DtcReader(data);
				result.SymbolId = reader.ReadUInt32();
				result.Side = (DtcAtBidOrAsks)reader.ReadUInt16();
				result.Price = reader.ReadDoubleDecimal();
				result.Volume = reader.ReadDoubleDecimal();
				result.Level = reader.ReadUInt16();
				result.IsFirstSnapshot = reader.ReadByte() != 0;
				result.FinalUpdate = reader.ReadByte() != 0 ? DtcFinalUpdates.Final : DtcFinalUpdates.NotFinal;
				result.Time = FromUnixFractionalSeconds(reader.ReadDouble());
				result.OrdersCount = reader.ReadUInt32();
				result.UpdateType = DtcDepthUpdateTypes.InsertOrUpdate;
				result.IsSnapshot = true;
				break;
			}
			case DtcMessageTypes.MarketDepthSnapshotLevelFloat:
			{
				var reader = new DtcReader(data, false, true);
				result.SymbolId = reader.ReadUInt32();
				result.Price = reader.ReadSingleDecimal();
				result.Volume = reader.ReadSingleDecimal();
				result.OrdersCount = reader.ReadUInt32();
				result.Level = reader.ReadUInt16();
				result.Side = (DtcAtBidOrAsks)reader.ReadByte();
				result.FinalUpdate = (DtcFinalUpdates)reader.ReadByte();
				result.IsFirstSnapshot = result.FinalUpdate == DtcFinalUpdates.BeginBatch;
				result.UpdateType = DtcDepthUpdateTypes.InsertOrUpdate;
				result.IsSnapshot = true;
				break;
			}
			case DtcMessageTypes.MarketDepthUpdateLevel:
			{
				var reader = new DtcReader(data);
				result.SymbolId = reader.ReadUInt32();
				result.Side = (DtcAtBidOrAsks)reader.ReadUInt16();
				result.Price = reader.ReadDoubleDecimal();
				result.Volume = reader.ReadDoubleDecimal();
				result.UpdateType = (DtcDepthUpdateTypes)reader.ReadByte();
				result.Time = FromUnixFractionalSeconds(reader.ReadDouble());
				result.OrdersCount = reader.ReadUInt32();
				result.FinalUpdate = (DtcFinalUpdates)reader.ReadByte();
				result.Level = reader.ReadUInt16();
				break;
			}
			case DtcMessageTypes.MarketDepthUpdateLevelFloatWithMilliseconds:
			{
				var reader = new DtcReader(data, false, true);
				result.SymbolId = reader.ReadUInt32();
				result.Time = FromUnixMilliseconds(reader.ReadInt64());
				result.Price = reader.ReadSingleDecimal();
				result.Volume = reader.ReadSingleDecimal();
				result.Side = (DtcAtBidOrAsks)reader.ReadByte();
				result.UpdateType = (DtcDepthUpdateTypes)reader.ReadByte();
				result.OrdersCount = reader.ReadUInt16();
				result.FinalUpdate = (DtcFinalUpdates)reader.ReadByte();
				result.Level = reader.ReadUInt16();
				break;
			}
			case DtcMessageTypes.MarketDepthUpdateLevelNoTimestamp:
			{
				var reader = new DtcReader(data, false, true);
				result.SymbolId = reader.ReadUInt32();
				result.Price = reader.ReadSingleDecimal();
				result.Volume = reader.ReadSingleDecimal();
				result.OrdersCount = reader.ReadUInt16();
				result.Side = (DtcAtBidOrAsks)reader.ReadSByte();
				result.UpdateType = (DtcDepthUpdateTypes)reader.ReadSByte();
				result.FinalUpdate = (DtcFinalUpdates)reader.ReadByte();
				result.Level = reader.ReadUInt16();
				break;
			}
		}
		return result;
	}

	private static DtcSecurityDefinition DecodeSecurityDefinition(byte[] data, DtcMessageTypes type)
		=> type == DtcMessageTypes.SecurityDefinitionResponseV2
			? DecodeSecurityDefinitionV2(data)
			: DecodeSecurityDefinitionV1(data);

	private static DtcSecurityDefinition DecodeSecurityDefinitionV1(byte[] data)
	{
		var reader = new DtcReader(data, true);
		var result = new DtcSecurityDefinition(DtcMessageTypes.SecurityDefinitionResponse)
		{
			RequestId = reader.ReadInt32(),
			Symbol = reader.ReadString(),
			Exchange = reader.ReadString(),
			SecurityType = (DtcSecurityTypes)reader.ReadInt32(),
			Description = reader.ReadString(),
			MinPriceIncrement = reader.ReadNullableSingleDecimal(false),
		};
		reader.ReadInt32();
		result.CurrencyValuePerIncrement = reader.ReadNullableSingleDecimal(false);
		result.IsFinal = reader.ReadByte() != 0;
		reader.ReadSingle();
		reader.ReadSingle();
		result.UnderlyingSymbol = reader.ReadString();
		result.IsBidAskOnly = reader.ReadByte() != 0;
		result.StrikePrice = reader.ReadNullableSingleDecimal(false);
		result.PutOrCall = (DtcPutCalls)reader.ReadByte();
		reader.ReadUInt32();
		result.ExpirationDate = FromUnixSeconds(reader.ReadUInt32());
		reader.ReadSingle();
		reader.ReadSingle();
		reader.ReadSingle();
		reader.ReadUInt32();
		result.QuantityDivisor = reader.ReadNullableSingleDecimal(false);
		result.IsMarketDepthSupported = reader.ReadByte() != 0;
		reader.ReadSingle();
		result.ExchangeSymbol = reader.ReadString();
		reader.ReadSingle();
		reader.ReadSingle();
		result.Currency = reader.ReadString();
		result.ContractSize = reader.ReadNullableSingleDecimal(false);
		result.OpenInterest = reader.ReadUInt32();
		reader.ReadUInt32();
		result.IsDelayed = reader.ReadByte() != 0;
		result.SecurityIdentifier = reader.ReadInt64();
		result.ProductIdentifier = reader.ReadString();
		return result;
	}

	private static DtcSecurityDefinition DecodeSecurityDefinitionV2(byte[] data)
	{
		var reader = new DtcReader(data, true);
		var result = new DtcSecurityDefinition(DtcMessageTypes.SecurityDefinitionResponseV2)
		{
			IsFinal = reader.ReadByte() != 0,
			IsBidAskOnly = reader.ReadByte() != 0,
			RequestId = reader.ReadInt32(),
			Symbol = reader.ReadString(),
			Exchange = reader.ReadString(),
			SecurityType = (DtcSecurityTypes)reader.ReadInt32(),
			Description = reader.ReadString(),
		};
		reader.ReadInt32();
		result.MinPriceIncrement = reader.ReadNullableDoubleDecimal(false);
		result.CurrencyValuePerIncrement = reader.ReadNullableDoubleDecimal(false);
		reader.ReadDouble();
		reader.ReadDouble();
		result.StrikePrice = reader.ReadNullableDoubleDecimal(false);
		reader.ReadUInt32();
		result.ExpirationDate = FromUnixSeconds(reader.ReadUInt32());
		reader.ReadDouble();
		reader.ReadDouble();
		reader.ReadDouble();
		reader.ReadUInt32();
		result.ExchangeSymbol = reader.ReadString();
		result.QuantityDivisor = reader.ReadNullableDoubleDecimal(false);
		reader.ReadDouble();
		reader.ReadDouble();
		reader.ReadDouble();
		result.ContractSize = reader.ReadNullableDoubleDecimal(false);
		result.Currency = reader.ReadString();
		result.OpenInterest = reader.ReadUInt32();
		reader.ReadUInt32();
		result.ProductIdentifier = reader.ReadString();
		result.SecurityIdentifier = reader.ReadInt64();
		result.UnderlyingSymbol = reader.ReadString();
		result.PutOrCall = (DtcPutCalls)reader.ReadByte();
		result.IsMarketDepthSupported = reader.ReadByte() != 0;
		result.IsDelayed = reader.ReadByte() != 0;
		return result;
	}

	private static DtcHistoricalPriceHeader DecodeHistoricalHeader(byte[] data)
	{
		var reader = new DtcReader(data);
		var result = new DtcHistoricalPriceHeader
		{
			RequestId = reader.ReadInt32(),
			IntervalSeconds = reader.ReadInt32(),
			IsCompressed = reader.ReadByte() != 0,
			IsEmpty = reader.ReadByte() != 0,
		};
		reader.ReadSingle();
		result.IsCompressed |= reader.ReadByte() != 0;
		return result;
	}

	private static DtcHistoricalPriceRecord DecodeHistoricalRecord(byte[] data)
	{
		var reader = new DtcReader(data);
		var requestId = reader.ReadInt32();
		var rawTime = reader.ReadInt64();
		return new()
		{
			RequestId = requestId,
			Time = FromHistoricalTime(rawTime),
			Open = reader.ReadDoubleDecimal(),
			High = reader.ReadDoubleDecimal(),
			Low = reader.ReadDoubleDecimal(),
			Close = reader.ReadDoubleDecimal(),
			Volume = reader.ReadDoubleDecimal(),
			OpenInterestOrTrades = reader.ReadUInt32(),
			BidVolume = reader.ReadDoubleDecimal(),
			AskVolume = reader.ReadDoubleDecimal(),
			IsFinal = reader.ReadByte() != 0,
		};
	}

	private static DtcHistoricalTickRecord DecodeHistoricalTick(byte[] data)
	{
		var reader = new DtcReader(data);
		return new()
		{
			RequestId = reader.ReadInt32(),
			Time = FromUnixFractionalSeconds(reader.ReadDouble()) ?? DateTime.UnixEpoch,
			AtBidOrAsk = (DtcAtBidOrAsks)reader.ReadUInt16(),
			Price = reader.ReadDoubleDecimal(),
			Volume = reader.ReadDoubleDecimal(),
			IsFinal = reader.ReadByte() != 0,
		};
	}

	private static DtcHistoricalPriceTrailer DecodeHistoricalTrailer(byte[] data)
	{
		var reader = new DtcReader(data);
		return new()
		{
			RequestId = reader.ReadInt32(),
			LastTime = FromUnixMicroseconds(reader.ReadInt64()),
		};
	}

	private static DtcOrderUpdate DecodeOrderUpdate(byte[] data)
	{
		var reader = new DtcReader(data, true);
		var result = new DtcOrderUpdate
		{
			RequestId = reader.ReadInt32(),
			TotalMessages = reader.ReadInt32(),
			MessageNumber = reader.ReadInt32(),
			Symbol = reader.ReadString(),
			Exchange = reader.ReadString(),
			PreviousServerOrderId = reader.ReadString(),
			ServerOrderId = reader.ReadString(),
			ClientOrderId = reader.ReadString(),
			ExchangeOrderId = reader.ReadString(),
			Status = (DtcOrderStatuses)reader.ReadInt32(),
			Reason = (DtcOrderUpdateReasons)reader.ReadInt32(),
			OrderType = (DtcOrderTypes)reader.ReadInt32(),
			Side = (DtcBuySells)reader.ReadInt32(),
			Price1 = reader.ReadNullableDoubleDecimal(),
			Price2 = reader.ReadNullableDoubleDecimal(),
			TimeInForce = (DtcTimeInForces)reader.ReadInt32(),
			GoodTillTime = FromUnixSeconds(reader.ReadInt64()),
			Quantity = reader.ReadNullableDoubleDecimal(),
			FilledQuantity = reader.ReadNullableDoubleDecimal(),
			RemainingQuantity = reader.ReadNullableDoubleDecimal(),
			AverageFillPrice = reader.ReadNullableDoubleDecimal(),
			LastFillPrice = reader.ReadNullableDoubleDecimal(),
			LastFillTime = FromUnixMilliseconds(reader.ReadInt64()),
			LastFillQuantity = reader.ReadNullableDoubleDecimal(),
			LastFillExecutionId = reader.ReadString(),
			TradeAccount = reader.ReadString(),
			InfoText = reader.ReadString(),
			IsNoOrders = reader.ReadByte() != 0,
		};
		reader.ReadString();
		reader.ReadString();
		result.OpenOrClose = (DtcOpenCloses)reader.ReadInt32();
		reader.ReadString();
		result.FreeFormText = reader.ReadString();
		result.OrderReceivedTime = FromUnixMilliseconds(reader.ReadInt64());
		result.LatestTransactionTime = FromUnixFractionalSeconds(reader.ReadDouble());
		reader.ReadString();
		return result;
	}

	private static DtcHistoricalFill DecodeHistoricalFill(byte[] data)
	{
		var reader = new DtcReader(data, true);
		var result = new DtcHistoricalFill
		{
			RequestId = reader.ReadInt32(),
			TotalMessages = reader.ReadInt32(),
			MessageNumber = reader.ReadInt32(),
			Symbol = reader.ReadString(),
			Exchange = reader.ReadString(),
			ServerOrderId = reader.ReadString(),
			Side = (DtcBuySells)reader.ReadInt32(),
			Price = reader.ReadDoubleDecimal(),
			Time = FromUnixSeconds(reader.ReadInt64()) ?? DateTime.UnixEpoch,
			Quantity = reader.ReadDoubleDecimal(),
			ExecutionId = reader.ReadString(),
			TradeAccount = reader.ReadString(),
		};
		reader.ReadInt32();
		result.IsNoFills = reader.ReadByte() != 0;
		result.InfoText = reader.ReadString();
		return result;
	}

	private static DtcPositionUpdate DecodePosition(byte[] data)
	{
		var reader = new DtcReader(data, true);
		return new()
		{
			RequestId = reader.ReadInt32(),
			TotalMessages = reader.ReadInt32(),
			MessageNumber = reader.ReadInt32(),
			Symbol = reader.ReadString(),
			Exchange = reader.ReadString(),
			Quantity = reader.ReadDoubleDecimal(),
			AveragePrice = reader.ReadDoubleDecimal(),
			PositionIdentifier = reader.ReadString(),
			TradeAccount = reader.ReadString(),
			IsNoPositions = reader.ReadByte() != 0,
			IsUnsolicited = reader.ReadByte() != 0,
			MarginRequirement = reader.ReadDoubleDecimal(),
			EntryTime = FromUnixSeconds(reader.ReadUInt32()),
			OpenProfitLoss = reader.ReadDoubleDecimal(),
		};
	}

	private static DtcTradeAccount DecodeTradeAccount(byte[] data)
	{
		var reader = new DtcReader(data, true);
		return new()
		{
			TotalMessages = reader.ReadInt32(),
			MessageNumber = reader.ReadInt32(),
			Account = reader.ReadString(),
			RequestId = reader.ReadInt32(),
			IsTradingDisabled = reader.ReadInt32() != 0,
		};
	}

	private static DtcAccountBalance DecodeAccountBalance(byte[] data)
	{
		var reader = new DtcReader(data, true);
		var result = new DtcAccountBalance
		{
			RequestId = reader.ReadInt32(),
			CashBalance = reader.ReadDoubleDecimal(),
			AvailableFunds = reader.ReadDoubleDecimal(),
			Currency = reader.ReadString(),
			TradeAccount = reader.ReadString(),
			SecuritiesValue = reader.ReadDoubleDecimal(),
			MarginRequirement = reader.ReadDoubleDecimal(),
			TotalMessages = reader.ReadInt32(),
			MessageNumber = reader.ReadInt32(),
			IsNoBalances = reader.ReadByte() != 0,
			IsUnsolicited = reader.ReadByte() != 0,
			OpenProfitLoss = reader.ReadDoubleDecimal(),
			DailyProfitLoss = reader.ReadDoubleDecimal(),
			InfoText = reader.ReadString(),
		};
		reader.ReadUInt64();
		reader.ReadDouble();
		reader.ReadDouble();
		reader.ReadByte();
		reader.ReadByte();
		reader.ReadByte();
		result.IsTradingDisabled = reader.ReadByte() != 0;
		reader.ReadString();
		reader.ReadByte();
		result.TransactionTime = FromUnixMicroseconds(reader.ReadInt64());
		return result;
	}

	private static DtcReject DecodeReject(byte[] data, DtcMessageTypes type)
	{
		var reader = new DtcReader(data, true);
		var result = new DtcReject(type);
		if (type is DtcMessageTypes.MarketDataReject or DtcMessageTypes.MarketDepthReject)
			result.SymbolId = reader.ReadUInt32();
		else
			result.RequestId = reader.ReadInt32();
		result.Text = reader.ReadString();
		if (type == DtcMessageTypes.HistoricalPriceDataReject)
		{
			result.ReasonCode = reader.ReadInt16();
			result.RetrySeconds = reader.ReadUInt16();
		}
		return result;
	}

	private static long ToUnixSeconds(DateTime time)
		=> checked((long)(time.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds);

	private static DateTime FromHistoricalTime(long value)
		=> Math.Abs(value) < 100_000_000_000L
			? FromUnixSeconds(value) ?? DateTime.UnixEpoch
			: FromUnixMicroseconds(value) ?? DateTime.UnixEpoch;

	private static DateTime? FromUnixSeconds(long value)
		=> value == 0 ? null : AddUnixTicks(value, TimeSpan.TicksPerSecond);

	private static DateTime? FromUnixFractionalSeconds(double value)
	{
		if (value == 0 || !double.IsFinite(value))
			return null;
		var ticks = value * TimeSpan.TicksPerSecond;
		if (ticks is < long.MinValue or > long.MaxValue)
			return null;
		return AddUnixTicks((long)Math.Round(ticks), 1);
	}

	private static DateTime? FromUnixMilliseconds(long value)
		=> value == 0 ? null : AddUnixTicks(value, TimeSpan.TicksPerMillisecond);

	private static DateTime? FromUnixMicroseconds(long value)
		=> value == 0 ? null : AddUnixTicks(value, 10);

	private static DateTime? AddUnixTicks(long value, long multiplier)
	{
		try
		{
			return DateTime.UnixEpoch.AddTicks(checked(value * multiplier));
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
		catch (OverflowException)
		{
			return null;
		}
	}

	private class DtcWriter
	{
		private readonly List<byte> _buffer = [];
		private int _maxAlignment = 2;

		public DtcWriter(DtcMessageTypes type)
		{
			WriteUInt16(0);
			WriteUInt16((ushort)type);
		}

		protected int Count => _buffer.Count;

		protected void Align(int alignment)
		{
			alignment = Math.Min(8, alignment);
			_maxAlignment = Math.Max(_maxAlignment, alignment);
			while (_buffer.Count % alignment != 0)
				_buffer.Add(0);
		}

		public void WriteByte(byte value)
		{
			Align(1);
			_buffer.Add(value);
		}

		public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

		public void WriteUInt16(ushort value)
		{
			Align(2);
			Span<byte> bytes = stackalloc byte[2];
			BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
			Add(bytes);
		}

		public void WriteInt32(int value)
		{
			Align(4);
			Span<byte> bytes = stackalloc byte[4];
			BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
			Add(bytes);
		}

		public void WriteUInt32(uint value)
		{
			Align(4);
			Span<byte> bytes = stackalloc byte[4];
			BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
			Add(bytes);
		}

		public void WriteInt64(long value)
		{
			Align(8);
			Span<byte> bytes = stackalloc byte[8];
			BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
			Add(bytes);
		}

		public void WriteDouble(double value)
			=> WriteInt64(BitConverter.DoubleToInt64Bits(value));

		public void WriteAscii(string value, int length)
		{
			Align(1);
			var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
			for (var i = 0; i < length; i++)
				_buffer.Add(i < bytes.Length ? bytes[i] : (byte)0);
		}

		protected void PatchUInt16(int offset, ushort value)
		{
			var bytes = new byte[2];
			BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
			_buffer[offset] = bytes[0];
			_buffer[offset + 1] = bytes[1];
		}

		protected void Add(ReadOnlySpan<byte> bytes)
		{
			foreach (var value in bytes)
				_buffer.Add(value);
		}

		protected virtual void BeforeComplete()
		{
			Align(_maxAlignment);
		}

		public virtual byte[] Complete()
		{
			BeforeComplete();
			if (_buffer.Count > MaxMessageLength)
				throw new InvalidOperationException("The encoded DTC message exceeds the 16-bit size field.");
			PatchUInt16(0, (ushort)_buffer.Count);
			return [.. _buffer];
		}

		protected List<byte> Buffer => _buffer;
	}

	private sealed class DtcVlsWriter : DtcWriter
	{
		private readonly List<(int Offset, byte[] Value)> _strings = [];

		public DtcVlsWriter(DtcMessageTypes type)
			: base(type)
		{
			WriteUInt16(0);
		}

		public void WriteString(string value)
		{
			Align(2);
			var position = Count;
			WriteUInt16(0);
			WriteUInt16(0);
			_strings.Add((position, value.IsEmpty() ? null : Encoding.ASCII.GetBytes(value)));
		}

		public override byte[] Complete()
		{
			BeforeComplete();
			if (Count > ushort.MaxValue)
				throw new InvalidOperationException("The DTC VLS base message exceeds the 16-bit size field.");
			PatchUInt16(4, (ushort)Count);

			foreach (var (fieldOffset, value) in _strings)
			{
				if (value == null)
					continue;
				var stringOffset = Buffer.Count;
				var stringLength = value.Length + 1;
				if (stringOffset > ushort.MaxValue || stringLength > ushort.MaxValue || stringOffset + stringLength > ushort.MaxValue)
					throw new InvalidOperationException("The encoded DTC VLS message exceeds the 16-bit offset or size field.");
				PatchUInt16(fieldOffset, (ushort)stringOffset);
				PatchUInt16(fieldOffset + 2, (ushort)stringLength);
				Buffer.AddRange(value);
				Buffer.Add(0);
			}

			if (Buffer.Count > MaxMessageLength)
				throw new InvalidOperationException("The encoded DTC VLS message exceeds the 16-bit size field.");
			PatchUInt16(0, (ushort)Buffer.Count);
			return [.. Buffer];
		}
	}

	private sealed class DtcReader
	{
		private readonly byte[] _data;
		private readonly int _fixedLimit;
		private readonly int _pack;
		private int _position;

		public DtcReader(byte[] data, bool isVls = false, bool isPacked = false)
		{
			_data = data;
			_pack = isPacked ? 1 : 8;
			if (isVls)
			{
				if (data.Length < 6)
					throw new InvalidDataException("A DTC VLS message must contain the base-size field.");
				_fixedLimit = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
				if (_fixedLimit is < 6 || _fixedLimit > data.Length)
					throw new InvalidDataException("The DTC VLS base-size field is invalid.");
				_position = 6;
			}
			else
			{
				_fixedLimit = data.Length;
				_position = 4;
			}
		}

		private bool Prepare(int size)
		{
			var alignment = Math.Min(_pack, size);
			if (alignment > 1)
				_position = (_position + alignment - 1) / alignment * alignment;
			if (_position + size <= _fixedLimit)
				return true;
			_position = _fixedLimit;
			return false;
		}

		public byte ReadByte()
		{
			if (!Prepare(1))
				return 0;
			return _data[_position++];
		}

		public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

		public short ReadInt16()
		{
			if (!Prepare(2))
				return 0;
			var value = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(_position));
			_position += 2;
			return value;
		}

		public ushort ReadUInt16()
		{
			if (!Prepare(2))
				return 0;
			var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_position));
			_position += 2;
			return value;
		}

		public int ReadInt32()
		{
			if (!Prepare(4))
				return 0;
			var value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_position));
			_position += 4;
			return value;
		}

		public uint ReadUInt32()
		{
			if (!Prepare(4))
				return 0;
			var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_position));
			_position += 4;
			return value;
		}

		public long ReadInt64()
		{
			if (!Prepare(8))
				return 0;
			var value = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(_position));
			_position += 8;
			return value;
		}

		public ulong ReadUInt64()
		{
			if (!Prepare(8))
				return 0;
			var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_position));
			_position += 8;
			return value;
		}

		public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

		public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

		public decimal ReadSingleDecimal() => ToDecimal(ReadSingle()) ?? 0;

		public decimal ReadDoubleDecimal() => ToDecimal(ReadDouble()) ?? 0;

		public decimal? ReadNullableSingleDecimal(bool sentinelIsNull = true)
			=> ToDecimal(ReadSingle(), sentinelIsNull);

		public decimal? ReadNullableDoubleDecimal(bool sentinelIsNull = true)
			=> ToDecimal(ReadDouble(), sentinelIsNull);

		public long? ReadNullableUInt32()
		{
			var value = ReadUInt32();
			return value == uint.MaxValue ? null : value;
		}

		public string ReadAscii(int length)
		{
			if (!Prepare(length))
				return string.Empty;
			var span = _data.AsSpan(_position, length);
			_position += length;
			var zero = span.IndexOf((byte)0);
			if (zero >= 0)
				span = span[..zero];
			return Encoding.ASCII.GetString(span);
		}

		public string ReadString()
		{
			var offset = ReadUInt16();
			var length = ReadUInt16();
			if (offset == 0 && length == 0)
				return string.Empty;
			if (length == 0 || offset < _fixedLimit || offset + length > _data.Length)
				throw new InvalidDataException("The DTC variable-length string points outside the message.");
			var actualLength = _data[offset + length - 1] == 0 ? length - 1 : length;
			return Encoding.ASCII.GetString(_data, offset, actualLength);
		}

		private static decimal? ToDecimal(double value, bool sentinelIsNull = true)
		{
			if (!double.IsFinite(value) || sentinelIsNull && value is double.MaxValue or float.MaxValue)
				return null;
			if (value > (double)decimal.MaxValue || value < (double)decimal.MinValue)
				return null;
			return (decimal)value;
		}
	}
}
