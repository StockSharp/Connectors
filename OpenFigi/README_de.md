# OpenFIGI-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **OpenFIGI-Konnektor** verbindet StockSharp mit einem Dienst zur Zuordnung von Finanzinstrumentenkennungen und für Referenzdaten. Er übersetzt anbieterspezifische Ergebnisse in das einheitliche StockSharp-Instrumentenmodell, sodass Anwendungen konsistente Kennungen aus verschiedenen Datenquellen verwenden können.

## Wichtige Funktionen

- Typische Abdeckung: globale Finanzinstrumente und Kennungsmetadaten.
- Zuordnung über FIGI, ISIN, CUSIP, SEDOL, Ticker oder einen anderen OpenFIGI-Kennungstyp.
- Instrumentensuche und Filterung nach Börsencode, MIC, Währung, Marktsektor und Instrumententyp.
- Normalisierte StockSharp-Instrumentennachrichten mit Referenzdaten und Kennungen des Anbieters.
- Dieser Adapter ist schreibgeschützt: Er liefert keine Preisströme und leitet keine Orders weiter.
- Anbieterspezifischer REST-Transport, Seitennavigation, Drosselung und Antwortformate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für Wertpapierstammdaten, Kennungsanreicherung, den Abgleich zwischen Datenanbietern und die Aufnahme von Instrumenten in StockSharp-Abläufe.

Zuordnungen, Suchergebnisse, Seitengrößen, Limits und Verfügbarkeit werden von OpenFIGI und davon bestimmt, ob ein API-Schlüssel konfiguriert ist.
