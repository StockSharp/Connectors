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
	private static readonly string[] _readmeFiles =
	[
		"README.md",
		"README_ru.md",
		"README_zh.md",
		"README_es.md",
		"README_de.md",
		"README_pt.md",
		"README_ja.md",
	];

	private static readonly Regex _classRegex = new(
		@"(?<attributes>(?:^[ \t]*\[[^\]]+\]\s*)*)^[ \t]*(?:(?:public|internal|private|protected|sealed|abstract|partial)\s+)*class\s+(?<name>\w+MessageAdapter)\b(?<bases>\s*:\s*[^\{]+)?",
		RegexOptions.Compiled | RegexOptions.Multiline);

	private static readonly Regex _baseAdapterRegex = new(
		@"^\s*:\s*(?:global::)?(?:\w+\.)*\w*MessageAdapter\b",
		RegexOptions.Compiled);

	private static readonly Regex _docRegex = new(
		@"\[Doc\(\s*""(?<path>[^""]+)""\s*\)\]",
		RegexOptions.Compiled);

	private static readonly Lazy<string> _repositoryRoot = new(FindRepositoryRoot);
	private static readonly Lazy<string[]> _connectorProjectPaths = new(() => GetConnectorProjectPaths(_repositoryRoot.Value));
	private static readonly Lazy<AdapterInfo[]> _adapters = new(LoadAdapters);

	private sealed record AdapterInfo(string Project, string Type, string DocPath);

	private sealed record PageCheckResult(string Page, HttpStatusCode? StatusCode, string Error)
	{
		public bool IsSuccess => StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices;
	}

	[TestMethod]
	public void EveryAdapterHasDocumentation()
	{
		var adapters = _adapters.Value;
		var undocumented = adapters
			.Where(a => a.DocPath.IsEmpty())
			.Select(a => $"{a.Project}: {a.Type}")
			.ToArray();

		if (undocumented.Length > 0)
			Fail($"The following adapters do not have a Doc attribute:{Environment.NewLine}{string.Join(Environment.NewLine, undocumented)}");
	}

	[TestMethod]
	public void EveryConnectorHasLocalizedReadmes()
	{
		var root = _repositoryRoot.Value;
		var failures = new List<string>();

		foreach (var projectPath in _connectorProjectPaths.Value)
		{
			var projectDirectory = Path.GetDirectoryName(Path.Combine(root, projectPath));

			if (!Directory.Exists(projectDirectory))
			{
				failures.Add($"{projectPath}: project directory does not exist.");
				continue;
			}

			var actualReadmes = Directory
				.EnumerateFiles(projectDirectory, "README*.md", SearchOption.TopDirectoryOnly)
				.Select(Path.GetFileName)
				.ToHashSet(StringComparer.Ordinal);

			foreach (var readme in _readmeFiles)
			{
				if (!actualReadmes.Contains(readme))
					failures.Add($"{projectPath}: missing {readme}.");
				else if (new FileInfo(Path.Combine(projectDirectory, readme)).Length == 0)
					failures.Add($"{projectPath}: {readme} is empty.");
			}

			foreach (var readme in actualReadmes.Except(_readmeFiles, StringComparer.Ordinal).Order(StringComparer.Ordinal))
				failures.Add($"{projectPath}: unexpected {readme}.");
		}

		if (failures.Count > 0)
			Fail($"Connector projects must contain exactly the seven localized README files:{Environment.NewLine}{string.Join(Environment.NewLine, failures.Order(StringComparer.Ordinal))}");
	}

	[TestMethod]
	public void ConnectorProjectsPreserveReadmePackageContract()
	{
		var root = _repositoryRoot.Value;
		var failures = new List<string>();
		var commonPropsPath = Path.Combine(root, "common_connectors.props");
		var webSocketPropsPath = Path.Combine(root, "common_connectors_websocket.props");

		try
		{
			var commonProps = XDocument.Load(commonPropsPath);
			var packageReadmes = GetElements(commonProps, "PackageReadmeFile")
				.Select(element => element.Value.Trim())
				.ToArray();

			if (packageReadmes.Length != 1 ||
				!packageReadmes[0].Equals("README.md", StringComparison.Ordinal))
				failures.Add("common_connectors.props must define PackageReadmeFile exactly once as README.md.");

			var packedReadmes = GetElements(commonProps, "None")
				.Where(element => ReferencesReadme(element.Attribute("Include")?.Value))
				.ToArray();

			if (packedReadmes.Length != 1 ||
				packedReadmes[0].Attribute("Pack")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) != true ||
				packedReadmes[0].Attribute("PackagePath")?.Value != string.Empty)
				failures.Add("common_connectors.props must pack README.md with Pack=\"true\" and PackagePath=\"\".");
		}
		catch (Exception ex)
		{
			failures.Add($"common_connectors.props cannot be read: {ex.Message}");
		}

		try
		{
			var webSocketProps = XDocument.Load(webSocketPropsPath);
			var importsCommonProps = GetElements(webSocketProps, "Import")
				.Select(element => element.Attribute("Project")?.Value)
				.Any(IsCommonConnectorProps);

			if (!importsCommonProps)
				failures.Add("common_connectors_websocket.props must import common_connectors.props.");
		}
		catch (Exception ex)
		{
			failures.Add($"common_connectors_websocket.props cannot be read: {ex.Message}");
		}

		foreach (var projectPath in _connectorProjectPaths.Value)
		{
			var fullPath = Path.Combine(root, projectPath);

			try
			{
				var project = XDocument.Load(fullPath);
				var importsConnectorProps = GetElements(project, "Import")
					.Select(element => element.Attribute("Project")?.Value)
					.Any(IsConnectorProps);

				if (!importsConnectorProps)
					failures.Add($"{projectPath}: does not import common_connectors.props or common_connectors_websocket.props.");

				foreach (var element in GetElements(project, "PackageReadmeFile"))
				{
					var value = element.Value.Trim();

					if (!value.Equals("README.md", StringComparison.Ordinal))
						failures.Add($"{projectPath}: overrides PackageReadmeFile with '{value}'.");
				}

				foreach (var item in GetElements(project, "None"))
				{
					if (ReferencesReadme(item.Attribute("Remove")?.Value))
						failures.Add($"{projectPath}: removes README.md from the None items.");

					var updatesReadme =
						ReferencesReadme(item.Attribute("Include")?.Value) ||
						ReferencesReadme(item.Attribute("Update")?.Value);
					var disablesPacking = item.Attribute("Pack")?.Value
						.Equals("false", StringComparison.OrdinalIgnoreCase) == true;

					if (updatesReadme && disablesPacking)
						failures.Add($"{projectPath}: marks README.md with Pack=\"false\".");
				}
			}
			catch (Exception ex)
			{
				failures.Add($"{projectPath}: project XML cannot be read: {ex.Message}");
			}
		}

		if (failures.Count > 0)
			Fail($"Connector README NuGet contract violations:{Environment.NewLine}{string.Join(Environment.NewLine, failures.Order(StringComparer.Ordinal))}");
	}

	[TestMethod]
	public async Task DocumentationPagesExist()
	{
		var adapters = _adapters.Value;
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
		var repositoryRoot = _repositoryRoot.Value;
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

	private static AdapterInfo[] LoadAdapters()
	{
		var root = _repositoryRoot.Value;
		var adapters = new List<AdapterInfo>();

		foreach (var projectPath in _connectorProjectPaths.Value)
		{
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
				.OrderBy(p => p.Key, StringComparer.Ordinal)
				.ToArray();

			if (adapterDeclarations.Length == 0)
				Fail($"Project '{project}' must declare at least one message adapter.");

			foreach (var adapter in adapterDeclarations)
			{
				var docPaths = adapter.Value
					.SelectMany(d => _docRegex.Matches(d.Attributes).Cast<Match>())
					.Select(m => m.Groups["path"].Value)
					.Distinct(StringComparer.Ordinal)
					.ToArray();

				if (docPaths.Length > 1)
					Fail($"Adapter '{adapter.Key}' has more than one Doc path: {string.Join(", ", docPaths)}.");

				adapters.Add(new AdapterInfo(project, adapter.Key, docPaths.FirstOrDefault() ?? string.Empty));
			}
		}

		return [.. adapters];
	}

	private static string[] GetConnectorProjectPaths(string root)
		=>
		[
			.. Directory
				.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
				.Select(path => Path.GetRelativePath(root, path))
				.Select(NormalizeProjectPath)
				.Where(path => IsConnectorProjectPath(root, path))
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

	private static bool IsConnectorProjectPath(string root, string path)
	{
		var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (segments.Length <= 1 ||
			!path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
			segments.Any(segment =>
				segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
				segment.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
				segment.Equals("Tests", StringComparison.OrdinalIgnoreCase) ||
				segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
				segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
				segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase)))
			return false;

		var project = XDocument.Load(Path.Combine(root, path));

		return !GetElements(project, "IsConnectorProject")
			.Any(element => element.Value.Trim()
				.Equals("false", StringComparison.OrdinalIgnoreCase));
	}

	private static string NormalizeProjectPath(string path)
		=> path.Replace('\\', '/').TrimStart('/');

	private static IEnumerable<XElement> GetElements(XContainer document, string localName)
		=> document.Descendants().Where(element =>
			element.Name.LocalName.Equals(localName, StringComparison.Ordinal));

	private static bool ReferencesReadme(string itemSpec)
		=> !itemSpec.IsEmpty() && itemSpec
			.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(value => value.Replace('\\', '/'))
			.Any(value => value.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
				value.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase));

	private static bool IsConnectorProps(string project)
		=> IsCommonConnectorProps(project) ||
			NormalizeProjectPath(project ?? string.Empty)
				.EndsWith("common_connectors_websocket.props", StringComparison.OrdinalIgnoreCase);

	private static bool IsCommonConnectorProps(string project)
		=> NormalizeProjectPath(project ?? string.Empty)
			.EndsWith("common_connectors.props", StringComparison.OrdinalIgnoreCase);

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
