using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace HrSystem.Services;

/// <summary>
/// Generiert pain.001.001.09-XML (ISO 20022 Customer Credit Transfer
/// Initiation) gemäss Swiss Payment Standards (SIX). Wird vom Lohnlauf-DTA
/// verwendet — ein XML-File für die MA-Auszahlungen, ein zweites für
/// Lohnabtretungs-Empfänger (Behörden).
///
/// Vereinfachung: Wir verwenden den Schweizer Inland-Standard (kein SEPA-
/// SvcLvl, kein LclInstrm). Die Hausbank verarbeitet sowohl CH-IBAN- als
/// auch ausländische IBANs (Revolut/Wise) korrekt — bei letzteren gibt es
/// die volle Cdtr-Adresse mit, wie's beim manuellen Erfassen in Mirus auch
/// üblich war.
/// </summary>
public class Iso20022PainService
{
    private static readonly XNamespace Ns =
        "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

    public record PaymentInstruction(
        string EndToEndId,
        decimal Amount,                  // CHF
        string  CreditorName,            // z.B. "Hans Müller" oder "Revolut Bank UAB"
        string? CreditorStreet,          // Strasse + Hausnummer
        string? CreditorPostalCode,
        string? CreditorCity,
        string  CreditorCountry,         // ISO-3166-1 alpha-2 (CH/LT/DE/...)
        string  CreditorIban,
        string? CreditorBic,
        string? RemittanceInfo           // Zahlungsgrund/Reference
    );

    public record DtaRequest(
        string  MessageId,                // Unique pro Generierung
        DateTime CreationDateTime,
        string  InitiatorName,            // Auftraggeber-Name
        string? InitiatorStreet,
        string? InitiatorPostalCode,
        string? InitiatorCity,
        string  InitiatorCountry,         // CH
        DateOnly ExecutionDate,           // Auszahlungsdatum
        string  DebtorName,
        string  DebtorIban,
        string? DebtorBic,
        IReadOnlyList<PaymentInstruction> Payments
    );

    public byte[] Generate(DtaRequest req)
    {
        if (req.Payments.Count == 0)
            throw new InvalidOperationException("Keine Zahlungen — DTA leer.");

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Document",
                new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
                BuildCstmrCdtTrfInitn(req)
            )
        );

        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(false)))
        {
            doc.Save(writer);
        }
        return ms.ToArray();
    }

    private XElement BuildCstmrCdtTrfInitn(DtaRequest req)
    {
        // CtrlSum = exakte Summe ALLER InstdAmt im File. Jeder Betrag wird VOR
        // dem Aufsummieren auf 2 Nachkommastellen gerundet — exakt so wie er
        // dann auch als InstdAmt ausgegeben wird (siehe BuildTransaction →
        // FmtAmount(R2(p.Amount))). Dadurch kann die Kontrollsumme niemals von
        // der Summe der Einzelpositionen abweichen.
        decimal total = 0;
        foreach (var p in req.Payments) total += R2(p.Amount);
        total = R2(total);

        return new XElement(Ns + "CstmrCdtTrfInitn",
            BuildGroupHeader(req, total),
            BuildPaymentInfo(req, total)
        );
    }

    private XElement BuildGroupHeader(DtaRequest req, decimal total) =>
        // SPS 2024-konform (Walter 21.05.2026):
        //   • InitgPty enthält NUR den Namen (PstlAdr ist verboten — siehe alter Bug-Report).
        //   • CtrlSum WIEDER drin: die LUKB (und weitere Banken) interpretieren
        //     eine FEHLENDE CtrlSum als 0.00 und melden dann „widersprüchliche
        //     Kontrollsumme im Vergleich zum Datei-Inhalt". CtrlSum ist im
        //     pain.001.001.09 optional, MUSS aber — wenn vorhanden — exakt der
        //     Summe aller InstdAmt entsprechen. Reihenfolge: nach NbOfTxs,
        //     vor InitgPty (Schema-Sequence).
        new(Ns + "GrpHdr",
            new XElement(Ns + "MsgId",   Truncate(req.MessageId, 35)),
            new XElement(Ns + "CreDtTm", req.CreationDateTime.ToString("yyyy-MM-ddTHH:mm:ss")),
            new XElement(Ns + "NbOfTxs", req.Payments.Count.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns + "CtrlSum", FmtAmount(total)),
            new XElement(Ns + "InitgPty",
                new XElement(Ns + "Nm", Truncate(req.InitiatorName, 70))
            )
        );

    private XElement BuildPaymentInfo(DtaRequest req, decimal total)
    {
        // SPS 2024-konform (Walter 21.05.2026):
        //   • CtrlSum WIEDER drin (gleicher Grund wie GrpHdr) — auf PmtInf-Ebene
        //     muss sie der Summe der InstdAmt innerhalb dieser PmtInf entsprechen.
        //     Da es genau EINE PmtInf pro File gibt, ist das identisch mit der
        //     GrpHdr-CtrlSum. Reihenfolge: nach NbOfTxs, vor ReqdExctnDt.
        //   • PmtTpInf/SvcLvl/Prtry „CH01" entfällt — der GEFEG-Validator weist
        //     darauf hin dass Prtry-Werte nur in Absprache mit der Bank gehören;
        //     ohne SvcLvl wählt die Bank ihr Inland-Default-Verhalten (für CH-IBAN-
        //     zu-CH-IBAN-Inlandzahlungen identisch zum CH01-Effekt).
        //   • Dbtr/PstlAdr und DbtrAcct/Ccy waren schon weg (vorheriger Fix).
        var pmtInf = new XElement(Ns + "PmtInf",
            new XElement(Ns + "PmtInfId",  Truncate(req.MessageId + "-1", 35)),
            new XElement(Ns + "PmtMtd",    "TRF"),
            new XElement(Ns + "BtchBookg", "true"),
            new XElement(Ns + "NbOfTxs",   req.Payments.Count.ToString(CultureInfo.InvariantCulture)),
            new XElement(Ns + "CtrlSum",   FmtAmount(total)),
            new XElement(Ns + "ReqdExctnDt",
                new XElement(Ns + "Dt", req.ExecutionDate.ToString("yyyy-MM-dd"))
            ),
            new XElement(Ns + "Dbtr",
                new XElement(Ns + "Nm", Truncate(req.DebtorName, 70))
            ),
            new XElement(Ns + "DbtrAcct",
                new XElement(Ns + "Id", new XElement(Ns + "IBAN", NormalizeIban(req.DebtorIban)))
            ),
            BuildAgent(req.DebtorBic, isDebtor: true)
        );

        foreach (var p in req.Payments)
            pmtInf.Add(BuildTransaction(p));

        return pmtInf;
    }

    private XElement BuildTransaction(PaymentInstruction p)
    {
        var tx = new XElement(Ns + "CdtTrfTxInf",
            new XElement(Ns + "PmtId",
                new XElement(Ns + "InstrId",    Truncate(p.EndToEndId, 35)),
                new XElement(Ns + "EndToEndId", Truncate(p.EndToEndId, 35))
            ),
            new XElement(Ns + "Amt",
                new XElement(Ns + "InstdAmt",
                    new XAttribute("Ccy", "CHF"),
                    FmtAmount(R2(p.Amount))
                )
            )
        );

        // Cdtr-Bank: nur wenn BIC bekannt — pain.001 erlaubt fehlende CdtrAgt
        if (!string.IsNullOrWhiteSpace(p.CreditorBic))
            tx.Add(BuildAgent(p.CreditorBic, isDebtor: false));

        // Cdtr (Empfänger) mit Adresse
        var cdtr = new XElement(Ns + "Cdtr",
            new XElement(Ns + "Nm", Truncate(p.CreditorName, 70))
        );
        var addr = BuildPostalAddress(p.CreditorStreet, p.CreditorPostalCode, p.CreditorCity, p.CreditorCountry);
        if (addr != null) cdtr.Add(addr);
        tx.Add(cdtr);

        // CdtrAcct (IBAN)
        tx.Add(new XElement(Ns + "CdtrAcct",
            new XElement(Ns + "Id", new XElement(Ns + "IBAN", NormalizeIban(p.CreditorIban)))
        ));

        // RmtInf (Zahlungsgrund)
        if (!string.IsNullOrWhiteSpace(p.RemittanceInfo))
        {
            tx.Add(new XElement(Ns + "RmtInf",
                new XElement(Ns + "Ustrd", Truncate(p.RemittanceInfo!, 140))
            ));
        }

        return tx;
    }

    private XElement BuildAgent(string? bic, bool isDebtor)
    {
        var elementName = isDebtor ? "DbtrAgt" : "CdtrAgt";
        var inner = new XElement(Ns + "FinInstnId");
        if (!string.IsNullOrWhiteSpace(bic))
        {
            inner.Add(new XElement(Ns + "BICFI", bic.Trim().ToUpperInvariant()));
        }
        else
        {
            // Falls kein BIC: "OTHR" als Fallback erlaubt (z.B. wenn IBAN ohne BIC)
            inner.Add(new XElement(Ns + "Othr", new XElement(Ns + "Id", "NOTPROVIDED")));
        }
        return new XElement(Ns + elementName, inner);
    }

    private XElement? BuildPostalAddress(string? street, string? plz, string? city, string country)
    {
        if (string.IsNullOrWhiteSpace(street)
            && string.IsNullOrWhiteSpace(plz)
            && string.IsNullOrWhiteSpace(city)
            && string.IsNullOrWhiteSpace(country))
            return null;

        var addr = new XElement(Ns + "PstlAdr");
        if (!string.IsNullOrWhiteSpace(street))
        {
            // SPS 2024 D V1 (Schweizer Inlandzahlung): Strasse + Hausnummer
            // getrennt — Banken können dann sauber abgleichen. Wir splitten
            // den letzten alphanumerischen Block ab wenn er mit einer Ziffer
            // beginnt (z.B. „Sonnenrainweg 5a" → StrtNm „Sonnenrainweg",
            // BldgNb „5a"). Postfach / „ohne Hausnummer" → kompletter String
            // bleibt im StrtNm. Walter-Fix 19.05.2026.
            var (strtNm, bldgNb) = SplitStreet(street);
            addr.Add(new XElement(Ns + "StrtNm", Truncate(strtNm, 70)));
            if (!string.IsNullOrWhiteSpace(bldgNb))
                addr.Add(new XElement(Ns + "BldgNb", Truncate(bldgNb, 16)));
        }
        if (!string.IsNullOrWhiteSpace(plz))
            addr.Add(new XElement(Ns + "PstCd", Truncate(plz, 16)));
        if (!string.IsNullOrWhiteSpace(city))
            addr.Add(new XElement(Ns + "TwnNm", Truncate(city, 35)));
        if (!string.IsNullOrWhiteSpace(country))
            addr.Add(new XElement(Ns + "Ctry", country.Trim().ToUpperInvariant()));
        return addr;
    }

    /// <summary>
    /// Splittet eine kombinierte Strassen-Zeile in StrtNm + BldgNb.
    /// Regex: nimmt die letzte Ziffern-Sequenz am Stringende, optional gefolgt
    /// von Buchstaben/`-`/`/`. Funktioniert MIT und OHNE Leerzeichen vor der
    /// Hausnummer (Walter-Fix 19.05.2026 für Daten wie „Bernstrasse120b").
    /// Beispiele:
    ///   "Sonnenrainweg 5a"             → ("Sonnenrainweg",  "5a")
    ///   "Äussere Luzernerstrasse 13"   → ("Äussere Luzernerstrasse", "13")
    ///   "Bernstrasse120b"              → ("Bernstrasse",    "120b")
    ///   "Postfach"                     → ("Postfach",       null)
    ///   "Postfach 123"                 → ("Postfach",       "123")
    ///   "Bahnhofstrasse 12, c/o Müller" → ("Bahnhofstrasse 12, c/o Müller", null)
    /// </summary>
    private static (string Street, string? Number) SplitStreet(string street)
    {
        var trimmed = street.Trim();
        // ^(.+?)\s*(\d[\dA-Za-z\-/]*)$
        //   Gruppe 1: alles vor der Hausnummer (lazy, damit Hausnummer maximal greift)
        //   \s*    : optionales Leerzeichen
        //   Gruppe 2: Hausnummer = Ziffer + optional Buchstaben/Ziffern/-/  am Stringende
        var m = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^(.+?)\s*(\d[\dA-Za-z\-/]*)$");
        if (!m.Success) return (trimmed, null);
        var strt = m.Groups[1].Value.Trim();
        var nb   = m.Groups[2].Value;
        // Defensiv: leere Strasse oder rein-numerische „Strasse" → kein Split
        if (string.IsNullOrWhiteSpace(strt)) return (trimmed, null);
        return (strt, nb);
    }

    private static string NormalizeIban(string iban)
        => new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    // Auf 2 Nachkommastellen runden (kaufmännisch). Wird sowohl für jeden
    // InstdAmt als auch für die CtrlSum-Aufsummierung verwendet, damit beide
    // garantiert übereinstimmen.
    private static decimal R2(decimal v)
        => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static string FmtAmount(decimal v)
        => v.ToString("F2", CultureInfo.InvariantCulture);

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);
}
