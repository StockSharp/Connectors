namespace StockSharp.ETrade;

using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;
using Ecng.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.ETrade.Native;
using StockSharp.Localization;
using StockSharp.Messages;

using DataType = StockSharp.Messages.DataType;

/// <summary>
/// The message adapter for E*TRADE API.
/// </summary>
[MediaIcon(Media.MediaNames.etrade)]
[Doc("topics/api/connectors/stock_market/e_trade.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ETradeKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Transactions | MessageAdapterCategories.Level1 | MessageAdapterCategories.Stock)]
public partial class ETradeMessageAdapter : MessageAdapter, IKeySecretAdapter, IDemoAdapter, ITokenAdapter
{
	private HttpClient _client;

	/// <summary>
	/// Initializes a new instance of the adapter.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public ETradeMessageAdapter(IdGenerator transactionIdGenerator) : base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	[BasicSetting]
	public SecureString Key { get; set; }
	/// <inheritdoc />
	[BasicSetting]
	public SecureString Secret { get; set; }
	/// <summary>
	/// OAuth access token.
	/// </summary>
	[BasicSetting]
	public SecureString Token { get; set; }
	/// <summary>
	/// OAuth access token secret.
	/// </summary>
	[BasicSetting]
	public SecureString AccessSecret { get; set; }
	/// <inheritdoc />
	[BasicSetting]
	public bool IsDemo { get; set; }

	private Uri BaseAddress => new(IsDemo ? "https://apisb.etrade.com/" : "https://api.etrade.com/");

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (Key.IsEmpty() || Secret.IsEmpty() || Token.IsEmpty() || AccessSecret.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		_client = new() { BaseAddress = BaseAddress };
		await base.ConnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		_client?.Dispose();
		_client = null;
		await base.ResetAsync(message, cancellationToken);
	}

	private async Task<JToken> Send(HttpMethod method, string path, JToken body, CancellationToken cancellationToken)
	{
		var uri = new Uri(BaseAddress, path);
		using var request = new HttpRequestMessage(method, uri);
		request.Headers.TryAddWithoutValidation("Authorization", ETradeSigner.CreateHeader(method.Method, uri, Key.UnSecure(), Secret.UnSecure(), Token.UnSecure(), AccessSecret.UnSecure()));
		if (body is not null)
			request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
		using var response = await _client.SendAsync(request, cancellationToken);
		var text = await response.Content.ReadAsStringAsync(cancellationToken);
		response.EnsureSuccessStatusCode();
		return text.IsEmpty() ? null : JToken.Parse(text);
	}

	private Task<JToken> Get(string path, CancellationToken cancellationToken)
		=> Send(HttpMethod.Get, path, null, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var data = await Get($"v1/market/lookup/{(message.SecurityId.SecurityCode ?? string.Empty).DataEscape()}.json", cancellationToken);
		foreach (var item in data.SelectTokens("$..Data"))
			await SendOutMessageAsync(new SecurityMessage { OriginalTransactionId = message.TransactionId, SecurityId = new() { SecurityCode = item.Value<string>("symbol"), BoardCode = BoardCodes.Nasdaq }, Name = item.Value<string>("description"), SecurityType = SecurityTypes.Stock, Currency = CurrencyTypes.USD, PriceStep = 0.01m }, cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (!message.IsSubscribe)
			return;
		var data = (await Get($"v1/market/quote/{message.SecurityId.SecurityCode.DataEscape()}.json?detailFlag=ALL", cancellationToken)).SelectToken("$..All");
		await SendOutMessageAsync(new Level1ChangeMessage { OriginalTransactionId = message.TransactionId, SecurityId = message.SecurityId, ServerTime = DateTime.UtcNow }
			.TryAdd(Level1Fields.BestBidPrice, data?["bid"]?.Value<decimal?>()).TryAdd(Level1Fields.BestAskPrice, data?["ask"]?.Value<decimal?>()).TryAdd(Level1Fields.LastTradePrice, data?["lastTrade"]?.Value<decimal?>()), cancellationToken);
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage) { base.Save(storage); storage.Set(nameof(Key), Key).Set(nameof(Secret), Secret).Set(nameof(Token), Token).Set(nameof(AccessSecret), AccessSecret).Set(nameof(IsDemo), IsDemo); }
	/// <inheritdoc />
	public override void Load(SettingsStorage storage) { base.Load(storage); Key = storage.GetValue<SecureString>(nameof(Key)); Secret = storage.GetValue<SecureString>(nameof(Secret)); Token = storage.GetValue<SecureString>(nameof(Token)); AccessSecret = storage.GetValue<SecureString>(nameof(AccessSecret)); IsDemo = storage.GetValue<bool>(nameof(IsDemo)); }
}
