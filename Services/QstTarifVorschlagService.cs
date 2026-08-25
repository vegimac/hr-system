using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Walter-Vorgabe 14.06.2026: zentrale serverseitige Logik für den
/// Quellensteuer-Tarifvorschlag. Vorher wurde das im Frontend heuristisch
/// gemacht — jetzt liegt die Wahrheit auf dem Server.
///
/// Architektur (analog PayrollCalculationService): die reine Berechnung
/// liegt in einer <see cref="QstTarifVorschlagLogic"/>-Klasse — statisch,
/// seiteneffekt-frei, alle Daten als Parameter. Damit ist die Logik
/// einzeln testbar ohne DB-Setup. Dieser DI-Service ist nur der Wrapper
/// fürs Datenladen (Employee + Familie + Tarif-Tabelle).
/// </summary>
public class QstTarifVorschlagService
{
    private readonly AppDbContext _db;
    private readonly QuellensteuerTarifService _tarifService;

    public QstTarifVorschlagService(AppDbContext db, QuellensteuerTarifService tarifService)
    {
        _db = db;
        _tarifService = tarifService;
    }

    /// <summary>
    /// Berechnet den Tarifvorschlag für einen MA am gewünschten Stichtag.
    /// Wenn der MA nicht existiert → NotFound. Wenn der MA keinen
    /// Wohnkanton hat, wird der Vorschlag trotzdem gebaut, aber
    /// `InTariftabelleGefunden=false` zurückgeliefert mit einer Warnung.
    /// </summary>
    public async Task<QstTarifVorschlagResult?> BerechneAsync(int employeeId, DateOnly stichtag)
    {
        var emp = await _db.Employees
            .Where(e => e.Id == employeeId)
            .Select(e => new {
                e.Id, e.MaritalStatus, e.Religion, e.CantonCode
            })
            .FirstOrDefaultAsync();
        if (emp == null) return null;

        // Kinder mit allen für die Berechnung nötigen Feldern laden.
        // WICHTIG (Walter-Bug 13.07.2026, HTTP 500): DateOnly.FromDateTime darf
        // NICHT in der SQL-Projektion stehen — Npgsql kann das auf date-Spalten
        // nicht übersetzen («Can only apply TimeOnly.FromDateTime on a
        // timestamp …»). Erst roh laden, dann im Speicher konvertieren.
        var kinderRaw = await _db.EmployeeFamilyMembers
            .Where(f => f.EmployeeId == employeeId
                     && f.MemberType  == "Kind"
                     && f.DateOfDeath == null)
            .Select(f => new { f.Id, f.QstDeductibleFrom, f.QstDeductibleUntil, f.DateOfBirth, f.AlternativeAddressId, f.InErstausbildung, f.LebtImHaushalt, f.GemeinsamesKindMitPartner })
            .ToListAsync();

        // Konkubinatspartner (Walter 25.08.2026, docs/konkubinat-qst-konzept.md):
        // liefert die Einkommensfrage in die Entscheidtabelle H1/A0.
        var kPartnerEinkommen = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId
                     && f.MemberType == "Konkubinatspartner"
                     && f.DateOfDeath == null)
            .OrderByDescending(f => f.Id)
            .Select(f => new { f.MaHatHoeheresEinkommen, f.Erwerbstaetig })
            .FirstOrDefaultAsync();
        var konkubinat = kPartnerEinkommen != null
            ? new QstKonkubinatInput(kPartnerEinkommen.MaHatHoeheresEinkommen,
                                     kPartnerEinkommen.Erwerbstaetig)
            : null;
        // Walter-Vorgabe 20.08.2026: Erstausbildung zusätzlich aus einer am
        // Stichtag AKTIVEN Ausbildungszulage (AZ) ableiten — wer AZ bekommt,
        // ist belegt in Ausbildung (gleiche Logik wie QstPflichtCheckService).
        var kindIds = kinderRaw.Select(f => f.Id).ToList();
        var azKindIds = kindIds.Count == 0
            ? new HashSet<int>()
            : (await _db.FamilyMemberAllowances.AsNoTracking()
                .Where(a => kindIds.Contains(a.FamilyMemberId)
                         && a.AllowanceType == "AZ"
                         && a.ValidFrom <= stichtag
                         && (a.ValidTo == null || a.ValidTo >= stichtag))
                .Select(a => a.FamilyMemberId)
                .ToListAsync()).ToHashSet();
        var kinder = kinderRaw
            .Select(f => new QstKindInput(
                f.QstDeductibleFrom.HasValue  ? DateOnly.FromDateTime(f.QstDeductibleFrom.Value)  : (DateOnly?)null,
                f.QstDeductibleUntil.HasValue ? DateOnly.FromDateTime(f.QstDeductibleUntil.Value) : (DateOnly?)null,
                f.DateOfBirth.HasValue        ? DateOnly.FromDateTime(f.DateOfBirth.Value)        : (DateOnly?)null,
                f.AlternativeAddressId,
                f.InErstausbildung || azKindIds.Contains(f.Id),
                f.LebtImHaushalt,
                f.GemeinsamesKindMitPartner
            ))
            .ToList();

        // Tariftabelle vom gewünschten Jahr (Stichtag.Year) — wenn der
        // Wohnkanton fehlt, leere Liste, das Logic-Modul liefert dann
        // `InTariftabelleGefunden=false`.
        IReadOnlyList<QstTarifInfo> tarife;
        if (string.IsNullOrWhiteSpace(emp.CantonCode))
        {
            tarife = Array.Empty<QstTarifInfo>();
        }
        else
        {
            try
            {
                // Walter-Vorgabe 19.08.2026: Tarife vor 2026 werden NICHT
                // integriert (Testjahr 2026, scharf ab 2027). Historische
                // Gültig-ab-Daten (z.B. Eintritt 2024) prüfen darum gegen
                // das früheste geführte Tarifjahr 2026 statt zu warnen.
                tarife = _tarifService.GetTarifKombinationen(emp.CantonCode, Math.Max(stichtag.Year, 2026));
            }
            catch
            {
                tarife = Array.Empty<QstTarifInfo>();
            }
        }

        return QstTarifVorschlagLogic.Berechne(
            zivilstand:   emp.MaritalStatus,
            religion:     emp.Religion,
            steuerkanton: emp.CantonCode,
            kinder:       kinder,
            stichtag:     stichtag,
            tarifTabelle: tarife,
            konkubinat:   konkubinat);
    }
}

/// <summary>Input pro Kind für die Vorschlag-Berechnung. Reine Daten, keine EF-Bindung.</summary>
public record QstKindInput(
    DateOnly? QstDeductibleFrom,
    DateOnly? QstDeductibleUntil,
    DateOnly? DateOfBirth,
    int?      AlternativeAddressId,      // erfasste andere Adresse (falls bekannt)
    // Walter-Vorgabe 20.08.2026: Kind in beruflicher/schulischer ERSTausbildung
    // — verlängert die QST-Berechtigung über den 18. Geburtstag hinaus (KS 45).
    bool      InErstausbildung = false,
    // Walter-Vorgabe 25.08.2026: expliziter Haushalt-Status aus dem Familien-
    // Modal (true = lebt beim MA). NULL = nicht übergeben (alte Aufrufer/Tests)
    // → Fallback auf die frühere Ableitung AlternativeAddressId == null.
    bool?     LebtImHaushalt = null,
    // Konkubinats-Logik (Walter 25.08.2026, docs/konkubinat-qst-konzept.md):
    // Gemeinsames Kind mit dem Konkubinatspartner? NULL = Frage offen.
    bool?     GemeinsamesKind = null
);

/// <summary>
/// Konkubinatspartner-Input für die Vorschlag-Logik (Walter 25.08.2026,
/// docs/konkubinat-qst-konzept.md). NULL-Objekt = kein K-Partner erfasst.
/// </summary>
public record QstKonkubinatInput(
    // Hat der/die MA das höhere Bruttoeinkommen als der Partner?
    // NULL = Frage offen (→ konservativ A0 + Warnung).
    bool? MaHatHoeheresEinkommen,
    // Walter 25.08.2026 (AG/ESTV-Praxis): ist der K-Partner NICHT erwerbstätig,
    // hat er kein Erwerbseinkommen → der MA ist zwangsläufig Hauptunterhalts-
    // träger → automatisch H1, auch ohne beantwortete Einkommensfrage.
    bool? PartnerErwerbstaetig = null
);

/// <summary>Resultat eines Tarifvorschlags. Geht 1:1 als JSON ans Frontend.</summary>
public record QstTarifVorschlagResult(
    string?  Steuerkanton,
    string   TarifCode,
    string?  TarifBezeichnung,
    int      AnzahlKinder,
    int      BerechneteKinder,
    int      KinderImSelbenHaushalt,
    bool     Kirchensteuer,
    string   QstCode,
    bool     InTariftabelleGefunden,
    string   Begruendung,
    IReadOnlyList<string> Warnings,
    DateOnly Stichtag,
    // Walter 25.08.2026: gemischter Konkubinatsfall (gemeinsame + nicht-
    // gemeinsame Kinder im Haushalt) — KEIN automatischer Vorschlag, das
    // Frontend zeigt stattdessen «Mit QST-Behörde abklären».
    bool     AbklaerungNoetig = false
);

/// <summary>
/// Reine, statische Berechnungs-Logik — testbar ohne DB. Nimmt alle
/// nötigen Daten als Parameter entgegen und gibt das fertige Result
/// zurück. Wird vom DI-Service (DB-Layer) UND von Unit-Tests aufgerufen.
/// </summary>
public static class QstTarifVorschlagLogic
{
    // Tarif-Bezeichnungen für die UI (gleich wie im Frontend).
    private static readonly Dictionary<string, string> TarifBezeichnungen = new()
    {
        ["A"] = "Alleinstehende ohne Kinder",
        ["B"] = "Verheiratet, Alleinverdiener",
        ["C"] = "Verheiratet, Doppelverdiener",
        ["D"] = "Nebenerwerb",
        ["H"] = "Alleinerziehend",
        ["L"] = "Grenzgänger alleinstehend",
        ["M"] = "Grenzgänger verheiratet",
        ["N"] = "Grenzgänger Nebenerwerb",
        ["P"] = "Pauschale",
        ["Q"] = "Grenzgänger alleinerziehend"
    };

    /// <summary>
    /// Hauptmethode der Vorschlag-Logik. Reihenfolge:
    ///   1) Kinder am Stichtag zählen (QST-Daten ODER Geburtsdatum-Fallback)
    ///   2) Tarif-Buchstaben aus Zivilstand + Kinder-im-Haushalt ableiten
    ///   3) Kirchensteuer aus Religion ableiten
    ///   4) Vorschlag gegen die Tariftabelle prüfen + Fallbacks
    /// </summary>
    public static QstTarifVorschlagResult Berechne(
        string?                       zivilstand,
        string?                       religion,
        string?                       steuerkanton,
        IReadOnlyList<QstKindInput>   kinder,
        DateOnly                      stichtag,
        IReadOnlyList<QstTarifInfo>   tarifTabelle,
        // Konkubinats-Logik (Walter 25.08.2026): NULL = kein K-Partner erfasst.
        QstKonkubinatInput?           konkubinat = null)
    {
        var warnings   = new List<string>();
        var begruendung = new List<string>();

        // 1) Kinder zählen — getrennt nach „im selben Haushalt" und „total
        // QST-berechtigt", da der H-Tarif explizit den selben Haushalt verlangt.
        var berechneteKinderTotal       = 0;
        var kinderImSelbenHaushalt = 0;
        // Konkubinat (Walter 25.08.2026): Haushalts-Kinder nach der Frage
        // «gemeinsames Kind mit dem K-Partner?» klassifizieren.
        int gemeinsamJa = 0, gemeinsamNein = 0, gemeinsamOffen = 0;
        foreach (var k in kinder)
        {
            if (!IstQstBerechtigt(k, stichtag)) continue;
            berechneteKinderTotal++;
            // Walter 25.08.2026: expliziter Haushalt-Status massgebend;
            // Fallback (NULL, alte Aufrufer): AlternativeAddressId == null.
            if (k.LebtImHaushalt ?? (k.AlternativeAddressId == null))
            {
                kinderImSelbenHaushalt++;
                if (k.GemeinsamesKind == true) gemeinsamJa++;
                else if (k.GemeinsamesKind == false) gemeinsamNein++;
                else gemeinsamOffen++;
            }
        }
        if (berechneteKinderTotal > 0)
        {
            begruendung.Add($"{berechneteKinderTotal} Kind(er) am Stichtag QST-berechtigt"
                + (kinderImSelbenHaushalt < berechneteKinderTotal
                    ? $" (davon {kinderImSelbenHaushalt} im selben Haushalt)"
                    : ""));
        }

        // 2) Tarif-Buchstaben
        var tarif = WaehleTarif(zivilstand, kinderImSelbenHaushalt, begruendung);

        // 2b) Konkubinats-Logik (Walter 25.08.2026, docs/konkubinat-qst-konzept.md):
        // greift nur, wenn ein K-Partner erfasst ist UND der Vorschlag H wäre
        // (= nicht verheiratet + Kind im Haushalt). Entscheidtabelle:
        //   alle Haushalts-Kinder NICHT gemeinsam → H bleibt (alleinerziehend)
        //   gemeinsames Kind + MA verdient mehr   → H bleibt (nie beide H1)
        //   gemeinsames Kind + Partner verdient mehr → A0 (H1 beim Partner)
        //   Fragen offen → konservativ A0 + Warnung (lieber zu viel abziehen)
        //   gemeinsam UND nicht-gemeinsam gemischt → KEIN Vorschlag,
        //   «Mit QST-Behörde abklären» (AbklaerungNoetig=true).
        bool abklaerungNoetig = false;
        if (konkubinat != null && tarif == "H")
        {
            if (gemeinsamJa > 0 && gemeinsamNein > 0)
            {
                abklaerungNoetig = true;
                begruendung.Add("Konkubinat mit gemeinsamen UND nicht-gemeinsamen Kindern im Haushalt — kein automatischer Vorschlag, mit der QST-Behörde abklären");
                warnings.Add("Gemischter Konkubinatsfall (gemeinsame und nicht-gemeinsame Kinder im Haushalt): Tarif mit der QST-Behörde abklären.");
            }
            else if (gemeinsamJa > 0)
            {
                // AG/ESTV-Praxis (Walter 25.08.2026): Partner nicht erwerbstätig
                // = kein Erwerbseinkommen → MA ist zwangsläufig Hauptunterhalt
                // → wie «MA verdient mehr» behandeln (auch ohne Antwort).
                var maMehr = konkubinat.MaHatHoeheresEinkommen
                    ?? (konkubinat.PartnerErwerbstaetig == false ? true : (bool?)null);
                if (maMehr == true)
                {
                    begruendung.Add(konkubinat.MaHatHoeheresEinkommen == null
                        ? "Konkubinat mit gemeinsamem Kind — Partner nicht erwerbstätig → MA ist Hauptunterhaltsträger → H"
                        : "Konkubinat mit gemeinsamem Kind — MA hat das höhere Bruttoeinkommen → H (nie beide H1)");
                }
                else if (maMehr == false)
                {
                    tarif = "A";
                    begruendung.Add("Konkubinat mit gemeinsamem Kind — der Partner verdient mehr → A0 (H1 gehört zum Partner)");
                }
                else
                {
                    tarif = "A";
                    begruendung.Add("Konkubinat mit gemeinsamem Kind — Einkommensfrage offen → konservativ A0");
                    warnings.Add("Einkommensfrage beim Konkubinatspartner offen («Hat der/die MA das höhere Bruttoeinkommen?») — bis dahin konservativ A0.");
                }
            }
            else if (gemeinsamOffen > 0)
            {
                tarif = "A";
                begruendung.Add("Konkubinat — Frage «gemeinsames Kind?» bei Haushalts-Kindern offen → konservativ A0");
                warnings.Add("Konkubinatspartner erfasst: beim Kind/bei den Kindern die Frage «gemeinsames Kind mit dem Konkubinatspartner?» beantworten.");
            }
            else
            {
                begruendung.Add("Konkubinat — Haushalts-Kind(er) NICHT vom Partner → H bleibt (alleinerziehend)");
            }
        }

        // 3) Kirchensteuer — Begründung IMMER nennen (Walter 12.08.2026):
        // auch bei «keine Kirchensteuer» soll sichtbar sein, WELCHE Konfession
        // der Ableitung zugrunde liegt (falsch gepflegte Stammdaten fallen
        // damit sofort auf, statt still A0N vorzuschlagen).
        var kirchensteuer = IstKirchensteuerPflichtig(religion);
        begruendung.Add(kirchensteuer
            ? $"Konfession '{religion}' -> kirchensteuerpflichtig (Y)"
            : $"Konfession '{(string.IsNullOrWhiteSpace(religion) ? "nicht erfasst" : religion)}' -> keine Kirchensteuer (N)");

        // 4) Kinderziffer je Tarif (Walter-Vorgabe 20.08.2026, KS 45):
        //    • H  → NUR Kinder im selben Haushalt (die anderen zählen nicht)
        //    • A  → IMMER 0: A1–9 gibt es nur mit Bewilligung der Steuer-
        //           behörde (Härtefall) — der Vorschlag darf das nie
        //           automatisch setzen (Alimente laufen über die NOV).
        //    • B/C → alle QST-berechtigten Kinder (Unterhalt zur Hauptsache,
        //           Haushalt nicht zwingend).
        var zifferBasis = tarif switch
        {
            "H" => kinderImSelbenHaushalt,
            "A" => 0,
            _   => berechneteKinderTotal
        };
        if (tarif == "A" && berechneteKinderTotal > 0)
            warnings.Add($"{berechneteKinderTotal} Kind(er) erfasst, aber Tarif A → Kinderziffer 0. "
                + "A1–9 nur mit Bewilligung der Steuerbehörde («Speziell bewilligt» + manuell setzen).");
        if (tarif == "H" && kinderImSelbenHaushalt < berechneteKinderTotal)
            begruendung.Add($"Tarif H zählt nur die {kinderImSelbenHaushalt} Kind(er) im selben Haushalt");

        // Tariftabelle prüfen + Fallbacks
        var (effektiveKinder, effektiveKirche, gefunden) = FindeTarifInTabelle(
            tarifTabelle, tarif, zifferBasis, kirchensteuer, warnings);

        var qstCode = $"{tarif}{effektiveKinder}{(effektiveKirche ? "Y" : "N")}";
        var bezeichnung = TarifBezeichnungen.GetValueOrDefault(tarif);

        if (string.IsNullOrWhiteSpace(steuerkanton))
            warnings.Add("Wohnkanton ist nicht gepflegt — Tariftabelle konnte nicht geprüft werden.");
        else if (!gefunden && tarifTabelle.Count == 0)
            warnings.Add($"Keine Tarifdaten für Kanton {steuerkanton} im Jahr {Math.Max(stichtag.Year, 2026)} geladen.");

        return new QstTarifVorschlagResult(
            Steuerkanton:           steuerkanton,
            TarifCode:              tarif,
            TarifBezeichnung:       bezeichnung,
            AnzahlKinder:           effektiveKinder,
            BerechneteKinder:       berechneteKinderTotal,
            KinderImSelbenHaushalt: kinderImSelbenHaushalt,
            Kirchensteuer:          effektiveKirche,
            QstCode:                qstCode,
            InTariftabelleGefunden: gefunden,
            Begruendung:            string.Join(" · ", begruendung),
            Warnings:               warnings,
            Stichtag:               stichtag,
            AbklaerungNoetig:       abklaerungNoetig);
    }

    /// <summary>
    /// Ist das Kind am Stichtag QST-abzugsberechtigt?
    /// 1) Wenn QstDeductibleFrom oder QstDeductibleUntil gesetzt → der
    ///    explizite Zeitraum gilt.
    /// 2) Sonst Fallback: Geburtsdatum bis 18. Geburtstag.
    /// Ohne jegliche Datums-Info → nicht zählen.
    /// </summary>
    public static bool IstQstBerechtigt(QstKindInput k, DateOnly stichtag)
    {
        // 1) Explizit gepflegt
        if (k.QstDeductibleFrom.HasValue || k.QstDeductibleUntil.HasValue)
        {
            if (k.QstDeductibleFrom.HasValue  && k.QstDeductibleFrom.Value  > stichtag) return false;
            // Walter-Vorgabe 20.08.2026: das «bis»-Datum wird beim Erfassen
            // automatisch auf den 18. Geburtstag vorbefüllt. Steht das Kind in
            // ERSTausbildung (Flag oder aktive Ausbildungszulage), verlängert
            // das über ein abgelaufenes «bis» hinaus — sonst müsste HR das
            // Datum von Hand löschen.
            if (k.QstDeductibleUntil.HasValue && k.QstDeductibleUntil.Value < stichtag)
                return k.InErstausbildung;
            return true;
        }
        // 2) Geburtsdatum-Fallback
        if (!k.DateOfBirth.HasValue) return false;
        var dob   = k.DateOfBirth.Value;
        if (dob > stichtag) return false;                    // noch nicht geboren
        var dob18 = dob.AddYears(18);
        if (dob18 >= stichtag) return true;                  // unter 18 am Stichtag
        // Walter-Vorgabe 20.08.2026 (KS 45): ab dem 18. Geburtstag zählt das
        // Kind nur noch, wenn es in ERSTausbildung steht (Lehrvertrag/
        // Immatrikulation als Beleg hinterlegen) — sonst endet die
        // Kinderziffer automatisch mit 18.
        return k.InErstausbildung;
    }

    private static string WaehleTarif(string? zivilstand, int kinderImHaushalt, List<string> begruendung)
    {
        var z = (zivilstand ?? "").Trim().ToLowerInvariant();
        var verheiratet = z.Contains("verheiratet")
            || (z.Contains("partnerschaft") && !z.Contains("aufgeloest") && !z.Contains("aufgelöste"));
        var alleinerziehend_basis =
            z.Contains("ledig") || z.Contains("geschieden") || z.Contains("verwitwet") || z.Contains("getrennt");

        if (verheiratet)
        {
            begruendung.Add($"Zivilstand '{zivilstand}' -> C (Doppelverdiener als Default; bei Alleinverdiener auf B wechseln)");
            return "C";
        }
        if (alleinerziehend_basis && kinderImHaushalt > 0)
        {
            begruendung.Add($"Zivilstand '{zivilstand}' + Kind im selben Haushalt -> H (Alleinerziehend)");
            return "H";
        }
        if (alleinerziehend_basis)
        {
            begruendung.Add($"Zivilstand '{zivilstand}' ohne Kind im Haushalt -> A");
            return "A";
        }
        begruendung.Add($"Zivilstand '{zivilstand}' nicht erkannt -> A als Default");
        return "A";
    }

    /// <summary>
    /// Konfession-Mapping aus der MA-Maske (Walter-Vorgabe):
    /// evangelisch-reformiert / römisch-katholisch / christ-katholisch → ja
    /// (= QST-Code mit Y, z.B. A0Y). Alles andere (keine, andere, jüdisch, …) → nein.
    ///
    /// Robust gegen Anzeige-Texte und Trennzeichen («Christ-katholisch»,
    /// «christ katholisch», Umlaute) — sonst landet ein korrekter Stammdaten-
    /// Eintrag fälschlich bei A0N.
    /// </summary>
    public static bool IstKirchensteuerPflichtig(string? religion)
    {
        if (string.IsNullOrWhiteSpace(religion)) return false;
        // Normalisieren: Kleinbuchstaben, Umlaute, alles Nicht-Buchstaben weg.
        var r = religion.Trim().ToLowerInvariant()
            .Replace('ä', 'a').Replace('ö', 'o').Replace('ü', 'u')
            .Replace('é', 'e');
        var compact = new string(r.Where(char.IsLetterOrDigit).ToArray());

        // Explizit keine Kirchensteuer
        if (compact is "keine" or "kein" or "none" or "konfessionslos"
            or "andere" or "other" or "juedisch" or "judisch" or "muslimisch"
            or "islamisch")
            return false;

        // Die drei kirchensteuerpflichtigen Konfessionen (Code + Freitext)
        if (compact is "evangelischreformiert" or "evangreformiert"
            or "reformiert" or "evangelisch")
            return true;
        if (compact is "roemischkatholisch" or "romischkatholisch"
            or "roemkatholisch" or "romkatholisch")
            return true;
        if (compact is "christkatholisch" or "christkath")
            return true;

        // Fallback: enthält «katholisch» oder «evangelisch»/«reformiert»
        // (deckt z.B. «Christ-katholisch» / Tippvarianten ab)
        if (compact.Contains("katholisch") || compact.Contains("evangelisch")
            || compact.Contains("reformiert"))
            return true;

        return false;
    }

    /// <summary>
    /// Sucht den (tarif, kinder, kirchensteuer)-Tripel in der Tariftabelle.
    /// Fallbacks (in dieser Reihenfolge):
    ///   1) exakt
    ///   2) selbe Tarif+Kinder, andere Kirchensteuer-Variante
    ///   3) selbe Tarif+Kirchensteuer, höchste Kinderstufe &lt;= gewünscht
    ///   4) selbe Tarif, beliebige Kombi mit Kirche=nein und Kinder &lt;= gewünscht
    /// Wenn weiterhin nichts gefunden → (gewünschte Kinder, gewünschte Kirche, false).
    /// </summary>
    private static (int Kinder, bool Kirche, bool Gefunden) FindeTarifInTabelle(
        IReadOnlyList<QstTarifInfo> tabelle,
        string tarif,
        int kinder,
        bool kirche,
        List<string> warnings)
    {
        if (tabelle.Count == 0) return (kinder, kirche, false);

        // 1) exakt
        if (tabelle.Any(t => t.Tarif == tarif && t.Kinder == kinder && t.Kirchensteuer == kirche))
            return (kinder, kirche, true);

        // 2) Kirchensteuer-Variante umdrehen — NUR in Richtung Y→N (Walter-
        //    Vorgabe 13.07.2026): wer kirchensteuerpflichtig ist, dessen
        //    Kanton kennt evtl. keine Y-Variante (einheitliche Tabelle) →
        //    N ist akzeptabel. Umgekehrt NIE: einem Konfessionslosen darf
        //    der Vorschlag keine Kirchensteuer (…Y) unterschieben.
        if (kirche)
        {
            var altKirche = tabelle.FirstOrDefault(t => t.Tarif == tarif && t.Kinder == kinder && !t.Kirchensteuer);
            if (altKirche != null)
            {
                warnings.Add($"Tarif {tarif}{kinder}Y nicht in Tariftabelle — Variante ohne Kirchensteuer (N) verwendet.");
                return (kinder, false, true);
            }
        }

        // 3) selbe Kirche, höchste Kinderzahl ≤ gewünscht
        var maxKinder = tabelle
            .Where(t => t.Tarif == tarif && t.Kirchensteuer == kirche && t.Kinder <= kinder)
            .OrderByDescending(t => t.Kinder)
            .FirstOrDefault();
        if (maxKinder != null)
        {
            if (maxKinder.Kinder != kinder)
                warnings.Add($"Tarif {tarif} mit {kinder} Kind(ern) nicht in Tariftabelle — höchste verfügbare Kinderstufe {maxKinder.Kinder} verwendet.");
            return (maxKinder.Kinder, kirche, true);
        }

        // 4) selbe Tarif, beliebige Kombi mit Kinder ≤ gewünscht
        var any = tabelle
            .Where(t => t.Tarif == tarif && t.Kinder <= kinder)
            .OrderByDescending(t => t.Kinder).ThenByDescending(t => t.Kirchensteuer == kirche)
            .FirstOrDefault();
        if (any != null)
        {
            warnings.Add($"Keine exakte Kombination — Tarif {any.Tarif}{any.Kinder}{(any.Kirchensteuer ? "Y" : "N")} als nächstbeste verwendet.");
            return (any.Kinder, any.Kirchensteuer, true);
        }

        warnings.Add($"Tarif {tarif} überhaupt nicht in der Tariftabelle vorhanden.");
        return (kinder, kirche, false);
    }
}
