# XRPL-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **XRPL-Konnektor** verbindet StockSharp mit der in XRP Ledger integrierten dezentralen Börse. Konfigurierte Währungspaare, Ledger-Orderbücher, ausgeführte Angebote, Kontosalden und signierte Transaktionen werden in StockSharp-Nachrichten übersetzt.

## Wichtige Funktionen

- Erkennung konfigurierter XRP- und ausgegebener Tokenpaare mit optionaler Auswahl einer zugangsbeschränkten DEX-Domäne.
- Level-1-Kurse und Orderbuch-Snapshots mit konfigurierbarer Tiefe und laufenden Ledger-Aktualisierungen.
- Historische und aktuelle Tick-Trades aus Buchänderungen sowie daraus erzeugte Zeitrahmenkerzen.
- Limit-Angebote und preisgeschützte IOC-Market-Angebote sowie Stornierung, Ersetzung und verfolgte Gruppenstornierung.
- Kontosalden, offene Angebote, Orderstatus, Ausführungen, Gebühren und Transaktionsstatus.
- Öffentliche Daten benötigen nur RPC und WebSocket; Handel erfordert klassische Kontoadresse und Family Seed.
- Historie ist durch das Ledger-Scanlimit begrenzt; Live-Snapshots nutzen das konfigurierte Abfrageintervall.
- Beträge, Emittenten, Signierung, Sequenzen, Gebühren und Ereignisse werden hinter der StockSharp-API verborgen.

## Typische Verwendung

Geeignet für XRPL-DEX-Terminals, Ledger-Analysen, historische Studien, Kontoüberwachung und direkte Ausführung von Angeboten.

Paare, Liquidität, Ledger-Historie, Kosten, Finalität, Domänenzugriff und Endpunkte hängen vom XRPL-Zustand und Dienst ab.
