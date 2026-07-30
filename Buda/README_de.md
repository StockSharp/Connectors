# Buda-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Buda-Konnektor** verbindet StockSharp mit der Spot-Kryptobörse Buda.com. Öffentliche Marktdaten sind ohne Zugangsdaten verfügbar; authentifizierte REST-Vorgänge verwenden API-Schlüssel und Secret.

## Wichtige Funktionen

- Die von Buda angebotenen Spot-Instrumente suchen.
- Level-1-Kurse, Markttiefe und Tick-Trades abonnieren.
- Öffentliche WebSocket-Aktualisierungen mit REST-Snapshots und -Abgleich kombinieren.
- Limit- und Market-Orders aufgeben sowie einzelne oder gruppierte Orders stornieren.
- Salden, Portfoliostatus, aktive und historische Orders sowie eigene Trades laden.
- Den privaten Status in einem konfigurierbaren Intervall abgleichen.

## Typische Verwendung

Der Konnektor eignet sich für die Echtzeitbeobachtung des Buda-Spotmarkts und den authentifizierten Handel über StockSharp. Öffentliche Daten können ohne Zugangsdaten genutzt werden; für Orders und Kontoinformationen sind ein Buda-API-Schlüssel und ein Secret mit passenden Berechtigungen erforderlich.

Der Adapter bietet weder Kerzen noch einen Orderlog-Datenstrom; Orderbücher werden als Snapshots statt als inkrementelle Updates geliefert. Ein atomarer Orderersatz wird nicht unterstützt, daher muss eine Strategie die alte Order separat stornieren und eine neue einstellen. Berechtigungen und Ratenlimits der Börse gelten weiterhin.
