using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Pott-Logik für anteilsmässige Ferien-Auszahlung bei MTP/UTP-MA
/// (Walter-Vorgabe 09.05.2026, erweitert für Akonto-Lauf 16.05.2026).
///
/// Im Bezugsmonat von Ferien werden bei Stundenlöhnern (UTP/MTP) die effektiv
/// bezogenen Tage aus dem aggregierten Pott bezahlt. Der Pott schliesst den
/// aktuellen Monat ein — sowohl beim CHF-Saldo als auch bei den Tagen.
///
///   Pott CHF   = Vormonats-Feriengeld-Saldo + Ferien-Akkumulation aktueller Monat
///   Pott Tage  = Vormonats-Tage-Saldo       + Ferien-Tage-Accrual aktueller Monat
///   Tagessatz  = Pott CHF / Pott Tage
///   Auszahlung = Tagessatz × bezogene Tage diesen Monat
///   Cap        = Pott CHF (kein Vorbezug, Saldo bleibt ≥ 0)
///
/// Beispiel: Pott 1000 CHF / 10 Tage = 100 CHF/Tag, 6 Tage bezogen → 600 CHF.
///
/// Verwendung:
///   • PayrollController (Definitivlauf): rechnet alle im Periodenmonat bezogenen Tage
///   • AkontoLaufService (Akonto-Lauf): rechnet NUR vollständig abgeschlossene
///     Ferien-Bezüge bis zum Akonto-Stichtag (FerienEnde ≤ Stichtag). Bezüge
///     die über den Stichtag hinausragen werden komplett ignoriert und im
///     Definitivlauf nachverrechnet (Walter 16.05.2026, Etappe 5).
/// </summary>
public class FerienAuszahlungService
{
    public record Pott(
        decimal PottChf,        // Vormonats-FerienGeld + Akkumulation aktueller Monat
        decimal PottTage,       // Vormonats-Tage-Saldo + Tage-Accrual aktueller Monat
        decimal BezogeneTage,   // Effektiv im Auszahlungszeitraum bezogene Tage
        decimal Tagessatz,      // PottChf / PottTage (0 falls Pott leer)
        decimal AuszahlungChf,  // Tagessatz × BezogeneTage, gedeckelt auf PottChf
        decimal SaldoNeuChf);   // PottChf − AuszahlungChf (immer ≥ 0)

    /// <summary>
    /// Berechnet die Auszahlung für einen einzigen MA in einem Bezugszeitraum.
    ///
    /// Alle CHF/Tage-Werte werden vom Aufrufer ermittelt:
    ///   • vormonatFerienGeldChf  — letzter PayrollSaldo der Vorperiode
    ///   • monatlicheAkkumulationChf — Berechnung diesen Monats (Brutto × Ferien-%)
    ///   • vormonatFerienTage     — letzter Tages-Saldo der Vorperiode
    ///   • monatlicheAkkumulationTage — diesen Monat hinzukommend (Vacation × Wochen)
    ///   • bezogeneTage           — relevante Ferien-Tage im Berechnungszeitraum
    ///                              (Akonto: nur abgeschlossene bis Stichtag)
    ///
    /// Liefert auch die Zwischenwerte (PottChf, PottTage, Tagessatz) zurück
    /// damit das Frontend transparent anzeigen kann woher der Betrag kommt.
    /// </summary>
    public static Pott Compute(
        decimal vormonatFerienGeldChf,
        decimal monatlicheAkkumulationChf,
        decimal vormonatFerienTage,
        decimal monatlicheAkkumulationTage,
        decimal bezogeneTage)
    {
        decimal pottChf  = vormonatFerienGeldChf + monatlicheAkkumulationChf;
        decimal pottTage = vormonatFerienTage    + monatlicheAkkumulationTage;
        decimal tagessatz   = 0m;
        decimal auszahlung  = 0m;
        if (bezogeneTage > 0m && pottTage > 0m && pottChf > 0m)
        {
            tagessatz  = pottChf / pottTage;
            auszahlung = Math.Round(tagessatz * bezogeneTage, 2);
            // Cap auf Pott (kein Vorbezug — Saldo bleibt ≥ 0)
            if (auszahlung > pottChf) auszahlung = Math.Round(pottChf, 2);
        }
        decimal saldoNeu = Math.Round(pottChf - auszahlung, 2);
        return new Pott(
            PottChf:        Math.Round(pottChf, 2),
            PottTage:       Math.Round(pottTage, 2),
            BezogeneTage:   Math.Round(bezogeneTage, 2),
            Tagessatz:      Math.Round(tagessatz, 2),
            AuszahlungChf:  auszahlung,
            SaldoNeuChf:    saldoNeu);
    }

    /// <summary>
    /// Helfer für den Akonto-Lauf (Walter 16.05.2026, Regel 5/6):
    /// Summiert die Tage aller Ferien-Absenzen, deren <c>DateTo</c> ≤
    /// <paramref name="stichtag"/> liegt UND deren <c>DateFrom</c> ≥
    /// Periodenstart ist. Ferien, die über den Stichtag hinausragen, werden
    /// komplett ignoriert (auch nicht anteilig) — sie kommen im Definitivlauf
    /// am Monatsende nachgereicht.
    /// </summary>
    public static decimal SumAbgeschlosseneFerientageBisStichtag(
        IEnumerable<Absence> absences,
        DateOnly periodFrom,
        DateOnly stichtag)
    {
        decimal sum = 0m;
        foreach (var a in absences)
        {
            if (a.AbsenceType != "FERIEN") continue;
            if (a.DateFrom < periodFrom)  continue;   // ausserhalb der Periode
            if (a.DateTo   > stichtag)    continue;   // noch nicht abgeschlossen
            int days = a.DateTo.DayNumber - a.DateFrom.DayNumber + 1;
            if (days > 0) sum += days;
        }
        return sum;
    }
}
