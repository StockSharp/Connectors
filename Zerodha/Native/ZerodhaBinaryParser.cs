namespace StockSharp.Zerodha.Native;

internal static class ZerodhaBinaryParser
{
	public static KiteTick[] Parse(ReadOnlySpan<byte> frame)
	{
		if (frame.Length < 2)
			return [];

		var packetCount = BinaryPrimitives.ReadUInt16BigEndian(frame);
		var offset = 2;
		var ticks = new List<KiteTick>(packetCount);
		for (var index = 0; index < packetCount; index++)
		{
			if (offset + 2 > frame.Length)
				throw new InvalidDataException("Zerodha WebSocket frame has a truncated packet length.");
			var length = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(offset, 2));
			offset += 2;
			if (offset + length > frame.Length)
				throw new InvalidDataException("Zerodha WebSocket frame has a truncated quote packet.");
			var tick = ParsePacket(frame.Slice(offset, length));
			if (tick != null)
				ticks.Add(tick);
			offset += length;
		}
		return [.. ticks];
	}

	private static KiteTick ParsePacket(ReadOnlySpan<byte> packet)
	{
		if (packet.Length is not (8 or 28 or 32 or 44 or 184))
			return null;

		var token = ReadUInt32(packet, 0);
		var segment = token & 0xff;
		var divisor = segment switch
		{
			3 => 10_000_000m,
			6 => 10_000m,
			_ => 100m,
		};
		var tick = new KiteTick
		{
			InstrumentToken = token,
			IsTradable = segment != 9,
			LastPrice = ReadUInt32(packet, 4) / divisor,
		};

		if (packet.Length == 8)
			return tick;

		if (packet.Length is 28 or 32)
		{
			tick.HighPrice = ReadUInt32(packet, 8) / divisor;
			tick.LowPrice = ReadUInt32(packet, 12) / divisor;
			tick.OpenPrice = ReadUInt32(packet, 16) / divisor;
			tick.ClosePrice = ReadUInt32(packet, 20) / divisor;
			if (packet.Length == 32)
				tick.ExchangeTime = ReadTime(packet, 28);
			return tick;
		}

		tick.LastQuantity = ReadUInt32(packet, 8);
		tick.AveragePrice = ReadUInt32(packet, 12) / divisor;
		tick.Volume = ReadUInt32(packet, 16);
		tick.TotalBuyQuantity = ReadUInt32(packet, 20);
		tick.TotalSellQuantity = ReadUInt32(packet, 24);
		tick.OpenPrice = ReadUInt32(packet, 28) / divisor;
		tick.HighPrice = ReadUInt32(packet, 32) / divisor;
		tick.LowPrice = ReadUInt32(packet, 36) / divisor;
		tick.ClosePrice = ReadUInt32(packet, 40) / divisor;
		if (packet.Length == 44)
			return tick;

		tick.LastTradeTime = ReadTime(packet, 44);
		tick.OpenInterest = ReadUInt32(packet, 48);
		tick.OpenInterestHigh = ReadUInt32(packet, 52);
		tick.OpenInterestLow = ReadUInt32(packet, 56);
		tick.ExchangeTime = ReadTime(packet, 60);
		tick.Bids = ReadDepth(packet, 64, divisor);
		tick.Asks = ReadDepth(packet, 124, divisor);
		return tick;
	}

	private static KiteDepthEntry[] ReadDepth(ReadOnlySpan<byte> packet, int start, decimal divisor)
	{
		var depth = new KiteDepthEntry[5];
		for (var index = 0; index < depth.Length; index++)
		{
			var offset = start + index * 12;
			depth[index] = new()
			{
				Quantity = ReadUInt32(packet, offset),
				Price = ReadUInt32(packet, offset + 4) / divisor,
				Orders = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 8, 2)),
			};
		}
		return depth;
	}

	private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
		=> BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

	private static DateTime? ReadTime(ReadOnlySpan<byte> data, int offset)
	{
		var seconds = ReadUInt32(data, offset);
		return seconds == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
	}
}
