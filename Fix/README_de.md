# FIX-Protokollkonnektor

Der **FIX-Protokollkonnektor** verbindet StockSharp über konfigurierbare Financial-Information-eXchange-Sitzungen mit Brokern, Börsen und Handelssystemen. Er bildet dialektspezifische Nachrichten auf das einheitliche StockSharp-Nachrichtenmodell ab.

## Wichtige Funktionen

- Konfigurierbare FIX-Dialekte für verschiedene Broker, Handelsplätze und Marktsegmente.
- Sitzungsanmeldung, Authentifizierung, Heartbeats, Sequenzverfolgung, Wiederholungen, Neuverbindung und optional sicherer Transport.
- Instrumentensuche und Marktdaten wie Level 1, Trades, Orderbücher, Kerzen, Nachrichten und Orderlog-Ereignisse, sofern vom Dialekt unterstützt.
- Orderaufgabe, Änderung, Stornierung, Massenstornierung, Statusabfrage und Ausführungsverarbeitung, sofern von der Gegenpartei unterstützt.
- Portfolio-, Salden- und Positionsaktualisierungen für transaktionale Sitzungen.
- Absender-, Ziel-, Konto-, Endpunkt- und Sitzungseinstellungen über das standardisierte StockSharp-Konfigurationsmodell.

## Typische Verwendung

Geeignet für individuelle Brokeranbindungen, Börsengateways, Live-Strategien, Ordermanagement-Dienste und vereinheitlichten Marktdatenzugriff.

Nachrichten, Felder, Ordertypen, Wiederherstellung und Rechte hängen vom gewählten FIX-Dialekt und der Sitzungsspezifikation der Gegenpartei ab.
