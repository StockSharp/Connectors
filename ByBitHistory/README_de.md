# Bybit-Konnektor für historische Daten

Der **Bybit-Konnektor für historische Daten** importiert öffentliche Bybit-Marktdatenarchive in StockSharp. Er vereinheitlicht herunterladbare Börsendaten für Spot- und Derivateinstrumente im standardisierten StockSharp-Nachrichtenmodell.

## Wichtige Funktionen

- Instrumentensuche für Spot-, lineare, inverse und Optionsmärkte.
- Historische Tick-Trades für unterstützte Spot- und Derivateinstrumente.
- Historische inkrementelle Orderbuchdaten für unterstützte Märkte und Tiefen.
- Downloads nach Datumsbereich für große Rückfüllungen und reproduzierbare Forschungsdatensätze.
- Dieser Adapter ist für historische Daten vorgesehen und bietet weder Echtzeitabonnements noch Orderweiterleitung.
- Archivformate und Marktkennungen von Bybit werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet zum Aufbau von Trade- und Orderbuchhistorien für Analysen, Marktwiedergabe und Strategietests.

Verfügbare Instrumente, Zeiträume, Orderbuchtiefen und Dateien hängen von den durch Bybit vorgehaltenen öffentlichen Datensätzen ab.
