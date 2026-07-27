namespace StockSharp.Injective;

/// <summary>Injective environments.</summary>
[DataContract]
public enum InjectiveEnvironments
{
	/// <summary>Mainnet.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MainnetKey)]
	Mainnet,

	/// <summary>Testnet.</summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TestnetKey)]
	Testnet,
}

public partial class InjectiveMessageAdapter
{
	/// <summary>Injective environment.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.EnvironmentKey,
		Description = LocalizedStrings.InjectiveApiEnvironmentDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public InjectiveEnvironments Environment { get; set; }

	/// <summary>Optional Injective wallet address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WalletAddressKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string WalletAddress { get; set; }

	/// <summary>Optional secp256k1 private key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PrivateKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString PrivateKey { get; set; }

	private int _subaccountIndex;

	/// <summary>Injective subaccount index.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SubaccountKey,
		Description = LocalizedStrings.InjectiveSubaccountIndexBetweenZeroAnd255DescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public int SubaccountIndex
	{
		get => _subaccountIndex;
		set => _subaccountIndex = value is >= 0 and <= byte.MaxValue
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Injective subaccount index must be between zero and 255.");
	}

	/// <summary>Optional Indexer REST endpoint override.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IndexerEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	public string IndexerEndpoint { get; set; }

	/// <summary>Optional Indexer native gRPC endpoint override.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.GRPCEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 1)]
	public string GrpcEndpoint { get; set; }

	/// <summary>Optional chain LCD endpoint override.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ChainEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 2)]
	public string ChainEndpoint { get; set; }

	/// <summary>Optional Tendermint WebSocket endpoint override.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ChainWebSocketEndpointKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 3)]
	public string ChainSocketEndpoint { get; set; }

	private int _historyLimit = 1000;

	/// <summary>Maximum history records per request.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey,
		Order = 4)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Injective history limit must be between one and 1000.");
	}

	private int _marketDepth = 100;

	/// <summary>Maximum published order-book depth.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketDepthKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	public int MarketDepth
	{
		get => _marketDepth;
		set => _marketDepth = value is >= 1 and <= 1000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Injective market depth must be between one and 1000.");
	}

	private decimal _marketOrderSlippage = 0.5m;

	/// <summary>Market-order protection price deviation, in percent.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 6)]
	public decimal MarketOrderSlippage
	{
		get => _marketOrderSlippage;
		set => _marketOrderSlippage = value is > 0 and <= 50
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Injective market-order slippage must be above zero and at most 50%.");
	}

	private long _gasLimit = 400_000;

	/// <summary>Transaction gas limit.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.GasLimitKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 7)]
	public long GasLimit
	{
		get => _gasLimit;
		set => _gasLimit = value is >= 100_000 and <= 10_000_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Injective gas limit must be between 100000 and 10000000.");
	}

	private string _feeAmount = "64000000000000";

	/// <summary>Transaction fee amount in the smallest fee-denom units.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FeeAmountKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 8)]
	public string FeeAmount
	{
		get => _feeAmount;
		set
		{
			value = value.ThrowIfEmpty(nameof(value)).Trim();
			if (!BigInteger.TryParse(value, NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
				throw new ArgumentOutOfRangeException(nameof(value), value,
					"Injective fee amount must be a non-negative integer.");
			_feeAmount = value;
		}
	}

	/// <summary>Transaction fee denomination.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FeeDenomKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 9)]
	public string FeeDenom { get; set; } = "inj";

	private long _blockLifetime = 50;

	/// <summary>Default good-till-block lifetime for limit orders.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BlockLifetimeKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 10)]
	public long BlockLifetime
	{
		get => _blockLifetime;
		set => _blockLifetime = value is >= 2 and <= 100_000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Injective block lifetime must be between two and 100000.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Environment), Environment)
			.Set(nameof(WalletAddress), WalletAddress)
			.Set(nameof(PrivateKey), PrivateKey)
			.Set(nameof(SubaccountIndex), SubaccountIndex)
			.Set(nameof(IndexerEndpoint), IndexerEndpoint)
			.Set(nameof(GrpcEndpoint), GrpcEndpoint)
			.Set(nameof(ChainEndpoint), ChainEndpoint)
			.Set(nameof(ChainSocketEndpoint), ChainSocketEndpoint)
			.Set(nameof(HistoryLimit), HistoryLimit)
			.Set(nameof(MarketDepth), MarketDepth)
			.Set(nameof(MarketOrderSlippage), MarketOrderSlippage)
			.Set(nameof(GasLimit), GasLimit)
			.Set(nameof(FeeAmount), FeeAmount)
			.Set(nameof(FeeDenom), FeeDenom)
			.Set(nameof(BlockLifetime), BlockLifetime);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Environment = storage.GetValue(nameof(Environment), Environment);
		WalletAddress = storage.GetValue<string>(nameof(WalletAddress));
		PrivateKey = storage.GetValue<SecureString>(nameof(PrivateKey));
		SubaccountIndex = storage.GetValue(nameof(SubaccountIndex),
			SubaccountIndex);
		IndexerEndpoint = storage.GetValue<string>(nameof(IndexerEndpoint));
		GrpcEndpoint = storage.GetValue<string>(nameof(GrpcEndpoint));
		ChainEndpoint = storage.GetValue<string>(nameof(ChainEndpoint));
		ChainSocketEndpoint = storage.GetValue<string>(
			nameof(ChainSocketEndpoint));
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
		MarketDepth = storage.GetValue(nameof(MarketDepth), MarketDepth);
		MarketOrderSlippage = storage.GetValue(nameof(MarketOrderSlippage),
			MarketOrderSlippage);
		GasLimit = storage.GetValue(nameof(GasLimit), GasLimit);
		FeeAmount = storage.GetValue(nameof(FeeAmount), FeeAmount);
		FeeDenom = storage.GetValue(nameof(FeeDenom), FeeDenom)
			.ThrowIfEmpty(nameof(FeeDenom)).Trim();
		BlockLifetime = storage.GetValue(nameof(BlockLifetime), BlockLifetime);
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
		=> new InjectiveMessageAdapter(TransactionIdGenerator)
		{
			Environment = Environment,
			WalletAddress = WalletAddress,
			PrivateKey = PrivateKey,
			SubaccountIndex = SubaccountIndex,
			IndexerEndpoint = IndexerEndpoint,
			GrpcEndpoint = GrpcEndpoint,
			ChainEndpoint = ChainEndpoint,
			ChainSocketEndpoint = ChainSocketEndpoint,
			HistoryLimit = HistoryLimit,
			MarketDepth = MarketDepth,
			MarketOrderSlippage = MarketOrderSlippage,
			GasLimit = GasLimit,
			FeeAmount = FeeAmount,
			FeeDenom = FeeDenom,
			BlockLifetime = BlockLifetime,
		};
}
