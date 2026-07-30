# CoinSwitch-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **CoinSwitch-Konnektor** verbindet StockSharp mit den CoinSwitch-PRO-APIs. Über die Produkteinstellung werden INR- oder USDT-Spotmärkte, USDT-marginierte Perpetual Futures oder die private HFT-Beta für Optionen ausgewählt.

## Wichtige Funktionen

- Instrumente für das ausgewählte CoinSwitch-Produkt suchen.
- Level 1, Markttiefe, Tick-Trades und Zeitrahmenkerzen abonnieren.
- Kerzenhistorien laden und Live-Updates per WebSocket empfangen, sofern Produkt und Intervall dies unterstützen.
- Spot-Limit-Orders, Futures-Limit-, Market- oder Stop-Market-Orders sowie HFT-Options-Limit- oder Market-Orders aufgeben.
- Reduce-only für unterstützte Derivateorders und verfügbare Gültigkeitsmodi für HFT-Optionen nutzen.
- Einzelne oder Gruppen passender Orders stornieren.
- Salden, Positionen, offene und historische Orders sowie eigene Trades laden.

## Typische Verwendung

Der Konnektor eignet sich für die CoinSwitch-PRO-Marktbeobachtung und den automatisierten Handel auf einer ausgewählten Produktoberfläche. Private Vorgänge erfordern API-Schlüssel und Ed25519-Secret mit passenden Rechten; Optionen benötigen zusätzlich Zugang zur privaten CoinSwitch-HFT-Beta.

Die Funktionen unterscheiden sich je Produkt: Spot-Eingabe ist auf Limit-Orders beschränkt, bedingte Eingabe ist nur bei Futures als Stop-Market umgesetzt, und Optionskerzen werden nicht per WebSocket gestreamt. Atomarer Ersatz, Iceberg- und GTD-Orders, inkrementelle Orderbücher und Orderlogs werden nicht unterstützt. CoinSwitch-Rechte und Ratenlimits gelten.
