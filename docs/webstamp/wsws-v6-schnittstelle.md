# WebStamp Webservice (SOAP) — Schnittstellenbeschreibung der Post

Abschrift der offiziellen Beschreibung «WebStamp web service: SOAP», Version V06.05 (November 2020),
von Walter am 05.10.2026 geliefert. Feld-Datentypen stehen in der WSDL (`wsws-v6.wsdl` daneben)
und werden hier nicht wiederholt. Eckige Klammern `[feld]` = optional. Konzept OneCrew: `docs/webstamp-konzept.md`.

## 1 Begriffe

| Begriff | Bedeutung |
|---|---|
| Kunde | Nutzer von WebStamp über Webservice oder WebStamp light; muss bei der Post registriert sein |
| Integrator | wer WebStamp über den Webservice in die eigene Plattform einbindet (wir) |
| Application-ID | eindeutiger Schlüssel der Anwendung, vergibt die Post |
| Kundencenter-Login | ein Login für alle Online-Dienste der Post, auch WebStamp |
| WS / WSWS | WebStamp / WebStamp-Webservice |

## 2 Allgemeines

- Zeichensatz immer **UTF-8**.
- Versionen: **V6** aktuell, V5 Vorgänger (Migration empfohlen). Abgeschaltete Version → HTTP **410**.
- Versionswechsel: URL anpassen; neue Funktionen eines Audits durch Aktualisieren der WSDL.

| Audit | Pflicht | Neu |
|---|---|---|
| V6.05 | nein | Zusatzinfos zu Medientypen; elektronische Zolldaten (EAD) für Auslandssendungen mit Waren |
| V6.04 | nein | **Rückschein für Einschreiben**; elektronische Nachnahme mit QR-IBAN |
| V6.03 | nein | Etiketten in ZPL2; Auflösung (dpi) wählbar; **Bestellung mit Druckservice**; Einzelbriefe und Mailings |
| V6.02 | nein | Rabattcodes prüfen/einlösen; detaillierte Preise |
| V6.01 | nein | Auslandsadressen ohne PLZ; Quittungen als XML |
| V5 | ja | neue Endpunkte, Fault-Handling, Strukturnamen |

### Server

| Infrastruktur | Zweck |
|---|---|
| `https://wsredesignint1.post.ch` | laufend erweiterte Testumgebung (neue Releases) |
| `https://wsredesignint2.post.ch` | **stabile Testumgebung für Integratoren** (Produktionsstand) — bevorzugt |
| `https://webstamp.post.ch` | Produktiv |

URL: `<Server>/wsws/soap/v6`, WSDL: `<Server>/wsws/soap/v6?wsdl`.

### Zugang

- **Application-ID** pro Anwendung, vergibt die Post.
- **WS-Kunden-ID + WSWS-Passwort**: WebStamp-Homepage → WebStamp-Einstellungen → WebStamp Webservice (dort auch Passwort setzen).
- Jede Bestellung braucht alle drei: Application-ID, WS-Kunden-ID, WSWS-Passwort. Ausnahme: Options-Abfragen (nur Application-ID).
- **Signierte Verbindung** (Sonderfall, nur nach Absprache): Zertifikat der Post, Host mit Präfix `cert.` (z.B. `https://cert.webstamp.post.ch/wsws/soap/v6`); dann kann das Passwort entfallen.

## 3 Authentifizierung — `wsws_identification`

| Feld | Beschreibung |
|---|---|
| application | Application-ID |
| language | `de` / `fr` / `it` / `en` — Sprache der Antworttexte |
| [userid] | WS-Kunden-ID |
| [password] | WSWS-Passwort |
| [encryption_type] | `md5` / `sha1` / leer (leer = Klartext) |

## 4 Options (Stammdaten)

Alle Options-Abfragen gehen ohne Benutzer/Passwort (nur Application-ID), ausser `get_licenses`.
Die Post empfiehlt: Optionen lokal speichern und gelegentlich neu abfragen; Produktänderungen erkennen.

**wsws_category**: number, name, recipient_manatory (Empfängeradresse Pflicht), valid_days.

**wsws_product**

| Feld | Beschreibung |
|---|---|
| number | WebStamp-ID; ändert bei jeder Produktänderung |
| post_product_number | Post-Nummer — **zur Identifikation empfohlen**, ändert seltener |
| category / category_number | Kategorie |
| price | Preis inkl. MWST |
| name, delivery (A-Post, B-Post …), format (Standardbrief, Midibrief …), size_din | |
| max_size_length / max_size_height / max_edge_length / max_scale / max_weight | Masse in mm, Gewicht in g |
| zone | 1 Europa, 2 übrige Länder, 3 Schweiz |
| product_list / product_list_number | Sortiment |
| subsystem / segment | gesetzt ⇒ **Frankierlizenz nötig** |
| **barcode** | true = Brief mit Barcode (BMB, z.B. Einschreiben) → bestimmt das Druckmedium |
| additions | Liste `wsws_addition` (code, short_name) = Zusatzleistungen |

Produkte in der Vergangenheit können nicht abgefragt werden.

**wsws_media_type**: number, name, image_possible, **printservice_possible**, ead_possible, zpl_support, categories.

**wsws_media**: number, name, type (Medientyp).

**wsws_zone**: number, name, zone (1 Europa / 2 übrige / 3 Schweiz). **wsws_country**: alias, zone, name.

**wsws_license** (braucht Kunden-ID + Passwort): number (wird an `new_order` übergeben), comment, subsystem, segment.

**wsws_product_list**: number, name, customer_type (`bc` Geschäftskunden / `pc` Privatkunden).

**wsws_customer_data**: license_state (`none`/`pending`/`rejected`/`ok`), payment_type (`prepaid` / `kurepo` = Rechnung), product_lists.

### Methoden

| Methode | Parameter | Rückgabe |
|---|---|---|
| get_categories | identification | categories |
| get_products | identification, [timestamp], post_product_number, customer_type, product_lists | products |
| get_media_types | identification, [timestamp] | media_types |
| get_medias | identification, [timestamp], [type] | medias |
| get_zones | identification | zones |
| get_countries | identification | countries |
| get_licenses | identification (mit Login), [post_product_number] | licenses — Kunde wählt selbst, können pro Bestellung ändern |
| get_product_lists | identification, [customer_type] (mit Login: Sortimente des Kunden) | product_lists |
| get_customer_data | identification (Kunden-ID Pflicht, Passwort optional) | customer_data |

## 5 Bestellung — Datentypen

**wsws_address**

| Feld | Beschreibung |
|---|---|
| [organization], [company_addition], [title], [firstname], [lastname], [addition] | |
| [street] | inkl. Hausnummer |
| [pobox], [pobox_lang] (de/fr/it/en), [pobox_nr] | Postfach |
| [zip], city | city Pflicht |
| [country] | Ländercode (Default `ch`) oder Name |
| [reference] | eigene Referenz, kommt im Resultat zurück |
| [cash_on_delivery_amount], [cash_on_delivery_esr], [cash_on_delivery_qr_ref] | Nachnahme |
| [print_address] | mehrzeilige Adresse für den Empfänger-Aufdruck; ersetzt die Einzelfelder |

**wsws_order**

| Feld | Beschreibung |
|---|---|
| order_id | Bestellnummer |
| print_data | base64 — bei PDF/ZPL mit Druckmedium und `single=true` auf Bestellebene, sonst pro Stamp |
| stamps | Liste `wsws_stamp` |
| consignments | Liste `wsws_consignments` (Einzelbriefe/Mailings) |
| messages | Liste `wsws_message` |
| reference, price, price_details, item_price | |
| valid_days, valid_until | Gültigkeit der Frankaturen |
| delivery_receipt | Lieferschein-PDF (falls vorhanden) |
| product_number, post_product_number | effektiv verwendetes Produkt |

**wsws_stamp** (die Frankatur selbst)

| Feld | Beschreibung |
|---|---|
| stamp_id | Frankatur-Nummer |
| **tracking_number** | bei Briefen mit Barcode: Sendungsnummer für Track & Trace |
| print_data | base64, einzelne Frankatur (leer bei `single=true` mit Medium) |
| compression | `gzip` oder leer |
| mime_type | |
| image_width_mm / image_height_mm / image_width_px / image_height_px | Bildgrösse |
| reference | Referenz der Empfängeradresse |

**wsws_message**: message_type (`announcement` / `error` / `confirm`), customer_message, system_message, system_message_code, url, confirm_until.
**confirm** muss der Kunde bis `confirm_until` bestätigen (z.B. neue AGB), sonst keine Bestellungen mehr — der Integrator muss das abbilden.

**wsws_cash_on_delivery**: transaction_type (`post_bank` / `esr` / `qr_iban`), esr_customer_number, iban, qr_iban, beneficiary.

**wsws_custom_media** (eigenes Druckmedium, alle Masse in mm)

| Feld | Beschreibung |
|---|---|
| type | `label` / `envelope` / `letter` / `paper` |
| page_width, page_height | |
| [margin_top/bottom/left/right] | |
| [cols], [rows], [colspacing], [rowspacing] | nur Etiketten |
| [recipient_orientation] + [recipient_x/y] | Empfänger; Orientierung top/left, top/right, bottom/left, bottom/right |
| [sender_orientation] + [sender_x/y] | Absender |
| [franking_orientation] + [franking_x/y] | **Frankatur-Position** |

Mindest-Druckbereich hängt von Produkt, Adressen, Bildern ab; zu klein ⇒ Meldung (Fehler 2262).

**wsws_order_item**: stamp_id, label_number (aufgedruckte Laufnummer), tracking_number.

**wsws_price_detail**: type (`webstamp` / `discount` / `charge` z.B. Druckservice-Gebühr), quantity, amount, description. Summe = Bestellbetrag.

**wsws_consignments** (Einzelbriefe/Mailings): stamp_id, number, pages, **window** (`left`/`right` — wo die Adresse im Fenster gefunden wurde), **state** (`valid`/`invalid`), **reason** (Grund bei invalid).

## 6 Info — Datentypen

- **wsws_receipt**: order_id, receipt (PDF), receipt_info (Liste `wsws_receipt_info`, bei XML-Ausgabe).
- **wsws_receipt_info**: address (Rechnungsadresse), journal, product, order_items.
- **wsws_delivery_receipt**: order_id, delivery_receipt (PDF).
- **wsws_journals / wsws_journal**: date, order_id, quantity, price, total_price, valid_until, order_comment.

## 7 Methoden Bestellung

### new_order_preview (kostenlose Vorschau) und new_order (Bestellung)

Gleiche Parameter:

| Parameter | Beschreibung |
|---|---|
| identification | |
| product | Produktnummer (siehe post_product) |
| single | Default false. **true**: alle Frankaturen in einer Datei, Medium Pflicht, nur PDF/ZPL, Daten auf Bestellebene. **false**: einzelne Dateien pro Stamp |
| file_type | `png` / `gif` / `bmp` / `pdf` / `zpl` |
| [dpi] | 72–600, Default 300 |
| [print_zone] | **1 = Frankierzone** (wie Briefmarke), **2 = Adresszone**; nur bei single=false; sonst automatisch |
| [media] | Druckmedium-ID |
| [quantity] | Anzahl; ignoriert, wenn Adressen übergeben |
| [a_addresses] | Empfängeradressen |
| [sender] | Absenderadresse |
| [media_startpos] | Start-Etikette (Default 1) |
| [image] | Bild in der Frankatur (GIF/PNG/JPG) |
| [document] | PDF mit Einzelbrief/Mailing; mindestens eine Empfängeradresse im Fensterbereich |
| [printservice] | Default false. **true**: an Druckerei, gedruckt zugestellt (Vorschau: Kosten + Adressprüfung, Dokumente zurück zur Kontrolle). **false**: Daten gehen an den Kunden, er verarbeitet selbst |
| [printservice_info] | Lieferadresse für gedruckte Frankaturen; nur mit printservice=true, nicht für Einzelbriefe |
| [post_product] | false = WebStamp-Nummer, true = Post-Nummer |
| [reference], [order_comment] | eigene Referenz; Bemerkung (auch im WebStamp-GUI/Journal) |
| [license_number] | Frankierlizenz |
| [discount_code] | Rabattcode |
| [return_code] | **Rückschein**: `registred` (Einschreiben zurück als Einschreiben) / `unregistred` |
| [ead_code] | Zolldaten Ausland |
| [cash_on_delivery_info] | Nachnahme |
| [custom_media] | eigenes Druckmedium |

Rückgabe: `order` (wsws_order).

### Weitere

| Methode | Parameter | Zweck |
|---|---|---|
| copy_order_by_id | identification, order_id, [reference], [quantity], [order_comment], [file_type] | gleiche Bestellung nochmals; nicht für Einzelbriefe/Mailings |
| previous_order | identification, order_id, stamp_id | Daten einer früheren Bestellung zum Nachdrucken |
| get_info_receipt | [output] `pdf`/`xml`, [label_number], Order-ID oder Zeitraum | Quittung einzeln oder Sammelquittung |
| get_info_delivery_receipt | Order-ID | Lieferschein-PDF |
| get_info_journal | | veraltet, durch get_info_receipt ersetzt |

## 8 Fehler

Fehler kommen als SoapFault; `get_error_codes` liefert alle Codes. **Für Support immer Code + `request_id` angeben.**

```xml
<SOAP-ENV:Fault>
  <faultcode>Client</faultcode>
  <faultstring>Allgemeiner technischer Fehler</faultstring>
  <detail><wsws:code>2300</wsws:code><wsws:request_id>WD2C7BkZJUdpIBLvVbWp9wAAAAY</wsws:request_id></detail>
</SOAP-ENV:Fault>
```

| Code | Meldung (de) |
|---|---|
| 2000 | Die XML-Anfrage ist fehlerhaft. |
| 2003 | Die Empfängeradresse %address_nr% ist nicht vollständig oder zu lang. |
| 2004 | Es können nur %max_stamps% Frankiervermerke pro Bestellung generiert werden. |
| 2006 | Das ausgewählte Produkt ist nicht mehr verfügbar und wurde ersetzt. |
| 2007 | Bitte wählen Sie ein Druckmedium. |
| 2008 | Das ausgewählte Druckmedium ist nicht mehr verfügbar und wurde ersetzt. |
| 2009 | Bitte geben Sie die Anzahl ein. |
| 2012 | Die maximale Bildgrösse von %bytes% Bytes wurde überschritten. |
| 2015 | Das Druckmedium wird mit den gewählten Optionen nicht unterstützt. |
| 2016 | Eine Adresse kann mit dem ausgewählten Produkt nicht verwendet werden. |
| 2017 | Für das gewählte Produkt sind Empfängeradressen erforderlich. |
| 2022 | Für dieses Produkt ist eine Absenderadresse erforderlich. |
| 2025 | Bestehende Bestellungen mit belegloser Nachnahme und ESR-Referenz können nicht Grundlage weiterer Bestellungen sein. |
| 2029 | Die Frankierlizenz im verwendeten Auftrag ist nicht mehr verfügbar. |
| 2200 | Der Auftrag wurde nicht gefunden. |
| 2202 | WS-Kunden-ID, Passwort oder Applikations-ID sind ungültig. |
| 2208 | Das Dateiformat ist unbekannt und kann nicht verwendet werden. |
| 2210 | Adressen können nicht in die Frankierzone gedruckt werden. |
| 2211 | Die Verwendung von Bildern in der Adresszone ist nicht möglich. |
| 2212 | Der Ausgabetyp «Single» ist nur für Bestellungen mit PDF zulässig. |
| 2216 | Die Transaktion ist fehlgeschlagen (Guthaben: Konto laden; Rechnung: später nochmals / Kundendienst). |
| 2221 | Das Feld «%field%» in der «%address%» ist ungültig. |
| 2223 | Die Angabe eines Druckmediums ist im Ausgabetyp «Multiple» nicht möglich. |
| 2224 | Das Produkt wurde nicht gefunden. |
| 2242 | Ihre Angaben sind nicht bestätigt — Login bei webstamp.post.ch, Verifikationscode eingeben. |
| 2243 | Zugriff nur für Benutzer mit Wohnsitz Schweiz oder Liechtenstein. |
| 2244 | Für diese Bestellung wurde kein Lieferschein generiert. |
| 2247 | Der Lieferschein für dieses Produkt ist nicht mehr verfügbar. |
| 2248 | Der maximale Nachnahmebetrag für %country% ist CHF %max_value% (min. 0.05). |
| 2249 | Die ESR-Referenznummer «%esr%» ist ungültig. |
| 2250 | Der Nachnahmebetrag ist nicht korrekt (auf 5 Rappen, keine Sonderzeichen). |
| 2251 | IBAN fehlt oder Angaben zum Endbegünstigten unvollständig. |
| 2252 | ESR-Teilnehmernummer fehlt oder ist ungültig. |
| 2253 | Die IBAN-Nummer ist ungültig. |
| 2256 | Der Nachnahmebetrag fehlt oder ist nicht möglich. |
| 2257 | Eine gültige Frankierlizenz ist erforderlich. |
| 2258 | Die gewählte Frankierlizenz passt nicht zum Produkt. |
| 2261 | Das Druckmedium «$type» wird für benutzerdefinierte Medien mit diesen Optionen nicht unterstützt. |
| 2262 | Der Druckbereich des benutzerdefinierten Mediums ist zu klein. |
| 2265 | Die übergebene Orientierung ist ungültig. |
| 2266 | Der Ausgabetyp «multiple» ist für den Dateityp «$file_format» unzulässig. |
| 2267 | Für diese Applikation ist der Dateityp CSV nicht erlaubt. |
| 2268 | Die AGB «Login Kundencenter» haben sich geändert — auf www.post.ch lesen und akzeptieren. |
| 2271 | Lieferscheine nach Bedarf können nicht über WSWS bezogen werden. |
| 2300 | Allgemeiner technischer Fehler. |
| 3100 | Daten nicht signiert oder Signatur nicht erkannt. |
| 5000–5008 | Rabattcode ungültig / abgelaufen / Mindestbetrag / Mindestmenge / nur Druckservice / nur Selbstdruck / bereits eingelöst / nicht mit BVE-Lizenz |

## 9 Beispiel `get_categories`

```xml
<soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:v6="https://webstamp.post.ch/wsws/soap/v6">
  <soapenv:Header/>
  <soapenv:Body>
    <v6:get_categories>
      <args>
        <identification>
          <application>16bc33bea7216bc33bea7216bc33bea</application>
          <language>de</language>
          <userid>10000000</userid>
          <password>12345678</password>
        </identification>
      </args>
    </v6:get_categories>
  </soapenv:Body>
</soapenv:Envelope>
```

Antwort: `get_categoriesResponse` → `get_categoriesResult` → `item` (name «Brief Inland», number 1, recipient_mandatory false, valid_days 365; «Brief Ausland», number 2 …).

## 10 Hinweise

- Weitere Hilfe: Dokument «Assistance for connection to WebStamp web service» (bei der Post anfragen).
- EAD (Zolldaten) nur für Auslandssendungen mit Waren — für OneCrew nicht relevant.
