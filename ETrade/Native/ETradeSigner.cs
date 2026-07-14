namespace StockSharp.ETrade.Native;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

using Ecng.Common;

static class ETradeSigner
{
	private static string Encode(string value) => value.DataEscape().Replace("%7E", "~");

	public static string Sign(string method, Uri uri, string consumerKey, string consumerSecret, string token, string tokenSecret, string timestamp, string nonce, bool includeVersion = false)
	{
		var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
		{
			["oauth_consumer_key"] = consumerKey, ["oauth_nonce"] = nonce, ["oauth_signature_method"] = "HMAC-SHA1",
			["oauth_timestamp"] = timestamp, ["oauth_token"] = token,
		};
		if (includeVersion)
			parameters["oauth_version"] = "1.0";
		foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var pair = part.Split('=', 2);
			parameters[Uri.UnescapeDataString(pair[0])] = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
		}

		var normalized = parameters.Select(p => $"{Encode(p.Key)}={Encode(p.Value)}").Join("&");
		var baseUri = $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}{uri.AbsolutePath}";
		var source = $"{method.ToUpperInvariant()}&{Encode(baseUri)}&{Encode(normalized)}";
		using var hmac = new HMACSHA1($"{Encode(consumerSecret)}&{Encode(tokenSecret)}".UTF8());
		return hmac.ComputeHash(source.UTF8()).Base64();
	}

	public static string CreateHeader(string method, Uri uri, string consumerKey, string consumerSecret, string token, string tokenSecret)
	{
		var timestamp = DateTime.UtcNow.ToUnix().ToString();
		var nonce = Guid.NewGuid().ToString("N");
		var signature = Sign(method, uri, consumerKey, consumerSecret, token, tokenSecret, timestamp, nonce);
		return "OAuth " + new Dictionary<string, string>
		{
			["oauth_consumer_key"] = consumerKey, ["oauth_token"] = token, ["oauth_signature_method"] = "HMAC-SHA1",
			["oauth_timestamp"] = timestamp, ["oauth_nonce"] = nonce, ["oauth_signature"] = signature,
		}.Select(p => $"{p.Key}=\"{Encode(p.Value)}\"").Join(",");
	}
}
