namespace StockSharp.Xrpl.Native;

sealed class XrplSigner : IDisposable
{
	private readonly SecureString _seed;
	private bool _isDisposed;

	public XrplSigner(string account, SecureString seed)
	{
		account = account?.Trim();
		if (!account.IsEmpty() && !XrplCodec.IsValidClassicAddress(account))
			throw new ArgumentException(
				$"XRPL account '{account}' is not a valid classic address.",
				nameof(account));
		if (!seed.IsEmpty())
		{
			_seed = seed.Copy();
			XrplWallet wallet;
			try
			{
				wallet = XrplWallet.FromSeed(_seed.UnSecure().Trim());
			}
			catch (Exception error)
			{
				_seed.Dispose();
				throw new ArgumentException(
					"The XRPL seed cannot be decoded.", nameof(seed), error);
			}
			if (!account.IsEmpty() &&
				!wallet.ClassicAddress.EqualsIgnoreCase(account))
			{
				_seed.Dispose();
				throw new ArgumentException(
					"The configured XRPL account does not match the seed.",
					nameof(account));
			}
			WalletAddress = wallet.ClassicAddress;
		}
		else
			WalletAddress = account;
	}

	public string WalletAddress { get; }

	public bool IsWalletAvailable => !WalletAddress.IsEmpty();

	public bool IsSigningAvailable => _seed is not null;

	public XrplSignedTransaction SignOffer(XrplMarket market, Sides side,
		decimal price, decimal volume, OrderTypes orderType,
		TimeInForce? timeInForce, bool? postOnly, DateTime? expiration,
		uint sequence, uint lastLedgerSequence, decimal feeDrops,
		uint? offerSequence = null)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(market);
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price));
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		if (sequence == 0)
			throw new ArgumentOutOfRangeException(nameof(sequence));
		if (lastLedgerSequence == 0)
			throw new ArgumentOutOfRangeException(
				nameof(lastLedgerSequence));
		if (feeDrops <= 0 || feeDrops != decimal.Truncate(feeDrops))
			throw new ArgumentOutOfRangeException(nameof(feeDrops));
		if (orderType is not (OrderTypes.Limit or OrderTypes.Market))
			throw new NotSupportedException(
				$"XRPL does not support order type '{orderType}'.");
		var flags = side == Sides.Sell
			? OfferCreateFlags.tfSell
			: 0;
		flags |= timeInForce switch
		{
			TimeInForce.CancelBalance =>
				OfferCreateFlags.tfImmediateOrCancel,
			TimeInForce.MatchOrCancel =>
				OfferCreateFlags.tfFillOrKill,
			null or TimeInForce.PutInQueue => 0,
			_ => throw new NotSupportedException(
				$"XRPL does not support time in force '{timeInForce}'."),
		};
		if (orderType == OrderTypes.Market)
			flags |= OfferCreateFlags.tfImmediateOrCancel;
		if (postOnly == true)
		{
			if ((flags & (OfferCreateFlags.tfImmediateOrCancel |
				OfferCreateFlags.tfFillOrKill)) != 0)
				throw new InvalidOperationException(
					"An XRPL passive offer cannot be IOC or FOK.");
			flags |= OfferCreateFlags.tfPassive;
		}
		var quoteVolume = checked(price * volume);
		var transaction = new OfferCreate
		{
			Account = WalletAddress,
			Fee = ToCurrency(XrplExtensions.ParseAsset("XRP"), feeDrops /
				1_000_000m),
			Sequence = sequence,
			LastLedgerSequence = lastLedgerSequence,
			Flags = flags == 0 ? null : flags,
			Expiration = expiration?.ToUniversalTime(),
			DomainID = market.DomainId,
			OfferSequence = offerSequence,
		};
		if (side == Sides.Sell)
		{
			transaction.TakerGets = ToCurrency(market.Base, volume);
			transaction.TakerPays = ToCurrency(market.Quote, quoteVolume);
		}
		else
		{
			transaction.TakerGets = ToCurrency(market.Quote, quoteVolume);
			transaction.TakerPays = ToCurrency(market.Base, volume);
		}
		return Sign(transaction, sequence);
	}

	public XrplSignedTransaction SignCancel(uint offerSequence,
		uint sequence, uint lastLedgerSequence, decimal feeDrops)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (offerSequence == 0)
			throw new ArgumentOutOfRangeException(nameof(offerSequence));
		if (sequence == 0)
			throw new ArgumentOutOfRangeException(nameof(sequence));
		if (feeDrops <= 0 || feeDrops != decimal.Truncate(feeDrops))
			throw new ArgumentOutOfRangeException(nameof(feeDrops));
		return Sign(new OfferCancel
		{
			Account = WalletAddress,
			Fee = ToCurrency(XrplExtensions.ParseAsset("XRP"), feeDrops /
				1_000_000m),
			Sequence = sequence,
			LastLedgerSequence = lastLedgerSequence,
			OfferSequence = offerSequence,
		}, sequence);
	}

	public void Dispose()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_seed?.Dispose();
	}

	private XrplSignedTransaction Sign(ITransactionRequest transaction,
		uint sequence)
	{
		if (!IsSigningAvailable)
			throw new InvalidOperationException(
				"An XRPL family seed is required for transaction signing.");
		var wallet = XrplWallet.FromSeed(_seed.UnSecure().Trim());
		var signature = wallet.Sign(transaction);
		if (signature?.TxBlob.IsEmpty() != false ||
			signature.Hash.IsEmpty())
			throw new InvalidDataException(
				"XRPL signing returned no transaction blob.");
		return new()
		{
			Blob = signature.TxBlob,
			Hash = signature.Hash.ToUpperInvariant(),
			Sequence = sequence,
		};
	}

	private static global::Xrpl.Models.Common.Currency ToCurrency(
		XrplAsset asset, decimal value)
	{
		var amount = asset.ToAmount(value);
		if (amount.Type == JTokenType.String)
			return new()
			{
				Value = amount.Value<string>(),
			};
		var issued = (JObject)amount;
		return new()
		{
			CurrencyCode = issued.Value<string>("currency"),
			Issuer = issued.Value<string>("issuer"),
			Value = issued.Value<string>("value"),
		};
	}
}
