# Quidax-Konnektor

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Quidax-Konnektor** verbindet StockSharp mit der Quidax-Spotbörse. Er eignet sich besonders für die Beobachtung und den Handel von Kryptomärkten in NGN und anderen afrikanischen Fiatwährungen sowie von Krypto-zu-Krypto-Paaren.

## Wichtigste Funktionen

- Ermittlung von Spot-Instrumenten mit Währungspaar, Preis- und Mengenpräzision sowie Mindestauftragswert.
- Level-1-Kurse, Level-2-Orderbücher, öffentliche Trades und historische Kerzen.
- Fortlaufende Marktdatenabonnements über REST-Abfragen mit konfigurierbarem Intervall.
- Wallet-Salden, offene und historische Aufträge sowie eigene Ausführungen.
- Limit- und Market-Aufträge, Einzelstornierung und gefilterte Sammelstornierung.
- Konfigurierbare REST-Adresse, Konto- oder Unterkonto-ID und Abfrageintervall.

Öffentliche Marktdaten sind ohne Zugangsdaten verfügbar. Portfolio- und Handelsfunktionen erfordern einen Quidax-Secret-Key. Die standardmäßige Benutzer-ID `me` adressiert den Tokeninhaber und kann durch eine unterstützte Unterkonto-ID ersetzt werden.
