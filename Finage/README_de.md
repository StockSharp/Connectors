# Finage-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Finage-Konnektor** verbindet StockSharp mit den Forex-Marktdatendiensten von Finage. Der ausschließlich lesende Adapter für Währungsinstrumente kombiniert REST-Referenz- und Historiendaten mit einem optionalen WebSocket-Kursstream.

## Wichtige Funktionen

- Suche nach Währungspaaren aus einer konfigurierten Symbolliste oder über die REST-Symbolsuche des Anbieters.
- Aktuelle Snapshots der besten Geld- und Briefkurse über REST.
- Live-Aktualisierungen der Level-1-Geld- und Briefkurse über WebSocket, wenn ein separater Streaming-Token konfiguriert ist.
- Historische Kerzen über REST für 1, 5, 10, 15 und 30 Minuten, 1, 2, 4, 6, 8 und 12 Stunden, 1 Tag und 1 Woche.
- Konfigurierbares Anfrageintervall und maximale Instrumentenanzahl zur Steuerung der REST-Nutzung.
- Historische Level-1-Ereignisse und Live-Kerzenaktualisierungen werden nicht unterstützt.
- Keine Tick-Trades, Orderbücher, Auftragserteilung, Portfoliodaten oder Kontovorgänge.

## Typische Verwendung

Verwenden Sie diesen Konnektor für Forex-Beobachtungslisten, Kursüberwachung, Charts, Research und Backtests auf Basis der Finage-Kerzenhistorie.

Ein Finage-REST-API-Schlüssel ist erforderlich; Live-Kurse benötigen zusätzlich einen Streaming-Token. Symbolabdeckung, Historientiefe, Echtzeitzugriff und Anfragelimits hängen vom gebuchten Finage-Tarif ab.
