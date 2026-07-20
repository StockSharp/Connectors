namespace StockSharp.Meteora.Native;

static class MeteoraInstructionBuilder
{
	public static MeteoraInstructionPlan BuildSwap(MeteoraMarket market,
		MeteoraQuote quote, string walletAddress, uint computeUnitLimit,
		ulong computeUnitPrice)
	{
		ArgumentNullException.ThrowIfNull(market);
		ArgumentNullException.ThrowIfNull(quote);
		if (!market.IsDirectTradingSupported)
			throw new NotSupportedException(
				"Direct trading is unavailable for disabled pools or Token-2022 " +
				"mints with transfer extensions.");
		if (quote.BinArrayAddresses.Length == 0)
			throw new InvalidDataException(
				"A Meteora swap requires at least one bin array.");
		walletAddress = walletAddress.NormalizePublicKey();
		var ownerX = MeteoraExtensions.AssociatedTokenAddress(walletAddress,
			market.TokenX.Mint, market.TokenX.TokenProgram);
		var ownerY = MeteoraExtensions.AssociatedTokenAddress(walletAddress,
			market.TokenY.Mint, market.TokenY.TokenProgram);
		var instructions = CreatePrelude(market, walletAddress, ownerX, ownerY,
			computeUnitLimit, computeUnitPrice);
		var inputToken = quote.IsSwapForY ? market.TokenX : market.TokenY;
		var inputAccount = quote.IsSwapForY ? ownerX : ownerY;
		var inputAmount = quote.IsExactInput
			? quote.BaseAmount
			: quote.OtherAmountThreshold;
		if (inputToken.Mint.Equals(MeteoraExtensions.WrappedSolMint,
			StringComparison.Ordinal))
		{
			instructions.Add(TransferSol(walletAddress, inputAccount,
				ToUInt64(inputAmount, "wrapped SOL input")));
			instructions.Add(SyncNative(inputAccount));
		}
		instructions.Add(Swap(market, quote, walletAddress, ownerX, ownerY));
		if (market.TokenX.Mint.Equals(MeteoraExtensions.WrappedSolMint,
			StringComparison.Ordinal))
			instructions.Add(CloseTokenAccount(ownerX, walletAddress,
				market.TokenX.TokenProgram));
		if (market.TokenY.Mint.Equals(MeteoraExtensions.WrappedSolMint,
			StringComparison.Ordinal))
			instructions.Add(CloseTokenAccount(ownerY, walletAddress,
				market.TokenY.TokenProgram));
		return new() { Instructions = [.. instructions] };
	}

	public static MeteoraInstructionPlan BuildPlaceLimitOrder(
		MeteoraMarket market, Sides side, BigInteger baseAmount, decimal price,
		string walletAddress, bool isBinArrayInitialized, uint computeUnitLimit,
		ulong computeUnitPrice)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (!market.IsLimitOrderPool)
			throw new NotSupportedException(
				"The selected Meteora pool does not support native limit orders.");
		if (!market.IsDirectTradingSupported)
			throw new NotSupportedException(
				"Native limit orders are unavailable for Token-2022 transfer " +
				"extensions.");
		walletAddress = walletAddress.NormalizePublicKey();
		var binId = market.PriceToBinId(price, side);
		var binArrayIndex = MeteoraExtensions.GetBinArrayIndex(binId);
		if ((binArrayIndex is < MeteoraExtensions.MinimumInternalBinArrayIndex or
			> MeteoraExtensions.MaximumInternalBinArrayIndex) &&
			!market.IsBitmapExtensionInitialized)
			throw new NotSupportedException(
				"This limit price requires an uninitialized extended bin bitmap.");
		var binArray = MeteoraExtensions.BinArrayAddress(market.PoolAddress,
			binArrayIndex);
		var amount = market.GetLimitOrderInputAmount(side, baseAmount, binId);
		var orderAccount = new Account();
		var ownerX = MeteoraExtensions.AssociatedTokenAddress(walletAddress,
			market.TokenX.Mint, market.TokenX.TokenProgram);
		var ownerY = MeteoraExtensions.AssociatedTokenAddress(walletAddress,
			market.TokenY.Mint, market.TokenY.TokenProgram);
		var userToken = side == Sides.Sell ? ownerX : ownerY;
		var token = side == Sides.Sell ? market.TokenX : market.TokenY;
		var instructions = CreatePrelude(market, walletAddress, ownerX, ownerY,
			computeUnitLimit, computeUnitPrice);
		if (!isBinArrayInitialized)
			instructions.Add(InitializeBinArray(market.PoolAddress, binArray,
				binArrayIndex, walletAddress));
		if (token.Mint.Equals(MeteoraExtensions.WrappedSolMint,
			StringComparison.Ordinal))
		{
			instructions.Add(TransferSol(walletAddress, userToken,
				ToUInt64(amount, "limit-order SOL deposit")));
			instructions.Add(SyncNative(userToken));
		}
		instructions.Add(PlaceLimitOrder(market, side, binId, amount,
			orderAccount.PublicKey.Key, walletAddress, userToken, binArray));
		if (token.Mint.Equals(MeteoraExtensions.WrappedSolMint,
			StringComparison.Ordinal))
			instructions.Add(CloseTokenAccount(userToken, walletAddress,
				token.TokenProgram));
		return new()
		{
			Instructions = [.. instructions],
			AdditionalSigner = orderAccount,
			OrderAddress = orderAccount.PublicKey.Key,
			BinId = binId,
		};
	}

	public static MeteoraInstructionPlan BuildCancelLimitOrder(
		MeteoraMarket market, string orderAddress, IEnumerable<int> binIds,
		string walletAddress, uint computeUnitLimit, ulong computeUnitPrice)
	{
		ArgumentNullException.ThrowIfNull(market);
		orderAddress = orderAddress.NormalizePublicKey();
		walletAddress = walletAddress.NormalizePublicKey();
		var bins = (binIds ?? []).Distinct().OrderBy(static id => id).ToArray();
		if (bins.Length is < 1 or > 50)
			throw new ArgumentOutOfRangeException(nameof(binIds),
				"A Meteora limit order must cancel between one and 50 bins.");
		var indexes = bins.Select(MeteoraExtensions.GetBinArrayIndex).Distinct()
			.ToArray();
		if (indexes.Any(index => index is <
				MeteoraExtensions.MinimumInternalBinArrayIndex or >
				MeteoraExtensions.MaximumInternalBinArrayIndex) &&
			!market.IsBitmapExtensionInitialized)
			throw new InvalidOperationException(
				"The Meteora extended bin bitmap is unavailable.");
		var arrays = indexes.Select(index =>
			MeteoraExtensions.BinArrayAddress(market.PoolAddress, index)).ToArray();
		var ownerX = MeteoraExtensions.AssociatedTokenAddress(walletAddress,
			market.TokenX.Mint, market.TokenX.TokenProgram);
		var ownerY = MeteoraExtensions.AssociatedTokenAddress(walletAddress,
			market.TokenY.Mint, market.TokenY.TokenProgram);
		var instructions = CreatePrelude(market, walletAddress, ownerX, ownerY,
			computeUnitLimit, computeUnitPrice);
		instructions.Add(CancelLimitOrder(market, orderAddress, bins,
			walletAddress, ownerX, ownerY, arrays));
		if (market.TokenX.Mint.Equals(MeteoraExtensions.WrappedSolMint,
			StringComparison.Ordinal))
			instructions.Add(CloseTokenAccount(ownerX, walletAddress,
				market.TokenX.TokenProgram));
		if (market.TokenY.Mint.Equals(MeteoraExtensions.WrappedSolMint,
			StringComparison.Ordinal))
			instructions.Add(CloseTokenAccount(ownerY, walletAddress,
				market.TokenY.TokenProgram));
		instructions.Add(CloseLimitOrder(orderAddress, walletAddress));
		return new()
		{
			Instructions = [.. instructions],
			OrderAddress = orderAddress,
		};
	}

	private static List<TransactionInstruction> CreatePrelude(
		MeteoraMarket market, string walletAddress, string ownerX, string ownerY,
		uint computeUnitLimit, ulong computeUnitPrice)
		=>
		[
			ComputeUnitLimit(computeUnitLimit),
			ComputeUnitPrice(computeUnitPrice),
			CreateAssociatedTokenAccount(walletAddress, ownerX, walletAddress,
				market.TokenX.Mint, market.TokenX.TokenProgram),
			CreateAssociatedTokenAccount(walletAddress, ownerY, walletAddress,
				market.TokenY.Mint, market.TokenY.TokenProgram),
		];

	private static TransactionInstruction Swap(MeteoraMarket market,
		MeteoraQuote quote, string walletAddress, string ownerX, string ownerY)
	{
		var keys = new List<AccountMeta>
		{
			Writable(market.PoolAddress),
			OptionalWritable(market.BitmapExtension,
				market.IsBitmapExtensionInitialized),
			Writable(market.TokenVaultX),
			Writable(market.TokenVaultY),
			Writable(quote.IsSwapForY ? ownerX : ownerY),
			Writable(quote.IsSwapForY ? ownerY : ownerX),
			ReadOnly(market.TokenX.Mint),
			ReadOnly(market.TokenY.Mint),
			Writable(market.Oracle),
			ReadOnly(MeteoraExtensions.ProgramAddress),
			ReadOnly(walletAddress, true),
			ReadOnly(market.TokenX.TokenProgram),
			ReadOnly(market.TokenY.TokenProgram),
			ReadOnly(MeteoraExtensions.MemoProgramAddress),
			ReadOnly(MeteoraExtensions.EventAuthorityAddress()),
			ReadOnly(MeteoraExtensions.ProgramAddress),
		};
		keys.AddRange(quote.BinArrayAddresses.Select(static address =>
			Writable(address)));
		return Create(MeteoraExtensions.ProgramAddress, keys,
			EncodeSwap(quote));
	}

	private static byte[] EncodeSwap(MeteoraQuote quote)
	{
		var data = new byte[8 + sizeof(ulong) * 2 + sizeof(uint)];
		(quote.IsExactInput
			? MeteoraExtensions.Swap2InstructionDiscriminator
			: MeteoraExtensions.SwapExactOut2InstructionDiscriminator)
			.CopyTo(data, 0);
		var first = quote.IsExactInput ? quote.BaseAmount :
			quote.OtherAmountThreshold;
		var second = quote.IsExactInput ? quote.OtherAmountThreshold :
			quote.BaseAmount;
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8),
			ToUInt64(first, "swap amount"));
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16),
			ToUInt64(second, "swap threshold"));
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 0);
		return data;
	}

	private static TransactionInstruction InitializeBinArray(string pool,
		string binArray, long index, string wallet)
	{
		var data = new byte[8 + sizeof(long)];
		MeteoraExtensions.InitializeBinArrayDiscriminator.CopyTo(data, 0);
		BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), index);
		return Create(MeteoraExtensions.ProgramAddress,
		[
			ReadOnly(pool),
			Writable(binArray),
			Writable(wallet, true),
			ReadOnly(MeteoraExtensions.SystemProgramAddress),
		], data);
	}

	private static TransactionInstruction PlaceLimitOrder(MeteoraMarket market,
		Sides side, int binId, BigInteger amount, string orderAddress,
		string wallet, string userToken, string binArray)
	{
		var isAsk = side == Sides.Sell;
		var token = isAsk ? market.TokenX : market.TokenY;
		var reserve = isAsk ? market.TokenVaultX : market.TokenVaultY;
		var arrayIndex = MeteoraExtensions.GetBinArrayIndex(binId);
		var isExtended = arrayIndex is <
			MeteoraExtensions.MinimumInternalBinArrayIndex or >
			MeteoraExtensions.MaximumInternalBinArrayIndex;
		var keys = new List<AccountMeta>
		{
			Writable(market.PoolAddress),
			isExtended
				? Writable(market.BitmapExtension)
				: ReadOnly(MeteoraExtensions.ProgramAddress),
			Writable(reserve),
			ReadOnly(token.Mint),
			Writable(orderAddress, true),
			Writable(wallet, true),
			ReadOnly(wallet),
			Writable(userToken),
			ReadOnly(wallet, true),
			ReadOnly(token.TokenProgram),
			ReadOnly(MeteoraExtensions.SystemProgramAddress),
			ReadOnly(MeteoraExtensions.EventAuthorityAddress()),
			ReadOnly(MeteoraExtensions.ProgramAddress),
			Writable(binArray),
		};
		return Create(MeteoraExtensions.ProgramAddress, keys,
			EncodePlaceLimitOrder(isAsk, binId, amount));
	}

	private static byte[] EncodePlaceLimitOrder(bool isAsk, int binId,
		BigInteger amount)
	{
		var data = new byte[8 + 1 + 16 + 1 + sizeof(uint) + sizeof(int) +
			sizeof(ulong) + sizeof(uint)];
		MeteoraExtensions.PlaceLimitOrderDiscriminator.CopyTo(data, 0);
		data[8] = isAsk ? (byte)1 : (byte)0;
		var offset = 8 + 1 + 16;
		data[offset++] = 0;
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), 1);
		offset += sizeof(uint);
		BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), binId);
		offset += sizeof(int);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset),
			ToUInt64(amount, "limit-order amount"));
		offset += sizeof(ulong);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), 0);
		return data;
	}

	private static TransactionInstruction CancelLimitOrder(MeteoraMarket market,
		string orderAddress, int[] bins, string wallet, string ownerX,
		string ownerY, string[] binArrays)
	{
		var isExtended = bins.Select(MeteoraExtensions.GetBinArrayIndex).Any(
			index => index is < MeteoraExtensions.MinimumInternalBinArrayIndex or
				> MeteoraExtensions.MaximumInternalBinArrayIndex);
		var keys = new List<AccountMeta>
		{
			Writable(market.PoolAddress),
			isExtended
				? Writable(market.BitmapExtension)
				: ReadOnly(MeteoraExtensions.ProgramAddress),
			Writable(market.TokenVaultX),
			Writable(market.TokenVaultY),
			ReadOnly(market.TokenX.Mint),
			ReadOnly(market.TokenY.Mint),
			Writable(orderAddress),
			Writable(ownerX),
			Writable(ownerY),
			ReadOnly(wallet, true),
			ReadOnly(market.TokenX.TokenProgram),
			ReadOnly(market.TokenY.TokenProgram),
			ReadOnly(MeteoraExtensions.MemoProgramAddress),
			ReadOnly(MeteoraExtensions.EventAuthorityAddress()),
			ReadOnly(MeteoraExtensions.ProgramAddress),
		};
		keys.AddRange(binArrays.Select(static address => Writable(address)));
		var data = new byte[8 + sizeof(uint) + bins.Length * sizeof(int) +
			sizeof(uint)];
		MeteoraExtensions.CancelLimitOrderDiscriminator.CopyTo(data, 0);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8),
			checked((uint)bins.Length));
		var offset = 8 + sizeof(uint);
		foreach (var bin in bins)
		{
			BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), bin);
			offset += sizeof(int);
		}
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), 0);
		return Create(MeteoraExtensions.ProgramAddress, keys, data);
	}

	private static TransactionInstruction CloseLimitOrder(string orderAddress,
		string wallet)
		=> Create(MeteoraExtensions.ProgramAddress,
		[
			Writable(orderAddress),
			ReadOnly(wallet, true),
			Writable(wallet),
			ReadOnly(MeteoraExtensions.EventAuthorityAddress()),
			ReadOnly(MeteoraExtensions.ProgramAddress),
		], MeteoraExtensions.CloseLimitOrderDiscriminator);

	private static TransactionInstruction ComputeUnitLimit(uint value)
	{
		var data = new byte[1 + sizeof(uint)];
		data[0] = 2;
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), value);
		return Create("ComputeBudget111111111111111111111111111111", [], data);
	}

	private static TransactionInstruction ComputeUnitPrice(ulong value)
	{
		var data = new byte[1 + sizeof(ulong)];
		data[0] = 3;
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(1), value);
		return Create("ComputeBudget111111111111111111111111111111", [], data);
	}

	private static TransactionInstruction CreateAssociatedTokenAccount(
		string payer, string account, string owner, string mint,
		string tokenProgram)
		=> Create(MeteoraExtensions.AssociatedTokenProgramAddress,
		[
			Writable(payer, true),
			Writable(account),
			ReadOnly(owner),
			ReadOnly(mint),
			ReadOnly(MeteoraExtensions.SystemProgramAddress),
			ReadOnly(tokenProgram),
		], [1]);

	private static TransactionInstruction TransferSol(string source,
		string destination, ulong amount)
	{
		var data = new byte[sizeof(uint) + sizeof(ulong)];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 2);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(sizeof(uint)),
			amount);
		return Create(MeteoraExtensions.SystemProgramAddress,
		[
			Writable(source, true),
			Writable(destination),
		], data);
	}

	private static TransactionInstruction SyncNative(string account)
		=> Create(MeteoraExtensions.TokenProgramAddress, [Writable(account)], [17]);

	private static TransactionInstruction CloseTokenAccount(string account,
		string destination, string tokenProgram)
		=> Create(tokenProgram,
		[
			Writable(account),
			Writable(destination),
			ReadOnly(destination, true),
		], [9]);

	private static ulong ToUInt64(BigInteger value, string name)
		=> value > 0 && value <= ulong.MaxValue
			? (ulong)value
			: throw new OverflowException(
				$"Meteora {name} does not fit into an unsigned 64-bit value.");

	private static AccountMeta OptionalWritable(string address,
		bool isInitialized)
		=> isInitialized ? Writable(address) :
			ReadOnly(MeteoraExtensions.ProgramAddress);

	private static AccountMeta Writable(string address, bool isSigner = false)
		=> AccountMeta.Writable(address.ToPublicKey(), isSigner);

	private static AccountMeta ReadOnly(string address, bool isSigner = false)
		=> AccountMeta.ReadOnly(address.ToPublicKey(), isSigner);

	private static TransactionInstruction Create(string programAddress,
		IList<AccountMeta> keys, byte[] data)
		=> TransactionInstructionFactory.Create(programAddress.ToPublicKey(),
			keys, data);
}
