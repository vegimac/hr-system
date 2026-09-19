using System.Text.Json;
using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// QST-Jahresmodell (ESTV / KS 45): GE, FR, VD, VS, TI.
/// Satz-Lohn = YTD-Durchschnitt, Steuer = Jahressatz × YTD − bereits bezahlt.
/// Negativ = Rückerstattung. Walter 19.09.2026, O1.
/// </summary>
public static class QstJahresmodell
{
    public readonly record struct Ergebnis(
        decimal SatzLohn,
        decimal SatzPct,
        decimal Jahressteuer,
        decimal QstMonat);

    public readonly record struct SlipZeile(
        decimal QstBezahlt,
        decimal IstBasis,
        decimal? SatzBasis);

    public readonly record struct YtdStand(
        decimal IstBisher,
        decimal BezahltBisher,
        int NMonate);

    public static bool GiltFuer(string? kanton)
        => QstTarifVorschlagLogic.IstQstJahresmodell(kanton);

    /// <summary>
    /// Erster Tag des Monats, ab dem dieser Kanton in der zusammenhängenden
    /// QST-Kette gilt — frühestens 1.1. des Lohnjahrs, frühestens Eintritt.
    /// Tarifwechsel im selben Kanton (A→C) setzt den Start NICHT zurück.
    /// </summary>
    public static DateOnly ModellStart(
        int jahr,
        DateOnly? eintritt,
        IEnumerable<EmployeeQuellensteuer> versionen,
        string kanton,
        DateOnly periodTo)
    {
        var start = new DateOnly(jahr, 1, 1);
        if (eintritt is { } e && e > start)
            start = new DateOnly(e.Year, e.Month, 1);

        var kantonAb = KantonBeginn(versionen, kanton, periodTo);
        if (kantonAb is { } kb)
        {
            var kbMonat = new DateOnly(kb.Year, kb.Month, 1);
            if (kbMonat > start) start = kbMonat;
        }
        return start;
    }

    /// <summary>Inklusive Anzahl Kalendermonate von Start bis Periodenbeginn.</summary>
    public static int AnzahlMonate(DateOnly von, DateOnly periodFrom)
    {
        int n = (periodFrom.Year - von.Year) * 12 + periodFrom.Month - von.Month + 1;
        return n < 1 ? 1 : n;
    }

    /// <summary>
    /// Beginn der aktuellen Kantons-Kette (ValidFrom der ältesten Version
    /// desselben Kantons ohne Unterbruch). null = keine Version.
    /// </summary>
    public static DateOnly? KantonBeginn(
        IEnumerable<EmployeeQuellensteuer> versionen,
        string kanton,
        DateOnly periodTo)
    {
        var aktuell = QstVersionWahl.Waehle(versionen, periodTo);
        if (aktuell == null) return null;
        var kt = (kanton ?? "").Trim();
        var begin = aktuell.ValidFrom;
        foreach (var v in versionen
                     .Where(v => v.ValidFrom <= aktuell.ValidFrom)
                     .OrderByDescending(v => v.ValidFrom)
                     .ThenByDescending(v => v.Id))
        {
            if (!string.Equals((v.Steuerkanton ?? "").Trim(), kt, StringComparison.OrdinalIgnoreCase))
                break;
            begin = v.ValidFrom;
        }
        return begin;
    }

    /// <summary>
    /// Jahressteuer = Satz% × YTD; Monat = Jahressteuer − bereits bezahlt.
    /// Satz-Lohn = Round05(YTD / n). Mindeststeuer der ESTV-Monatsstufe
    /// greift hier nicht (sonst läge sie auf dem ganzen YTD).
    /// </summary>
    public static Ergebnis Rechne(
        decimal ytdIst,
        decimal bereitsBezahlt,
        int nMonate,
        decimal satzPct)
    {
        if (nMonate < 1) nMonate = 1;
        if (ytdIst < 0) ytdIst = 0;
        var satzLohn = PayrollCalculations.Round05(ytdIst / nMonate);
        var jahressteuer = Math.Round(ytdIst * satzPct / 100m, 2, MidpointRounding.AwayFromZero);
        var qstMonat = Math.Round(jahressteuer - bereitsBezahlt, 2);
        return new Ergebnis(satzLohn, satzPct, jahressteuer, qstMonat);
    }

    /// <summary>
    /// QST-Zeile aus SlipJson. QstBezahlt ist vorzeichenbehaftet
    /// (positiv = Abzug, negativ = Rückerstattung). IST = Bemessung der Zeile.
    /// </summary>
    public static SlipZeile LeseSlip(string? slipJson)
    {
        if (string.IsNullOrWhiteSpace(slipJson))
            return new SlipZeile(0, 0, null);
        try
        {
            using var doc = JsonDocument.Parse(slipJson);
            decimal brutto = 0;
            if (doc.RootElement.TryGetProperty("totalLohn", out var b)
                && b.ValueKind == JsonValueKind.Number)
                brutto = b.GetDecimal();

            if (doc.RootElement.TryGetProperty("abzugLines", out var lines)
                && lines.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (!line.TryGetProperty("categoryCode", out var cc)
                        || cc.ValueKind != JsonValueKind.String
                        || cc.GetString() != "QST")
                        continue;
                    decimal betrag = line.TryGetProperty("betrag", out var be)
                        && be.ValueKind == JsonValueKind.Number
                        ? be.GetDecimal() : 0;
                    decimal basis = line.TryGetProperty("basis", out var ba)
                        && ba.ValueKind == JsonValueKind.Number
                        ? ba.GetDecimal() : brutto;
                    decimal? satzBasis = line.TryGetProperty("satzBasis", out var sb)
                        && sb.ValueKind == JsonValueKind.Number
                        ? sb.GetDecimal() : null;
                    // Slip: Abzug negativ, Gutschrift positiv → bezahlt = −betrag.
                    return new SlipZeile(-betrag, basis, satzBasis);
                }
            }
            return new SlipZeile(0, brutto, null);
        }
        catch (JsonException)
        {
            return new SlipZeile(0, 0, null);
        }
    }
}
