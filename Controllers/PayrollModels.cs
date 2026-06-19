using System.Collections.Generic;
using HrSystem.Services;

namespace HrSystem.Controllers;

// ============================================================================
// Payroll-Modelle: Berechnungs-Records (intern) + Request-DTOs.
// Walter-Vorgabe 20.05.2026: aus PayrollController.cs ausgelagert (Etappe 1 der
// Controller-Entflechtung). KEINE Logik — nur Datenträger. Bewusst im selben
// Namespace HrSystem.Controllers belassen, damit alle bestehenden Referenzen
// (BuildResult-Parameter, new SvBases(...)/new SaldoBlock(...), [FromBody]-DTOs)
// unverändert auflösen.
// ============================================================================

/// <summary>
/// Granulare SV-Berechnungsbasen pro Versicherungstyp.
/// Jede Basis = Summe aller Lohnpositionen, die für diese SV pflichtig sind.
/// </summary>
public record SvBases(
    decimal Ahv,   // AHV / IV / EO + ALV
    decimal Nbuv,  // Nichtberufsunfallversicherung
    decimal Ktg,   // Krankentaggeldversicherung
    decimal Bvg,   // Pensionskasse (vor Koordinationsabzug)
    decimal Qst    // Quellensteuer-Basis
);

/// <summary>
/// Alle Saldo-relevanten Werte für BuildResult gebündelt:
/// Stunden (Arbeit + Nacht), Ferien-Tage, Ferien-Geld, Feiertag-Tage
/// und 13. ML. Jeder Block folgt dem Muster
/// Vormonat → Accrual → Genommen → Neu (so weit zutreffend).
///
/// Die einzelnen Felder werden im Return-Objekt von BuildResult 1:1
/// als JSON-Felder ausgegeben — die Property-Namen hier müssen also
/// stabil bleiben, wenn das Frontend nicht brechen soll.
/// </summary>
public record SaldoBlock(
    // ── Stunden (Arbeitszeit-Saldo) ──────────────────────────────
    decimal VormonatHourSaldo,
    decimal NeuerHourSaldo,
    decimal WorkedHours,
    decimal SollStunden,
    decimal Mehrstunden,
    decimal AbsenzGutschrift,

    // ── Nacht-Zeitzuschlag ───────────────────────────────────────
    decimal NightHours,
    decimal NightBonus,
    decimal NachtKompStunden,
    decimal VormonatNachtSaldo,
    decimal NeuerNachtSaldo,

    // ── Ferien-Tage-Saldo ────────────────────────────────────────
    int     VacationWeeks,
    decimal VormonatFerienTage,
    decimal FerienTageAccrual,
    decimal FerienTageGenommen,
    decimal FerienTageSaldoNeu,

    // ── Ferien-Geld-Saldo (nur UTP/MTP; FIX = 0) ────────────────
    decimal VormonatFerienGeld,
    decimal FerienGeldSaldoNeu,
    decimal FerienGeldAuszahlung,

    // ── Feiertag-Tage-Saldo (nur FIX/FIX-M; sonst = 0) ──────────
    decimal VormonatFeiertagTage,
    decimal FeiertagTageAccrual,
    decimal FeiertagTageGenommen,
    decimal FeiertagTageSaldoNeu,

    // ── 13. Monatslohn ───────────────────────────────────────────
    decimal ThirteenthPct,
    decimal PrevThirteenth,

    // ── 13. Monatslohn — Auszahlungs-Details (für Saldi-Anzeige im
    //    Auszahlungsmonat, damit Walter Vormonat / Aktuell / Bezogen /
    //    Saldo sehen kann statt nur "alles 0"): ──────────────────────
    decimal? ThirteenthPrevForDisplay   = null,   // Saldo VOR der Auszahlung
    decimal? ThirteenthAccrualForDisplay = null,  // aktueller Monatszuwachs
    decimal? ThirteenthPayout            = null,  // ausbezahlter Betrag

    // ── Absenz-Aufschlüsselung pro AbsenceType (für Anzeige im Lohnzettel) ──
    Dictionary<string, decimal>? AbsenzBreakdown = null,

    // ── Optional: Soll-Berechnungs-Details für Anzeige im Lohnzettel ──
    decimal? SollStundenVoll = null,             // vor Ferien-Reduktion
    decimal? SollFerienReduktion = null,         // GuarH/7 × Ferientage
    decimal? SollKrankReduktion = null,          // MTP: GuarH/5 × Krank-Werktage
    decimal? SollUnfallReduktion = null,         // MTP: GuarH/5 × Unfall-Werktage
    decimal? GuaranteedHoursPerWeek = null,      // 21 (für Erläuterung)
    decimal? FerienTageInPeriode = null,         // 4 (für Erläuterung)

    // ── Optional: Ferien-Kürzungs-Vorschlag (Art. 329b OR) ────────────
    FerienKuerzungResult? FerienKuerzungVorschlag = null,
    decimal? FerienKuerzungVorschlagTage = null,

    // ── 13.-ML-Basis (für Saldo-Berechnung: Summe aller Lohnpositionen
    //    mit Flag ZaehltAlsBasis13ml = true; via SumByFlag im Controller) ──
    decimal Basis13ml = 0
);

// SaveSaldoDto entfernt am 09.06.2026 mit dem /api/payroll/save-Endpoint.
// Grund: nahm Brutto/Netto/Saldi UNVERIFIZIERT aus dem Body und speicherte sie
// direkt — der einzig sichere Schreibpfad zu PayrollSaldo ist /confirm, das
// die Beträge intern via CalculateAsync server-autoritativ regeneriert.

public record ConfirmPayrollDto(
    int EmployeeId, int CompanyProfileId, int PayrollPeriodeId,
    int Year, int Month,
    decimal HourSaldo, decimal NachtSaldo, decimal NightHoursWorked,
    decimal FerienGeldSaldo, decimal FerienTageSaldo,
    decimal ThirteenthMonthMonthly, decimal ThirteenthMonthAccumulated,
    decimal GrossAmount, decimal NetAmount,
    decimal SvBasisAhv, decimal SvBasisBvg, decimal QstBetrag,
    string SlipJson,
    LohnAbtretungConfirmDto[]? LohnAbtretungen = null,
    decimal FeiertagTageSaldo = 0m,
    // Walter-Vorgabe 20.05.2026: einzige GF-Entscheidung, die der Server beim
    // autoritativen Nachrechnen nicht selbst ableiten kann — Ferien-Kürzung
    // (Art. 329b OR) anwenden ja/nein. Alle Geldbeträge rechnet der Server selbst.
    bool ApplyFerienKuerzung = false);

public record LohnAbtretungConfirmDto(int AssignmentId, decimal Betrag);

public record ReopenPayrollDto(
    int EmployeeId, int CompanyProfileId, int PayrollPeriodeId,
    int Year, int Month);
