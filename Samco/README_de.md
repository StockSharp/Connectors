# Samco-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Samco-Konnektor** verbindet StockSharp über die Samco Trade API mit indischen Wertpapieren und Derivaten. Markt- und Handelsdienste des Brokers stehen über das einheitliche StockSharp-Nachrichtenmodell bereit.

## Wichtige Funktionen

- Suche nach unterstützten Aktien, Futures und Optionen an NSE, BSE, NFO, BFO, CDS, MCX und MFO.
- Echtzeit-Level-1-Kurse, Tick-Trades und fünfstufige Orderbücher über den Samco-Feed.
- Historische Kerzen mit anschließenden Aktualisierungen per Streaming oder REST-Abfrage.
- Übermittlung und Änderung von Limit- und weiteren unterstützten Orders sowie Einzelstornierung; keine atomare Gruppenstornierung.
- Portfoliolimits, Bestände, Positionen, Orders und Trades mit regelmäßigem Abgleich privater Daten.
- Optionales WebSocket-Streaming mit REST-Rückfall sowie konfigurierbare Intervalle und Endpunkte.
- Authentifizierung mit einem gültigen Tagessitzungstoken oder Samco-API-Zugangsdaten gemäß den Sitzungsregeln des Brokers.
- Samco-IDs, Sitzungen und Formate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für indische Handelsterminals, Live-Strategien, Portfolioüberwachung und Ordermanagement mit einem Samco-Konto.

Abdeckung, fünfstufige Tiefe, Historie, Handelsrechte, Limits und Sitzungsdauer werden von Samco und dem verbundenen Konto bestimmt.
