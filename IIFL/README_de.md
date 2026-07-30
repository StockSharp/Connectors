# IIFL-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **IIFL-Konnektor** verbindet StockSharp über die IIFL Markets Open API mit indischen Börsenmarktdaten und Brokerage-Funktionen. Er überführt die REST- und MQTT-Dienste von IIFL in das standardisierte StockSharp-Nachrichtenmodell.

## Wichtige Funktionen

- Instrumentensuche in NSE, BSE sowie Segmenten für Aktien-, Währungs- und Rohstoffderivate einschließlich Aktien, Indizes, Futures und Optionen.
- Level-1-Snapshots, Orderbücher mit fünf Ebenen und Live-Tick-Aktualisierungen über REST und den offiziellen MQTT-Stream.
- Historische Kerzen und Polling-Aktualisierungen für 1, 5, 10, 15 und 30 Minuten, 1 Stunde, 1 Tag, 1 Woche und 1 Monat.
- Markt-, Limit-, Stop-Limit- und Stop-Markt-Aufträge mit Änderung und einzelner Stornierung; Sammelstornierung wird nicht unterstützt.
- IIFL-spezifische Produkte und Auftragskomplexitäten, Auslösepreise, offengelegte Mengen, Marktpreisschutz, Algorithmuskennungen und Kunden-Tags.
- Portfoliomittel, Bestände, Positionen, Auftragsstatus und Ausführungen über REST-Snapshots und private MQTT-Aktualisierungen.
- Konfigurierbares MQTT-Streaming und REST-Polling für privaten Zustand und aktive Kerzen.

## Typische Verwendung

Verwenden Sie diesen Konnektor für Handelsterminals am indischen Markt, Live-Strategien, Order-Management-Dienste, Portfolioüberwachung und kerzenbasierte Analysen.

Die Verbindung erfordert IIFL-Anwendungszugangsdaten, eine Kundenkennung und entweder die tägliche Autorisierung oder einen vorhandenen Sitzungstoken. Instrumente, Marktdatenrechte, Auftragsfunktionen, Handelszeiten und Anfragelimits hängen vom IIFL-Konto und Börsensegment ab.
