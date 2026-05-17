using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace HrSystem.Services;

/// <summary>
/// Erstellt den Daten-Payload für den ESTV-Lohnausweis-Barcode (Position H,
/// oben rechts auf Form 11 dfe).
///
/// Format (Swissdec SalaryDeclarationTxAB, aus Mirus-Referenz rückentwickelt
/// am 13.05.2026):
///
///   Byte-Layout:
///     [14 Byte Pre-ZIP-Header] [ZIP-Container mit einer Datei "txab"]
///
///   Pre-ZIP-Header (fix, 1:1 von Mirus übernommen):
///     1e 34 cd 8b 7a 00 02 f7 01 03 00 01 00 01
///     (\x1e = RS Record-Separator, \x34 = '4' = Form-Typ Lohnausweis,
///      Rest = Swissdec-Envelope/Versions-Marker)
///
///   ZIP-Container:
///     • genau eine Datei mit Namen "txab"
///     • Inhalt: UTF-8 XML im Swissdec-Namespace
///       http://www.swissdec.ch/schema/sd/20200220/SalaryDeclarationTxAB
///
///   XML-Struktur:
///     &lt;T SID="000" SysV="001"&gt;
///       &lt;Company UID-BFS Person HR-RC-Name ZIP CL Street City Country Phone /&gt;
///       &lt;PersonID Lastname Firstname ZIP CL Street Postbox Locality City Country&gt;
///         &lt;SV-AS-Nr&gt;756.XXXX.XXXX.XX&lt;/SV-AS-Nr&gt;
///       &lt;/PersonID&gt;
///       &lt;S&gt;
///         &lt;DocID&gt;GUID&lt;/DocID&gt;
///         &lt;Period&gt;&lt;from&gt;YYYY-MM-DD&lt;/from&gt;&lt;until&gt;YYYY-MM-DD&lt;/until&gt;&lt;/Period&gt;
///         &lt;CanteenLunchCheck/&gt;  -- nur wenn Box G = true
///         &lt;Income&gt;X.XX&lt;/Income&gt;
///         &lt;GrossIncome&gt;X.XX&lt;/GrossIncome&gt;
///         &lt;AHV-ALV-NBUV-AVS-AC-AANP-Contribution&gt;X.XX&lt;/AHV-ALV-NBUV-AVS-AC-AANP-Contribution&gt;
///         &lt;BVG-LPP-Contribution&gt;&lt;Regular&gt;X.XX&lt;/Regular&gt;&lt;/BVG-LPP-Contribution&gt;
///         &lt;NetIncome&gt;X.XX&lt;/NetIncome&gt;
///         &lt;Remark&gt;Bemerkungen-Text&lt;/Remark&gt;
///       &lt;/S&gt;
///     &lt;/T&gt;
///
/// Wichtige Beobachtungen aus dem Mirus-Referenz-Barcode:
///   • KTG (Krankentaggeld) gehört NICHT in Ziffer 9, sondern als Remark
///     "Krankengeldversicherung CHF X.XX" (ESTV-Wegleitung Rz 35).
///   • HR-RC-Name ist auf 30 Zeichen begrenzt ("McDonald's Restaurant Langenth"
///     statt "...Langenthal" — der Buchstabe geht verloren).
///   • CL (Company Location) enthält die volle Filial-Bezeichnung
///     "Zweigniederlassung Langenthal" — kann länger als HR-RC-Name sein.
/// </summary>
public class LohnausweisBarcodeService
{
    /// <summary>
    /// 14-Byte Barcode-Steuerzeichen-Header gemäss ELM 6.0 Anhang 5
    /// Kapitel 2.2. Wird dynamisch pro Barcode erzeugt (Identification +
    /// Size müssen pro Dokument unterschiedlich sein).
    /// </summary>
    private static byte[] BuildSteuerzeichen(int totalSize, byte[] identification)
    {
        // totalSize = Header-14 + ZIP-Bytes (big-endian in 3 Bytes)
        var h = new byte[14];

        // Bytes 1–4: Identification (4 zufällige Bytes, alle Barcodes eines
        // Dokuments teilen dieselbe ID — beim Lohnausweis haben wir nur einen).
        Array.Copy(identification, 0, h, 0, 4);

        // Byte 5: Compression Type = 'z' (= Info-ZIP, einziger erlaubter Wert)
        h[4] = (byte)'z';

        // Bytes 6–8: Size der gesamten Daten (inkl. 14-Byte-Header), big-endian.
        // Max = 2^24-1 ≈ 16 MB, was PDF417 sowieso nicht aufnehmen kann.
        h[5] = (byte)((totalSize >> 16) & 0xFF);
        h[6] = (byte)((totalSize >>  8) & 0xFF);
        h[7] = (byte)( totalSize        & 0xFF);

        // Byte 9: Page Control = 1 (Legacy aus v5, ab v20200220 durch
        // Bytes 11–14 ersetzt, aber weiterhin auf 1 zu setzen).
        h[8] = 0x01;

        // Byte 10: Steuerzeichen-Version = 3 (ab v20200220 fix).
        h[9] = 0x03;

        // Bytes 11–12: Barcode-Counter — bei nur EINEM Barcode pro Lohnausweis
        // immer „01" (Byte 11 = Zehnerstelle, Byte 12 = Einerstelle).
        h[10] = 0x00;
        h[11] = 0x01;

        // Bytes 13–14: Anzahl Barcodes total — bei uns immer „01".
        h[12] = 0x00;
        h[13] = 0x01;

        return h;
    }

    private static readonly XNamespace Ns =
        "http://www.swissdec.ch/schema/sd/20200220/SalaryDeclarationTxAB";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Baut den vollständigen Barcode-Payload (14-Byte-Steuerzeichen + ZIP+XML)
    /// als byte[]. Wird im LohnausweisPdfService an iText's
    /// BarcodePDF417.SetCode(byte[]) übergeben — Binary-Mode aktiviert.
    /// Header wird gemäss ELM 6.0 Anhang 5 Kapitel 2.2 dynamisch berechnet
    /// (Size + zufällige Identification pro Dokument).
    /// </summary>
    public byte[] BuildPayload(LohnausweisData d)
    {
        var xml = BuildXml(d);
        var xmlBytes = Encoding.UTF8.GetBytes(xml);

        // 1) ZIP-Container mit Datei "txab" bauen (Info-ZIP Format)
        byte[] zipBytes;
        using (var zipMs = new MemoryStream())
        {
            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry("txab", CompressionLevel.Optimal);
                using var es = entry.Open();
                es.Write(xmlBytes, 0, xmlBytes.Length);
            }
            zipBytes = zipMs.ToArray();
        }

        // 2) Header: 14 Bytes mit dynamischer Size + zufälliger Identification
        int totalSize = zipBytes.Length + 14;
        var identification = new byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(identification);
        var header = BuildSteuerzeichen(totalSize, identification);

        // 3) Zusammenfügen: Header + ZIP
        var result = new byte[header.Length + zipBytes.Length];
        Buffer.BlockCopy(header,   0, result, 0,             header.Length);
        Buffer.BlockCopy(zipBytes, 0, result, header.Length, zipBytes.Length);
        return result;
    }

    // ════════════════════════════════════════════════════════════════════
    // XML-AUFBAU (Swissdec SalaryDeclarationTxAB)
    // ════════════════════════════════════════════════════════════════════
    private static string BuildXml(LohnausweisData d)
    {
        var company = new XElement(Ns + "Company",
            new XAttribute("UID-BFS",    d.CompanyUidFormatted ?? ""),
            new XAttribute("Person",     d.HrVerantwortlicherName ?? ""),
            new XAttribute("HR-RC-Name", Truncate(d.CompanyName ?? "", 30)),
            new XAttribute("ZIP",        d.CompanyZip ?? ""),
            new XAttribute("CL",         d.BranchName ?? ""),
            new XAttribute("Street",     d.CompanyStreet ?? ""),
            new XAttribute("City",       d.CompanyCity ?? ""),
            new XAttribute("Country",    string.IsNullOrWhiteSpace(d.CompanyCountry) ? "SWITZERLAND" : d.CompanyCountry),
            new XAttribute("Phone",      d.CompanyPhone ?? "")
        );

        var personId = new XElement(Ns + "PersonID",
            new XAttribute("Lastname",  d.MaLastname ?? ""),
            new XAttribute("Firstname", d.MaFirstname ?? ""),
            new XAttribute("ZIP",       d.MaZip ?? ""),
            new XAttribute("CL",        ""),
            new XAttribute("Street",    d.MaStreet ?? ""),
            new XAttribute("Postbox",   ""),
            new XAttribute("Locality",  ""),
            new XAttribute("City",      d.MaCity ?? ""),
            new XAttribute("Country",   string.IsNullOrWhiteSpace(d.MaCountry) ? "SWITZERLAND" : d.MaCountry),
            new XElement(Ns + "SV-AS-Nr", d.AhvNummer ?? "")
        );

        // <S> — Statement / Slip (Lohnausweis-Daten).
        // ELM 6.0 Anhang 5 Kap. 3.1 Tabelle 3.2: Reihenfolge muss sein:
        //   DocID → CreationDate → Period → FreeTransport → CanteenLunchCheck →
        //   Income → FringeBenefits → SporadicBenefits → CapitalPayment →
        //   OwnershipRight → BoardOfDirectorsRemuneration → OtherBenefits →
        //   GrossIncome → AHV-ALV-NBUV-AVS-AC-AANP-Contribution →
        //   BVG-LPP-Contribution → NetIncome → DeductionAtSource →
        //   ChargeRule → Charges → OtherFringeBenefits → StandardRemark →
        //   Remark → Contact
        var s = new XElement(Ns + "S",
            new XElement(Ns + "DocID", Guid.NewGuid().ToString().ToUpperInvariant()),
            // CreationDate: xs:dateTime, PFLICHT [1..1] gemäss ELM 6.0 Spec
            new XElement(Ns + "CreationDate",
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv)),
            new XElement(Ns + "Period",
                new XElement(Ns + "from",  ToIsoDate(d.PeriodeVon)),
                new XElement(Ns + "until", ToIsoDate(d.PeriodeBis))
            )
        );

        // Box G = Kantine gratis → leeres <CanteenLunchCheck/> einfügen
        if (d.BoxGKantineGratis)
            s.Add(new XElement(Ns + "CanteenLunchCheck"));

        // Ziffer 1 → <Income>
        if (d.Ziffer1Lohn is decimal lohn)
            s.Add(new XElement(Ns + "Income", FormatMoney(lohn)));

        // Ziffer 8 → <GrossIncome>
        if (d.Ziffer8BruttoTotal is decimal brutto)
            s.Add(new XElement(Ns + "GrossIncome", FormatMoney(brutto)));

        // Ziffer 9 → <AHV-ALV-NBUV-AVS-AC-AANP-Contribution>
        if (d.Ziffer9AhvIvEoAlvNbu is decimal sv && sv > 0)
            s.Add(new XElement(Ns + "AHV-ALV-NBUV-AVS-AC-AANP-Contribution", FormatMoney(sv)));

        // Ziffer 10.1 / 10.2 → <BVG-LPP-Contribution><Regular/><Buyin/>
        if ((d.Ziffer101BvgOrdentlich is decimal bvgR && bvgR > 0)
            || (d.Ziffer102BvgEinkauf is decimal bvgE && bvgE > 0))
        {
            var bvg = new XElement(Ns + "BVG-LPP-Contribution");
            if (d.Ziffer101BvgOrdentlich is decimal r && r > 0)
                bvg.Add(new XElement(Ns + "Regular", FormatMoney(r)));
            if (d.Ziffer102BvgEinkauf is decimal e && e > 0)
                bvg.Add(new XElement(Ns + "Buyin", FormatMoney(e)));
            s.Add(bvg);
        }

        // Ziffer 11 → <NetIncome>
        if (d.Ziffer11Nettolohn is decimal netto)
            s.Add(new XElement(Ns + "NetIncome", FormatMoney(netto)));

        // Ziffer 12 → <DeductionAtSource> (Quellensteuerabzug, ELM 6.0 Kap. 3.1)
        if (d.Ziffer12Quellensteuer is decimal qst && qst > 0)
            s.Add(new XElement(Ns + "DeductionAtSource", FormatMoney(qst)));

        // <Remark> — Bemerkungen Zeile 1..4 zusammengefasst
        var remarks = new[]
        {
            d.Ziffer141Bemerkungen,
            d.Ziffer142Bemerkungen,
            d.Ziffer151Bemerkungen,
            d.Ziffer152Bemerkungen
        }.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();
        if (remarks.Length > 0)
            s.Add(new XElement(Ns + "Remark", string.Join(" — ", remarks)));

        var t = new XElement(Ns + "T",
            new XAttribute("SID", "000"),
            new XAttribute("SysV", "001"),
            company,
            personId,
            s
        );

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), t);
        // SaveOptions.DisableFormatting → keine Einrückung/Newlines (= kompakter Barcode)
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    // ════════════════════════════════════════════════════════════════════
    // HELPERS
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// CHF-Betrag mit Punkt-Dezimaltrennzeichen, 2 Nachkommastellen, keine
    /// Tausendertrennzeichen. Beispiel: 1234.5 → "1234.50".
    /// </summary>
    private static string FormatMoney(decimal v) => v.ToString("F2", Inv);

    /// <summary>
    /// Schweizer Datum "dd.MM.yyyy" → ISO "yyyy-MM-dd". Leer → "".
    /// </summary>
    private static string ToIsoDate(string? swiss)
    {
        if (string.IsNullOrWhiteSpace(swiss)) return "";
        if (DateTime.TryParseExact(swiss, "dd.MM.yyyy", Inv, DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd");
        if (DateTime.TryParse(swiss, out var dt2))
            return dt2.ToString("yyyy-MM-dd");
        return "";
    }

    /// <summary>
    /// Trim auf max. n Zeichen (Swissdec-Schema-Constraint für HR-RC-Name).
    /// </summary>
    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));
}
