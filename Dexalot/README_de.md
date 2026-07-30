# Dexalot-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Dexalot-Konnektor** verbindet StockSharp mit dem zentralen On-Chain-Limit-Orderbuch von Dexalot auf der Avalanche-basierten Dexalot L1. Er kombiniert öffentliche REST- und WebSocket-Daten mit EVM-Vertragsaufrufen für Spothandel und Kontostände.

## Wichtige Funktionen

- Instrumentensuche und Referenzdaten für Dexalot-Spot-Tokenpaare.
- Level-1- und Orderbuch-Snapshots aus Vertragsabfragen mit anschließenden Live-Aktualisierungen über WebSocket; historische Level-1- und Orderbuchereignisse sind nicht verfügbar.
- WebSocket-Streams für Trades und Kerzen mit Datums- und Mengenfilterung innerhalb der vom Anbieter gelieferten Historie und anschließender Live-Auslieferung.
- Kerzenintervalle von 5, 15 und 30 Minuten, 1 und 4 Stunden sowie 1 Tag.
- Limit- und Marktaufträge auf der Blockchain einschließlich Post-only-Verhalten und konfigurierbarer Vermeidung von Eigengeschäften.
- Ändern, einzelnes Stornieren und Sammelstornierung von Aufträgen; Iceberg-Aufträge, absolute Ablauffristen und gruppenweises Schließen von Positionen werden nicht unterstützt.
- Token-Guthaben des Portfolios, Auftrags- und Ausführungshistorie sowie Abgleich des privaten Zustands über REST, WebSocket und EVM-RPC.

## Typische Verwendung

Verwenden Sie diesen Konnektor für Live-Spotstrategien, Handelsterminals und Order-Management-Dienste, die das Dexalot-Orderbuch und On-Chain-Ausführung benötigen.

Für den Handel sind Wallet-Adresse und privater Schlüssel erforderlich; außerdem fallen Gas-Kosten und Bestätigungslatenzen an. Verfügbare Paare, Stream-Rückblick, API-Limits, Vertragsverfügbarkeit und Finalität hängen von Dexalot und den gewählten Netzwerkendpunkten ab.
