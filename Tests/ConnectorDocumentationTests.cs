namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class ConnectorDocumentationTests : BaseTestClass
{
	private const string _docPrefix = "topics/api/connectors/";
	private static readonly Uri _docBaseUri = new("https://doc.stocksharp.com/");

	private static readonly Regex _classRegex = new(
		@"(?<attributes>(?:^[ \t]*\[[^\]]+\]\s*)*)^[ \t]*(?:(?:public|internal|private|protected|sealed|abstract|partial)\s+)*class\s+(?<name>\w+MessageAdapter)\b(?<bases>\s*:\s*[^\{]+)?",
		RegexOptions.Compiled | RegexOptions.Multiline);

	private static readonly Regex _baseAdapterRegex = new(
		@"\b(?:MessageAdapter|HistoricalMessageAdapter|FixMessageAdapter)\b",
		RegexOptions.Compiled);

	private static readonly Regex _docRegex = new(
		@"\[Doc\(\s*""(?<path>[^""]+)""\s*\)\]",
		RegexOptions.Compiled);

	private sealed record AdapterInfo(string Project, string Type, string DocPath);

	private sealed record PageCheckResult(string Page, HttpStatusCode? StatusCode, string Error)
	{
		public bool IsSuccess => StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices;
	}

	[TestMethod]
	public void EveryAdapterHasDocumentation()
	{
		var adapters = GetAdapters();
		var undocumented = adapters
			.Where(a => a.DocPath.IsEmpty())
			.Select(a => $"{a.Project}: {a.Type}")
			.ToArray();

		if (undocumented.Length > 0)
			Fail($"The following adapters do not have a Doc attribute:{Environment.NewLine}{string.Join(Environment.NewLine, undocumented)}");
	}

	[TestMethod]
	public async Task DocumentationPagesExist()
	{
		var adapters = GetAdapters();
		var invalidPaths = adapters
			.Where(a => !a.DocPath.IsEmpty() &&
				(!a.DocPath.StartsWith(_docPrefix, StringComparison.Ordinal) || !a.DocPath.EndsWith(".html", StringComparison.Ordinal)))
			.Select(a => $"{a.Project}: {a.DocPath}")
			.ToArray();

		if (invalidPaths.Length > 0)
			Fail($"The following Doc paths must start with '{_docPrefix}' and end with '.html':{Environment.NewLine}{string.Join(Environment.NewLine, invalidPaths)}");

		var docPaths = adapters
			.Where(a => !a.DocPath.IsEmpty())
			.Select(a => a.DocPath)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();
		var repositoryRoot = FindRepositoryRoot();
		var documentationRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "doc", "en"));

		if (Directory.Exists(documentationRoot))
		{
			var missingFiles = docPaths
				.Select(p => Path.Combine(documentationRoot, $"{p[..^".html".Length]}.md"))
				.Where(p => !File.Exists(p))
				.Select(p => Path.GetRelativePath(documentationRoot, p))
				.ToArray();

			if (missingFiles.Length > 0)
				Fail($"The following documentation pages do not exist:{Environment.NewLine}{string.Join(Environment.NewLine, missingFiles)}");

			return;
		}

		var pages = docPaths
			.Select(p => new Uri(_docBaseUri, p))
			.OrderBy(u => u.AbsoluteUri, StringComparer.Ordinal)
			.ToArray();

		using var handler = new HttpClientHandler { AllowAutoRedirect = true };
		using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Connector-Documentation-Test/1.0");

		using var gate = new SemaphoreSlim(8);
		var results = await Task.WhenAll(pages.Select(async page =>
		{
			await gate.WaitAsync(CancellationToken);

			try
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, page);
				using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken);
				return new PageCheckResult(page.AbsoluteUri, response.StatusCode, string.Empty);
			}
			catch (Exception ex)
			{
				return new PageCheckResult(page.AbsoluteUri, null, ex.Message);
			}
			finally
			{
				gate.Release();
			}
		}));

		var failures = results
			.Where(r => !r.IsSuccess)
			.Select(r => r.StatusCode is null
				? $"{r.Page}: {r.Error}"
				: $"{r.Page}: {(int)r.StatusCode.Value} {r.StatusCode.Value}")
			.ToArray();

		if (failures.Length > 0)
			Fail($"The following documentation pages are unavailable:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
	}

	private static AdapterInfo[] GetAdapters()
	{
		var root = FindRepositoryRoot();
		var solution = XDocument.Load(Path.Combine(root, "Connectors.slnx"));
		var adapters = new List<AdapterInfo>();

		foreach (var projectElement in solution.Descendants("Project"))
		{
			var projectPath = projectElement.Attribute("Path")?.Value;

			if (projectPath.IsEmpty() || projectPath.StartsWith("Tests/", StringComparison.OrdinalIgnoreCase))
				continue;

			var project = Path.GetFileNameWithoutExtension(projectPath);
			var projectDirectory = Path.GetDirectoryName(Path.Combine(root, projectPath));
			var declarations = new Dictionary<string, List<(string Attributes, string Bases)>>(StringComparer.Ordinal);

			foreach (var file in Directory.EnumerateFiles(projectDirectory, "*MessageAdapter*.cs", SearchOption.AllDirectories))
			{
				if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
					file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
					continue;

				foreach (Match match in _classRegex.Matches(File.ReadAllText(file)))
				{
					var type = match.Groups["name"].Value;

					if (!declarations.TryGetValue(type, out var parts))
						declarations.Add(type, parts = []);

					parts.Add((match.Groups["attributes"].Value, match.Groups["bases"].Value));
				}
			}

			var adapterDeclarations = declarations
				.Where(p => p.Value.Any(d => _baseAdapterRegex.IsMatch(d.Bases)))
				.ToArray();

			if (adapterDeclarations.Length != 1)
				Fail($"Project '{project}' must declare exactly one message adapter, but {adapterDeclarations.Length} were found.");

			var adapter = adapterDeclarations[0];
			var docPaths = adapter.Value
				.SelectMany(d => _docRegex.Matches(d.Attributes).Cast<Match>())
				.Select(m => m.Groups["path"].Value)
				.Distinct(StringComparer.Ordinal)
				.ToArray();

			if (docPaths.Length > 1)
				Fail($"Adapter '{adapter.Key}' has more than one Doc path: {string.Join(", ", docPaths)}.");

			adapters.Add(new AdapterInfo(project, adapter.Key, docPaths.FirstOrDefault() ?? string.Empty));
		}

		return [.. adapters];
	}

	private static string FindRepositoryRoot()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Connectors.slnx")))
				return directory.FullName;
		}

		throw new DirectoryNotFoundException("Cannot locate the Connectors repository root.");
	}
}
