namespace StockSharp.ManifestTrade.Native;

static class ManifestTradeInstructionBuilder
{
	public static TransactionInstruction[] BuildMarketOrder(
		ManifestTradeMarket market, ManifestTradeQuote quote, Sides side,
		string walletAddress, uint computeUnitLimit, ulong computeUnitPrice)
	{
		ValidateTrading(market);
		ArgumentNullException.ThrowIfNull(quote);
		walletAddress = walletAddress.NormalizePublicKey();
		var ownerBase = ManifestTradeExtensions.AssociatedTokenAddress(
			walletAddress, market.BaseToken.Mint,
			market.BaseToken.TokenProgram);
		var ownerQuote = ManifestTradeExtensions.AssociatedTokenAddress(
			walletAddress, market.QuoteToken.Mint,
			market.QuoteToken.TokenProgram);
		var instructions = CreatePrelude(market, walletAddress, ownerBase,
			ownerQuote, computeUnitLimit, computeUnitPrice);
		var inputToken = side == Sides.Sell
			? market.BaseToken
			: market.QuoteToken;
		var inputAccount = side == Sides.Sell ? ownerBase : ownerQuote;
		var inputAtoms = side == Sides.Sell
			? quote.BaseAtoms
			: quote.InputLimitAtoms;
		AddWrappedSolFunding(instructions, walletAddress, inputAccount,
			inputToken, inputAtoms);
		instructions.Add(Swap(market, quote, side, walletAddress, ownerBase,
			ownerQuote));
		return [.. instructions];
	}

	public static TransactionInstruction[] BuildLimitOrder(
		ManifestTradeMarket market, string walletAddress, Sides side,
		ulong baseAtoms, uint priceMantissa, sbyte priceExponent,
		ManifestTradeOrderTypes orderType, uint lastValidSlot,
		bool isSeatClaimRequired, uint? traderIndex, ulong depositAtoms,
		uint computeUnitLimit, ulong computeUnitPrice)
		=> BuildBatch(market, walletAddress, side, baseAtoms, priceMantissa,
			priceExponent, orderType, lastValidSlot, isSeatClaimRequired,
			traderIndex, depositAtoms, null, null, computeUnitLimit,
			computeUnitPrice);

	public static TransactionInstruction[] BuildCancel(
		ManifestTradeMarket market, string walletAddress, ulong sequence,
		uint? orderIndex, uint? traderIndex, uint computeUnitLimit,
		ulong computeUnitPrice)
	{
		ValidateTrading(market);
		walletAddress = walletAddress.NormalizePublicKey();
		return
		[
			ComputeUnitLimit(computeUnitLimit),
			ComputeUnitPrice(computeUnitPrice),
			BatchUpdate(market, walletAddress, traderIndex,
				(sequence, orderIndex), null, false),
		];
	}

	public static TransactionInstruction[] BuildReplace(
		ManifestTradeMarket market, string walletAddress, Sides side,
		ulong baseAtoms, uint priceMantissa, sbyte priceExponent,
		ManifestTradeOrderTypes orderType, uint lastValidSlot,
		uint? traderIndex, ulong depositAtoms, ulong oldSequence,
		uint? oldOrderIndex, uint computeUnitLimit, ulong computeUnitPrice)
		=> BuildBatch(market, walletAddress, side, baseAtoms, priceMantissa,
			priceExponent, orderType, lastValidSlot, false, traderIndex,
			depositAtoms, oldSequence, oldOrderIndex, computeUnitLimit,
			computeUnitPrice);

	private static TransactionInstruction[] BuildBatch(
		ManifestTradeMarket market, string walletAddress, Sides side,
		ulong baseAtoms, uint priceMantissa, sbyte priceExponent,
		ManifestTradeOrderTypes orderType, uint lastValidSlot,
		bool isSeatClaimRequired, uint? traderIndex, ulong depositAtoms,
		ulong? cancelSequence, uint? cancelOrderIndex, uint computeUnitLimit,
		ulong computeUnitPrice)
	{
		ValidateTrading(market);
		if (baseAtoms == 0 || priceMantissa == 0)
			throw new ArgumentOutOfRangeException(nameof(baseAtoms));
		walletAddress = walletAddress.NormalizePublicKey();
		var ownerBase = ManifestTradeExtensions.AssociatedTokenAddress(
			walletAddress, market.BaseToken.Mint,
			market.BaseToken.TokenProgram);
		var ownerQuote = ManifestTradeExtensions.AssociatedTokenAddress(
			walletAddress, market.QuoteToken.Mint,
			market.QuoteToken.TokenProgram);
		var instructions = CreatePrelude(market, walletAddress, ownerBase,
			ownerQuote, computeUnitLimit, computeUnitPrice);
		if (isSeatClaimRequired)
			instructions.Add(ClaimSeat(walletAddress, market.MarketAddress));
		if (depositAtoms > 0)
		{
			var token = side == Sides.Sell
				? market.BaseToken
				: market.QuoteToken;
			var ownerToken = side == Sides.Sell ? ownerBase : ownerQuote;
			var vault = side == Sides.Sell
				? market.BaseVault
				: market.QuoteVault;
			AddWrappedSolFunding(instructions, walletAddress, ownerToken,
				token, depositAtoms);
			instructions.Add(Deposit(walletAddress, market.MarketAddress,
				ownerToken, vault, token, depositAtoms,
				isSeatClaimRequired ? null : traderIndex));
		}
		instructions.Add(BatchUpdate(market, walletAddress,
			isSeatClaimRequired ? null : traderIndex,
			cancelSequence is ulong sequence
				? (sequence, cancelOrderIndex)
				: null,
			(baseAtoms, priceMantissa, priceExponent, side == Sides.Buy,
				lastValidSlot, orderType), true));
		return [.. instructions];
	}

	private static List<TransactionInstruction> CreatePrelude(
		ManifestTradeMarket market, string walletAddress, string ownerBase,
		string ownerQuote, uint computeUnitLimit, ulong computeUnitPrice)
		=>
		[
			ComputeUnitLimit(computeUnitLimit),
			ComputeUnitPrice(computeUnitPrice),
			CreateAssociatedTokenAccount(walletAddress, ownerBase,
				walletAddress, market.BaseToken.Mint,
				market.BaseToken.TokenProgram),
			CreateAssociatedTokenAccount(walletAddress, ownerQuote,
				walletAddress, market.QuoteToken.Mint,
				market.QuoteToken.TokenProgram),
		];

	private static TransactionInstruction Swap(ManifestTradeMarket market,
		ManifestTradeQuote quote, Sides side, string walletAddress,
		string ownerBase, string ownerQuote)
	{
		var isBaseIn = side == Sides.Sell;
		var globalMint = isBaseIn
			? market.QuoteToken.Mint
			: market.BaseToken.Mint;
		var data = new byte[19];
		data[0] = 4;
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(1),
			isBaseIn ? quote.BaseAtoms : quote.InputLimitAtoms);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(9),
			isBaseIn ? quote.OutputLimitAtoms : quote.BaseAtoms);
		data[17] = isBaseIn ? (byte)1 : (byte)0;
		data[18] = isBaseIn ? (byte)1 : (byte)0;
		return Create(ManifestTradeExtensions.ProgramAddress,
		[
			Writable(walletAddress, true),
			Writable(market.MarketAddress),
			ReadOnly(ManifestTradeExtensions.SystemProgramAddress),
			Writable(ownerBase),
			Writable(ownerQuote),
			Writable(market.BaseVault),
			Writable(market.QuoteVault),
			ReadOnly(market.BaseToken.TokenProgram),
			ReadOnly(market.BaseToken.Mint),
			ReadOnly(market.QuoteToken.TokenProgram),
			ReadOnly(market.QuoteToken.Mint),
			Writable(ManifestTradeExtensions.GlobalAddress(globalMint)),
			Writable(ManifestTradeExtensions.GlobalVaultAddress(globalMint)),
		], data);
	}

	private static TransactionInstruction ClaimSeat(string payer,
		string market)
		=> Create(ManifestTradeExtensions.ProgramAddress,
		[
			Writable(payer, true),
			Writable(market),
			ReadOnly(ManifestTradeExtensions.SystemProgramAddress),
		], [1]);

	private static TransactionInstruction Deposit(string payer, string market,
		string traderToken, string vault, ManifestTradeToken token,
		ulong amount, uint? traderIndex)
	{
		var data = new List<byte> { 2 };
		Add(data, amount);
		AddOption(data, traderIndex);
		return Create(ManifestTradeExtensions.ProgramAddress,
		[
			Writable(payer, true),
			Writable(market),
			Writable(traderToken),
			Writable(vault),
			ReadOnly(token.TokenProgram),
			ReadOnly(token.Mint),
		], [.. data]);
	}

	private static TransactionInstruction BatchUpdate(
		ManifestTradeMarket market, string payer, uint? traderIndex,
		(ulong Sequence, uint? OrderIndex)? cancel,
		(ulong BaseAtoms, uint Mantissa, sbyte Exponent, bool IsBid,
			uint LastValidSlot, ManifestTradeOrderTypes OrderType)? order,
		bool isGlobalLiquidityIncluded)
	{
		var data = new List<byte> { 6 };
		AddOption(data, traderIndex);
		Add(data, cancel is null ? 0u : 1u);
		if (cancel is { } cancellation)
		{
			Add(data, cancellation.Sequence);
			AddOption(data, cancellation.OrderIndex);
		}
		Add(data, order is null ? 0u : 1u);
		if (order is { } placement)
		{
			Add(data, placement.BaseAtoms);
			Add(data, placement.Mantissa);
			data.Add(unchecked((byte)placement.Exponent));
			data.Add(placement.IsBid ? (byte)1 : (byte)0);
			Add(data, placement.LastValidSlot);
			data.Add((byte)placement.OrderType);
		}
		var keys = new List<AccountMeta>
		{
			Writable(payer, true),
			Writable(market.MarketAddress),
			ReadOnly(ManifestTradeExtensions.SystemProgramAddress),
		};
		if (isGlobalLiquidityIncluded)
		{
			AddGlobalGroup(keys, market.BaseToken,
				market.BaseVault);
			AddGlobalGroup(keys, market.QuoteToken,
				market.QuoteVault);
		}
		return Create(ManifestTradeExtensions.ProgramAddress, keys, [.. data]);
	}

	private static void AddGlobalGroup(ICollection<AccountMeta> keys,
		ManifestTradeToken token, string marketVault)
	{
		keys.Add(ReadOnly(token.Mint));
		keys.Add(Writable(ManifestTradeExtensions.GlobalAddress(token.Mint)));
		keys.Add(ReadOnly(ManifestTradeExtensions.GlobalVaultAddress(
			token.Mint)));
		keys.Add(ReadOnly(marketVault));
		keys.Add(ReadOnly(token.TokenProgram));
	}

	private static void AddWrappedSolFunding(
		ICollection<TransactionInstruction> instructions, string walletAddress,
		string tokenAccount, ManifestTradeToken token, ulong amount)
	{
		if (!token.Mint.Equals(ManifestTradeExtensions.WrappedSolMint,
			StringComparison.Ordinal) || amount == 0)
			return;
		instructions.Add(TransferSol(walletAddress, tokenAccount, amount));
		instructions.Add(SyncNative(tokenAccount));
	}

	private static TransactionInstruction ComputeUnitLimit(uint value)
	{
		var data = new byte[5];
		data[0] = 2;
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), value);
		return Create("ComputeBudget111111111111111111111111111111", [], data);
	}

	private static TransactionInstruction ComputeUnitPrice(ulong value)
	{
		var data = new byte[9];
		data[0] = 3;
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(1), value);
		return Create("ComputeBudget111111111111111111111111111111", [], data);
	}

	private static TransactionInstruction CreateAssociatedTokenAccount(
		string payer, string account, string owner, string mint,
		string tokenProgram)
		=> Create(ManifestTradeExtensions.AssociatedTokenProgramAddress,
		[
			Writable(payer, true),
			Writable(account),
			ReadOnly(owner),
			ReadOnly(mint),
			ReadOnly(ManifestTradeExtensions.SystemProgramAddress),
			ReadOnly(tokenProgram),
		], [1]);

	private static TransactionInstruction TransferSol(string source,
		string destination, ulong amount)
	{
		var data = new byte[12];
		BinaryPrimitives.WriteUInt32LittleEndian(data, 2);
		BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(4), amount);
		return Create(ManifestTradeExtensions.SystemProgramAddress,
			[Writable(source, true), Writable(destination)], data);
	}

	private static TransactionInstruction SyncNative(string account)
		=> Create(ManifestTradeExtensions.TokenProgramAddress,
			[Writable(account)], [17]);

	private static void Add(ICollection<byte> target, uint value)
	{
		foreach (var item in BitConverter.GetBytes(value))
			target.Add(item);
	}

	private static void Add(ICollection<byte> target, ulong value)
	{
		foreach (var item in BitConverter.GetBytes(value))
			target.Add(item);
	}

	private static void AddOption(ICollection<byte> target, uint? value)
	{
		target.Add(value is null ? (byte)0 : (byte)1);
		if (value is uint actual)
			Add(target, actual);
	}

	private static void ValidateTrading(ManifestTradeMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (!market.IsDirectTradingSupported)
			throw new NotSupportedException(
				"Direct Manifest Trade transactions currently require legacy " +
				"SPL tokens; Token-2022 markets remain available for data.");
	}

	private static AccountMeta Writable(string address, bool isSigner = false)
		=> AccountMeta.Writable(address.ToPublicKey(), isSigner);

	private static AccountMeta ReadOnly(string address,
		bool isSigner = false)
		=> AccountMeta.ReadOnly(address.ToPublicKey(), isSigner);

	private static TransactionInstruction Create(string programAddress,
		IList<AccountMeta> keys, byte[] data)
		=> TransactionInstructionFactory.Create(programAddress.ToPublicKey(),
			keys, data);
}
