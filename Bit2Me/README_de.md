# Bit2Me-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Bit2Me-Konnektor** verbindet StockSharp mit Bit2Me Pro, der Spot-Handelsplattform des spanischen Anbieters für digitale Vermögenswerte. Er eignet sich für Systeme, die über das einheitliche StockSharp-Nachrichtenmodell direkten Zugang zu Kryptomärkten mit EUR-Liquidität benötigen.

## Hauptfunktionen

- Ermittlung der verfügbaren Bit2Me-Pro-Spotmärkte sowie ihrer Preis-, Mengen- und Mindestauftragsregeln.
- REST-Snapshots für Level-1-Kurse und das Level-2-Orderbuch.
- Öffentliche Trades und vollständige Orderbuch-Aktualisierungen in Echtzeit über WebSocket.
- Historische Trades und OHLCV-Kerzen für die von Bit2Me angebotenen Intervalle.
- Aufgabe von Market-, Limit- und Stop-Limit-Orders.
- Stornierung von Orders sowie Abruf von Orders und Ausführungen.
- Portfoliosalden und durch aktive Orders blockierte Beträge.
- Konfigurierbare REST- und WebSocket-Adressen für Tests, Routing oder Infrastrukturänderungen.

## Typische Verwendung

Verwenden Sie den Konnektor in Handelsrobotern, Terminals, Datensammlern, Order-Management-Diensten und Überwachungssystemen für Bit2Me-Pro-Spotinstrumente.

Öffentliche Marktdaten erfordern keine Zugangsdaten. Handels- und Kontofunktionen benötigen einen Bit2Me-API-Schlüssel und ein Secret mit den passenden Berechtigungen. Märkte, Limits und Kontofunktionen werden von Bit2Me festgelegt.
