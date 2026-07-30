# WazirX-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **WazirX-Konnektor** verbindet StockSharp mit der zentralisierten Kryptowährungs-Spotbörse WazirX. REST- und WebSocket-Daten sowie Kontovorgänge werden in das einheitliche StockSharp-Nachrichtenmodell übersetzt.

## Wichtige Funktionen

- Spotmarktsuche mit Preis- und Mengenschritten sowie Handelsregeln.
- Echtzeit-Level-1, Tick-Trades, Orderbücher und Zeitrahmenkerzen über öffentliche Streams.
- REST-Snapshots und verfügbare Trade- und Kerzenhistorie vor der Live-Fortsetzung.
- Limit- und unterstützte Stop-Limit-Orders, einzelne oder gefilterte Gruppenstornierung sowie Order- und Ausführungsstatus.
- Salden und Portfolios über private Streams mit REST-Abgleich.
- Private Vorgänge benötigen API-Schlüssel und Secret; öffentliche Daten funktionieren ohne Handelszugangsdaten.
- Market-Orders und atomare Orderersetzung werden vom Adapter nicht angeboten.
- Authentifizierung, Symbole, Transporte, Filter und Formate werden hinter der StockSharp-API verborgen.

## Typische Verwendung

Geeignet für WazirX-Spotterminals, Live-Strategien, Charts, Kontoüberwachung und Ordermanagement.

Märkte, Historie, Stop-Limit-Unterstützung, Rechte, Filter, Limits und Verfügbarkeit werden von WazirX und dem Konto bestimmt.
