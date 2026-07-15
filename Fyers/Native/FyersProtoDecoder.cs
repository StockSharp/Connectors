namespace StockSharp.Fyers.Native;

static class FyersProtoDecoder
{
	public static FyersTbtPacket Decode(ReadOnlySpan<byte> data)
	{
		var reader = new ProtoReader(data);
		var packet = new FyersTbtPacket();
		var feeds = new List<FyersTbtFeed>();
		while (reader.TryReadField(out var field, out var wireType))
		{
			switch (field)
			{
				case 2 when wireType == 2:
					var feed = ReadFeedEntry(reader.ReadBytes());
					if (feed != null)
						feeds.Add(feed);
					break;
				case 3 when wireType == 0:
					packet.IsSnapshot = reader.ReadVarint() != 0;
					break;
				case 4 when wireType == 2:
					packet.Message = reader.ReadString();
					break;
				case 5 when wireType == 0:
					packet.IsError = reader.ReadVarint() != 0;
					break;
				default:
					reader.Skip(wireType);
					break;
			}
		}
		packet.Feeds = [.. feeds];
		return packet;
	}

	private static FyersTbtFeed ReadFeedEntry(ReadOnlySpan<byte> data)
	{
		var reader = new ProtoReader(data);
		FyersTbtFeed feed = null;
		while (reader.TryReadField(out var field, out var wireType))
		{
			if (field == 2 && wireType == 2)
				feed = ReadMarketFeed(reader.ReadBytes());
			else
				reader.Skip(wireType);
		}
		return feed;
	}

	private static FyersTbtFeed ReadMarketFeed(ReadOnlySpan<byte> data)
	{
		var reader = new ProtoReader(data);
		var feed = new FyersTbtFeed();
		while (reader.TryReadField(out var field, out var wireType))
		{
			switch (field)
			{
				case 5 when wireType == 2:
					feed.Depth = ReadDepth(reader.ReadBytes());
					break;
				case 6 when wireType == 2:
					feed.FeedTime = ReadWrappedUInt64(reader.ReadBytes());
					break;
				case 7 when wireType == 2:
					feed.SendTime = ReadWrappedUInt64(reader.ReadBytes());
					break;
				case 9 when wireType == 0:
					feed.SequenceNumber = reader.ReadVarint();
					break;
				case 10 when wireType == 0:
					feed.IsSnapshot = reader.ReadVarint() != 0;
					break;
				case 11 when wireType == 2:
					feed.Symbol = reader.ReadString();
					break;
				default:
					reader.Skip(wireType);
					break;
			}
		}
		return feed;
	}

	private static FyersTbtDepth ReadDepth(ReadOnlySpan<byte> data)
	{
		var reader = new ProtoReader(data);
		var depth = new FyersTbtDepth();
		var bids = new List<FyersTbtLevel>();
		var asks = new List<FyersTbtLevel>();
		while (reader.TryReadField(out var field, out var wireType))
		{
			switch (field)
			{
				case 1 when wireType == 2:
					depth.TotalBidQuantity = ReadWrappedUInt64(reader.ReadBytes());
					break;
				case 2 when wireType == 2:
					depth.TotalAskQuantity = ReadWrappedUInt64(reader.ReadBytes());
					break;
				case 3 when wireType == 2:
					asks.Add(ReadLevel(reader.ReadBytes()));
					break;
				case 4 when wireType == 2:
					bids.Add(ReadLevel(reader.ReadBytes()));
					break;
				default:
					reader.Skip(wireType);
					break;
			}
		}
		depth.Bids = [.. bids];
		depth.Asks = [.. asks];
		return depth;
	}

	private static FyersTbtLevel ReadLevel(ReadOnlySpan<byte> data)
	{
		var reader = new ProtoReader(data);
		var level = new FyersTbtLevel();
		while (reader.TryReadField(out var field, out var wireType))
		{
			if (wireType != 2)
			{
				reader.Skip(wireType);
				continue;
			}

			var wrapped = reader.ReadBytes();
			switch (field)
			{
				case 1:
					level.Price = unchecked((long)ReadWrappedUInt64(wrapped));
					break;
				case 2:
					level.Quantity = (uint)ReadWrappedUInt64(wrapped);
					break;
				case 3:
					level.OrdersCount = (uint)ReadWrappedUInt64(wrapped);
					break;
				case 4:
					level.Number = (uint)ReadWrappedUInt64(wrapped);
					break;
			}
		}
		return level;
	}

	private static ulong ReadWrappedUInt64(ReadOnlySpan<byte> data)
	{
		var reader = new ProtoReader(data);
		while (reader.TryReadField(out var field, out var wireType))
		{
			if (field == 1 && wireType == 0)
				return reader.ReadVarint();
			reader.Skip(wireType);
		}
		return 0;
	}

	private ref struct ProtoReader
	{
		private readonly ReadOnlySpan<byte> _data;
		private int _offset;

		public ProtoReader(ReadOnlySpan<byte> data)
		{
			_data = data;
			_offset = 0;
		}

		public bool TryReadField(out int field, out int wireType)
		{
			if (_offset >= _data.Length)
			{
				field = 0;
				wireType = 0;
				return false;
			}

			var tag = ReadVarint();
			field = (int)(tag >> 3);
			wireType = (int)(tag & 7);
			if (field <= 0)
				throw new InvalidDataException("FYERS TBT packet contains an invalid Protobuf field number.");
			return true;
		}

		public ulong ReadVarint()
		{
			ulong value = 0;
			for (var shift = 0; shift < 64; shift += 7)
			{
				Ensure(1);
				var current = _data[_offset++];
				value |= (ulong)(current & 0x7f) << shift;
				if ((current & 0x80) == 0)
					return value;
			}
			throw new InvalidDataException("FYERS TBT packet contains an oversized Protobuf varint.");
		}

		public ReadOnlySpan<byte> ReadBytes()
		{
			var length = checked((int)ReadVarint());
			Ensure(length);
			var value = _data.Slice(_offset, length);
			_offset += length;
			return value;
		}

		public string ReadString() => Encoding.UTF8.GetString(ReadBytes());

		public void Skip(int wireType)
		{
			switch (wireType)
			{
				case 0:
					ReadVarint();
					break;
				case 1:
					Ensure(8);
					_offset += 8;
					break;
				case 2:
					ReadBytes();
					break;
				case 5:
					Ensure(4);
					_offset += 4;
					break;
				default:
					throw new InvalidDataException($"FYERS TBT packet uses unsupported Protobuf wire type {wireType}.");
			}
		}

		private void Ensure(int count)
		{
			if (count < 0 || _offset > _data.Length - count)
				throw new InvalidDataException("FYERS TBT packet is truncated.");
		}
	}
}
