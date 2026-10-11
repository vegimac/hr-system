namespace HrSystem.Models;

/// <summary>
/// Ausbildungs-Abschnitt eines Kindes (Walter 11.10.2026,
/// docs/kind-qst-verlauf-konzept.md, Ebene 1). Sachverhalt — nicht lohnrelevant.
/// </summary>
public class FamilyMemberAusbildung
{
    public int Id { get; set; }
    public int FamilyMemberId { get; set; }

    /// <summary>NULL = Beginn unbekannt (nur Altbestand).</summary>
    public DateOnly? Von { get; set; }
    /// <summary>NULL = läuft.</summary>
    public DateOnly? Bis { get; set; }

    /// <summary>LEHRE / SCHULE / STUDIUM / BRUECKE / ANDERE.</summary>
    public string Art { get; set; } = "ANDERE";
    /// <summary>ABGESCHLOSSEN / ABGEBROCHEN / NULL.</summary>
    public string? EndeGrund { get; set; }
    /// <summary>NULL = ungeklärt (z.B. Studium nach abgeschlossener Lehre).</summary>
    public bool? Erstausbildung { get; set; } = true;
    public bool Geprueft { get; set; } = true;
    public int? DokumentId { get; set; }
    /// <summary>HAND / ALTBESTAND / ZULAGE.</summary>
    public string Quelle { get; set; } = "HAND";
    public DateOnly? ErfahrenAm { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime ErfasstAm { get; set; } = DateTime.Now;
    public string? ErfasstVon { get; set; }
}

/// <summary>Verhältnis-Version eines Kindes ab Datum (Ebene 2). NULL = ungeklärt.</summary>
public class FamilyMemberVerhaeltnis
{
    public int Id { get; set; }
    public int FamilyMemberId { get; set; }
    public DateOnly GueltigAb { get; set; }
    public bool? ImHaushalt { get; set; }
    public bool? UnterhaltHauptsache { get; set; }
    public string Quelle { get; set; } = "HAND";
    public DateOnly? ErfahrenAm { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime ErfasstAm { get; set; } = DateTime.Now;
    public string? ErfasstVon { get; set; }
}

/// <summary>QST-Entscheid zum Kind (Ebene 3): ANERKANNT / NICHT_ANERKANNT / IN_PRUEFUNG.</summary>
public class FamilyMemberQstEntscheid
{
    public int Id { get; set; }
    public int FamilyMemberId { get; set; }
    public string Status { get; set; } = "ANERKANNT";
    public DateOnly? Von { get; set; }
    public DateOnly? Bis { get; set; }
    public string? Grund { get; set; }
    public int? DokumentId { get; set; }
    /// <summary>HAND / ALTBESTAND / SWISSDEC.</summary>
    public string Quelle { get; set; } = "HAND";
    public DateOnly? ErfahrenAm { get; set; }
    public DateTime ErfasstAm { get; set; } = DateTime.Now;
    public string? ErfasstVon { get; set; }
}
