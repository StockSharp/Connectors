namespace StockSharp.CowProtocol.Native;

sealed class CowProtocolSigner
{
    private static readonly byte[] _domainTypeHash = Keccak(
        "EIP712Domain(string name,string version,uint256 chainId," +
        "address verifyingContract)");
    private static readonly byte[] _orderTypeHash = Keccak(
        "Order(address sellToken,address buyToken,address receiver," +
        "uint256 sellAmount,uint256 buyAmount,uint32 validTo," +
        "bytes32 appData,uint256 feeAmount,string kind," +
        "bool partiallyFillable,string sellTokenBalance," +
        "string buyTokenBalance)");
    private static readonly byte[] _cancellationTypeHash = Keccak(
        "OrderCancellation(bytes orderUid)");
    private static readonly byte[] _domainNameHash = Keccak(
        "Gnosis Protocol");
    private static readonly byte[] _domainVersionHash = Keccak("v2");
    private static readonly byte[] _erc20Hash = Keccak("erc20");
    private static readonly byte[] _buyHash = Keccak("buy");
    private static readonly byte[] _sellHash = Keccak("sell");

    private readonly EthECKey _key;
    private readonly byte[] _domainSeparator;

    public CowProtocolSigner(string privateKey, CowProtocolChains chain)
    {
        if (privateKey.IsEmpty())
            throw new ArgumentNullException(nameof(privateKey));
        if (!System.Enum.IsDefined(chain))
            throw new ArgumentOutOfRangeException(nameof(chain), chain,
                "Unsupported CoW Protocol chain.");
        _key = new(privateKey.Trim());
        Address = _key.GetPublicAddress().NormalizeAddress();
        _domainSeparator = GetDomainSeparatorBytes(chain);
    }

    public static string GetDomainSeparator(CowProtocolChains chain)
        => GetDomainSeparatorBytes(chain).ToHex(true).NormalizeBytes32();

    private static byte[] GetDomainSeparatorBytes(CowProtocolChains chain)
    {
        if (!System.Enum.IsDefined(chain))
            throw new ArgumentOutOfRangeException(nameof(chain), chain,
                "Unsupported CoW Protocol chain.");
        return Keccak(Concat(
            _domainTypeHash,
            _domainNameHash,
            _domainVersionHash,
            ToUInt256((int)chain),
            ToAddressWord(CowProtocolExtensions.SettlementAddress)));
    }

    public string Address { get; }

    public string DomainSeparator => _domainSeparator.ToHex(true);

    public CowProtocolSignedOrder SignOrder(CowProtocolOrderData order)
    {
        ValidateOrder(order);
        var structHash = Keccak(Concat(
            _orderTypeHash,
            ToAddressWord(order.SellToken),
            ToAddressWord(order.BuyToken),
            ToAddressWord(order.Receiver),
            ToUInt256(order.SellAmount),
            ToUInt256(order.BuyAmount),
            ToUInt256(order.ValidTo),
            ParseBytes32(order.AppDataHash),
            ToUInt256(order.FeeAmount),
            order.Kind == CowProtocolOrderKinds.Sell ? _sellHash : _buyHash,
            ToUInt256(order.IsPartiallyFillable ? 1 : 0),
            _erc20Hash,
            _erc20Hash));
        var digest = TypedDataDigest(structHash);
        var uid = new byte[56];
        Buffer.BlockCopy(digest, 0, uid, 0, digest.Length);
        var owner = Address[2..].HexToByteArray();
        Buffer.BlockCopy(owner, 0, uid, 32, owner.Length);
        BinaryPrimitives.WriteUInt32BigEndian(uid.AsSpan(52), order.ValidTo);
        return new()
        {
            Digest = digest.ToHex(true).NormalizeBytes32(),
            Signature = SignDigest(digest),
            Uid = uid.ToHex(true).NormalizeOrderUid(),
        };
    }

    public string SignCancellation(string uid)
    {
        uid = uid.NormalizeOrderUid();
        var structHash = Keccak(Concat(_cancellationTypeHash,
            Keccak(uid[2..].HexToByteArray())));
        return SignDigest(TypedDataDigest(structHash));
    }

    private byte[] TypedDataDigest(byte[] structHash)
        => Keccak(Concat([0x19, 0x01], _domainSeparator, structHash));

    private string SignDigest(byte[] digest)
    {
        if (digest is not { Length: 32 })
            throw new ArgumentOutOfRangeException(nameof(digest));
        var signed = _key.SignAndCalculateV(digest);
        var result = new byte[65];
        CopyScalar(signed.R, result.AsSpan(0, 32));
        CopyScalar(signed.S, result.AsSpan(32, 32));
        var v = signed.V is { Length: > 0 } ? signed.V[^1] : (byte)27;
        if (v is 0 or 1)
            v += 27;
        if (v is not 27 and not 28)
            throw new InvalidOperationException(
                $"EIP-712 signer returned invalid recovery id '{v}'.");
        result[64] = v;
        return result.ToHex(true).NormalizeSignature();
    }

    private static void ValidateOrder(CowProtocolOrderData order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _ = order.SellToken.NormalizeAddress();
        _ = order.BuyToken.NormalizeAddress();
        _ = order.Receiver.NormalizeAddress();
        _ = order.AppDataHash.NormalizeBytes32();
        if (order.SellToken.EqualsIgnoreCase(order.BuyToken))
            throw new InvalidDataException(
                "CoW Protocol order tokens must be different.");
        if (order.SellAmount <= 0 || order.BuyAmount <= 0 ||
            order.FeeAmount < 0)
            throw new InvalidDataException(
                "CoW Protocol order amounts are invalid.");
        if (order.ValidTo == 0)
            throw new InvalidDataException(
                "CoW Protocol order expiry is invalid.");
        if (!System.Enum.IsDefined(order.Kind) ||
            !System.Enum.IsDefined(order.SellTokenBalance) ||
            !System.Enum.IsDefined(order.BuyTokenBalance) ||
            order.SellTokenBalance != CowProtocolTokenBalances.Erc20 ||
            order.BuyTokenBalance != CowProtocolTokenBalances.Erc20)
            throw new InvalidDataException(
                "CoW Protocol order enum value is unsupported.");
    }

    private static byte[] ParseBytes32(string value)
        => value.NormalizeBytes32()[2..].HexToByteArray();

    private static byte[] ToAddressWord(string address)
    {
        var result = new byte[32];
        var bytes = address.NormalizeAddress()[2..].HexToByteArray();
        Buffer.BlockCopy(bytes, 0, result, 12, bytes.Length);
        return result;
    }

    private static byte[] ToUInt256(BigInteger value)
    {
        if (value < 0 || value >= BigInteger.One << 256)
            throw new ArgumentOutOfRangeException(nameof(value));
        var bytes = value.ToByteArray(true, true);
        var result = new byte[32];
        Buffer.BlockCopy(bytes, 0, result, result.Length - bytes.Length,
            bytes.Length);
        return result;
    }

    private static void CopyScalar(byte[] source, Span<byte> target)
    {
        if (source is null || source.Length == 0 || source.Length > target.Length)
            throw new InvalidOperationException(
                "ECDSA signer returned an invalid scalar.");
        target.Clear();
        source.CopyTo(target[^source.Length..]);
    }

    private static byte[] Keccak(string value)
        => Keccak(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static byte[] Keccak(byte[] value)
        => Sha3Keccack.Current.CalculateHash(value ?? []);

    private static byte[] Concat(params byte[][] chunks)
    {
        var length = chunks?.Sum(static chunk => chunk?.Length ?? 0) ?? 0;
        var result = new byte[length];
        var offset = 0;
        foreach (var chunk in chunks ?? [])
        {
            if (chunk is null || chunk.Length == 0)
                continue;
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }
}
