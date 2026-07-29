# KuCoin-Konnektor für historische Daten

Der **KuCoin-Konnektor für historische Daten** importiert öffentliche KuCoin-Marktdatenarchive in StockSharp. Er vereinheitlicht herunterladbare Spot- und Futuresdaten im standardisierten StockSharp-Nachrichtenmodell.

## Wichtige Funktionen

- Instrumentensuche und Referenzdaten für Spot- und Futuresmärkte.
- Historische Tick-Trades, Orderbücher und Zeitrahmenkerzen.
- Downloads nach Datumsbereich zum reproduzierbaren Auffüllen von Marktdatenspeichern.
- Börsensymbole und Marktsegmente werden StockSharp-Instrumentenkennungen zugeordnet.
- Dieser Adapter ist für historische Daten vorgesehen und bietet weder Echtzeitabonnements noch Orderweiterleitung.
- Archivtransport und Dateiformate von KuCoin werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet zum Vorbereiten von KuCoin-Historien für Charts, Analysen, Marktwiedergabe und Strategietests.

Verfügbare Instrumente, Dateien, Zeiträume, Tiefen und Kerzenintervalle hängen von den durch KuCoin vorgehaltenen öffentlichen Datensätzen ab.
