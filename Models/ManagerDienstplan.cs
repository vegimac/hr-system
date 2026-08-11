namespace HrSystem.Models;

/// <summary>
/// Manager-Dienstplan (Walter-Vorgabe 08.08.2026, ersetzt die Excel
/// «Manager DP»): pro FIX-M-MA und Tag ein Schicht-Kürzel aus dem
/// <see cref="DienstplanCode"/>-Katalog. Absenzen (Ferien/Krank/…) werden
/// NICHT hier gespeichert — sie kommen als Live-Overlay aus den Absenzen
/// und sperren die Zelle.
/// </summary>
public class ManagerDienstplanEntry
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly Datum { get; set; }
    /// <summary>Kürzel aus dienstplan_code (F/M/S/-/SK/SKM …).</summary>
    public string Code { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    /// <summary>Anzeigename des letzten Bearbeiters (aus JWT, nie aus dem Body).</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>Kürzel-Katalog (Walter kann selbst ergänzen).</summary>
public class DienstplanCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Bezeichnung { get; set; } = "";
    /// <summary>Hex-Hintergrundfarbe der Zelle, z.B. «#fef9c3» (frei = gelb).</summary>
    public string? Farbe { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Feiertag für den Manager-Dienstplan (Walter-Vorgabe 09.08.2026).
/// Geltungsbereich dreistufig: NATIONAL (alle Filialen), KANTON (Filialen
/// mit passendem <see cref="CompanyProfile.KantonCode"/>), FILIALE (genau
/// eine Filiale — Gemeinde-Feiertage). Reiner Planungs-Marker, KEINE
/// Lohn-Wirkung (Feiertags-Saldo-Logik läuft separat in der Payroll).
/// </summary>
public class DienstplanFeiertag
{
    public int Id { get; set; }
    public DateOnly Datum { get; set; }
    public string Bezeichnung { get; set; } = "";
    /// <summary>NATIONAL | KANTON | FILIALE</summary>
    public string Scope { get; set; } = "NATIONAL";
    /// <summary>Bei Scope=KANTON: 2-Zeichen-Code (LU, AG, BE, …).</summary>
    public string? KantonCode { get; set; }
    /// <summary>Bei Scope=FILIALE: die betroffene Filiale.</summary>
    public int? CompanyProfileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Vorstellungsgespräch-Zeitfenster eines GF/Managers (Walter-Vorgabe
/// 09.08.2026, Stufe 1): der GF teilt NUR mit, wann er an einem seiner im
/// Manager-Dienstplan als ARBEIT (F/M/S) geplanten Tage Zeit für
/// Vorstellungsgespräche hat. HR sieht die Fenster im HR-Hub (read-only).
/// Die eigentliche Terminbuchung durch HR ist Stufe 2 (noch nicht gebaut).
/// </summary>
public class InterviewFenster
{
    public int Id { get; set; }
    /// <summary>Der Manager/GF (employee), dem das Fenster gehört.</summary>
    public int EmployeeId { get; set; }
    public DateOnly Datum { get; set; }
    public TimeOnly VonZeit { get; set; }
    public TimeOnly BisZeit { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>Anzeigename des Erfassers (aus JWT, nie aus dem Body).</summary>
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Gebuchtes Vorstellungsgespräch (Walter-Vorgabe 09.08.2026, Stufe 2):
/// HR bucht in einem <see cref="InterviewFenster"/> einen 30-Minuten-Slot
/// (Raster 45 Min = 30 Gespräch + 15 Puffer, verankert am Fensterstart).
/// ABGESAGT gibt den Slot wieder frei (Historie bleibt).
/// </summary>
public class InterviewTermin
{
    public int Id { get; set; }
    public int FensterId { get; set; }
    /// <summary>Slot-Start; Ende = Start + 30 Minuten.</summary>
    public TimeOnly VonZeit { get; set; }
    public string Kandidat { get; set; } = "";
    public string? Telefon { get; set; }
    public string? Bemerkung { get; set; }
    /// <summary>GEPLANT | ABGESAGT</summary>
    public string Status { get; set; } = "GEPLANT";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>Anzeigename des Buchers (aus JWT, nie aus dem Body).</summary>
    public string? CreatedBy { get; set; }
}

/// <summary>
/// HR-Büro-Kalender-Termin für Vorstellungsgespräche (Walter-Vorgabe
/// 09.08.2026, ersetzt den GF-Zeitfenster-Prozess): HR pflegt Termine mit
/// Platz-Kapazität, max. 2 Monate im Voraus, und bucht Kandidaten selbst.
/// </summary>
public class HrInterviewTermin
{
    public int Id { get; set; }
    public DateOnly Datum { get; set; }
    public TimeOnly VonZeit { get; set; }
    public TimeOnly? BisZeit { get; set; }
    /// <summary>Anzahl verfügbare Plätze (Kandidaten) an diesem Termin.</summary>
    public int Plaetze { get; set; } = 1;
    public string? Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Kandidat (Walter-Vorgabe 10.08.2026, Etappe 1): der GF reicht nach dem
/// Vorstellungsgespräch einen Einstellungs-Kandidaten an HR ein — bewusst
/// KEIN Employee (der MA entsteht erst nach HR-Annahme in easy@work).
/// Status: NEU → ANGENOMMEN / ABGELEHNT → ERLEDIGT (Etappe 2: Checkliste).
/// </summary>
public class Kandidat
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }
    public string Vorname { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Telefon { get; set; }
    public string? Email { get; set; }
    public DateOnly? FruehesterEintritt { get; set; }
    /// <summary>L-GAV-Ausbildungsstufe (Ia, Ib, II, IIIa, IIIb, IV).</summary>
    public string? LgavAusbildung { get; set; }
    /// <summary>Unverbindlicher Wunschtermin fürs Onboarding (hr_interview_termin).</summary>
    public int? WunschTerminId { get; set; }
    public string? Bemerkung { get; set; }
    /// <summary>NEU | ANGENOMMEN | ABGELEHNT | ERLEDIGT</summary>
    public string Status { get; set; } = "NEU";
    public string? Ablehnungsgrund { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    /// <summary>Absage an den Kandidaten verschickt (Etappe 2).</summary>
    public DateTime? AbsageGesendetAm { get; set; }
    /// <summary>SMS | EMAIL</summary>
    public string? AbsageKanal { get; set; }
    /// <summary>Zeitpunkt der MA-Verknüpfung (Status ERLEDIGT) — Löschung erst 30 Tage später.</summary>
    public DateTime? ErledigtAm { get; set; }
    /// <summary>Der verknüpfte MA (Referenz, falls Rückfragen kommen).</summary>
    public int? VerknuepftEmployeeId { get; set; }
    /// <summary>Freie HR-Notiz (z.B. «hat sich nach Absage nochmals gemeldet»).</summary>
    public string? Notiz { get; set; }
    /// <summary>Willkommenstag-Einladung (Walter 11.08.2026): SHA-256-Hash des öffentlichen Links /willkommen/{token}.</summary>
    public string? WillkommenTokenHash { get; set; }
    public DateTime? WillkommenGesendetAm { get; set; }
}

/// <summary>
/// Onboarding-Wunschtermin des MA (Walter 10.08.2026): beim Verknüpfen des
/// Kandidaten mit dem importierten MA wird der vom GF erfasste Wunschtermin
/// hierher übernommen — sichtbar beim Einladen im Onboarding-Kalender.
/// Wird beim tatsächlichen Buchen (Einladung mit Termin) wieder gelöscht.
/// </summary>
public class OnboardingWunsch
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int TerminId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Anhang zum Kandidaten (CV, Bewerbungsbogen …) — Datei im Storage unter kandidaten/{kandidatId}/.</summary>
public class KandidatDokument
{
    public int Id { get; set; }
    public int KandidatId { get; set; }
    public string OriginalFilename { get; set; } = "";
    public string StorageFilename { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
}

/// <summary>Gebuchter Platz eines Kandidaten; ABGESAGT gibt den Platz frei.</summary>
public class HrInterviewBuchung
{
    public int Id { get; set; }
    public int TerminId { get; set; }
    public string Kandidat { get; set; } = "";
    public string? Telefon { get; set; }
    public string? Bemerkung { get; set; }
    /// <summary>GEPLANT | ABGESAGT</summary>
    public string Status { get; set; } = "GEPLANT";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
    /// <summary>MA hinter der Buchung (Walter 10.08.2026, gesetzt bei Onboarding-Einladung) — für Termin-Antwort + Umbuchen.</summary>
    public int? EmployeeId { get; set; }
    /// <summary>Kandidat hinter der Buchung (Walter 11.08.2026, Willkommenstag-SMS vor easy@work-Erfassung).</summary>
    public int? KandidatId { get; set; }
    /// <summary>Onboarding-Abschluss (Walter 11.08.2026): HR bestätigt nach dem Willkommenstag — der MA läuft danach regulär.</summary>
    public DateTime? OnboardingAbgeschlossenAm { get; set; }
    public string? OnboardingAbgeschlossenVon { get; set; }
    /// <summary>Antwort des MA über den Vertrags-Link: ANGENOMMEN | ABGELEHNT (NULL = noch unbestätigt).</summary>
    public string? MaAntwort { get; set; }
    public DateTime? MaAntwortAm { get; set; }
}

/// <summary>
/// Schulferien pro Filiale (Walter-Vorgabe 09.08.2026) — Anzeige-Band in der
/// Filial-Zeile des Manager-Dienstplans (wie «Sportferien» in der alten Excel).
/// </summary>
public class BranchSchulferien
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }
    public string Bezeichnung { get; set; } = "";
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
