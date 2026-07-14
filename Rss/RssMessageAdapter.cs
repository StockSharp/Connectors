namespace StockSharp.Rss;

using System.IO;
using System.Linq;
using System.Xml;
using System.ServiceModel.Syndication;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using StockSharp.Messages;

public partial class RssMessageAdapter
{
	private readonly HttpClient _client = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="RssMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public RssMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddSupportedMessage(MessageTypes.MarketData, true);
		this.AddSupportedMarketDataType(DataType.News);

		_client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AcmeInc/1.0)");
	}

	/// <inheritdoc />
	protected override async ValueTask OnNewsSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var from = mdMsg.From.Value;
		var to = mdMsg.To.Value;
		var left = mdMsg.Count ?? long.MaxValue;

		var str = await _client.GetStringAsync(Address, cancellationToken);

		var ind = str.IndexOf('<');
		if (ind > 0)
			str = str[ind..];

		using var reader = XmlReader.Create(new StringReader(str));

		var feed = SyndicationFeed.Load(reader);

		foreach (var item in feed.Items.OrderBy(i => i.PublishDate))
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (item.PublishDate < from)
				continue;

			if (item.PublishDate > to)
				break;

			await SendOutMessageAsync(new NewsMessage
			{
				Id = item.Id,
				Source = feed.Authors.Select(a => a.Name).JoinComma(),
				ServerTime = item.PublishDate.UtcDateTime,
				Headline = item.Title.Text,
				Story = item.Summary == null ? string.Empty : item.Summary.Text,
				Url = item.Links.Any() ? item.Links[0].Uri.ToString() : null,
				OriginalTransactionId = mdMsg.TransactionId,
			}, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}
}