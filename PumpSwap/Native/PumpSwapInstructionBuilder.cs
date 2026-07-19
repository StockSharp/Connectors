namespace StockSharp.PumpSwap.Native;

static class PumpSwapInstructionBuilder
{
	private const int _poolAccountSize = 300;
	private static int _recipientIndex;

	public static TransactionInstruction[] Build(PumpSwapMarket market,
		Sides side, PumpSwapQuote quote, string walletAddress,
		PumpSwapGlobalConfig globalConfig, uint computeUnitLimit,
		ulong computeUnitPrice)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		ArgumentNullException.ThrowIfNull(globalConfig);
		walletAddress = walletAddress.NormalizePublicKey();

		var instructions = new List<TransactionInstruction>
		{
			ComputeUnitLimit(computeUnitLimit),
			ComputeUnitPrice(computeUnitPrice),
		};
		var userBase = PumpSwapExtensions.AssociatedTokenAddress(walletAddress,
			market.BaseToken.Mint, market.BaseToken.TokenProgram);
		var userQuote = PumpSwapExtensions.AssociatedTokenAddress(walletAddress,
			market.QuoteToken.Mint, market.QuoteToken.TokenProgram);

		instructions.Add(CreateAssociatedTokenAccount(walletAddress, userBase,
			walletAddress, market.BaseToken.Mint,
			market.BaseToken.TokenProgram));
		instructions.Add(CreateAssociatedTokenAccount(walletAddress, userQuote,
			walletAddress, market.QuoteToken.Mint,
			market.QuoteToken.TokenProgram));

		if (market.PoolDataLength < _poolAccountSize)
			instructions.Add(ExtendPool(market.PoolAddress, walletAddress));

		var inputMint = side == Sides.Buy
			? market.QuoteToken.Mint
			: market.BaseToken.Mint;
		var inputAccount = side == Sides.Buy ? userQuote : userBase;
		var inputAmount = side == Sides.Buy
			? quote.QuoteLimit
			: quote.BaseAmount;
		if (inputMint.Equals(PumpSwapExtensions.WrappedSolMint,
			StringComparison.Ordinal))
		{
			instructions.Add(TransferSol(walletAddress, inputAccount,
				ToUInt64(inputAmount, "wrapped SOL input")));
			instructions.Add(SyncNative(inputAccount));
		}

		instructions.Add(Swap(market, side, quote, walletAddress, userBase,
			userQuote, globalConfig));

		if (market.BaseToken.Mint.Equals(
				PumpSwapExtensions.WrappedSolMint, StringComparison.Ordinal))
			instructions.Add(CloseTokenAccount(userBase, walletAddress));
		else if (market.QuoteToken.Mint.Equals(
				PumpSwapExtensions.WrappedSolMint, StringComparison.Ordinal))
			instructions.Add(CloseTokenAccount(userQuote, walletAddress));

		return [.. instructions];
	}

	private static TransactionInstruction Swap(PumpSwapMarket market,
		Sides side, PumpSwapQuote quote, string walletAddress, string userBase,
		string userQuote, PumpSwapGlobalConfig globalConfig)
	{
		var protocolFeeRecipient = SelectRecipient(market.IsMayhemMode
			? [globalConfig.ReservedFeeRecipient,
				.. globalConfig.ReservedFeeRecipients ?? []]
			: globalConfig.ProtocolFeeRecipients,
			"protocol fee");
		var buybackFeeRecipient = SelectRecipient(
			globalConfig.BuybackFeeRecipients, "buyback fee");
		var coinCreatorVaultAuthority =
			PumpSwapExtensions.CoinCreatorVaultAuthorityAddress(
				market.CoinCreator);
		var protocolFeeTokenAccount =
			PumpSwapExtensions.AssociatedTokenAddress(protocolFeeRecipient,
				market.QuoteToken.Mint, market.QuoteToken.TokenProgram);
		var coinCreatorVaultAccount =
			PumpSwapExtensions.AssociatedTokenAddress(
				coinCreatorVaultAuthority, market.QuoteToken.Mint,
				market.QuoteToken.TokenProgram);
		var buybackFeeTokenAccount =
			PumpSwapExtensions.AssociatedTokenAddress(buybackFeeRecipient,
				market.QuoteToken.Mint, market.QuoteToken.TokenProgram);
		var userAccumulator =
			PumpSwapExtensions.UserVolumeAccumulatorAddress(walletAddress);

		var keys = new List<AccountMeta>
		{
			Writable(market.PoolAddress),
			Writable(walletAddress, true),
			ReadOnly(PumpSwapExtensions.GlobalConfigAddress),
			ReadOnly(market.BaseToken.Mint),
			ReadOnly(market.QuoteToken.Mint),
			Writable(userBase),
			Writable(userQuote),
			Writable(market.PoolBaseTokenAccount),
			Writable(market.PoolQuoteTokenAccount),
			ReadOnly(protocolFeeRecipient),
			Writable(protocolFeeTokenAccount),
			ReadOnly(market.BaseToken.TokenProgram),
			ReadOnly(market.QuoteToken.TokenProgram),
			ReadOnly(PumpSwapExtensions.SystemProgramAddress),
			ReadOnly(PumpSwapExtensions.AssociatedTokenProgramAddress),
			ReadOnly(PumpSwapExtensions.EventAuthorityAddress()),
			ReadOnly(PumpSwapExtensions.ProgramAddress),
			Writable(coinCreatorVaultAccount),
			ReadOnly(coinCreatorVaultAuthority),
		};

		if (side == Sides.Buy)
		{
			keys.Add(ReadOnly(
				PumpSwapExtensions.GlobalVolumeAccumulatorAddress()));
			keys.Add(Writable(userAccumulator));
		}
		keys.Add(ReadOnly(PumpSwapExtensions.FeeConfigAddress()));
		keys.Add(ReadOnly(PumpSwapExtensions.FeeProgramAddress));

		if (market.IsCashbackCoin)
		{
			keys.Add(Writable(PumpSwapExtensions.AssociatedTokenAddress(
				userAccumulator, market.QuoteToken.Mint,
				market.QuoteToken.TokenProgram)));
			if (side == Sides.Sell)
				keys.Add(Writable(userAccumulator));
		}
		if (!market.CoinCreator.IsDefaultPublicKey())
			keys.Add(ReadOnly(PumpSwapExtensions.PoolV2Address(
				market.BaseToken.Mint)));
		keys.Add(ReadOnly(buybackFeeRecipient));
		keys.Add(Writable(buybackFeeTokenAccount));

		var data = side == Sides.Buy
			? EncodeBuy(quote)
			: EncodeSell(quote);
		return Create(PumpSwapExtensions.ProgramAddress, keys, data);
	}

	private static TransactionInstruction ExtendPool(string poolAddress,
		string walletAddress)
		=> Create(PumpSwapExtensions.ProgramAddress,
		[
			Writable(poolAddress),
			ReadOnly(walletAddress, true),
			ReadOnly(PumpSwapExtensions.SystemProgramAddress),
			ReadOnly(PumpSwapExtensions.EventAuthorityAddress()),
			ReadOnly(PumpSwapExtensions.ProgramAddress),
		], PumpSwapExtensions.ExtendAccountDiscriminator);

	private static TransactionInstruction ComputeUnitLimit(uint value)
	{
		var data = new byte[1 + sizeof(uint)];
		data[0] = 2;
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), value);
		return Create("ComputeBudget111111111111111111111111111111",
			[], data);
	}

	private static TransactionInstruction ComputeUnitPrice(ulong value)
	{
		var data = new byte[1 + sizeof(ulong)];
		data[0] = 3;
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(1), value);
		return Create("ComputeBudget111111111111111111111111111111",
			[], data);
	}

	private static TransactionInstruction CreateAssociatedTokenAccount(
		string payer, string account, string owner, string mint,
		string tokenProgram)
		=> Create(PumpSwapExtensions.AssociatedTokenProgramAddress,
		[
			Writable(payer, true),
			Writable(account),
			ReadOnly(owner),
			ReadOnly(mint),
			ReadOnly(PumpSwapExtensions.SystemProgramAddress),
			ReadOnly(tokenProgram),
		], [1]);

	private static TransactionInstruction TransferSol(string source,
		string destination, ulong amount)
	{
		var data = new byte[sizeof(uint) + sizeof(ulong)];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 2);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(sizeof(uint)),
			amount);
		return Create(PumpSwapExtensions.SystemProgramAddress,
		[
			Writable(source, true),
			Writable(destination),
		], data);
	}

	private static TransactionInstruction SyncNative(string account)
		=> Create(PumpSwapExtensions.TokenProgramAddress,
			[Writable(account)], [17]);

	private static TransactionInstruction CloseTokenAccount(string account,
		string walletAddress)
		=> Create(PumpSwapExtensions.TokenProgramAddress,
		[
			Writable(account),
			Writable(walletAddress),
			ReadOnly(walletAddress, true),
		], [9]);

	private static byte[] EncodeBuy(PumpSwapQuote quote)
	{
		var data = new byte[PumpSwapExtensions.BuyDiscriminator.Length +
			sizeof(ulong) * 2 + 1];
		PumpSwapExtensions.BuyDiscriminator.CopyTo(data, 0);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8),
			ToUInt64(quote.BaseAmount, "buy base amount"));
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16),
			ToUInt64(quote.QuoteLimit, "maximum quote amount"));
		data[24] = 1;
		return data;
	}

	private static byte[] EncodeSell(PumpSwapQuote quote)
	{
		var data = new byte[PumpSwapExtensions.SellDiscriminator.Length +
			sizeof(ulong) * 2];
		PumpSwapExtensions.SellDiscriminator.CopyTo(data, 0);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8),
			ToUInt64(quote.BaseAmount, "sell base amount"));
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16),
			ToUInt64(quote.QuoteLimit, "minimum quote amount"));
		return data;
	}

	private static string SelectRecipient(IEnumerable<string> recipients,
		string purpose)
	{
		var valid = (recipients ?? []).Where(static address =>
			!address.IsEmpty() && !address.IsDefaultPublicKey()).ToArray();
		if (valid.Length == 0)
			throw new InvalidDataException(
				$"PumpSwap {purpose} recipient list is empty.");
		var index = (uint)Interlocked.Increment(ref _recipientIndex) %
			(uint)valid.Length;
		return valid[checked((int)index)];
	}

	private static ulong ToUInt64(BigInteger value, string name)
		=> value > 0 && value <= ulong.MaxValue
			? (ulong)value
			: throw new OverflowException(
				$"PumpSwap {name} does not fit into an unsigned 64-bit value.");

	private static AccountMeta Writable(string address, bool isSigner = false)
		=> AccountMeta.Writable(address.ToPublicKey(), isSigner);

	private static AccountMeta ReadOnly(string address, bool isSigner = false)
		=> AccountMeta.ReadOnly(address.ToPublicKey(), isSigner);

	private static TransactionInstruction Create(string programAddress,
		IList<AccountMeta> keys, byte[] data)
		=> TransactionInstructionFactory.Create(programAddress.ToPublicKey(),
			keys, data);
}
