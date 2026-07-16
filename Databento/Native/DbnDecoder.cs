namespace StockSharp.Databento.Native;

internal sealed class DbnDecoder
{
	private const int _preludeLength = 8;
	private const int _recordHeaderLength = 16;
	private const int _maximumMetadataLength = 64 * 1024 * 1024;
	private const int _maximumRecordLength = 4096;
	private const ulong _undefinedTimestamp = ulong.MaxValue;

	public DbnMetadata Metadata { get; private set; }

	public async Task<DbnMetadata> ReadMetadata(Stream stream, CancellationToken cancellationToken)
	{
		if (stream == null)
			throw new ArgumentNullException(nameof(stream));

		var prelude = new byte[_preludeLength];
		await ReadExactly(stream, prelude, cancellationToken);
		if (prelude[0] != (byte)'D' || prelude[1] != (byte)'B' || prelude[2] != (byte)'N')
			throw new InvalidDataException("The response does not start with a DBN header.");

		var version = prelude[3];
		if (version is < 1 or > 3)
			throw new NotSupportedException($"DBN version {version} is not supported.");

		var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(prelude.AsSpan(4)));
		if (length is < 100 or > _maximumMetadataLength)
			throw new InvalidDataException($"Invalid DBN metadata length {length}.");

		var data = new byte[length];
		await ReadExactly(stream, data, cancellationToken);
		Metadata = DecodeMetadata(version, data);
		return Metadata;
	}

	public async IAsyncEnumerable<DbnRecord> ReadRecords(Stream stream,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (Metadata == null)
			throw new InvalidOperationException("Read the DBN metadata before reading records.");

		while (true)
		{
			var record = await ReadRecord(stream, cancellationToken);
			if (record == null)
				yield break;
			yield return record;
		}
	}

	public async Task<DbnRecord> ReadRecord(Stream stream, CancellationToken cancellationToken)
	{
		if (Metadata == null)
			throw new InvalidOperationException("Read the DBN metadata before reading records.");

		var first = new byte[1];
		if (!await ReadExactlyOrEof(stream, first, cancellationToken))
			return null;

		var length = first[0] * 4;
		if (length is < _recordHeaderLength or > _maximumRecordLength)
			throw new InvalidDataException($"Invalid DBN record length {length}.");

		var data = new byte[length];
		data[0] = first[0];
		await ReadExactly(stream, data.AsMemory(1), cancellationToken);
		return DecodeRecord(data, Metadata.Version);
	}

	private static DbnMetadata DecodeMetadata(byte version, byte[] data)
	{
		var pos = 0;
		var dataset = ReadString(data, ref pos, 16);
		var rawSchema = ReadUInt16(data, ref pos);
		var start = ReadUInt64(data, ref pos);
		var end = ReadUInt64(data, ref pos);
		var limit = ReadUInt64(data, ref pos);
		if (version == 1)
			pos += 8;
		Ensure(data, pos, 3);
		var inputSymbology = data[pos++];
		var outputSymbology = data[pos++];
		var hasOutputTimestamp = data[pos++] != 0;
		var symbolLength = version == 1 ? 22 : ReadUInt16(data, ref pos);
		pos += version == 1 ? 47 : 53;
		var schemaDefinitionLength = checked((int)ReadUInt32(data, ref pos));
		Ensure(data, pos, schemaDefinitionLength);
		pos += schemaDefinitionLength;

		var symbols = ReadStrings(data, ref pos, symbolLength);
		var partial = ReadStrings(data, ref pos, symbolLength);
		var notFound = ReadStrings(data, ref pos, symbolLength);
		var mappings = ReadMappings(data, ref pos, symbolLength);

		return new()
		{
			Version = version,
			Dataset = dataset,
			Schema = rawSchema == ushort.MaxValue ? null : (DbnSchemas)rawSchema,
			Start = start,
			End = end == _undefinedTimestamp ? null : end,
			Limit = limit == 0 ? null : limit,
			InputSymbology = inputSymbology == byte.MaxValue ? null : inputSymbology,
			OutputSymbology = outputSymbology,
			HasOutputTimestamp = hasOutputTimestamp,
			SymbolLength = symbolLength,
			Symbols = symbols,
			PartialSymbols = partial,
			NotFoundSymbols = notFound,
			Mappings = mappings,
		};
	}

	private static DbnRecord DecodeRecord(byte[] data, byte version)
	{
		var header = new DbnRecordHeader
		{
			Length = data.Length,
			Type = (DbnRecordTypes)data[1],
			PublisherId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)),
			InstrumentId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)),
			EventTimestamp = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8)),
		};

		return header.Type switch
		{
			DbnRecordTypes.Trades when data.Length >= 48 => DecodeTrade(header, data),
			DbnRecordTypes.Mbp1 when data.Length >= 80 => DecodeMbp1(header, data),
			DbnRecordTypes.Mbp10 when data.Length >= 80 => DecodeMbp10(header, data),
			DbnRecordTypes.Mbo when data.Length >= 56 => DecodeMbo(header, data),
			DbnRecordTypes.OhlcvDeprecated or DbnRecordTypes.Ohlcv1Second or
				DbnRecordTypes.Ohlcv1Minute or DbnRecordTypes.Ohlcv1Hour or
				DbnRecordTypes.Ohlcv1Day or DbnRecordTypes.OhlcvEndOfDay when data.Length >= 56
				=> DecodeOhlcv(header, data),
			DbnRecordTypes.Status when data.Length >= 40 => DecodeStatus(header, data),
			DbnRecordTypes.Statistics when data.Length >= 64 => DecodeStatistics(header, data, version),
			DbnRecordTypes.Imbalance when data.Length >= 112 => DecodeImbalance(header, data),
			DbnRecordTypes.InstrumentDefinition => DecodeDefinition(header, data, version),
			DbnRecordTypes.SymbolMapping => DecodeSymbolMapping(header, data, version),
			DbnRecordTypes.System when data.Length >= 80 => DecodeSystem(header, data, version),
			DbnRecordTypes.Error when data.Length >= 80 => DecodeError(header, data, version),
			_ => new DbnUnknownRecord { Header = header },
		};
	}

	private static DbnTradeRecord DecodeTrade(DbnRecordHeader header, byte[] data)
	{
		var common = DecodeMarketByPrice(data);
		return new()
		{
			Header = header,
			Price = common.Price,
			Size = common.Size,
			Action = common.Action,
			Side = common.Side,
			Flags = common.Flags,
			Depth = common.Depth,
			ReceiveTimestamp = common.ReceiveTimestamp,
			InTimestampDelta = common.InTimestampDelta,
			Sequence = common.Sequence,
		};
	}

	private static DbnMbp1Record DecodeMbp1(DbnRecordHeader header, byte[] data)
	{
		var common = DecodeMarketByPrice(data);
		return new()
		{
			Header = header,
			Price = common.Price,
			Size = common.Size,
			Action = common.Action,
			Side = common.Side,
			Flags = common.Flags,
			Depth = common.Depth,
			ReceiveTimestamp = common.ReceiveTimestamp,
			InTimestampDelta = common.InTimestampDelta,
			Sequence = common.Sequence,
			Level = DecodePair(data, 48),
		};
	}

	private static DbnMbp10Record DecodeMbp10(DbnRecordHeader header, byte[] data)
	{
		var common = DecodeMarketByPrice(data);
		var count = Math.Min(10, (data.Length - 48) / 32);
		var levels = new DbnBidAskPair[count];
		for (var i = 0; i < count; i++)
			levels[i] = DecodePair(data, 48 + i * 32);

		return new()
		{
			Header = header,
			Price = common.Price,
			Size = common.Size,
			Action = common.Action,
			Side = common.Side,
			Flags = common.Flags,
			Depth = common.Depth,
			ReceiveTimestamp = common.ReceiveTimestamp,
			InTimestampDelta = common.InTimestampDelta,
			Sequence = common.Sequence,
			Levels = levels,
		};
	}

	private static DbnMarketByPriceRecord DecodeMarketByPrice(byte[] data)
		=> new DbnTradeRecord
		{
			Price = ReadInt64(data, 16),
			Size = ReadUInt32(data, 24),
			Action = data[28],
			Side = data[29],
			Flags = data[30],
			Depth = data[31],
			ReceiveTimestamp = ReadUInt64(data, 32),
			InTimestampDelta = ReadInt32(data, 40),
			Sequence = ReadUInt32(data, 44),
		};

	private static DbnBidAskPair DecodePair(byte[] data, int offset)
		=> new()
		{
			BidPrice = ReadInt64(data, offset),
			AskPrice = ReadInt64(data, offset + 8),
			BidSize = ReadUInt32(data, offset + 16),
			AskSize = ReadUInt32(data, offset + 20),
			BidCount = ReadUInt32(data, offset + 24),
			AskCount = ReadUInt32(data, offset + 28),
		};

	private static DbnMboRecord DecodeMbo(DbnRecordHeader header, byte[] data)
		=> new()
		{
			Header = header,
			OrderId = ReadUInt64(data, 16),
			Price = ReadInt64(data, 24),
			Size = ReadUInt32(data, 32),
			Flags = data[36],
			ChannelId = data[37],
			Action = data[38],
			Side = data[39],
			ReceiveTimestamp = ReadUInt64(data, 40),
			InTimestampDelta = ReadInt32(data, 48),
			Sequence = ReadUInt32(data, 52),
		};

	private static DbnOhlcvRecord DecodeOhlcv(DbnRecordHeader header, byte[] data)
		=> new()
		{
			Header = header,
			Open = ReadInt64(data, 16),
			High = ReadInt64(data, 24),
			Low = ReadInt64(data, 32),
			Close = ReadInt64(data, 40),
			Volume = ReadUInt64(data, 48),
		};

	private static DbnStatusRecord DecodeStatus(DbnRecordHeader header, byte[] data)
		=> new()
		{
			Header = header,
			ReceiveTimestamp = ReadUInt64(data, 16),
			Action = (DbnStatusActions)ReadUInt16(data, 24),
			Reason = ReadUInt16(data, 26),
			TradingEvent = ReadUInt16(data, 28),
			IsTrading = data[30],
			IsQuoting = data[31],
			IsShortSaleRestricted = data[32],
		};

	private static DbnStatisticsRecord DecodeStatistics(DbnRecordHeader header, byte[] data,
		byte version)
	{
		if (version < 3 || data.Length < 80)
		{
			return new()
			{
				Header = header,
				ReceiveTimestamp = ReadUInt64(data, 16),
				ReferenceTimestamp = ReadUInt64(data, 24),
				Price = ReadInt64(data, 32),
				Quantity = ReadInt32(data, 40),
				Sequence = ReadUInt32(data, 44),
				InTimestampDelta = ReadInt32(data, 48),
				StatisticType = (DbnStatisticTypes)ReadUInt16(data, 52),
				ChannelId = ReadUInt16(data, 54),
				UpdateAction = data[56],
				Flags = data[57],
			};
		}

		return new()
		{
			Header = header,
			ReceiveTimestamp = ReadUInt64(data, 16),
			ReferenceTimestamp = ReadUInt64(data, 24),
			Price = ReadInt64(data, 32),
			Quantity = ReadInt64(data, 40),
			Sequence = ReadUInt32(data, 48),
			InTimestampDelta = ReadInt32(data, 52),
			StatisticType = (DbnStatisticTypes)ReadUInt16(data, 56),
			ChannelId = ReadUInt16(data, 58),
			UpdateAction = data[60],
			Flags = data[61],
		};
	}

	private static DbnImbalanceRecord DecodeImbalance(DbnRecordHeader header, byte[] data)
		=> new()
		{
			Header = header,
			ReceiveTimestamp = ReadUInt64(data, 16),
			ReferencePrice = ReadInt64(data, 24),
			AuctionTimestamp = ReadUInt64(data, 32),
			IndicativeMatchPrice = ReadInt64(data, 64),
			PairedQuantity = ReadUInt32(data, 88),
			TotalImbalanceQuantity = ReadUInt32(data, 92),
			Side = data[105],
		};

	private static DbnRecord DecodeDefinition(DbnRecordHeader header, byte[] data, byte version)
	{
		return version switch
		{
			1 when data.Length >= 360 => DecodeDefinitionV1(header, data),
			2 when data.Length >= 400 => DecodeDefinitionV2(header, data),
			3 when data.Length >= 520 => DecodeDefinitionV3(header, data),
			_ => new DbnUnknownRecord { Header = header },
		};
	}

	private static DbnInstrumentDefinitionRecord DecodeDefinitionV1(
		DbnRecordHeader header, byte[] data)
		=> new()
		{
			Header = header,
			ReceiveTimestamp = ReadUInt64(data, 16),
			MinimumPriceIncrement = ReadInt64(data, 24),
			DisplayFactor = ReadInt64(data, 32),
			Expiration = ReadUInt64(data, 40),
			Activation = ReadUInt64(data, 48),
			HighLimitPrice = ReadInt64(data, 56),
			LowLimitPrice = ReadInt64(data, 64),
			UnitOfMeasureQuantity = ReadInt64(data, 88),
			StrikePrice = ReadInt64(data, 328),
			RawInstrumentId = ReadUInt32(data, 120),
			UnderlyingId = ReadUInt32(data, 116),
			MinimumLotSize = ReadInt32(data, 140),
			MinimumRoundLotSize = ReadInt32(data, 148),
			ContractMultiplier = ReadInt32(data, 160),
			OriginalContractSize = ReadInt32(data, 168),
			MaturityYear = ReadUInt16(data, 180),
			Currency = ReadString(data, 186, 4),
			SettlementCurrency = ReadString(data, 190, 4),
			SecuritySubtype = ReadString(data, 194, 6),
			RawSymbol = ReadString(data, 200, 22),
			Group = ReadString(data, 222, 21),
			Exchange = ReadString(data, 243, 5),
			Asset = ReadString(data, 248, 7),
			Cfi = ReadString(data, 255, 7),
			SecurityType = ReadString(data, 262, 7),
			UnitOfMeasure = ReadString(data, 269, 31),
			Underlying = ReadString(data, 300, 21),
			StrikePriceCurrency = ReadString(data, 321, 4),
			InstrumentClass = data[325],
			SecurityUpdateAction = data[349],
			MaturityMonth = data[350],
			MaturityDay = data[351],
			MaturityWeek = data[352],
		};

	private static DbnInstrumentDefinitionRecord DecodeDefinitionV2(
		DbnRecordHeader header, byte[] data)
		=> new()
		{
			Header = header,
			ReceiveTimestamp = ReadUInt64(data, 16),
			MinimumPriceIncrement = ReadInt64(data, 24),
			DisplayFactor = ReadInt64(data, 32),
			Expiration = ReadUInt64(data, 40),
			Activation = ReadUInt64(data, 48),
			HighLimitPrice = ReadInt64(data, 56),
			LowLimitPrice = ReadInt64(data, 64),
			UnitOfMeasureQuantity = ReadInt64(data, 88),
			StrikePrice = ReadInt64(data, 112),
			RawInstrumentId = ReadUInt32(data, 128),
			UnderlyingId = ReadUInt32(data, 124),
			MinimumLotSize = ReadInt32(data, 148),
			MinimumRoundLotSize = ReadInt32(data, 156),
			ContractMultiplier = ReadInt32(data, 164),
			OriginalContractSize = ReadInt32(data, 172),
			MaturityYear = ReadUInt16(data, 180),
			Currency = ReadString(data, 186, 4),
			SettlementCurrency = ReadString(data, 190, 4),
			SecuritySubtype = ReadString(data, 194, 6),
			RawSymbol = ReadString(data, 200, 71),
			Group = ReadString(data, 271, 21),
			Exchange = ReadString(data, 292, 5),
			Asset = ReadString(data, 297, 7),
			Cfi = ReadString(data, 304, 7),
			SecurityType = ReadString(data, 311, 7),
			UnitOfMeasure = ReadString(data, 318, 31),
			Underlying = ReadString(data, 349, 21),
			StrikePriceCurrency = ReadString(data, 370, 4),
			InstrumentClass = data[374],
			SecurityUpdateAction = data[382],
			MaturityMonth = data[383],
			MaturityDay = data[384],
			MaturityWeek = data[385],
		};

	private static DbnInstrumentDefinitionRecord DecodeDefinitionV3(
		DbnRecordHeader header, byte[] data)
		=> new()
		{
			Header = header,
			ReceiveTimestamp = ReadUInt64(data, 16),
			MinimumPriceIncrement = ReadInt64(data, 24),
			DisplayFactor = ReadInt64(data, 32),
			Expiration = ReadUInt64(data, 40),
			Activation = ReadUInt64(data, 48),
			HighLimitPrice = ReadInt64(data, 56),
			LowLimitPrice = ReadInt64(data, 64),
			UnitOfMeasureQuantity = ReadInt64(data, 80),
			StrikePrice = ReadInt64(data, 104),
			RawInstrumentId = ReadUInt64(data, 112),
			UnderlyingId = ReadUInt32(data, 140),
			MinimumLotSize = ReadInt32(data, 160),
			MinimumRoundLotSize = ReadInt32(data, 168),
			ContractMultiplier = ReadInt32(data, 176),
			OriginalContractSize = ReadInt32(data, 184),
			MaturityYear = ReadUInt16(data, 214),
			Currency = ReadString(data, 224, 4),
			SettlementCurrency = ReadString(data, 228, 4),
			SecuritySubtype = ReadString(data, 232, 6),
			RawSymbol = ReadString(data, 238, 71),
			Group = ReadString(data, 309, 21),
			Exchange = ReadString(data, 330, 5),
			Asset = ReadString(data, 335, 11),
			Cfi = ReadString(data, 346, 7),
			SecurityType = ReadString(data, 353, 7),
			UnitOfMeasure = ReadString(data, 360, 31),
			Underlying = ReadString(data, 391, 21),
			StrikePriceCurrency = ReadString(data, 412, 4),
			InstrumentClass = data[487],
			SecurityUpdateAction = data[493],
			MaturityMonth = data[494],
			MaturityDay = data[495],
			MaturityWeek = data[496],
		};

	private static DbnRecord DecodeSymbolMapping(DbnRecordHeader header, byte[] data, byte version)
	{
		if (version == 1 && data.Length >= 80)
		{
			return new DbnSymbolMappingRecord
			{
				Header = header,
				InputSymbol = ReadString(data, 16, 22),
				OutputSymbol = ReadString(data, 38, 22),
				StartTimestamp = ReadUInt64(data, 64),
				EndTimestamp = ReadUInt64(data, 72),
			};
		}

		if (data.Length < 176)
			return new DbnUnknownRecord { Header = header };

		return new DbnSymbolMappingRecord
		{
			Header = header,
			InputSymbology = data[16],
			InputSymbol = ReadString(data, 17, 71),
			OutputSymbology = data[88],
			OutputSymbol = ReadString(data, 89, 71),
			StartTimestamp = ReadUInt64(data, 160),
			EndTimestamp = ReadUInt64(data, 168),
		};
	}

	private static DbnSystemRecord DecodeSystem(DbnRecordHeader header, byte[] data, byte version)
		=> new()
		{
			Header = header,
			Message = ReadString(data, 16, version == 1 ? 64 : Math.Min(303, data.Length - 17)),
			Code = version == 1 ? DbnSystemCodes.Unset : (DbnSystemCodes)data[319],
		};

	private static DbnErrorRecord DecodeError(DbnRecordHeader header, byte[] data, byte version)
		=> new()
		{
			Header = header,
			Message = ReadString(data, 16, version == 1 ? 64 : Math.Min(302, data.Length - 18)),
			Code = version == 1 ? byte.MaxValue : data[318],
			IsLast = version != 1 && data[319] != 0,
		};

	private static string[] ReadStrings(byte[] data, ref int pos, int symbolLength)
	{
		var count = checked((int)ReadUInt32(data, ref pos));
		if (count > data.Length / Math.Max(1, symbolLength))
			throw new InvalidDataException("Invalid DBN metadata symbol count.");
		var result = new string[count];
		for (var i = 0; i < count; i++)
			result[i] = ReadString(data, ref pos, symbolLength);
		return result;
	}

	private static DbnMetadataMapping[] ReadMappings(byte[] data, ref int pos, int symbolLength)
	{
		var count = checked((int)ReadUInt32(data, ref pos));
		if (count > data.Length / Math.Max(1, symbolLength + 4))
			throw new InvalidDataException("Invalid DBN metadata mapping count.");
		var result = new DbnMetadataMapping[count];
		for (var i = 0; i < count; i++)
		{
			var rawSymbol = ReadString(data, ref pos, symbolLength);
			var intervalCount = checked((int)ReadUInt32(data, ref pos));
			if (intervalCount > data.Length / Math.Max(1, symbolLength + 8))
				throw new InvalidDataException("Invalid DBN metadata interval count.");
			var intervals = new DbnMappingInterval[intervalCount];
			for (var j = 0; j < intervalCount; j++)
			{
				intervals[j] = new()
				{
					StartDate = ReadUInt32(data, ref pos),
					EndDate = ReadUInt32(data, ref pos),
					Symbol = ReadString(data, ref pos, symbolLength),
				};
			}
			result[i] = new() { RawSymbol = rawSymbol, Intervals = intervals };
		}
		return result;
	}

	private static string ReadString(byte[] data, ref int pos, int length)
	{
		var result = ReadString(data, pos, length);
		pos += length;
		return result;
	}

	private static string ReadString(byte[] data, int offset, int length)
	{
		Ensure(data, offset, length);
		var span = data.AsSpan(offset, length);
		var terminator = span.IndexOf((byte)0);
		if (terminator >= 0)
			span = span[..terminator];
		return Encoding.UTF8.GetString(span);
	}

	private static ushort ReadUInt16(byte[] data, ref int pos)
	{
		var result = ReadUInt16(data, pos);
		pos += 2;
		return result;
	}

	private static ushort ReadUInt16(byte[] data, int offset)
	{
		Ensure(data, offset, 2);
		return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
	}

	private static uint ReadUInt32(byte[] data, ref int pos)
	{
		var result = ReadUInt32(data, pos);
		pos += 4;
		return result;
	}

	private static uint ReadUInt32(byte[] data, int offset)
	{
		Ensure(data, offset, 4);
		return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
	}

	private static int ReadInt32(byte[] data, int offset)
	{
		Ensure(data, offset, 4);
		return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
	}

	private static ulong ReadUInt64(byte[] data, ref int pos)
	{
		var result = ReadUInt64(data, pos);
		pos += 8;
		return result;
	}

	private static ulong ReadUInt64(byte[] data, int offset)
	{
		Ensure(data, offset, 8);
		return BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
	}

	private static long ReadInt64(byte[] data, int offset)
	{
		Ensure(data, offset, 8);
		return BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset));
	}

	private static void Ensure(byte[] data, int offset, int length)
	{
		if (offset < 0 || length < 0 || offset > data.Length - length)
			throw new InvalidDataException("Unexpected end of DBN data.");
	}

	private static async Task ReadExactly(Stream stream, byte[] buffer,
		CancellationToken cancellationToken)
		=> await ReadExactly(stream, buffer.AsMemory(), cancellationToken);

	private static async Task ReadExactly(Stream stream, Memory<byte> buffer,
		CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
			if (read == 0)
				throw new EndOfStreamException("The DBN stream ended inside a message.");
			offset += read;
		}
	}

	private static async Task<bool> ReadExactlyOrEof(Stream stream, Memory<byte> buffer,
		CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
			if (read == 0)
			{
				if (offset == 0)
					return false;
				throw new EndOfStreamException("The DBN stream ended inside a message.");
			}
			offset += read;
		}
		return true;
	}
}
