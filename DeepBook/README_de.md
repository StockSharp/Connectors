# DeepBook-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **DeepBook-Konnektor** verbindet StockSharp mit dem DeepBook-Liquiditätsprotokoll auf Sui. Er kombiniert den öffentlichen DeepBook-Indexer mit einem Sui-Fullnode-gRPC-Endpunkt für Pooldaten, Wallet-Salden und lokal signierte sofortige Swaps.

## Wichtige Funktionen

- DeepBook-Pools suchen und optional nach Poolname, ID oder Instrumentcode filtern.
- Level-1-Snapshots, Orderbuchtiefe sowie historische oder abgefragte Tick-Trades beziehen.
- Zeitrahmenkerzen von 1 Minute bis 7 Tagen laden und per Polling aktualisieren.
- Indexer, Sui-Fullnode, Paket, Clock-Objekt, Tiefe, Historie und Polling konfigurieren.
- Sui-Token-Salden bei konfigurierter Wallet-Adresse als StockSharp-Portfolio bereitstellen.
- Eine Market-Order als lokal signierten DeepBook-Swap mit konfigurierbarem Slippage-Schutz senden.
- Den entstandenen Sui-Transaktionsdigest und die Swap-Ausführung verfolgen.

## Typische Verwendung

Der Konnektor eignet sich zur Überwachung von DeepBook-Pools, zur Erfassung von Sui-DEX-Marktdaten oder zur Ausführung sofortiger Swaps aus einer konfigurierten Wallet. Öffentliche Daten benötigen keinen privaten Schlüssel; Portfoliodaten erfordern eine Wallet-Adresse und Swaps deren Ed25519-Signierschlüssel.

Die Transaktionsschnittstelle bildet sofortige Swaps statt ruhender DeepBook-Orders ab. Limit-, bedingte, Post-only- und Gültigkeitsorders sind nicht verfügbar; eine ausgeführte Sui-Transaktion kann weder storniert noch ersetzt oder gesammelt storniert werden. Polling-Latenz, Indexer-Abdeckung, Slippage, Gas, Liquidität und Sui-Finalität beeinflussen das Ergebnis.
