# CoinGlass-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **CoinGlass-Konnektor** verbindet StockSharp mit der CoinGlass-API für Kryptomarktanalysen. Er bildet ausgewählte Daten zu Futures, Spot, Optionen, Bitcoin-ETFs und Ethereum-ETFs auf StockSharp-Instrumente, Level-1-Nachrichten und historische Kerzen ab.

## Wichtige Funktionen

- Einen CoinGlass-Markttyp wählen und Abfragen optional nach Börse oder Symbol einschränken.
- Die im konfigurierten Datensatz verfügbaren Instrumente suchen.
- Aktuelle Level-1-Kennzahlen wie Preis, Volumen, Veränderung und verfügbares Open Interest abrufen.
- Level-1-Snapshots in einem konfigurierbaren Intervall abfragen.
- Historische Zeitrahmenserien für Preis, Open Interest, Funding Rate oder Liquidationen laden.
- Ein Verlaufslimit von bis zu 1.000 Datensätzen pro Anfrage festlegen.

## Typische Verwendung

Der Konnektor eignet sich für Research-Dashboards, Derivateüberwachung und historische Analysen von CoinGlass-Kennzahlen. API-Token, Markttyp und Metrik werden konfiguriert; Börse oder Symbol können für einen gezielten Datensatz eingeschränkt werden.

CoinGlass ist eine Analysequelle und kein Ausführungsplatz. Der Adapter bietet keine Orders, Portfolios, Tick-Trades oder Markttiefe. Historische Level-1-Ereignisse und Live-Kerzenaktualisierungen werden nicht unterstützt; Kerzenanfragen liefern nur Historie. Datenverfügbarkeit und Anfragegrenzen hängen vom CoinGlass-Tarif ab.
