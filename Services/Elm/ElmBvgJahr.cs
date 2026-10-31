namespace HrSystem.Services.Elm;

/// <summary>Freigegebene voraussichtliche BVG-Lohnsummen eines Jahres (Cockpit Vorsorge).</summary>
public class ElmBvgJahr
{
    public int Jahr { get; set; }
    public bool Freigegeben { get; set; }
    public DateTime? FreigegebenAm { get; set; }
    public List<ElmBvgZeile> Zeilen { get; set; } = new();
}

public class ElmBvgZeile
{
    public int EmployeeId { get; set; }
    public decimal Betrag { get; set; }
}
