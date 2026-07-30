# CoinCatch-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **CoinCatch-Konnektor** verbindet StockSharp mit den Spot- und Derivatemärkten von CoinCatch. Über die Produkteinstellung wird zwischen Spot, USDT-marginierten Futures und Coin-marginierten Futures gewählt; REST- und WebSocket-APIs liefern Marktdaten und authentifizierten Handel.

## Wichtige Funktionen

- Instrumente für das ausgewählte CoinCatch-Produkt suchen.
- Level 1, Markttiefe, Tick-Trades und Zeitrahmenkerzen abonnieren.
- Historische Kerzen laden und anschließend Live-Aktualisierungen per WebSocket empfangen.
- Limit- und Market-Orders einschließlich Reduce-only für Futures und Post-only für Limit-Orders aufgeben.
- Einzelne Orders oder alle Orders eines Symbols stornieren.
- Salden, Positionen, offene und historische Orders sowie eigene Trades laden.
- Privaten Status mit API-Schlüssel, Secret und Passphrase authentifiziert abgleichen.

## Typische Verwendung

Der Konnektor eignet sich zur Beobachtung von Spot- oder Futures-Märkten, zum Laden von Kerzenhistorien und zum automatisierten Handel auf CoinCatch. Das Produkt wird vor dem Verbinden gewählt; private Vorgänge erfordern Zugangsdaten mit passenden Lese- oder Handelsrechten.

Der Adapter stellt keine Plan- oder Trigger-Orders von CoinCatch, Iceberg-Orders oder atomare Orderänderungen bereit. Orderbücher sind snapshotbasiert und ein Orderlog-Datenstrom fehlt. Instrumentregeln, Kontomodus, API-Berechtigungen und Ratenlimits der Börse sind zu beachten.
