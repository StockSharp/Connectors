namespace StockSharp.Hyperliquid.Native;

using System.Buffers.Binary;
using System.IO;

static class MsgPackWriter
{
	public static byte[] Serialize(JToken token)
	{
		if (token is null)
			throw new ArgumentNullException(nameof(token));

		using var stream = new MemoryStream();
		WriteToken(stream, token);
		return stream.ToArray();
	}

	private static void WriteToken(Stream stream, JToken token)
	{
		switch (token.Type)
		{
			case JTokenType.Object:
				WriteObject(stream, (JObject)token);
				break;

			case JTokenType.Array:
				WriteArray(stream, (JArray)token);
				break;

			case JTokenType.Integer:
				WriteInteger(stream, token.Value<long>());
				break;

			case JTokenType.String:
				WriteString(stream, token.Value<string>());
				break;

			case JTokenType.Boolean:
				stream.WriteByte(token.Value<bool>() ? (byte)0xc3 : (byte)0xc2);
				break;

			case JTokenType.Null:
			case JTokenType.Undefined:
				stream.WriteByte(0xc0);
				break;

			default:
				throw new NotSupportedException($"Unsupported token type for msgpack encoding: {token.Type}.");
		}
	}

	private static void WriteObject(Stream stream, JObject obj)
	{
		WriteMapHeader(stream, obj.Count);

		foreach (var property in obj.Properties())
		{
			WriteString(stream, property.Name);
			WriteToken(stream, property.Value);
		}
	}

	private static void WriteArray(Stream stream, JArray array)
	{
		WriteArrayHeader(stream, array.Count);

		foreach (var item in array)
			WriteToken(stream, item);
	}

	private static void WriteString(Stream stream, string value)
	{
		value ??= string.Empty;

		var bytes = value.UTF8();
		var length = bytes.Length;

		if (length <= 31)
		{
			stream.WriteByte((byte)(0xa0 | length));
		}
		else if (length <= byte.MaxValue)
		{
			stream.WriteByte(0xd9);
			stream.WriteByte((byte)length);
		}
		else if (length <= ushort.MaxValue)
		{
			stream.WriteByte(0xda);
			WriteUInt16(stream, (ushort)length);
		}
		else
		{
			stream.WriteByte(0xdb);
			WriteUInt32(stream, (uint)length);
		}

		stream.Write(bytes, 0, bytes.Length);
	}

	private static void WriteInteger(Stream stream, long value)
	{
		if (value >= 0)
		{
			WriteUnsigned(stream, (ulong)value);
			return;
		}

		if (value >= -32)
		{
			stream.WriteByte(unchecked((byte)value));
		}
		else if (value >= sbyte.MinValue)
		{
			stream.WriteByte(0xd0);
			stream.WriteByte(unchecked((byte)(sbyte)value));
		}
		else if (value >= short.MinValue)
		{
			stream.WriteByte(0xd1);
			WriteInt16(stream, (short)value);
		}
		else if (value >= int.MinValue)
		{
			stream.WriteByte(0xd2);
			WriteInt32(stream, (int)value);
		}
		else
		{
			stream.WriteByte(0xd3);
			WriteInt64(stream, value);
		}
	}

	private static void WriteUnsigned(Stream stream, ulong value)
	{
		if (value <= 127)
		{
			stream.WriteByte((byte)value);
		}
		else if (value <= byte.MaxValue)
		{
			stream.WriteByte(0xcc);
			stream.WriteByte((byte)value);
		}
		else if (value <= ushort.MaxValue)
		{
			stream.WriteByte(0xcd);
			WriteUInt16(stream, (ushort)value);
		}
		else if (value <= uint.MaxValue)
		{
			stream.WriteByte(0xce);
			WriteUInt32(stream, (uint)value);
		}
		else
		{
			stream.WriteByte(0xcf);
			WriteUInt64(stream, value);
		}
	}

	private static void WriteMapHeader(Stream stream, int count)
	{
		if (count < 0)
			throw new ArgumentOutOfRangeException(nameof(count));

		if (count <= 15)
		{
			stream.WriteByte((byte)(0x80 | count));
		}
		else if (count <= ushort.MaxValue)
		{
			stream.WriteByte(0xde);
			WriteUInt16(stream, (ushort)count);
		}
		else
		{
			stream.WriteByte(0xdf);
			WriteUInt32(stream, (uint)count);
		}
	}

	private static void WriteArrayHeader(Stream stream, int count)
	{
		if (count < 0)
			throw new ArgumentOutOfRangeException(nameof(count));

		if (count <= 15)
		{
			stream.WriteByte((byte)(0x90 | count));
		}
		else if (count <= ushort.MaxValue)
		{
			stream.WriteByte(0xdc);
			WriteUInt16(stream, (ushort)count);
		}
		else
		{
			stream.WriteByte(0xdd);
			WriteUInt32(stream, (uint)count);
		}
	}

	private static void WriteUInt16(Stream stream, ushort value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(ushort)];
		BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteUInt32(Stream stream, uint value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(uint)];
		BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteUInt64(Stream stream, ulong value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(ulong)];
		BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteInt16(Stream stream, short value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(short)];
		BinaryPrimitives.WriteInt16BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteInt32(Stream stream, int value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(int)];
		BinaryPrimitives.WriteInt32BigEndian(buffer, value);
		stream.Write(buffer);
	}

	private static void WriteInt64(Stream stream, long value)
	{
		Span<byte> buffer = stackalloc byte[sizeof(long)];
		BinaryPrimitives.WriteInt64BigEndian(buffer, value);
		stream.Write(buffer);
	}
}
