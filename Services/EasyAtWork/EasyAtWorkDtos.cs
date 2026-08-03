using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Datums-Helfer für easy@work-Felder. Die API liefert Datumswerte mal als reines
/// Datum ("yyyy-MM-dd"), mal als UTC-Timestamp ("yyyy-MM-dd HH:mm:ss"). Mitternacht
/// Schweizer Zeit wird als 22:00/23:00 UTC des Vortags gespeichert — die
/// UTC→Europe/Zurich-Konvertierung ergibt das korrekte Kalenderdatum (z.B.
/// "2025-12-31 23:00:00" → 01.01.2026). Walter-Vorgabe 23.06.2026.
/// </summary>
public static class EawDateUtil
{
    private static readonly TimeZoneInfo SwissTz = ResolveSwissTz();
    private static TimeZoneInfo ResolveSwissTz()
    {
        foreach (var id in new[] { "Europe/Zurich", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* nächste ID versuchen */ }
        }
        return TimeZoneInfo.Utc;
    }

    public static DateOnly? ParseSwissDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            return null;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), SwissTz);
        return DateOnly.FromDateTime(local);
    }

    /// <summary>
    /// Intervall-«to» als exklusives Mitternacht lesen: Zürich 00:00 → Vortag
    /// (UI-Bis). End-of-day (21:59:59) bleibt das Kalenderdatum.
    /// ACHTUNG: easy@work speichert «to» inkonsistent (mal inklusiv, mal
    /// exklusiv) — für Nachtarbeit-Ende NICHT blind verwenden; dort gilt
    /// <see cref="IntervalEndMatchesSoll"/>.
    /// </summary>
    public static DateOnly? ParseSwissInclusiveEndDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            return null;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), SwissTz);
        var date = DateOnly.FromDateTime(local);
        if (local.TimeOfDay == TimeSpan.Zero && date > DateOnly.MinValue)
            return date.AddDays(-1);
        return date;
    }

    /// <summary>
    /// Prüft ob das easy@work-Intervall-«to» (roh) zum Soll-Datum passt —
    /// akzeptiert BEIDE UTC-Interpretationen (Kalendertag Zürich ODER
    /// exklusives Mitternacht → Vortag). Walter 26.07.2026: easy speichert
    /// dasselbe UI-Bis mal als 28.5. 00:00, mal als 29.5. 00:00 Zürich.
    /// </summary>
    public static bool IntervalEndMatchesSoll(string? toRaw, DateOnly soll)
    {
        if (string.IsNullOrWhiteSpace(toRaw)) return false;
        var plain = ParseSwissDate(toRaw);
        var exclusive = ParseSwissInclusiveEndDate(toRaw);
        return (plain.HasValue && plain.Value == soll)
            || (exclusive.HasValue && exclusive.Value == soll);
    }

    /// <summary>
    /// Parst einen easy@work-Timestamp (Space-Format "yyyy-MM-dd HH:mm:ss" ODER ISO-T)
    /// in einen Kind=Unspecified-DateTime — für `timestamp without time zone`-Spalten
    /// und als Versions-Marker. System.Text.Json würde am Space-Format scheitern und
    /// die ganze DTO-Deserialisierung werfen; DateTime.TryParse ist tolerant.
    /// </summary>
    public static DateTime? ParseTimestamp(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : (DateTime?)null;
    }
}

// ════════════════════════════════════════════════════════════════════════
// DTOs für die easy@work-API. Aus openapi.yaml (Stand 17.06.2026) abgeleitet,
// auf die für unseren Sync nötigen Felder reduziert. Snake_case der API wird
// per JsonPropertyName auf C#-PascalCase gemappt.
// ════════════════════════════════════════════════════════════════════════

/// <summary>OAuth2-Token-Antwort vom /oauth/token-Endpoint.</summary>
public class EawTokenResponse
{
    [JsonPropertyName("token_type")]   public string TokenType   { get; set; } = "";
    [JsonPropertyName("expires_in")]   public int    ExpiresIn   { get; set; }   // Sekunden
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
}

/// <summary>
/// Customer = Tenant / Filiale bei easy@work. Pro Filiale eine Customer-ID;
/// `Number` ist die Filial-Nummer (entspricht unserem RestaurantCode).
/// </summary>
public class EawCustomer
{
    [JsonPropertyName("id")]         public int     Id        { get; set; }
    [JsonPropertyName("number")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Number    { get; set; }
    [JsonPropertyName("name")]       public string? Name      { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Listen-Antwort mit Pagination — Format: { data: [...], total, current_page, ... }.
/// </summary>
public class EawPaginated<T>
{
    [JsonPropertyName("data")]         public List<T> Data         { get; set; } = new();
    [JsonPropertyName("total")]        public int?    Total        { get; set; }
    [JsonPropertyName("current_page")] public int?    CurrentPage  { get; set; }
    [JsonPropertyName("last_page")]    public int?    LastPage     { get; set; }
    [JsonPropertyName("per_page")]     public int?    PerPage      { get; set; }
}

/// <summary>Einzel-Resource-Antwort ({ "data": {...} }) — z.B. employees/{id}.</summary>
public class EawSingle<T>
{
    [JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary>Mitarbeiter (Auszug — siehe openapi.yaml Schema Employee).</summary>
public class EawEmployee
{
    [JsonPropertyName("id")]           public int      Id          { get; set; }
    [JsonPropertyName("customer_id")]  public int?     CustomerId  { get; set; }
    /// <summary>Login-User-ID — `edited_by_id` aus Stempel-Audits zeigt darauf.</summary>
    [JsonPropertyName("user_id")]      public int?     UserId      { get; set; }
    // easy@work liefert `number` bei Employees als JSON-Zahl (nicht-String),
    // bei Customers als String. Toleranter Converter überspielt das.
    [JsonPropertyName("number")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Number { get; set; }   // = unsere employee_number
    [JsonPropertyName("first_name")]   public string?  FirstName   { get; set; }
    [JsonPropertyName("last_name")]    public string?  LastName    { get; set; }
    /// <summary>easy@work Nickname → Cowork <c>short_name</c> (Kurzname).</summary>
    [JsonPropertyName("nickname")]     public string?  Nickname    { get; set; }
    [JsonPropertyName("gender")]       public string?  Gender      { get; set; }
    [JsonPropertyName("birth_date")]   public DateOnly? BirthDate  { get; set; }
    [JsonPropertyName("address1")]     public string?  Address1    { get; set; }
    [JsonPropertyName("address2")]     public string?  Address2    { get; set; }
    [JsonPropertyName("postal_code")]  public string?  PostalCode  { get; set; }
    [JsonPropertyName("city")]         public string?  City        { get; set; }
    [JsonPropertyName("country")]      public string?  Country     { get; set; }
    [JsonPropertyName("country_key")]  public string?  CountryKey  { get; set; }   // ISO-Code (CH, DE, ...)
    [JsonPropertyName("nationality")]  public string?  Nationality { get; set; }
    [JsonPropertyName("phone")]        public string?  Phone       { get; set; }
    [JsonPropertyName("email")]        public string?  Email       { get; set; }
    [JsonPropertyName("from")]         public DateOnly? From       { get; set; }   // Eintritt
    [JsonPropertyName("to")]           public DateOnly? To         { get; set; }   // Austritt
    [JsonPropertyName("updated_at")]   public DateTime? UpdatedAt  { get; set; }
}

/// <summary>Vertragstyp-Katalog eines Customers (GET …/contract_types).</summary>
public class EawContractType
{
    [JsonPropertyName("id")]   public int     Id   { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>Vertrag pro MA.</summary>
public class EawContract
{
    [JsonPropertyName("id")]           public int      Id           { get; set; }
    [JsonPropertyName("employee_id")]  public int      EmployeeId   { get; set; }
    [JsonPropertyName("title")]        public string?  Title        { get; set; }   // Funktion
    /// <summary>Typ-Name — oft leer; dann über <see cref="TypeId"/> + contract_types auflösen.</summary>
    [JsonPropertyName("type")]         public string?  Type         { get; set; }
    /// <summary>FK auf Customer-ContractType (z.B. 105 = MTP/TPM). Walter 02.08.2026.</summary>
    [JsonPropertyName("type_id")]      public int?     TypeId       { get; set; }
    [JsonPropertyName("amount_type")]  public string?  AmountType   { get; set; }   // "week" / "month" / "hour"
    /// <summary>bei "week": Wochenstunden. Modell kommt aus Type/TypeId — NICHT aus 17h-Default.</summary>
    [JsonPropertyName("amount")]       public decimal? Amount       { get; set; }
    [JsonPropertyName("week_hours")]   public decimal? WeekHours    { get; set; }
    [JsonPropertyName("percentage")]   public decimal? Percentage   { get; set; }
    // easy@work liefert Datum mal als "yyyy-MM-dd", mal als UTC-Timestamp — als
    // String einlesen und über EawDateUtil ins Schweizer Datum wandeln (sonst
    // scheitert DateOnly am Timestamp → Vertrag leer → alles fälschlich UTP).
    [JsonPropertyName("from")]         public string?  FromRaw      { get; set; }
    [JsonPropertyName("to")]           public string?  ToRaw        { get; set; }
    // Space-Format-Timestamp → string-backed (sonst wirft STJ und die ganze
    // Contract-Deserialisierung scheitert → leere Liste → timeline=0).
    [JsonPropertyName("updated_at")]   public string?  UpdatedAtRaw { get; set; }
    // Soft-Delete (Walter-Vorgabe 22.07.2026): easy@work liefert auch GELÖSCHTE
    // Verträge mit («Include deleted contracts») — die dürfen NIE in die Timeline
    // (ein gelöschter offener Vertrag überlappt sonst alles → STRICT-Skip,
    // der aktuelle Vertrag wird nicht importiert; Fall Ceng Ozan 104800alt).
    [JsonPropertyName("deleted_at")]   public string?  DeletedAtRaw { get; set; }
    [JsonIgnore] public bool IsDeleted => !string.IsNullOrWhiteSpace(DeletedAtRaw);
    [JsonIgnore] public DateOnly? From => EawDateUtil.ParseSwissDate(FromRaw);
    [JsonIgnore] public DateOnly? To   => EawDateUtil.ParseSwissDate(ToRaw);
    [JsonIgnore] public DateTime? UpdatedAt => EawDateUtil.ParseTimestamp(UpdatedAtRaw);
}

/// <summary>
/// Funktion/Position eines MA. <c>Name</c> = der Funktions-Code, der 1:1 unserem
/// <c>job_group.code</c> entspricht (z.B. "SHIFT_LEADER_7_PLUS", "REST_MANAGER",
/// "CREW"). Quelle: <c>…/employees/{id}/positions</c>. Walter-Vorgabe 22.06.2026.
/// </summary>
public class EawPosition
{
    [JsonPropertyName("id")]   public int     Id   { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>Lohnstufe pro MA mit From-Datum.</summary>
public class EawPayRate
{
    [JsonPropertyName("id")]           public int      Id          { get; set; }
    [JsonPropertyName("employee_id")]  public int      EmployeeId  { get; set; }
    // Datum als String (Timestamp oder reines Datum) → Schweizer Datum via EawDateUtil.
    [JsonPropertyName("from")]         public string?  FromRaw     { get; set; }
    [JsonPropertyName("to")]           public string?  ToRaw       { get; set; }
    [JsonPropertyName("rate")]         public decimal? Rate        { get; set; }
    [JsonPropertyName("type")]         public string?  Type        { get; set; }   // "hour" / "month" / "fte"
    // Space-Format-Timestamp → string-backed (sonst wirft STJ und die ganze
    // PayRate-Deserialisierung scheitert → leere Liste → kein Lohn, timeline=0).
    [JsonPropertyName("updated_at")]   public string?  UpdatedAtRaw { get; set; }
    // Soft-Delete: gelöschte Tarife («INCLUDE_DELETED_PAYRATE») nie verwenden.
    [JsonPropertyName("deleted_at")]   public string?  DeletedAtRaw { get; set; }
    [JsonIgnore] public bool IsDeleted => !string.IsNullOrWhiteSpace(DeletedAtRaw);
    [JsonIgnore] public DateOnly? From => EawDateUtil.ParseSwissDate(FromRaw);
    [JsonIgnore] public DateOnly? To   => EawDateUtil.ParseSwissDate(ToRaw);
    [JsonIgnore] public DateTime? UpdatedAt => EawDateUtil.ParseTimestamp(UpdatedAtRaw);
}

/// <summary>
/// Schweizer Fiscal-Info — an das ECHTE easy@work-Schema angepasst (Walter
/// 19.06.2026, aus den API-Docs verifiziert). Enthält Bank/IBAN, Bewilligung
/// (visa_permit_type + Daten) und Ehepartner-Permit (relevant für QST-Pflicht).
/// Achtung: AHV ist NICHT hier, sondern ein Custom Field (siehe EawProperty).
/// </summary>
public class EawFiscalInfo
{
    [JsonPropertyName("id")]              public int?     Id            { get; set; }
    [JsonPropertyName("employee_id")]    public int      EmployeeId    { get; set; }
    [JsonPropertyName("customer_id")]    public int?     CustomerId    { get; set; }
    [JsonPropertyName("iban")]           public string?  Iban          { get; set; }
    [JsonPropertyName("bank_id")]        public string?  BankId        { get; set; }   // Swiss bank clearing code
    [JsonPropertyName("bank_branch_id")] public string?  BankBranchId  { get; set; }
    [JsonPropertyName("account_number")] public string?  AccountNumber { get; set; }
    [JsonPropertyName("account_name")]   public string?  AccountName   { get; set; }
    [JsonPropertyName("country")]        public string?  Country       { get; set; }   // "CHE"
    /// <summary>Bewilligung: G/B/C/L/CI/N/F/S.</summary>
    [JsonPropertyName("visa_permit_type")] public string? VisaPermitType { get; set; }
    [JsonPropertyName("emission")]       public DateOnly? Emission     { get; set; }   // Permit issue date
    [JsonPropertyName("expiration")]     public DateOnly? Expiration   { get; set; }   // Permit expiry date
    [JsonPropertyName("spouse_works_switzerland")] public int? SpouseWorksSwitzerland { get; set; }
    [JsonPropertyName("spouse_visa_permit_type")]  public string? SpouseVisaPermitType { get; set; }
    [JsonPropertyName("fte_other_employment")]     public decimal? FteOtherEmployment   { get; set; }
    [JsonPropertyName("emplid")]         public string?  Emplid        { get; set; }   // Payroll employee number
    [JsonPropertyName("created_at")]     public DateTime? CreatedAt    { get; set; }
    [JsonPropertyName("updated_at")]     public DateTime? UpdatedAt    { get; set; }
}

/// <summary>
/// easy@work Custom Field / „Property" (Benutzerdefiniertes Feld), zeitlich
/// versioniert. Quelle: <c>…/employees/{n+Nummer}/properties</c>. Hier liegen
/// AHV-Nummer, Familienstand, Funktion, Qualification CCNT etc. — identifiziert
/// über den stabilen <see cref="Key"/> (nicht das deutsche Label).
/// </summary>
public class EawProperty
{
    [JsonPropertyName("id")]          public int?      Id         { get; set; }
    [JsonPropertyName("object_type")] public string?   ObjectType { get; set; }
    [JsonPropertyName("object_id")]   public int?      ObjectId   { get; set; }
    [JsonPropertyName("key")]         public string?   Key        { get; set; }
    [JsonPropertyName("value")]       public string?   Value      { get; set; }
    // from/to = UTC-Datetime-Strings. «from» via ParseSwissDate (Tagesbeginn Zürich).
    // «to» ebenfalls ParseSwissDate als Default — für Nachtarbeit-Bis gilt die
    // ArG-Regel aus dem Beginn, nicht dieses To (UTC inkonsistent, Walter 26.07.2026).
    [JsonPropertyName("from")]        public string?   FromRaw    { get; set; }
    [JsonPropertyName("to")]          public string?   ToRaw      { get; set; }
    [JsonPropertyName("updated_at")]  public DateTime? UpdatedAt  { get; set; }
    [JsonIgnore] public DateOnly? From => EawDateUtil.ParseSwissDate(FromRaw);
    [JsonIgnore] public DateOnly? To   => EawDateUtil.ParseSwissDate(ToRaw);
}

/// <summary>Einzelner Kommentar (aus dem `comments`-Array eines Timepunch).</summary>
public class EawTimepunchComment
{
    [JsonPropertyName("id")]         public int?      Id        { get; set; }
    [JsonPropertyName("text")]       public string?   Text      { get; set; }
    [JsonPropertyName("comment")]    public string?   Comment   { get; set; }
    [JsonPropertyName("body")]       public string?   Body      { get; set; }
    [JsonPropertyName("message")]    public string?   Message   { get; set; }
    [JsonPropertyName("content")]    public string?   Content   { get; set; }
    [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; set; }
    // Manche easy@work-Versionen liefern `created_by` als String (Name),
    // andere als Integer (User-ID) → FlexibleStringConverter akzeptiert beides.
    [JsonPropertyName("created_by")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string?   CreatedBy { get; set; }
    // Bei der API-Konvention `<feld>_by` = ID + `<feld>_by_name` = Display-Name
    // (analog `approved_by`/`approved_by_name`).
    [JsonPropertyName("created_by_name")] public string? CreatedByName { get; set; }
    [JsonPropertyName("user_name")]       public string? UserName      { get; set; }
    [JsonPropertyName("user")]            public string? UserDisplay   { get; set; }

    /// <summary>Erster nicht-leerer Textwert.</summary>
    public string? AnyText => new[] { Text, Comment, Body, Message, Content }
        .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>
    /// Bester Display-Name des Bearbeiters: Name-Feld bevorzugt, sonst CreatedBy
    /// (welcher auch ein Klartext-Name sein kann, je nach API-Version).
    /// Reine numerische Strings (User-IDs) werden ausgefiltert.
    /// </summary>
    public string? EditorDisplayName
    {
        get
        {
            foreach (var v in new[] { CreatedByName, UserName, UserDisplay, CreatedBy })
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                var s = v.Trim();
                if (int.TryParse(s, out _)) continue; // numerische ID → kein Display-Name
                return s;
            }
            return null;
        }
    }
}

/// <summary>
/// Changelog-Eintrag eines Timepunch (API: <c>include_changelog=true</c> am
/// Einzelabruf). Enthält die Audit-Texte «Ein/Aus vom … bis … geändert» —
/// die normalen <c>comments</c> haben oft nur den Freitext («Falsch gestempelt»).
/// </summary>
public class EawTimepunchChangelogEntry
{
    [JsonPropertyName("id")]         public int?      Id        { get; set; }
    [JsonPropertyName("text")]       public string?   Text      { get; set; }
    [JsonPropertyName("body")]       public string?   Body      { get; set; }
    [JsonPropertyName("message")]    public string?   Message   { get; set; }
    [JsonPropertyName("comment")]    public string?   Comment   { get; set; }
    [JsonPropertyName("content")]    public string?   Content   { get; set; }
    [JsonPropertyName("description")] public string?  Description { get; set; }
    [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("created_by_name")] public string? CreatedByName { get; set; }

    public string? AnyText => new[] { Text, Body, Message, Comment, Content, Description }
        .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

/// <summary>Stempelzeit (Timepunch).</summary>
public class EawTimepunch
{
    [JsonPropertyName("id")]            public int      Id            { get; set; }
    [JsonPropertyName("employee_id")]   public int      EmployeeId    { get; set; }
    [JsonPropertyName("business_date")] public DateOnly? BusinessDate { get; set; }
    [JsonPropertyName("in")]            public DateTime? In           { get; set; }
    [JsonPropertyName("out")]           public DateTime? Out          { get; set; }
    [JsonPropertyName("hours")]         public decimal? Hours         { get; set; }
    /// <summary>Legacy-Flag aus früherer API-Version.</summary>
    [JsonPropertyName("edited")]        public bool?    Edited        { get; set; }
    /// <summary>Aktuelles Feld: nicht-null → MA-Stempel wurde manuell bearbeitet.</summary>
    [JsonPropertyName("edited_by_id")]  public int?     EditedById    { get; set; }
    /// <summary>Kommentare als Array — nur befüllt, wenn `?with[]=comments` mitgesendet wurde.</summary>
    [JsonPropertyName("comments")]      public List<EawTimepunchComment>? Comments { get; set; }
    /// <summary>Audit-Historie — nur am Einzelabruf mit <c>include_changelog=true</c>.</summary>
    [JsonPropertyName("changelog")]     public List<EawTimepunchChangelogEntry>? Changelog { get; set; }

    // ── Original-Zeit (falls bearbeitet). Wir probieren mehrere wahrscheinliche
    //    Feldnamen; sobald die API einen davon liefert, ist OriginalIn/Out befüllt.
    [JsonPropertyName("original_in")]   public DateTime? OriginalInRaw1   { get; set; }
    [JsonPropertyName("previous_in")]   public DateTime? OriginalInRaw2   { get; set; }
    [JsonPropertyName("in_original")]   public DateTime? OriginalInRaw3   { get; set; }
    [JsonPropertyName("original_out")]  public DateTime? OriginalOutRaw1  { get; set; }
    [JsonPropertyName("previous_out")]  public DateTime? OriginalOutRaw2  { get; set; }
    [JsonPropertyName("out_original")]  public DateTime? OriginalOutRaw3  { get; set; }

    public DateTime? OriginalIn  => OriginalInRaw1  ?? OriginalInRaw2  ?? OriginalInRaw3;
    public DateTime? OriginalOut => OriginalOutRaw1 ?? OriginalOutRaw2 ?? OriginalOutRaw3;
    [JsonPropertyName("deleted_at")]    public DateTime? DeletedAt    { get; set; }   // != null = storniert
    [JsonPropertyName("updated_at")]    public DateTime? UpdatedAt    { get; set; }
    /// <summary>
    /// Zeitpunkt der Eintragerstellung — bei einem MA-Punch entspricht das
    /// dem Ur-Stempel. Wenn der Stempel später manuell korrigiert wurde
    /// (IsEdited), bleibt CreatedAt unverändert und repräsentiert damit
    /// die Original-Zeit des MA.
    /// </summary>
    [JsonPropertyName("created_at")]    public DateTime? CreatedAt    { get; set; }
    /// <summary>Worked duration in Sekunden (out - in).</summary>
    [JsonPropertyName("length")]        public int?      Length       { get; set; }
    [JsonPropertyName("approved")]      public bool?     Approved     { get; set; }
    [JsonPropertyName("approved_by")]   public int?      ApprovedById { get; set; }
    [JsonPropertyName("approved_by_name")] public string? ApprovedByName { get; set; }

    /// <summary>Wurde dieser Stempel manuell bearbeitet?</summary>
    public bool IsEdited => EditedById.HasValue || Edited == true;

    /// <summary>Alle nicht-leeren Kommentar-Texte zu einem String zusammengezogen.</summary>
    public string? JoinedComments =>
        Comments == null || Comments.Count == 0
            ? null
            : string.Join(" / ",
                Comments.Select(c => c.AnyText).Where(t => !string.IsNullOrWhiteSpace(t)));

    /// <summary>Changelog-Texte (Audit) zusammengezogen — Quelle für Original-Zeiten.</summary>
    public string? JoinedChangelog =>
        Changelog == null || Changelog.Count == 0
            ? null
            : string.Join(" / ",
                Changelog.Select(c => c.AnyText).Where(t => !string.IsNullOrWhiteSpace(t)));
}

// ════════════════════════════════════════════════════════════════════════
// Verfügbarkeit (Walter 09.07.2026) — Endpunkte von easy@work-Support bestätigt:
//   GET customers/{c}/employees/{e}/availabilities            (Laravel-paginiert)
//   GET customers/{c}/employees/{e}/availabilities/{a}/days
// Struktur aus echtem Dump (MA 580009): availability = Gültigkeits-Kopf mit
// Wochenmuster (repeat=7); day = Zeile pro Wochentag — day-Index 0..6 zählt AB
// dem from-Datum, offset/length in SEKUNDEN ab LOKALEM Tagesbeginn,
// whole_day=true → ganztags. FEHLENDE Tage = an dem Wochentag NICHT verfügbar.
// from/to sind UTC-Timestamps → EawDateUtil (Europe/Zurich) verwenden.
// ════════════════════════════════════════════════════════════════════════

public class EawAvailability
{
    [JsonPropertyName("id")]          public long      Id         { get; set; }
    [JsonPropertyName("employee_id")] public int       EmployeeId { get; set; }
    [JsonPropertyName("from")]        public string?   FromRaw    { get; set; }
    [JsonPropertyName("to")]          public string?   ToRaw      { get; set; }
    [JsonPropertyName("repeat")]      public int?      Repeat     { get; set; }
    [JsonPropertyName("active")]      public bool?     Active     { get; set; }
    [JsonPropertyName("deleted_at")]  public string?   DeletedAtRaw { get; set; }
    /// <summary>Bedeutung nicht offiziell dokumentiert — im Dump [5]; vermutlich
    /// gewünschte Arbeitstage pro Woche. Nur informativ übernommen.</summary>
    [JsonPropertyName("work_days")]   public List<int>? WorkDays  { get; set; }

    [JsonIgnore] public DateOnly? From => EawDateUtil.ParseSwissDate(FromRaw);
    [JsonIgnore] public DateOnly? To   => EawDateUtil.ParseSwissDate(ToRaw);
    [JsonIgnore] public bool IsDeleted => !string.IsNullOrWhiteSpace(DeletedAtRaw);
}

public class EawAvailabilityDay
{
    [JsonPropertyName("id")]              public long    Id             { get; set; }
    [JsonPropertyName("availability_id")] public long    AvailabilityId { get; set; }
    /// <summary>Tag-Index 0..6 AB dem availability-from (NICHT fix Mo=0!).</summary>
    [JsonPropertyName("day")]             public int     Day            { get; set; }
    /// <summary>Sekunden ab lokalem Tagesbeginn (28800 = 08:00).</summary>
    [JsonPropertyName("offset")]          public long    Offset         { get; set; }
    /// <summary>Dauer in Sekunden (86400 = ganztags).</summary>
    [JsonPropertyName("length")]          public long    Length         { get; set; }
    [JsonPropertyName("whole_day")]       public bool    WholeDay       { get; set; }
    [JsonPropertyName("from")]            public string? FromRaw        { get; set; }
    [JsonPropertyName("to")]              public string? ToRaw          { get; set; }
    [JsonPropertyName("date")]            public string? DateRaw        { get; set; }
    [JsonPropertyName("deleted_at")]      public string? DeletedAtRaw   { get; set; }

    /// <summary>LOKALES Datum des Muster-Tags → daraus der Wochentag.</summary>
    [JsonIgnore] public DateOnly? LocalDate => EawDateUtil.ParseSwissDate(FromRaw ?? DateRaw);
    [JsonIgnore] public bool IsDeleted => !string.IsNullOrWhiteSpace(DeletedAtRaw);
}
