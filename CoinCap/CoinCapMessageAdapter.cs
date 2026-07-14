namespace StockSharp.CoinCap;

#if !NO_LICENSE
using StockSharp.Licensing;
#endif

public partial class CoinCapMessageAdapter
{
	private HttpClient _httpClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="CoinCapMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public CoinCapMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = new[] { BoardCodes.CoinCap };

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId secId)
		=> _exchanges.ContainsKey(secId.BoardCode) || secId.IsAssociated(BoardCodes.CoinCap);

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => nameof(CoinCap);
#endif

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage msg, CancellationToken cancellationToken)
	{
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);

#if !NO_LICENSE
		var lic = await nameof(CoinCap).ValidateLicenseAsync(component: GetType(), cancellationToken: cancellationToken);
		if (!lic.IsEmpty())
			throw new InvalidOperationException(lic);
#endif

		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_httpClient = new HttpClient(Address, Token) { Parent = this };
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_httpClient.Dispose();
		_httpClient = null;

		return default;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage msg, CancellationToken cancellationToken)
	{
		if (_httpClient != null)
		{
			try
			{
				_httpClient.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			_httpClient = null;
		}

		_assets.Clear();
		_exchanges.Clear();
		_pricesAssets.Clear();

		try
		{
			await ReCreatePricesPusherAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}

		if (_tradesPushers.Count > 0)
		{
			foreach (var pusher in _tradesPushers.Values)
			{
				try
				{
					pusher.NewTrade -= PusherOnNewTrade;
					pusher.Disconnect();
					pusher.Dispose();
				}
				catch (Exception ex)
				{
					await SendOutErrorAsync(ex, cancellationToken);
				}
			}

			_tradesPushers.Clear();
		}

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}
}
