# Dukascopy-JForex-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Dukascopy-JForex-Konnektor** verbindet StockSharp über das offizielle Java-JForex-SDK mit Dukascopy Bank. Das SDK stellt die sichere, authentifizierte Sitzung zu den Dukascopy-Handelsservern her; der .NET-Adapter tauscht Befehle und Ereignisse über eine ausschließlich lokale Bridge aus.

## Wichtigste Funktionen

- Instrumentensuche für die im Konto verfügbaren FX-, CFD-, Metall-, Index-, Rohstoff- und Anleihemärkte.
- Level-1-Kurse, Tick-Trades, Orderbuchänderungen und Zeitintervall-Kerzen.
- Historische Ticks und Kerzen über die JForex-Historiendienste.
- Markt-, Limit-, Stop-, Stop-Limit- und JForex-spezifische Orderbefehle.
- Aufgabe, Änderung und Stornierung von Orders sowie Ausführungs-, Saldo- und Positionsmeldungen.
- Getrennte, konfigurierbare JForex-Adressen für Demo- und Live-Betrieb.
- Start der Bridge aus einer angegebenen ausführbaren JAR-Datei oder Betrieb als eigener lokaler Prozess.

## Laufzeitmodell

Java ist erforderlich, da Dukascopy JForex als Java-API veröffentlicht und unterstützt. Das enthaltene Maven-Bridge-Projekt verwendet das offizielle Paket `DDS2-jClient-JForex`. Die Bridge lauscht ausschließlich auf der lokalen Loopback-Schnittstelle und gibt Kontozugangsdaten nicht im Netzwerk frei.

Der Konnektor eignet sich für Handelsroboter, Terminals, Überwachung und Order-Management über das Standard-Nachrichtenmodell von StockSharp. Instrumente, Historie, Markttiefe und Handelsrechte hängen vom Dukascopy-Konto ab.
