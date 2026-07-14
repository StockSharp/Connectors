namespace StockSharp.Webull.Native;

using System;
using System.Collections.Generic;
using System.Globalization;

sealed record WebullBasic(string Symbol, string Timestamp);
sealed record WebullSnapshot(WebullBasic Basic, string TradeTime, decimal? Price, decimal? Open, decimal? High, decimal? Low, decimal? PreviousClose, decimal? Volume);
sealed record WebullBookLevel(decimal Price, decimal Volume);
sealed record WebullQuote(WebullBasic Basic, IReadOnlyList<WebullBookLevel> Asks, IReadOnlyList<WebullBookLevel> Bids);
sealed record WebullTick(WebullBasic Basic, string Time, decimal? Price, decimal? Volume, string Side);

static class WebullProtoDecoder
{
	public static WebullSnapshot DecodeSnapshot(byte[] payload)
	{
		var reader = new ProtoReader(payload);
		WebullBasic basic = null;
		string tradeTime = null;
		decimal? price = null, open = null, high = null, low = null, previousClose = null, volume = null;

		while (reader.TryReadField(out var field, out var wire))
		{
			switch (field)
			{
				case 1: basic = DecodeBasic(reader.ReadBytes(wire)); break;
				case 2: tradeTime = reader.ReadString(wire); break;
				case 3: price = ParseDecimal(reader.ReadString(wire)); break;
				case 4: open = ParseDecimal(reader.ReadString(wire)); break;
				case 5: high = ParseDecimal(reader.ReadString(wire)); break;
				case 6: low = ParseDecimal(reader.ReadString(wire)); break;
				case 7: previousClose = ParseDecimal(reader.ReadString(wire)); break;
				case 8: volume = ParseDecimal(reader.ReadString(wire)); break;
				default: reader.Skip(wire); break;
			}
		}

		return new(basic, tradeTime, price, open, high, low, previousClose, volume);
	}

	public static WebullQuote DecodeQuote(byte[] payload)
	{
		var reader = new ProtoReader(payload);
		WebullBasic basic = null;
		var asks = new List<WebullBookLevel>();
		var bids = new List<WebullBookLevel>();

		while (reader.TryReadField(out var field, out var wire))
		{
			switch (field)
			{
				case 1: basic = DecodeBasic(reader.ReadBytes(wire)); break;
				case 2: asks.Add(DecodeLevel(reader.ReadBytes(wire))); break;
				case 3: bids.Add(DecodeLevel(reader.ReadBytes(wire))); break;
				default: reader.Skip(wire); break;
			}
		}

		return new(basic, asks, bids);
	}

	public static WebullTick DecodeTick(byte[] payload)
	{
		var reader = new ProtoReader(payload);
		WebullBasic basic = null;
		string time = null, side = null;
		decimal? price = null, volume = null;

		while (reader.TryReadField(out var field, out var wire))
		{
			switch (field)
			{
				case 1: basic = DecodeBasic(reader.ReadBytes(wire)); break;
				case 2: time = reader.ReadString(wire); break;
				case 3: price = ParseDecimal(reader.ReadString(wire)); break;
				case 4: volume = ParseDecimal(reader.ReadString(wire)); break;
				case 5: side = reader.ReadString(wire); break;
				default: reader.Skip(wire); break;
			}
		}

		return new(basic, time, price, volume, side);
	}

	private static WebullBasic DecodeBasic(byte[] payload)
	{
		var reader = new ProtoReader(payload);
		string symbol = null, timestamp = null;
		while (reader.TryReadField(out var field, out var wire))
		{
			switch (field)
			{
				case 1: symbol = reader.ReadString(wire); break;
				case 3: timestamp = reader.ReadString(wire); break;
				default: reader.Skip(wire); break;
			}
		}
		return new(symbol, timestamp);
	}

	private static WebullBookLevel DecodeLevel(byte[] payload)
	{
		var reader = new ProtoReader(payload);
		decimal price = 0, volume = 0;
		while (reader.TryReadField(out var field, out var wire))
		{
			switch (field)
			{
				case 1: price = ParseDecimal(reader.ReadString(wire)) ?? 0; break;
				case 2: volume = ParseDecimal(reader.ReadString(wire)) ?? 0; break;
				default: reader.Skip(wire); break;
			}
		}
		return new(price, volume);
	}

	private static decimal? ParseDecimal(string value)
		=> decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;

	internal ref struct ProtoReader
	{
		private readonly ReadOnlySpan<byte> _data;
		private int _offset;

		public ProtoReader(ReadOnlySpan<byte> data) => _data = data;

		public bool TryReadField(out int field, out int wire)
		{
			if (_offset >= _data.Length)
			{
				field = wire = 0;
				return false;
			}

			var tag = ReadVarInt();
			field = (int)(tag >> 3);
			wire = (int)(tag & 7);
			return true;
		}

		public string ReadString(int wire) => ReadBytes(wire).UTF8();

		public byte[] ReadBytes(int wire)
		{
			if (wire != 2)
				throw new FormatException("A length-delimited protobuf field was expected.");
			var length = checked((int)ReadVarInt());
			if (length < 0 || _offset + length > _data.Length)
				throw new FormatException("Invalid protobuf field length.");
			var result = _data.Slice(_offset, length).ToArray();
			_offset += length;
			return result;
		}

		public ulong ReadVarInt()
		{
			ulong result = 0;
			for (var shift = 0; shift < 64; shift += 7)
			{
				if (_offset >= _data.Length)
					throw new FormatException("Unexpected end of protobuf payload.");
				var value = _data[_offset++];
				result |= (ulong)(value & 0x7f) << shift;
				if ((value & 0x80) == 0)
					return result;
			}
			throw new FormatException("Invalid protobuf varint.");
		}

		public void Skip(int wire)
		{
			switch (wire)
			{
				case 0: ReadVarInt(); break;
				case 1: _offset += 8; break;
				case 2: _offset += checked((int)ReadVarInt()); break;
				case 5: _offset += 4; break;
				default: throw new FormatException($"Unsupported protobuf wire type {wire}.");
			}
			if (_offset > _data.Length)
				throw new FormatException("Unexpected end of protobuf payload.");
		}
	}
}
