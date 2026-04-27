# Bank-Master-Datei

`bank_master.csv` wird beim Server-Start vom `BankLookupService` in den Speicher
geladen und liefert aus einer IBAN (über die Institut-ID) die Bankdaten.

## Format

Spaltengetrennt mit `;` (Semikolon), UTF-8, erste Zeile = Header:

```
iid;bic;name;ort;strasse;plz
```

- **iid** — 5-stellige Institut-ID (Stellen 5–9 einer CH/LI-IBAN)
- **bic** — SWIFT/BIC-Code (optional, leer lassen wenn unbekannt)
- **name** — Banknname
- **ort** — Stadt
- **strasse** — Hauptsitz-Strasse (optional)
- **plz** — Postleitzahl (optional)

## Offizielle Datenquelle

SIX Interbank Clearing pflegt die authoritative Liste aller Schweizer Banken:

https://www.six-group.com/interbank-clearing/de/home/bank-master-data/download-bc-bank-master.html

Von dort monatlich die aktuelle CSV herunterladen und in das Format oben
konvertieren. Die SIX-Datei heisst typischerweise `bcbankenstamm_cur.txt`
(semikolongetrennt, mit mehr Spalten). Nur die relevanten Spalten
übernehmen und als `bank_master.csv` ersetzen.

## QR-IBAN-Einträge

QR-IBANs haben IIDs im Bereich 30000–31999. Der Lookup findet beide
Varianten (normale IBAN-IID und QR-IBAN-IID), weil beide in derselben
CSV stehen. Die SIX-Liste enthält ebenfalls beide.

## Nach Update

Server-Neustart genügt; die Datei wird einmal pro Prozesslaufzeit geladen.
