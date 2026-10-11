using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

public class EmployeeFamilyMember
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    public string MemberType { get; set; } = "Kind"; // Kind, Ehepartner, Konkubinatspartner, AndererElternteil (Kindsmutter/-vater, nicht Partner), Mutter, Vater, Sonstige
    public string? Gender { get; set; }
    public string? FamilyStatus { get; set; }

    public string? LastName { get; set; }
    public string? MaidenName { get; set; }
    public string? FirstName { get; set; }

    public string? SocialSecurityNumber { get; set; }

    /// <summary>
    /// Telefonnummer des Familienangehörigen (v.a. Ehepartner) —
    /// gleiches Format wie beim MA (+41 79 333 44 55).
    /// </summary>
    public string? Phone { get; set; }

    public bool LivesInSwitzerland { get; set; } = false;

    public DateTime? DateOfBirth { get; set; }
    public DateTime? DateOfDeath { get; set; }

    // Legacy-Felder. Bleiben in der DB stehen, werden im Frontend aber nicht
    // mehr angezeigt — Zulagen sind nun zeitlich versioniert in family_member_allowance.
    public DateTime? Allowance1Until { get; set; }
    public DateTime? Allowance2Until { get; set; }
    public DateTime? Allowance3Until { get; set; }

    /// <summary>Versionierte Familienzulagen-Einträge (Von/Bis/Monatsbetrag).</summary>
    public List<FamilyMemberAllowance> Allowances { get; set; } = new();

    public int? AlternativeAddressId { get; set; }

    /// <summary>
    /// Walter-Vorgabe 25.08.2026: lebt der/die Angehörige im GLEICHEN Haushalt
    /// wie der MA? Ersetzt die frühere Ableitung «AlternativeAddressId == null
    /// = im Haushalt» — die kannte den Zustand «nicht im Haushalt, ohne
    /// erfasste Adresse» (ausgezogenes Kind) nicht. true = Hauptadresse MA;
    /// false = eigener Haushalt (mit oder ohne AlternativeAddressId).
    /// Für die QST-Haushalt-Logik (H-Tarif) ist NUR dieses Flag massgebend.
    /// </summary>
    public bool LebtImHaushalt { get; set; } = true;

    public DateTime? QstDeductibleFrom { get; set; }
    public DateTime? QstDeductibleUntil { get; set; }

    /// <summary>
    /// Ab wann wir den QST-Abzug des Kindes kannten (Walter 15.09.2026).
    /// Wirkung bleibt <see cref="QstDeductibleFrom"/> / Geburt; NULL = gleich
    /// wie die Wirkung (Altbestand).
    /// </summary>
    public DateOnly? ErfahrenAm { get; set; }

    /// <summary>
    /// Aufenthaltsbewilligung des Familienangehörigen (B/C/L/G/F/N) —
    /// referenziert PermitType wie beim MA selbst.
    /// </summary>
    public int? PermitTypeId { get; set; }

    /// <summary>Ablaufdatum der Bewilligung (Gültig bis).</summary>
    public DateTime? PermitExpiryDate { get; set; }

    /// <summary>
    /// ZEMIS-Nummer (Zentrales Migrationsinformationssystem) des Familien-
    /// angehörigen — bleibt während des ganzen Aufenthalts gleich, auch wenn
    /// die Bewilligung wechselt.
    /// </summary>
    public string? ZemisNumber { get; set; }

    /// <summary>Nationalität (FK auf Nationality-Tabelle, wie beim MA).</summary>
    public int? NationalityId { get; set; }

    // ── QST-Relevanz (Walter-Vorgabe 20.08.2026) ────────────────────────────
    /// <summary>
    /// Ehepartner: erwerbstätig? NULL = Frage noch nicht beantwortet (blockt
    /// bei QST-pflichtigen verheirateten MA den Lohnlauf), true/false = erfasst.
    /// Entscheidet Tarif B (Alleinverdiener) vs. C (Doppelverdiener) und wird
    /// 1:1 in die kantonale QST-Anmeldung übernommen.
    /// </summary>
    public bool? Erwerbstaetig { get; set; }

    /// <summary>Ehepartner: Arbeitgeber-Name (Pflicht wenn erwerbstätig).</summary>
    public string? ArbeitgeberName { get; set; }

    /// <summary>Ehepartner: Arbeitgeber Strasse/Nr. (Walter 20.08.2026 — analog QST-Anmeldeformular).</summary>
    public string? ArbeitgeberStrasse { get; set; }

    /// <summary>Ehepartner: Arbeitgeber PLZ (mit Ort/Kanton-Auto-Lookup im UI).</summary>
    public string? ArbeitgeberPlz { get; set; }

    /// <summary>Ehepartner: Arbeitgeber Ort.</summary>
    public string? ArbeitgeberOrt { get; set; }

    /// <summary>Ehepartner: Arbeitgeber Kanton (Kürzel, z.B. «AG»).</summary>
    public string? ArbeitgeberKanton { get; set; }

    /// <summary>Ehepartner: Stellenantritt beim Arbeitgeber.</summary>
    public DateTime? Stellenantritt { get; set; }

    // ── Historie-Felder nur für Create/Update-Payload (Walter 08.10.2026) ──
    // Werden beim Speichern in family_member_*_history geschrieben; nicht
    // eigene Spalten auf employee_family_member.
    [NotMapped] public DateOnly? PermitSeit { get; set; }
    [NotMapped] public DateOnly? PermitErfahrenAm { get; set; }
    [NotMapped] public DateOnly? ErwerbSeit { get; set; }
    [NotMapped] public DateOnly? ErwerbErfahrenAm { get; set; }

    public List<FamilyMemberPermitHistory> PermitHistories { get; set; } = new();
    public List<FamilyMemberErwerbHistory> ErwerbHistories { get; set; } = new();

    /// <summary>
    /// Kind: steht in beruflicher/schulischer ERSTausbildung. Relevant ab dem
    /// 18. Geburtstag — ohne dieses Flag (oder explizites QstDeductibleUntil)
    /// endet die QST-Kinderziffer automatisch mit 18 (KS 45 Ziff. 3.2.2).
    /// </summary>
    public bool InErstausbildung { get; set; } = false;

    /// <summary>
    /// Nur noch Abbild: «heute kein Unterhalt zur Hauptsache» laut Verlauf am
    /// Kind (family_member_verhaeltnis), nachgeführt von
    /// KindQstVerlaufLader.SnapshotNachfuehrenAsync. Seit 11.10.2026 kein
    /// Häkchen mehr in der Maske und keine Rechenquelle — der QST-Verlauf
    /// liest die datierten Versionen. Formulare (QST-Anmeldung/-Info) lesen
    /// das Abbild für «heute».
    /// </summary>
    public bool KeineUnterhaltspflicht { get; set; } = false;

    // ── Konkubinats-Logik (Walter-Vorgabe 25.08.2026,
    //    docs/konkubinat-qst-konzept.md) ────────────────────────────────────
    /// <summary>
    /// Ehepartner, Konkubinatspartner, anderer Elternteil: Hat der/die MA das
    /// HÖHERE Bruttoeinkommen als diese Person? Konkubinat: H1 (Ja) vs. A0
    /// (Nein). Familienzulagen: verdient die andere Person mehr, hat sie in der
    /// Regel den Erstanspruch (FamZG Art. 7). NULL = Frage offen.
    /// </summary>
    public bool? MaHatHoeheresEinkommen { get; set; }

    // ── Familienzulagen-Anmeldung (Walter 11.10.2026, Stand 61,
    //    docs/famz-anmeldung-konzept.md) ────────────────────────────────────
    /// <summary>Kind: Beziehung zum MA wie im FAK-Formular — L leiblich, A Adoptiv, S Stief, P Pflege, G Geschwister, E Enkel.</summary>
    public string? BeziehungZumMa { get; set; }

    /// <summary>Kind: erwerbsunfähig (Zulage bis 20 möglich).</summary>
    public bool Erwerbsunfaehig { get; set; }

    /// <summary>Kind: elterliche Sorge — ELTERN | MUTTER | VATER (NULL = offen).</summary>
    public string? ElterlicheSorge { get; set; }

    /// <summary>
    /// Kind: der andere Elternteil (Familienmitglied desselben MA vom Typ
    /// Ehepartner, Konkubinatspartner oder AndererElternteil). Ist es der
    /// Konkubinatspartner, gilt das Kind als gemeinsames Kind (QST H1/A0).
    /// </summary>
    public int? AndererElternteilId { get; set; }

    /// <summary>Partner / anderer Elternteil: Arbeitspensum in Prozent.</summary>
    public decimal? Arbeitspensum { get; set; }

    /// <summary>Partner / anderer Elternteil: Telefon des Arbeitgebers.</summary>
    public string? ArbeitgeberTelefon { get; set; }

    /// <summary>Partner / anderer Elternteil: Erwerbseinkommen über CHF 7'560 im Jahr? NULL = offen.</summary>
    public bool? EinkommenUeberMinimum { get; set; }

    /// <summary>Partner / anderer Elternteil: bei einer Ausgleichskasse als SE / NE angemeldet — NEIN | SE | NE (NULL = offen).</summary>
    public string? SeNeAngemeldet { get; set; }

    /// <summary>Zuständige Ausgleichskasse, wenn SE/NE angemeldet.</summary>
    public string? SeNeAusgleichskasse { get; set; }

    /// <summary>Partner / anderer Elternteil: bezieht eine Leistung (IV, ALV, UVG, KTG, Mutterschaft)? NULL = offen.</summary>
    public bool? BeziehtLeistung { get; set; }

    /// <summary>Welche Leistung, von wem.</summary>
    public string? LeistungVon { get; set; }

    /// <summary>Neues Kind: Startwerte für die erste Verlaufs-Version (nicht gespeichert).</summary>
    [NotMapped] public bool? StartUnterhalt { get; set; }

    /// <summary>
    /// Typ Kind: Gemeinsames Kind mit dem Konkubinatspartner? Nur relevant,
    /// wenn beim MA ein Konkubinatspartner erfasst ist. NULL = Frage offen.
    /// </summary>
    public bool? GemeinsamesKindMitPartner { get; set; }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: explizite Verknüpfung zum Beleg-Dokument
    /// dieses Familienmitglieds (Pass / ID-Karte für Schweizer Spouse, oder
    /// Bewilligungs-Dokument für C-Ausweis-Spouse). Wird von QstPflichtCheck-
    /// Service genutzt, um die Befreiung über Ehepartner zu validieren.
    /// NULL = nicht verknüpft → roter Warnbanner.
    /// </summary>
    public int? DokumentId { get; set; }

    /// <summary>Geburtsurkunde dieses Familienmitglieds, v.a. Kinder (Walter 23.09.2026).</summary>
    public int? GeburtsurkundeDokumentId { get; set; }

    // Walter-Regel ACHTUNG TIME (vereinheitlicht 04.08.2026): Lokalzeit,
    // Spalten timestamp without time zone — nie UtcNow.
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // Navigation
    public Employee? Employee { get; set; }
    public PermitType? PermitType { get; set; }
    public Nationality? NationalityRef { get; set; }
}
