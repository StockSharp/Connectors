# J-Quants-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **J-Quants-Konnektor** verbindet StockSharp über die J-Quants API V2 mit japanischen Referenz- und Historiendaten. Der ausschließlich lesende REST-Adapter ist für Research gedacht, nicht für Live-Marktdatenstreaming oder Handel.

## Wichtige Funktionen

- Instrumentensuche und Referenzdaten für japanische Aktien, Futures und Optionen einschließlich Basiswerten, Ausübungspreisen, Optionstypen und Verfallsterminen.
- Eine einmalige Level-1-Nachricht, die aus dem letzten verfügbaren Tagesbalken erzeugt wird; dies ist kein Live-Kursabonnement.
- Historische Tick-Trades für Aktien; für Futures und Optionen ist keine Tick-Historie verfügbar.
- Historische Aktienkerzen für 1, 5, 15 und 30 Minuten, 1 Stunde und 1 Tag.
- Historische Tageskerzen für Futures und Optionen.
- Konfigurierbare Verzögerung zwischen REST-Aufrufen und maximale Seitentiefe.
- Keine Orderbücher, Live-Aktualisierungen, Auftragserteilung, Portfoliodaten oder Kontovorgänge.

## Typische Verwendung

Verwenden Sie diesen Konnektor für japanische Instrumentenkataloge, historische Analysen, Charts, Datenaufbereitung und Backtests mit J-Quants-Datensätzen.

Ein J-Quants-V2-API-Schlüssel ist erforderlich. Verfügbare Endpunkte, Instrumente, Zeiträume, Paginierung und Anfrageraten hängen vom gebuchten Tarif ab; Level-1-Werte stammen aus einem Tagesbalken und dürfen nicht als Echtzeit-Geld- und Briefkurse behandelt werden.
