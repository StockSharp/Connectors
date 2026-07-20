namespace StockSharp.Meteora.Native;

sealed class MeteoraDataReader
{
	private static readonly UTF8Encoding _utf8 = new(false, true);
	private readonly byte[] _data;
	private int _offset;

	public MeteoraDataReader(byte[] data)
		=> _data = data ?? throw new ArgumentNullException(nameof(data));

	public int Length => _data.Length;
	public int Position => _offset;
	public int Remaining => _data.Length - _offset;

	public void ReadDiscriminator(byte[] expected, string typeName)
	{
		ArgumentNullException.ThrowIfNull(expected);
		var actual = ReadBytes(expected.Length);
		if (!actual.SequenceEqual(expected))
			throw new InvalidDataException(
				$"Account or event data is not an Meteora {typeName}.");
	}

	public byte ReadByte()
	{
		EnsureAvailable(1);
		return _data[_offset++];
	}

	public bool ReadBoolean()
		=> ReadByte() switch
		{
			0 => false,
			1 => true,
			var value => throw new InvalidDataException(
				$"Invalid Borsh boolean value '{value}'."),
		};

	public ushort ReadUInt16()
	{
		EnsureAvailable(sizeof(ushort));
		var value = BinaryPrimitives.ReadUInt16LittleEndian(
			_data.AsSpan(_offset, sizeof(ushort)));
		_offset += sizeof(ushort);
		return value;
	}

	public uint ReadUInt32()
	{
		EnsureAvailable(sizeof(uint));
		var value = BinaryPrimitives.ReadUInt32LittleEndian(
			_data.AsSpan(_offset, sizeof(uint)));
		_offset += sizeof(uint);
		return value;
	}

	public int ReadInt32()
	{
		EnsureAvailable(sizeof(int));
		var value = BinaryPrimitives.ReadInt32LittleEndian(
			_data.AsSpan(_offset, sizeof(int)));
		_offset += sizeof(int);
		return value;
	}

	public ulong ReadUInt64()
	{
		EnsureAvailable(sizeof(ulong));
		var value = BinaryPrimitives.ReadUInt64LittleEndian(
			_data.AsSpan(_offset, sizeof(ulong)));
		_offset += sizeof(ulong);
		return value;
	}

	public long ReadInt64()
	{
		EnsureAvailable(sizeof(long));
		var value = BinaryPrimitives.ReadInt64LittleEndian(
			_data.AsSpan(_offset, sizeof(long)));
		_offset += sizeof(long);
		return value;
	}

	public BigInteger ReadUInt128()
	{
		EnsureAvailable(16);
		var value = new BigInteger(_data.AsSpan(_offset, 16), true, false);
		_offset += 16;
		return value;
	}

	public BigInteger ReadInt128()
	{
		EnsureAvailable(16);
		var value = new BigInteger(_data.AsSpan(_offset, 16), false, false);
		_offset += 16;
		return value;
	}

	public string ReadPublicKey()
		=> new PublicKey(ReadBytes(PublicKey.PublicKeyLength)).Key;

	public string ReadString(int maximumBytes)
	{
		var length = ReadUInt32();
		if (length > maximumBytes || length > int.MaxValue)
			throw new InvalidDataException(
				$"Borsh string length '{length}' exceeds the safety limit.");
		try
		{
			return _utf8.GetString(ReadBytes((int)length));
		}
		catch (DecoderFallbackException error)
		{
			throw new InvalidDataException(
				"Borsh string contains invalid UTF-8.", error);
		}
	}

	public byte[] ReadBytes(int length)
	{
		EnsureAvailable(length);
		var result = _data.AsSpan(_offset, length).ToArray();
		_offset += length;
		return result;
	}

	public void Skip(int length)
	{
		EnsureAvailable(length);
		_offset += length;
	}

	private void EnsureAvailable(int length)
	{
		if (length < 0 || length > Remaining)
			throw new InvalidDataException(
				"Meteora account or event data is truncated.");
	}
}
