# Velodrome-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Velodrome-Konnektor** verbindet StockSharp mit klassischen und Slipstream-Pools von Velodrome auf Optimism. Konfigurierte Pools, ausführbare Kurse, On-Chain-Swaps, Wallet-Salden und gesendete Transaktionen werden in StockSharp-Nachrichten übersetzt.

## Wichtige Funktionen

- Erkennung konfigurierter klassischer und konzentrierter Liquiditätspools samt Tokenmetadaten.
- Level-1-Geld- und Briefkurse aus ausführbaren Poolproben mit WebSocket und Abfrage-Rückfall.
- Historische und aktuelle Tick-Trades aus Swap-Logs sowie daraus erzeugte Zeitrahmenkerzen.
- Sofortige Market-Swaps, signiert mit optionalem EVM-Schlüssel, einschließlich Allowance-Verwaltung und Slippage.
- Wallet-Tokensalden, Transaktionsbelege sowie Order- und Ausführungsstatus.
- Historische Sammlung ist durch konfigurierte Optimism-Blockbereiche und -anzahlen begrenzt.
- Kein zentrales Orderbuch, keine ruhenden Limit-Orders, atomare Ersetzung oder Stornierung.
- RPC, Tokeneinheiten, Poolvarianten, Signierung und Logs werden hinter der StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Optimism-DEX-Monitoring, Velodrome-Poolanalyse, ereignisbasierte Backtests, Wallet-Tracking und direkte Swaps.

Poolabdeckung, Preise, Liquidität, RPC-Historie, Gas, Finalität und Endpunkte hängen von Velodrome, Optimism und den RPC-Diensten ab.
