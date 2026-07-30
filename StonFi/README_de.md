# STON.fi-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **STON.fi-Konnektor** verbindet StockSharp mit STON.fi-Liquiditätspools und der TON-Blockchain. Konfigurierte oder gefundene Pools werden als Instrumente dargestellt; Swap-Kurse, Poolereignisse, Wallet-Salden und gesendete Swaps werden in StockSharp-Nachrichten übersetzt.

## Wichtige Funktionen

- Erkennung konfigurierter Pools oder einer begrenzten Auswahl beliebter STON.fi-Pools samt Tokenmetadaten.
- Per Abfrage aktualisierte Level-1-Geld- und Briefkurse aus ausführbaren Swap-Simulationen.
- Historische und aktuelle Tick-Trades aus TON-Poolereignissen sowie daraus erzeugte Zeitrahmenkerzen.
- Sofortige Market-Swaps mit TON-Wallet-V4-Mnemonik, konfigurierbarer Slippage und TON-Center-Übertragung.
- Wallet-Tokensalden sowie verfolgte Order- und Ausführungszustände von Swaps.
- Historie ist durch den konfigurierten TON-Blockbereich begrenzt; Live-Daten beruhen auf Abfragen.
- Kein zentrales Orderbuch, keine ruhenden Limit-Orders, Ersetzung oder Stornierung.
- REST-Daten, TON-Einheiten, Wallet-Signierung und Blockchain-Ereignisse werden hinter der StockSharp-API verborgen.

## Typische Verwendung

Geeignet für TON-DEX-Kursüberwachung, Poolanalyse, Swap-Strategien, Wallet-Tracking und direkte STON.fi-Ausführung.

Poolabdeckung, Kurse, Ereignishistorie, Routing, Gebühren, Finalität und Verfügbarkeit hängen von STON.fi, TON Center, Endpunkten und Blockchainzustand ab.
