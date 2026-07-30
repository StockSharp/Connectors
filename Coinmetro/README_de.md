# Coinmetro-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Coinmetro-Konnektor** verbindet StockSharp mit der Coinmetro-Spotbörse. Er kombiniert REST-Endpunkte für Instrumente, Konto, Orders und Kerzen mit WebSocket-Aktualisierungen für Markt- und private Aktivitäten und unterstützt getrennte Live- und Demoumgebungen.

## Wichtige Funktionen

- Coinmetro-Spotinstrumente und deren Handelsregeln suchen.
- Live-Level-1-Kurse, Markttiefe und Tick-Trades per WebSocket abonnieren.
- Historische Kerzen für 1, 5 und 30 Minuten, 4 Stunden und einen Tag laden.
- Limit- und Market-Orders mit unterstützten GTC-, IOC-, FOK- und GTD-Parametern aufgeben.
- Einzelne Orders oder Gruppen passender offener Orders stornieren.
- Salden, offene und historische Orders sowie eigene Trades laden.
- Zwischen konfigurierbaren Live- und Demo-REST- und WebSocket-Endpunkten wechseln.

## Typische Verwendung

Der Konnektor eignet sich zur Beobachtung des Coinmetro-Spotmarkts, zum Laden von Kerzenhistorien und für automatisierten Handel. Private Live-Vorgänge benötigen ein Zugriffstoken mit passenden Rechten; der Demomodus nutzt separate offene Endpunkte und kann sein Demotoken automatisch beziehen.

Kerzen sind ausschließlich historisch und werden nicht live fortgesetzt. Atomarer Ersatz sowie bedingte, Iceberg- und Post-only-Orders werden nicht unterstützt; Orderbücher werden als Snapshots statt als StockSharp-Inkremente veröffentlicht. Privates Abgleichintervall und API-Ratenlimits sollten beim Strategiedesign berücksichtigt werden.
