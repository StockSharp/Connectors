namespace StockSharp.Orca.Native;

static class OrcaInstructionBuilder
{
	public static TransactionInstruction[] Build(OrcaMarket market,
		OrcaQuote quote, string walletAddress, uint computeUnitLimit,
		ulong computeUnitPrice)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		if (!market.IsDirectTradingSupported)
			throw new NotSupportedException(
				"Direct trading is unavailable for adaptive-fee, transfer-fee, " +
				"or transfer-hook Orca pools.");
		if (quote.TickArrayAddresses is not { Length: 5 })
			throw new InvalidDataException(
				"An Orca swap requires five tick-array addresses.");
		walletAddress = walletAddress.NormalizePublicKey();
		var ownerA = OrcaExtensions.AssociatedTokenAddress(walletAddress,
			market.TokenA.Mint, market.TokenA.TokenProgram);
		var ownerB = OrcaExtensions.AssociatedTokenAddress(walletAddress,
			market.TokenB.Mint, market.TokenB.TokenProgram);
		var instructions = new List<TransactionInstruction>
		{
			ComputeUnitLimit(computeUnitLimit),
			ComputeUnitPrice(computeUnitPrice),
			CreateAssociatedTokenAccount(walletAddress, ownerA, walletAddress,
				market.TokenA.Mint, market.TokenA.TokenProgram),
			CreateAssociatedTokenAccount(walletAddress, ownerB, walletAddress,
				market.TokenB.Mint, market.TokenB.TokenProgram),
		};
		var inputToken = quote.IsAToB ? market.TokenA : market.TokenB;
		var inputAccount = quote.IsAToB ? ownerA : ownerB;
		var inputAmount = quote.IsAmountSpecifiedInput
			? quote.BaseAmount
			: quote.QuoteLimit;
		if (inputToken.Mint.Equals(OrcaExtensions.WrappedSolMint,
			StringComparison.Ordinal))
		{
			instructions.Add(TransferSol(walletAddress, inputAccount,
				ToUInt64(inputAmount, "wrapped SOL input")));
			instructions.Add(SyncNative(inputAccount));
		}
		instructions.Add(SwapV2(market, quote, walletAddress, ownerA, ownerB));
		return [.. instructions];
	}

	private static TransactionInstruction SwapV2(OrcaMarket market,
		OrcaQuote quote, string walletAddress, string ownerA, string ownerB)
	{
		var arrays = quote.TickArrayAddresses;
		var keys = new List<AccountMeta>
		{
			ReadOnly(market.TokenA.TokenProgram),
			ReadOnly(market.TokenB.TokenProgram),
			ReadOnly(OrcaExtensions.MemoProgramAddress),
			ReadOnly(walletAddress, true),
			Writable(market.PoolAddress),
			ReadOnly(market.TokenA.Mint),
			ReadOnly(market.TokenB.Mint),
			Writable(ownerA),
			Writable(market.TokenVaultA),
			Writable(ownerB),
			Writable(market.TokenVaultB),
			Writable(arrays[0]),
			Writable(arrays[1]),
			Writable(arrays[2]),
			Writable(OrcaExtensions.OracleAddress(market.PoolAddress)),
			Writable(arrays[3]),
			Writable(arrays[4]),
		};
		return Create(OrcaExtensions.ProgramAddress, keys, EncodeSwapV2(quote));
	}

	private static byte[] EncodeSwapV2(OrcaQuote quote)
	{
		var data = new byte[49];
		OrcaExtensions.SwapV2Discriminator.CopyTo(data, 0);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8),
			ToUInt64(quote.BaseAmount, "specified base amount"));
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16),
			ToUInt64(quote.QuoteLimit, "other amount threshold"));
		data[40] = quote.IsAmountSpecifiedInput ? (byte)1 : (byte)0;
		data[41] = quote.IsAToB ? (byte)1 : (byte)0;
		data[42] = 1;
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(43), 1);
		data[47] = 6;
		data[48] = 2;
		return data;
	}

	private static TransactionInstruction ComputeUnitLimit(uint value)
	{
		var data = new byte[1 + sizeof(uint)];
		data[0] = 2;
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), value);
		return Create("ComputeBudget111111111111111111111111111111", [],
			data);
	}

	private static TransactionInstruction ComputeUnitPrice(ulong value)
	{
		var data = new byte[1 + sizeof(ulong)];
		data[0] = 3;
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(1), value);
		return Create("ComputeBudget111111111111111111111111111111", [],
			data);
	}

	private static TransactionInstruction CreateAssociatedTokenAccount(
		string payer, string account, string owner, string mint,
		string tokenProgram)
		=> Create(OrcaExtensions.AssociatedTokenProgramAddress,
		[
			Writable(payer, true),
			Writable(account),
			ReadOnly(owner),
			ReadOnly(mint),
			ReadOnly(OrcaExtensions.SystemProgramAddress),
			ReadOnly(tokenProgram),
		], [1]);

	private static TransactionInstruction TransferSol(string source,
		string destination, ulong amount)
	{
		var data = new byte[sizeof(uint) + sizeof(ulong)];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 2);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(sizeof(uint)),
			amount);
		return Create(OrcaExtensions.SystemProgramAddress,
		[
			Writable(source, true),
			Writable(destination),
		], data);
	}

	private static TransactionInstruction SyncNative(string account)
		=> Create(OrcaExtensions.TokenProgramAddress, [Writable(account)], [17]);

	private static ulong ToUInt64(BigInteger value, string name)
		=> value > 0 && value <= ulong.MaxValue
			? (ulong)value
			: throw new OverflowException(
				$"Orca {name} does not fit into an unsigned 64-bit value.");

	private static AccountMeta Writable(string address, bool isSigner = false)
		=> AccountMeta.Writable(address.ToPublicKey(), isSigner);

	private static AccountMeta ReadOnly(string address, bool isSigner = false)
		=> AccountMeta.ReadOnly(address.ToPublicKey(), isSigner);

	private static TransactionInstruction Create(string programAddress,
		IList<AccountMeta> keys, byte[] data)
		=> TransactionInstructionFactory.Create(programAddress.ToPublicKey(),
			keys, data);
}
