namespace HrSystem.Models;

public class AppText
{
    public int Id { get; set; }
    public string Module { get; set; } = "";
    public string TextKey { get; set; } = "";
    public string LanguageCode { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsActive { get; set; } = true;
}