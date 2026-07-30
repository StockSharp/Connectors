# Velora-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Velora-Konnektor** verbindet StockSharp mit der Velora Market API und unterstützten EVM-Netzen. Konfigurierte Tokenpaare werden als Instrumente dargestellt; ausführbare Preise, Wallet-Salden und geroutete Swaps werden in StockSharp-Nachrichten übersetzt.

## Wichtige Funktionen

- Erkennung konfigurierter Tokenpaare auf Ethereum, Optimism, BNB Chain, Gnosis, Polygon, Base, Arbitrum und Avalanche.
- Per Abfrage ermittelte Level-1-Geld- und Briefpreise aus ausführbaren Velora-Routen.
- Aufbau, Signierung und Übertragung sofortiger Market-Swaps über JSON-RPC des gewählten Netzes.
- Optionale automatische Tokenfreigabe sowie Slippage-, Probevolumen- und Belegzeitlimit-Einstellungen.
- Wallet-Tokensalden und Verfolgung von Belegen, Orderstatus und Ausführungen.
- Konfigurierbare Velora-Partner-ID, Wallet, Tokenpaare sowie API- und RPC-Endpunkte.
- Keine Tick-Trades, Orderbücher, Kerzen, Historie, ruhenden Orders, Ersetzung oder Stornierung.
- Routen, Einheiten, Freigaben, Signierung und EVM-Belege werden hinter der StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Tokenpreisüberwachung, Wallet-Dashboards und direkte, von Velora geroutete Swaps in einem unterstützten EVM-Netz.

Paare, Routen, Liquidität, Preiseinfluss, Gas, Freigaben, Finalität und Limits hängen von Velora, Netz und RPC-Anbieter ab.
