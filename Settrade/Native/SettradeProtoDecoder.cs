namespace StockSharp.Settrade.Native;

static class SettradeProtoDecoder
{
	private ref struct Reader
	{
		private ReadOnlySpan<byte> _data;
		private int _offset;

		public Reader(ReadOnlySpan<byte> data)
		{
			_data = data;
			_offset = 0;
		}

		public bool End => _offset >= _data.Length;

		public (int Field, int Wire) ReadTag()
		{
			var tag = ReadVarInt();
			if (tag == 0)
				throw new InvalidDataException(
					"Settrade protobuf contains an empty tag.");
			return ((int)(tag >> 3), (int)(tag & 7));
		}

		public ulong ReadVarInt()
		{
			ulong result = 0;
			for (var shift = 0; shift < 64; shift += 7)
			{
				if (_offset >= _data.Length)
					throw new EndOfStreamException(
						"Unexpected end of Settrade protobuf varint.");
				var value = _data[_offset++];
				result |= (ulong)(value & 0x7f) << shift;
				if ((value & 0x80) == 0)
					return result;
			}
			throw new InvalidDataException(
				"Settrade protobuf varint is too long.");
		}

		public ReadOnlySpan<byte> ReadBytes()
		{
			var length = checked((int)ReadVarInt());
			if (length < 0 || _offset + length > _data.Length)
				throw new EndOfStreamException(
					"Unexpected end of Settrade protobuf field.");
			var result = _data.Slice(_offset, length);
			_offset += length;
			return result;
		}

		public string ReadString()
			=> Encoding.UTF8.GetString(ReadBytes());

		public void Skip(int wire)
		{
			switch (wire)
			{
				case 0:
					ReadVarInt();
					break;
				case 1:
					SkipBytes(8);
					break;
				case 2:
					SkipBytes(checked((int)ReadVarInt()));
					break;
				case 5:
					SkipBytes(4);
					break;
				default:
					throw new InvalidDataException(
						$"Unsupported Settrade protobuf wire type {wire}.");
			}
		}

		private void SkipBytes(int length)
		{
			if (length < 0 || _offset + length > _data.Length)
				throw new EndOfStreamException(
					"Unexpected end of Settrade protobuf field.");
			_offset += length;
		}
	}

	public static SettradeLevel1 DecodeLevel1(ReadOnlySpan<byte> payload)
	{
		var reader = new Reader(payload);
		string symbol = null;
		decimal? projected = null;
		decimal? high = null;
		decimal? low = null;
		decimal? last = null;
		decimal? change = null;
		decimal? volume = null;
		decimal? value = null;
		var status = 0;
		while (!reader.End)
		{
			var (field, wire) = reader.ReadTag();
			switch (field)
			{
				case 1 when wire == 2:
					symbol = reader.ReadString();
					break;
				case 2 when wire == 2:
					high = DecodeMoney(reader.ReadBytes());
					break;
				case 3 when wire == 2:
					low = DecodeMoney(reader.ReadBytes());
					break;
				case 4 when wire == 2:
					last = DecodeMoney(reader.ReadBytes());
					break;
				case 5 when wire == 0:
					volume = reader.ReadVarInt();
					break;
				case 6 when wire == 2:
					projected = DecodeMoney(reader.ReadBytes());
					break;
				case 7 when wire == 2:
					change = DecodeMoney(reader.ReadBytes());
					break;
				case 8 when wire == 2:
					value = DecodeMoney(reader.ReadBytes());
					break;
				case 9 when wire == 0:
					status = checked((int)reader.ReadVarInt());
					break;
				default:
					reader.Skip(wire);
					break;
			}
		}
		return new()
		{
			Symbol = symbol,
			ProjectedOpenPrice = projected,
			High = high,
			Low = low,
			Last = last,
			Change = change,
			TotalVolume = volume,
			TotalValue = value,
			MarketStatus = status,
		};
	}

	public static SettradeOrderBook DecodeOrderBook(
		ReadOnlySpan<byte> payload)
	{
		var reader = new Reader(payload);
		string symbol = null;
		var bidPrices = new decimal?[10];
		var askPrices = new decimal?[10];
		var bidVolumes = new decimal[10];
		var askVolumes = new decimal[10];
		while (!reader.End)
		{
			var (field, wire) = reader.ReadTag();
			if (field == 1 && wire == 2)
			{
				symbol = reader.ReadString();
				continue;
			}
			var bidPriceIndex = ToPriceIndex(field, true);
			if (bidPriceIndex >= 0 && wire == 2)
			{
				bidPrices[bidPriceIndex] =
					DecodeMoney(reader.ReadBytes());
				continue;
			}
			var askPriceIndex = ToPriceIndex(field, false);
			if (askPriceIndex >= 0 && wire == 2)
			{
				askPrices[askPriceIndex] =
					DecodeMoney(reader.ReadBytes());
				continue;
			}
			var bidVolumeIndex = ToVolumeIndex(field, true);
			if (bidVolumeIndex >= 0 && wire == 0)
			{
				bidVolumes[bidVolumeIndex] = reader.ReadVarInt();
				continue;
			}
			var askVolumeIndex = ToVolumeIndex(field, false);
			if (askVolumeIndex >= 0 && wire == 0)
			{
				askVolumes[askVolumeIndex] = reader.ReadVarInt();
				continue;
			}
			reader.Skip(wire);
		}
		return new()
		{
			Symbol = symbol,
			Bids = Enumerable.Range(0, 10)
				.Where(index => bidPrices[index] is > 0 &&
					bidVolumes[index] > 0)
				.Select(index => new SettradeBookLevel(
					bidPrices[index].Value, bidVolumes[index]))
				.ToArray(),
			Asks = Enumerable.Range(0, 10)
				.Where(index => askPrices[index] is > 0 &&
					askVolumes[index] > 0)
				.Select(index => new SettradeBookLevel(
					askPrices[index].Value, askVolumes[index]))
				.ToArray(),
		};
	}

	private static int ToPriceIndex(int field, bool bid)
	{
		if (bid)
		{
			if (field is >= 2 and <= 6)
				return field - 2;
			if (field is >= 24 and <= 28)
				return field - 19;
		}
		else
		{
			if (field is >= 7 and <= 11)
				return field - 7;
			if (field is >= 29 and <= 33)
				return field - 24;
		}
		return -1;
	}

	private static int ToVolumeIndex(int field, bool bid)
	{
		if (bid)
		{
			if (field is >= 12 and <= 16)
				return field - 12;
			if (field is >= 34 and <= 38)
				return field - 29;
		}
		else
		{
			if (field is >= 17 and <= 21)
				return field - 17;
			if (field is >= 39 and <= 43)
				return field - 34;
		}
		return -1;
	}

	public static SettradeCandle DecodeCandle(
		ReadOnlySpan<byte> payload)
	{
		var reader = new Reader(payload);
		string symbol = null;
		string interval = null;
		long sequence = 0;
		var time = default(DateTime);
		decimal open = 0;
		decimal high = 0;
		decimal low = 0;
		decimal close = 0;
		decimal volume = 0;
		decimal turnover = 0;
		while (!reader.End)
		{
			var (field, wire) = reader.ReadTag();
			switch (field)
			{
				case 1 when wire == 2:
					symbol = reader.ReadString();
					break;
				case 2 when wire == 2:
					interval = reader.ReadString();
					break;
				case 3 when wire == 0:
					sequence = unchecked((long)reader.ReadVarInt());
					break;
				case 4 when wire == 2:
					time = DecodeTimestamp(reader.ReadBytes());
					break;
				case 5 when wire == 2:
					open = DecodeMoney(reader.ReadBytes());
					break;
				case 6 when wire == 2:
					high = DecodeMoney(reader.ReadBytes());
					break;
				case 7 when wire == 2:
					low = DecodeMoney(reader.ReadBytes());
					break;
				case 8 when wire == 2:
					close = DecodeMoney(reader.ReadBytes());
					break;
				case 9 when wire == 0:
					volume = reader.ReadVarInt();
					break;
				case 10 when wire == 2:
					turnover = DecodeMoney(reader.ReadBytes());
					break;
				default:
					reader.Skip(wire);
					break;
			}
		}
		return new()
		{
			Symbol = symbol,
			Interval = interval,
			Sequence = sequence,
			Time = time == default ? DateTime.UtcNow : time,
			Open = open,
			High = high,
			Low = low,
			Close = close,
			Volume = volume,
			Turnover = turnover,
		};
	}

	public static SettradeOrder DecodeEquityOrder(
		ReadOnlySpan<byte> payload)
		=> DecodeOrder(payload, false);

	public static SettradeOrder DecodeDerivativeOrder(
		ReadOnlySpan<byte> payload)
		=> DecodeOrder(payload, true);

	private static SettradeOrder DecodeOrder(ReadOnlySpan<byte> payload,
		bool derivative)
	{
		var reader = new Reader(payload);
		var version = 0;
		string orderNo = null;
		string account = null;
		string symbol = null;
		string status = null;
		string side = null;
		string position = null;
		string priceType = null;
		string validity = null;
		decimal price = 0;
		decimal volume = 0;
		decimal matched = 0;
		decimal balance = 0;
		decimal cancelled = 0;
		var time = default(DateTime);
		var tradeDate = default(DateTime);
		var canCancel = false;
		while (!reader.End)
		{
			var (field, wire) = reader.ReadTag();
			if (field == 1 && wire == 0)
				version = checked((int)reader.ReadVarInt());
			else if (field == 2 && wire == 2)
				orderNo = reader.ReadString();
			else if (field == 4 && wire == 2)
				account = reader.ReadString();
			else if (derivative && field == 6 && wire == 2)
				time = DecodeTimestamp(reader.ReadBytes());
			else if (!derivative && field == 5 && wire == 2)
				time = DecodeTimeOfDay(reader.ReadBytes());
			else if (!derivative && field == 6 && wire == 2)
				tradeDate = DecodeDate(reader.ReadBytes());
			else if (field == (derivative ? 7 : 7) && wire == 2)
				symbol = reader.ReadString();
			else if (field == (derivative ? 8 : 11) && wire == 0)
				side = DecodeSide(reader.ReadVarInt(), derivative);
			else if (derivative && field == 9 && wire == 0)
				position = DecodePosition(reader.ReadVarInt());
			else if (field == (derivative ? 10 : 9) && wire == 2)
				price = DecodeMoney(reader.ReadBytes());
			else if (field == (derivative ? 11 : 10) && wire == 0)
				priceType = DecodePriceType(reader.ReadVarInt());
			else if (field == 12 && wire == 0)
				volume = reader.ReadVarInt();
			else if (field == (derivative ? 14 : 13) && wire == 0)
				matched = reader.ReadVarInt();
			else if (field == (derivative ? 13 : 14) && wire == 0)
				balance = reader.ReadVarInt();
			else if (field == 15 && wire == 0)
				cancelled = reader.ReadVarInt();
			else if (field == (derivative ? 16 : 20) && wire == 0)
				validity = DecodeValidity(reader.ReadVarInt(), derivative);
			else if (field == (derivative ? 18 : 16) && wire == 2)
				status = reader.ReadString();
			else if (field == (derivative ? 19 : 17) && wire == 0)
				canCancel = reader.ReadVarInt() != 0;
			else
				reader.Skip(wire);
		}
		if (!derivative && tradeDate != default)
			time = tradeDate.Date + time.TimeOfDay;
		return new()
		{
			Version = version,
			OrderNo = orderNo,
			AccountNo = account,
			Symbol = symbol,
			Status = status,
			Side = side,
			Position = position,
			PriceType = priceType,
			Validity = validity,
			Price = price,
			Volume = volume,
			MatchedVolume = matched,
			BalanceVolume = balance,
			CancelledVolume = cancelled,
			Time = time == default ? DateTime.UtcNow : time,
			CanCancel = canCancel,
		};
	}

	private static decimal DecodeMoney(ReadOnlySpan<byte> payload)
	{
		var reader = new Reader(payload);
		long units = 0;
		int nanos = 0;
		while (!reader.End)
		{
			var (field, wire) = reader.ReadTag();
			if (field == 1 && wire == 0)
				units = unchecked((long)reader.ReadVarInt());
			else if (field == 2 && wire == 0)
				nanos = unchecked((int)reader.ReadVarInt());
			else
				reader.Skip(wire);
		}
		return units + nanos / 1_000_000_000m;
	}

	private static DateTime DecodeTimestamp(ReadOnlySpan<byte> payload)
	{
		var reader = new Reader(payload);
		long seconds = 0;
		var nanos = 0;
		while (!reader.End)
		{
			var (field, wire) = reader.ReadTag();
			if (field == 1 && wire == 0)
				seconds = unchecked((long)reader.ReadVarInt());
			else if (field == 2 && wire == 0)
				nanos = unchecked((int)reader.ReadVarInt());
			else
				reader.Skip(wire);
		}
		return DateTimeOffset.FromUnixTimeSeconds(seconds)
			.AddTicks(nanos / 100).UtcDateTime;
	}

	private static DateTime DecodeTimeOfDay(ReadOnlySpan<byte> payload)
	{
		var reader = new Reader(payload);
		var hour = 0;
		var minute = 0;
		var second = 0;
		var nanos = 0;
		while (!reader.End)
		{
			var (field, wire) = reader.ReadTag();
			if (wire != 0)
			{
				reader.Skip(wire);
				continue;
			}
			var value = checked((int)reader.ReadVarInt());
			switch (field)
			{
				case 1:
					hour = value;
					break;
				case 2:
					minute = value;
					break;
				case 3:
					second = value;
					break;
				case 4:
					nanos = value;
					break;
			}
		}
		return DateTime.UtcNow.Date
			.AddHours(hour)
			.AddMinutes(minute)
			.AddSeconds(second)
			.AddTicks(nanos / 100);
	}

	private static DateTime DecodeDate(ReadOnlySpan<byte> payload)
	{
		var reader = new Reader(payload);
		var year = 0;
		var month = 0;
		var day = 0;
		while (!reader.End)
		{
			var (field, wire) = reader.ReadTag();
			if (wire != 0)
			{
				reader.Skip(wire);
				continue;
			}
			var value = checked((int)reader.ReadVarInt());
			switch (field)
			{
				case 1:
					year = value;
					break;
				case 2:
					month = value;
					break;
				case 3:
					day = value;
					break;
			}
		}
		return year > 0 && month is >= 1 and <= 12 &&
			day >= 1 && day <= DateTime.DaysInMonth(year, month)
				? new DateTime(year, month, day, 0, 0, 0,
					DateTimeKind.Utc)
				: default;
	}

	private static string DecodeSide(ulong value, bool derivative)
		=> derivative
			? value switch
			{
				1 => "Long",
				2 => "Short",
				3 => "LongAndShort",
				_ => null,
			}
			: value switch
			{
				1 => "Buy",
				2 => "Sell",
				_ => null,
			};

	private static string DecodePosition(ulong value)
		=> value switch
		{
			1 => "Open",
			2 => "Close",
			3 => "Auto",
			_ => null,
		};

	private static string DecodePriceType(ulong value)
		=> value switch
		{
			1 => "Limit",
			2 => "ATO",
			3 => "ATC",
			4 => "MP-MKT",
			6 => "MP-MTL",
			_ => null,
		};

	private static string DecodeValidity(ulong value, bool derivative)
		=> value switch
		{
			1 => "FOK",
			2 => "IOC",
			3 => "Date",
			4 => "Cancel",
			5 when !derivative => "Day",
			8 when derivative => "Day",
			_ => null,
		};
}
