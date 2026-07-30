# SEC-EDGAR-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **SEC-EDGAR-Konnektor** bietet StockSharp schreibgeschützten Zugriff auf offizielle Einreichungen der US-Börsenaufsicht. Emittenten, Dokumente und XBRL-Unternehmensfakten werden auf Wertpapiere, Nachrichten und einen eigenen Fundamentaldatentyp von StockSharp abgebildet.

## Wichtige Funktionen

- Unternehmenssuche nach Ticker oder CIK über den SEC-Tickerkatalog.
- Einreichungen als StockSharp-Nachrichten, einschließlich aktueller Meldungen und einer konfigurierbaren Zahl historischer Dateien.
- Filter für Formulare wie 10-K, 10-Q, 8-K, 20-F, 40-F und 6-K.
- XBRL-Unternehmensfakten mit Datums- und Mengenfiltern über den Datentyp Company Facts.
- Endliche REST-Abfragen für historische Sammlung und periodische Aktualisierung; kein Push-Stream.
- Kein API-Schlüssel erforderlich, jedoch verlangt die SEC einen identifizierenden User-Agent und angemessene Abstände.
- Keine Kurse, Trades, Orderbücher, Kerzen, Portfolios oder Orderfunktionen.
- SEC-Endpunkte, CIKs, Historiendateien und Formate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Einreichungsmonitore, Fundamentalanalyse, Emittentenauswahl und Datensätze, die SEC-Offenlegungen mit Marktdaten anderer Konnektoren verbinden.

Abdeckung und Aktualität hängen von SEC-Veröffentlichungen ab; Intervalle, Datei- und Faktenlimits sowie Formularfilter werden durch Einstellungen und SEC-Zugriffsrichtlinien bestimmt.
