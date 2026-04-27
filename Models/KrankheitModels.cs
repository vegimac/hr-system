namespace HrSystem.Models;

/// <summary>
/// Kumulierte Karenztage pro Mitarbeiter pro individuelles Arbeitsjahr
/// </summary>
public class KrankheitKarenzSaldo
{
    public int      Id                { get; set; }
    public int      EmployeeId        { get; set; }
    public int      CompanyProfileId  { get; set; }
    public DateOnly ArbeitsjährVon    { get; set; }
    public DateOnly ArbeitsjährBis    { get; set; }
    public decimal  KarenztageUsed    { get; set; } = 0;
    public DateTime UpdatedAt         { get; set; } = DateTime.UtcNow;

    public Employee?       Employee       { get; set; }
    public CompanyProfile? CompanyProfile { get; set; }
}

/// <summary>
/// Rollender 6-Monats-Lohndurchschnitt pro Mitarbeiter (für KTG/UVG-Meldung)
/// </summary>
public class EmployeeLohnDurchschnitt
{
    public int     Id                  { get; set; }
    public int     EmployeeId          { get; set; }
    public int     CompanyProfileId    { get; set; }
    public int     BerechnetPerYear    { get; set; }
    public int     BerechnetPerMonth   { get; set; }
    public int     MonateBasis         { get; set; }   // 1–6
    public decimal DurchschnittBrutto  { get; set; }   // Ø Monatslohn
    public decimal DurchschnittTaglohn { get; set; }   // ÷ 21.7
    public string  DetailJson          { get; set; } = "[]"; // [{monat, jahr, brutto}]
    public DateTime UpdatedAt          { get; set; } = DateTime.UtcNow;

    public Employee?       Employee       { get; set; }
    public CompanyProfile? CompanyProfile { get; set; }
}
