# Settrade-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Settrade-Konnektor** verbindet StockSharp über Settrade Open API v2 mit thailändischen Aktien und Derivaten. REST- und MQTT-Dienste für Markt- und Brokerage-Daten werden im StockSharp-Nachrichtenmodell vereinheitlicht.

## Wichtige Funktionen

- Direkte Symbolsuche für das konfigurierte SET-Aktien- oder TFEX-Derivatekonto; kein vollständiger Instrumentendownload.
- Echtzeit-Level-1-Kurse sowie Orderbuch-Snapshots und -Aktualisierungen; keine Tick-Trade-Abonnements.
- Historische Kerzen mit anschließenden MQTT-Aktualisierungen für unterstützte Intervalle.
- Market- und Limit-Orders sowie unterstützte TFEX-Bedingungsorders; Aktienkonten bieten keine Stop-Orders.
- Änderung und Stornierung mit Settrade-Feldern für Gültigkeit, NVDR, Iceberg, Position und Auslöser.
- Kontodaten, Portfolios, Positionen, Orders und Trades über Snapshots, private Themen und periodischen Abgleich.
- Produktions- und Sandbox-Endpunkte sind konfigurierbar; je nach Funktion werden Zugangsdaten, Broker-ID, Konto, Typ und PIN benötigt.
- Authentifizierung, MQTT-Themen und Formate werden hinter der standardisierten StockSharp-API verborgen.

## Typische Verwendung

Geeignet für thailändische Handelsterminals, Live-Strategien, Ordermanagement und Kontoüberwachung über Settrade.

Symbole, Kerzenintervalle, Tiefe, Kontofunktionen, Handelsrechte und Limits werden von Settrade, dem Kontotyp und dessen Berechtigungen bestimmt.
