# Delta-Exchange-India-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Delta-Exchange-India-Konnektor** verbindet StockSharp mit einem zentralisierten indischen Handelsplatz für Digital-Asset-Derivate. Er überführt Marktdaten zu Futures und Optionen, Aufträge und Kontostände in das standardisierte StockSharp-Nachrichtenmodell.

## Wichtige Funktionen

- Instrumentensuche und Referenzdaten für die bei Delta Exchange India gelisteten Futures und Optionen.
- Level-1-Snapshots über REST und Echtzeitaktualisierungen über WebSocket; historische Level-1-Ereignisse sind nicht verfügbar.
- Jüngste Tick-Historie über REST mit höchstens 50 Trades pro Anfrage sowie Live-Trades über WebSocket.
- Orderbuch-Snapshots und Live-Aktualisierungen mit bis zu 15 Ebenen; inkrementelle und historische Orderbücher werden nicht unterstützt.
- Historische Kerzen mit bis zu 1.999 Balken pro Anfrage und Live-Aktualisierungen für die vom Anbieter unterstützten Intervalle.
- Limit-, Markt- und bedingte Stop-Aufträge einschließlich Post-only und Reduce-only sowie Ändern, Stornieren und Sammelstornierung.
- Aktualisierungen von Portfolio, Guthaben, Positionen, Aufträgen und Ausführungen über authentifiziertes REST und private Streams.

## Typische Verwendung

Verwenden Sie diesen Konnektor für Live-Derivatestrategien, Handelsterminals, Order-Management-Dienste und Analysen, die aktuelle Trades oder Kerzenhistorien von Delta Exchange India benötigen.

Private Vorgänge erfordern API-Zugangsdaten und die nötigen Kontoberechtigungen. Instrumentzugang, Historienumfang, Anfragelimits und regionale Verfügbarkeit bestimmt der Anbieter; Iceberg-Aufträge, absolute Auftragsabläufe und das gruppenweise Schließen von Positionen sind nicht implementiert.
