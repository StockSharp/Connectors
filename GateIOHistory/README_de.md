# Gate.io-Konnektor für historische Daten

Der **Gate.io-Konnektor für historische Daten** importiert öffentliche Gate.io-Marktdatenarchive in StockSharp. Er wandelt Spot- und Derivatedatensätze zur Speicherung, Analyse und Wiedergabe in das einheitliche StockSharp-Nachrichtenmodell um.

## Wichtige Funktionen

- Instrumentensuche für Spotmärkte sowie unbefristete und lieferbare Futures.
- Historische Tick-Trades, inkrementelle Orderbücher und Zeitrahmenkerzen.
- Downloads nach Datumsbereich zum systematischen Auffüllen von Marktdaten.
- Native Symbole und Marktvarianten werden StockSharp-Instrumentenkennungen zugeordnet.
- Dieser Adapter ist für historische Daten vorgesehen und bietet weder Echtzeitabonnements noch Orderweiterleitung.
- Archivformate von Gate.io werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet zum Vorbereiten von Kryptohistorien für Charts, Analysen, Orderbuchforschung und Strategietests.

Verfügbare Instrumente, Dateien, Zeiträume, Tiefen und Kerzenintervalle hängen von den durch Gate.io veröffentlichten Datensätzen ab.
