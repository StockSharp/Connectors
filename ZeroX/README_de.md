# 0x-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **0x-Konnektor** verbindet StockSharp mit 0x Swap API v2 und unterstützten EVM-Netzen. Konfigurierte Tokenpaare werden als Instrumente dargestellt; ausführbare Preise, Wallet-Salden und geroutete Swaps werden in StockSharp-Nachrichten übersetzt.

## Wichtige Funktionen

- Erkennung konfigurierter Tokenpaare auf Ethereum, Optimism, BNB Chain, Polygon, Base, Arbitrum, Avalanche und Linea.
- Per Abfrage ermittelte Level-1-Geld- und Briefpreise aus ausführbaren 0x-Preisproben.
- Abruf, Signierung und Übertragung sofortiger Market-Swaps über JSON-RPC des gewählten Netzes.
- Optionale automatische Allowance-Freigabe sowie Slippage-, Probevolumen- und Belegzeitlimit-Einstellungen.
- Wallet-Tokensalden und Verfolgung von Belegen, Orderstatus und Ausführungen.
- Konfigurierbare 0x-Dashboard-API-ID, Wallet, Tokenpaare sowie API- und RPC-Endpunkte.
- Keine Tick-Trades, Orderbücher, Kerzen, Historie, ruhenden Orders, Ersetzung oder Stornierung.
- Routen, Einheiten, Freigaben, Signierung und EVM-Belege werden hinter der StockSharp-API verborgen.

## Typische Verwendung

Geeignet für die Überwachung ausführbarer Tokenpreise, Wallet-Dashboards und direkte, von 0x geroutete Swaps in einem unterstützten EVM-Netz.

Paare, Routen, Liquidität, Preiseinfluss, Gas, Freigaben, Finalität und Limits hängen von 0x, Netz und RPC-Anbieter ab.
