# Coincall-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Coincall-Konnektor** verbindet StockSharp mit Coincall-Optionen und -Futures. Über die Produkteinstellung wird die Derivateoberfläche gewählt; REST liefert Snapshots und Historie, authentifizierte WebSocket-Sitzungen liefern Live-Markt- und private Aktualisierungen.

## Wichtige Funktionen

- Coincall-Options- oder Futures-Instrumente suchen.
- Level 1, Markttiefe, Tick-Trades und Zeitrahmenkerzen abonnieren.
- Aktuelle Trades und historische Kerzen laden und anschließend WebSocket-Live-Updates empfangen.
- Limit-, Market- und bedingte Orders mit Triggerpreis sowie unterstützten GTC-, IOC-, FOK-, Post-only- und Reduce-only-Parametern aufgeben.
- Einzelne Orders ändern oder stornieren und Gruppen passender Orders stornieren.
- Salden, Positionen, offene und historische Orders sowie eigene Trades laden.
- Privaten Status in einem konfigurierbaren Intervall abgleichen.

## Typische Verwendung

Der Konnektor eignet sich zur Derivateüberwachung und zum automatisierten Options- oder Futures-Handel auf Coincall. REST-Instrumentsuche und Snapshots können ohne Zugangsdaten verbinden; WebSocket-Streaming und alle privaten Vorgänge erfordern API-Schlüssel und Secret.

Pro Adapterinstanz wird nur eine Produktoberfläche gewählt. Iceberg-Orders und absolute Ablaufzeiten werden nicht unterstützt; Orderbücher sind snapshotbasiert und ein Orderlog fehlt. Instrumente, Handelsberechtigungen und API-Grenzen werden von Coincall bestimmt.
