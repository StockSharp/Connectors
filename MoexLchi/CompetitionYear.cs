namespace StockSharp.MoexLchi;

using System.IO;
using System.Net;
using System.Text;
using System.Web;

using Ecng.IO;
using Ecng.IO.Compression;

using FluentFTP;

using HtmlAgilityPack;

/// <summary>
/// Results of the Best Private Investor contest for the specified year.
/// </summary>
class CompetitionYear
{
	private readonly SynchronizedDictionary<string, int> _memberIds = [];

	//private readonly SynchronizedSet<string> _portfolios = new SynchronizedSet<string>();

	internal CompetitionYear(DateTime year)
	{
		Year = year;
	}

	/// <summary>
	/// BPI year.
	/// </summary>
	public DateTime Year { get; }

	private IEnumerable<string> _members;

	/// <summary>
	/// Competitors.
	/// </summary>
	public async ValueTask<IEnumerable<string>> GetMembers(CancellationToken cancellationToken)
	{
		if (_members == null)
			await InitAsync(cancellationToken);

		return _members;
	}

	private IEnumerable<DateTime> _days;

	/// <summary>
	/// Dates for which there is information about trades.
	/// </summary>
	public async ValueTask<IEnumerable<DateTime>> GetDays(CancellationToken cancellationToken)
	{
		if (_days == null)
			await InitAsync(cancellationToken);

		return _days;
	}

	//private Security GetSecurity(ISecurityStorage securityStorage, string code)
	//{
	//	if (code.IsEmpty())
	//		throw new ArgumentNullException(nameof(code));

	//	var securities = securityStorage.LookupByCode(code).ToArray();

	//	if (securities.Length == 1)
	//		return securities[0];
	//	else if (securities.Length > 1)
	//	{
	//		var s1 = securities.FirstOrDefault(s => s.Board != ExchangeBoard.Finam && s.Board != ExchangeBoard.Forts);

	//		if (s1 != null)
	//			return s1;

	//		s1 = securities.FirstOrDefault(s => s.Board != ExchangeBoard.Finam);

	//		if (s1 != null)
	//			return s1;

	//		return securities[0];
	//	}

	//	var security = securityStorage.LookupByCode(code).FirstOrDefault();

	//	if (security != null)
	//		return security;

	//	security = new Security
	//	{
	//		Code = code,
	//		Board = ExchangeBoard.Forts,
	//	};

	//	var info = SecurityInfoCache.TryGetByCode(code)?.FirstOrDefault();

	//	info?.FillTo(security, _adapter._exchangeInfoProvider);

	//	security.Id = _adapter.SecurityIdGenerator.GenerateId(security.Code, security.Board);

	//	securityStorage.Save(security, false);

	//	return security;
	//}

	/// <summary>
	/// To get trades of the participant on the specified date.
	/// </summary>
	/// <param name="member">Participant.</param>
	/// <param name="date">Date of competition.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Trades of the participant.</returns>
	public async Task<IEnumerable<ExecutionMessage>> GetTradesAsync(string member, DateTime date, CancellationToken cancellationToken)
	{
		if (member.IsEmpty())
			throw new ArgumentNullException(nameof(member));

		if (!(await GetDays(cancellationToken)).Contains(date))
			throw new ArgumentOutOfRangeException(nameof(date), date, LocalizedStrings.InvalidValue);

		if (Year.Year >= 2013)
		{
            using var client = new AsyncFtpClient();

            client.Host = "ftp.moex.com";
            client.Credentials = new NetworkCredential("anonymous", "anonymous");
            client.Config.ReadTimeout = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;

            await client.Connect(cancellationToken);

            if (!_memberIds.TryGetValue(member, out var id))
                throw new ArgumentOutOfRangeException(nameof(member), member, LocalizedStrings.InvalidValue);

            var files = (await client.GetListing("pub/info/stats_contest/{0:yyyy}/{0:yyyyMMdd}".Put(date), cancellationToken))
                .Where(item => item.Name.ContainsIgnoreCase($"_{id}.zip"));

            var results = new List<ExecutionMessage>();

            foreach (var file in files)
            {
                var stream = await DownloadFileAsync(client, file.FullName, cancellationToken);

                var trades = Do.Invariant(() =>
                    stream
                        .Unzip()
                        .SelectMany(t => t.body.EnumerateLines().Select(l =>
                        {
                            var parts = l.SplitByDotComma();

                            var volume = parts[2].To<decimal>();
                            var price = parts[3].To<decimal>();

                            return new ExecutionMessage
                            {
                                DataTypeEx = DataType.OrderLog,
                                TradeId = 1,
                                OrderId = 1,
                                PortfolioName = member,
                                ServerTime = parts[0].ToDateTime("yyyy-MM-dd HH:mm:ss.fff").ApplyMoscow().UtcDateTime,
                                SecurityId = GetSecurityId(parts[1].Trim()),
                                OrderVolume = volume.Abs(),
                                TradePrice = price,
                                OrderPrice = price,
                                Side = (volume > 0) ? Sides.Buy : Sides.Sell,
                                OrderState = OrderStates.Done,
                            };
                        }).ToArray())
                    .ToArray());

                results.AddRange(trades);
            }

            return [.. results];
        }
		else
		{
			var doc = GetWeb().Load("http://investor.moex.com/ru/statistics/{0}/?act=deals&nick={1}&date={2:yyyyMMdd}".Put(Year.Year, member, date));

			var offset = date.Year >= 2009 ? 1 : 0;
			var fieldOffset = date.Year >= 2011 ? 1 : 0;

			return Do.Invariant(() =>
			{
				var table = doc.DocumentNode.SelectNodes("//table[@class='table table-bordered']");

				if (table == null)
					return [];

				return table
					.Last()
					.Descendants("tr")
					.Skip(1)
					.Select(tr =>
					{
						var tds = tr.Descendants("td").Skip(offset).ToArray();

						var securityCode = HttpUtility.HtmlDecode(tds[0 + fieldOffset].InnerText).ReplaceWhiteSpaces().Trim();
						var volume = HttpUtility.HtmlDecode(tds[3 + fieldOffset].InnerText).ReplaceWhiteSpaces().RemoveSpaces().To<decimal>();
						var price = HttpUtility.HtmlDecode(tds[2 + fieldOffset].InnerText).ReplaceWhiteSpaces().RemoveSpaces().Replace(',', '.').To<decimal>();
						var sideStr = HttpUtility.HtmlDecode(tds[1 + fieldOffset].InnerText).ReplaceWhiteSpaces().RemoveSpaces().Trim();

                        var side = sideStr.ToLowerInvariant() switch
                        {
                            "покупка" => Sides.Buy,
                            "продажа" => Sides.Sell,
                            _ => throw new ArgumentOutOfRangeException(sideStr),
                        };

                        var ol = new ExecutionMessage
						{
							DataTypeEx = DataType.OrderLog,
							Side = side,
							TradeId = 1,
							OrderId = 1,
							PortfolioName = member,
							SecurityId = GetSecurityId(securityCode),
							TradePrice = price,
							OrderPrice = price,
							OrderVolume = volume.Abs(),
							ServerTime = (date + HttpUtility.HtmlDecode(tds[4 + fieldOffset].InnerText).ReplaceWhiteSpaces().Trim().ToTimeSpan("c")).ApplyMoscow().UtcDateTime,
							OrderState = OrderStates.Done,
						};

						return ol;
					})
					.ToArray();
			});
		}
	}

	private static SecurityId GetSecurityId(string code)
	{
		return new SecurityId
		{
			SecurityCode = code,
			BoardCode = BoardCodes.Forts,
		};
	}

	private async Task InitAsync(CancellationToken cancellationToken)
	{
		if (Year.Year >= 2013)
		{
            using var client = new AsyncFtpClient();

            client.Host = "ftp.moex.com";
            client.Credentials = new NetworkCredential("anonymous", "anonymous");

            await client.Connect(cancellationToken);

            var yearDir = (await client.GetListing("pub/info/stats_contest/", cancellationToken))
                .FirstOrDefault(item => item.Name == Year.Year.To<string>())
				?? throw new InvalidOperationException("Для {0} года нет данных.".Put(Year.Year));

            _days = [.. (await client.GetListing(yearDir.FullName, cancellationToken))
                    .Where(item => item.Type == FtpObjectType.Directory && !item.Name.EqualsIgnoreCase("all"))
                    .Select(item => item.Name.ToDateTime("yyyyMMdd").UtcKind())
                    .Where(d => d.Year == Year.Year)
                    .OrderBy()];

            var stream = await DownloadFileAsync(client, "pub/info/stats_contest/{0}/trader.csv".Put(Year.Year), cancellationToken);

            var lines = stream.To<byte[]>().Cyrillic().SplitByRN();

            foreach (var line in lines)
            {
                var parts = line.SplitByDotComma();
                _memberIds.TryAdd2(parts[0], parts[1].To<int>());
            }

            _members = [.. _memberIds.Keys];
        }
		else
		{
			var doc = GetWeb().Load($"https://investor.moex.com/ru/statistics/{Year.Year}/default.aspx?act=deals");

			if (Year.Year == 2012)
			{
				_members = Properties.Resources.lci_2012_names.SplitByRN();
			}
			else
			{
				var membersSelect = doc.DocumentNode.SelectSingleNode("//select[@name='nick']");
				_members = membersSelect?.Descendants("option").Select(n => n.Attributes["value"].Value).ToArray() ?? [];
			}

			_days = [.. doc.DocumentNode.SelectSingleNode("//select[@name='date']").Descendants("option").Select(n => n.Attributes["value"].Value.ToDateTime("yyyyMMdd").UtcKind()).OrderBy()];
		}
	}

	private static HtmlWeb GetWeb()
	{
		return new HtmlWeb { OverrideEncoding = Encoding.UTF8 };
	}

	private static async Task<Stream> DownloadFileAsync(IAsyncFtpClient client, string fullPath, CancellationToken cancellationToken)
	{
		if (client == null)
			throw new ArgumentNullException(nameof(client));

		var local = new MemoryStream();

		using (var remote = await client.OpenRead(fullPath, token: cancellationToken))
			await remote.CopyToAsync(local, cancellationToken);

		local.Position = 0;

		return local;
	}
}