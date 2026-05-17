namespace HrSystem.Models;

/// <summary>
/// Kantonale Familienzulagen-Sätze (FAK), versioniert über Gültigkeits-
/// perioden. Jeder Eintrag beschreibt die für einen Kanton gültigen
/// Sätze in einem bestimmten Zeitraum.
///
/// Walter-Anforderung: Familienzulage richtet sich – im Gegensatz zur
/// Quellensteuer – nach dem STANDORT der Filiale, nicht dem Wohnort des
/// MA. Der Lohnberechnungs-Code zieht daher den Tarif zur
/// `CompanyProfile.KantonCode` der Filiale heran, in der der Vertrag
/// läuft.
///
/// Versionierung: Bei Tarifanpassung (z.B. neue Sätze ab 1.1.2027) wird
/// ein neuer Eintrag mit neuem ValidFrom angelegt; alter Eintrag
/// bekommt ValidTo am Vortag. Eindeutigkeit erzwungen über
/// (KantonCode, ValidFrom).
///
/// Sätze in der Tabelle dürfen NULL sein — Walter pflegt sie im
/// Admin-UI ("Familienzulagen-Tarife" in Systemeinstellungen) nach
/// Bedarf. Solange ein Satz NULL ist, kann der Lohnlauf für diesen
/// Kanton keine Familienzulage berechnen — das wird in Phase B als
/// Validation-Warning behandelt.
/// </summary>
public class FamilienzulagenTarif
{
    public int Id { get; set; }

    /// <summary>2-Zeichen-Kantonscode (LU, AG, BE, ZH, …).</summary>
    public string KantonCode { get; set; } = "";

    /// <summary>Gültig ab Datum (inklusive).</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Gültig bis Datum (inklusive). NULL = offen / läuft weiter.</summary>
    public DateOnly? ValidTo { get; set; }

    // ── Kinderzulage (Alter 0 bis AltersGrenzeKinder, normalerweise 16) ──

    /// <summary>Standard-Kinderzulage pro Kind und Monat in CHF.</summary>
    public decimal? KinderzulageSatz1 { get; set; }

    /// <summary>
    /// Erhöhte Kinderzulage. Greift entweder ab dem N-ten Kind
    /// (siehe SchwelleSatz2AnzahlKinder) oder ab einem bestimmten
    /// Alter pro Kind (siehe KinderzulageSatz2AbAlter).
    /// NULL wenn der Kanton keine Differenzierung kennt.
    /// </summary>
    public decimal? KinderzulageSatz2 { get; set; }

    /// <summary>
    /// Wenn gesetzt: KZ Satz2 greift pro Kind ab diesem Alter.
    /// Beispiel LU: 12 → 0–11 Jahre Satz1, 12–15 Jahre Satz2.
    /// NULL = keine Altersstaffel.
    /// </summary>
    public int? KinderzulageSatz2AbAlter { get; set; }

    // ── Ausbildungszulage (Alter > AltersGrenzeKinder bis AltersGrenzeAusbildung) ──

    /// <summary>Standard-Ausbildungszulage pro Kind in Ausbildung und Monat in CHF.</summary>
    public decimal? AusbildungszulageSatz1 { get; set; }

    /// <summary>Erhöhte Ausbildungszulage analog zu KinderzulageSatz2.</summary>
    public decimal? AusbildungszulageSatz2 { get; set; }

    /// <summary>
    /// Wenn gesetzt: AZ Satz2 greift pro Kind ab diesem Alter.
    /// Beispiel ZG: 18 → 16–17 Satz1, 18–24 Satz2.
    /// NULL = keine Altersstaffel.
    /// </summary>
    public int? AusbildungszulageSatz2AbAlter { get; set; }

    // ── Schwellen / Grenzen ──

    /// <summary>
    /// Ab welchem Kind greift Satz2 (NICHT alters-basiert)? Z.B. FR/VD/VS:
    /// 3 = ab 3. Kind höherer Satz für ALLE Kinder ab dem 3.
    /// NULL = keine Differenzierung nach Kindzahl.
    /// </summary>
    public int? SchwelleSatz2AnzahlKinder { get; set; }

    /// <summary>
    /// Mindesterwerbseinkommen pro Jahr in CHF, ab dem ein Anspruch auf
    /// Familienzulagen besteht (FamZG, kantonal teilweise abweichend).
    /// 2025 typisch 7'350 CHF.
    /// </summary>
    public decimal? MindesterwerbseinkommenJahr { get; set; }

    /// <summary>
    /// AHV-pflichtiges Mindesteinkommen pro MONAT. Wenn der MA in einem
    /// Monat unter diesem Wert bleibt, wird die FAK in diesem Monat NICHT
    /// ausgezahlt (siehe GastroSocial-Bescheid LU: 630 CHF/Monat).
    /// NULL = Lohnlauf fällt auf MindesterwerbseinkommenJahr/12 zurück.
    /// </summary>
    public decimal? MindesterwerbseinkommenMonat { get; set; }

    // ── Einmalige Zulagen (Geburt / Adoption) ──

    /// <summary>
    /// Einmalige Geburtszulage in CHF. Z.B. LU 1'075. NULL = Kanton zahlt keine.
    /// Wird ausgezahlt wenn ein FamilyMemberAllowance mit AllowanceType="GZ"
    /// im Geburtsmonat aktiv ist.
    /// </summary>
    public decimal? GeburtszulageBetrag { get; set; }

    /// <summary>
    /// Einmalige Adoptionszulage in CHF. Bei den meisten Kantonen identisch
    /// zur Geburtszulage; in VS aber höher (3'213 vs 2'142). NULL = Kanton
    /// zahlt keine. AllowanceType="AdoptZ".
    /// </summary>
    public decimal? AdoptionszulageBetrag { get; set; }

    /// <summary>Altersgrenze für Kinderzulage (Standard FamZG: 16).</summary>
    public int AltersGrenzeKinder { get; set; } = 16;

    /// <summary>Altersgrenze für Ausbildungszulage (Standard FamZG: 25).</summary>
    public int AltersGrenzeAusbildung { get; set; } = 25;

    // ── Meta ──

    /// <summary>Quelle der Sätze (URL der kantonalen FAK / Verordnung).</summary>
    public string? Quelle { get; set; }

    /// <summary>Optionale Bemerkung (z.B. "Initialwerte, noch nicht verifiziert").</summary>
    public string? Bemerkung { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
