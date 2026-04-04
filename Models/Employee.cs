namespace HrSystem.Models;

public class Employee
{
    public int Id { get; set; }

    public string EmployeeNumber { get; set; } = "";
    public string? Salutation { get; set; }

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";

    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }

    public DateTime? DateOfBirth { get; set; }

    // alter Textwert vorläufig behalten
    public string? Nationality { get; set; }

    // neue saubere Referenz
    public int? NationalityId { get; set; }

    public string? LanguageCode { get; set; }

    public string? PhoneMobile { get; set; }
    public string? Email { get; set; }

    public DateTime? EntryDate { get; set; }
    public DateTime? ExitDate { get; set; }

    public int? PermitTypeId { get; set; }
    public DateTime? PermitExpiryDate { get; set; }

    public bool IsActive { get; set; } = true;

    public PermitType? PermitType { get; set; }
    public Nationality? NationalityRef { get; set; }

    public List<Employment> Employments { get; set; } = new();
}