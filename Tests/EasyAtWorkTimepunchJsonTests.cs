using System.Text.Json;
using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

public class EasyAtWorkTimepunchJsonTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleDateTimeConverter(), new FlexibleDateOnlyConverter() },
    };

    [Fact]
    public void Kommentar_mit_User_als_Objekt_macht_Stempel_nicht_unlesbar()
    {
        const string json = """
        { "data": {
            "id": 77, "employee_id": 5, "business_date": "2026-09-16",
            "in": "2026-09-16 18:01:00", "out": "2026-09-16 22:44:00", "edited_by_id": 9,
            "comments": [
              { "id": 1, "body": "vergessen auszustempeln", "commentable_type": "timepunch",
                "created_at": "2026-09-17 11:53:00", "user": { "id": 9, "firstname": "Anna", "lastname": "Muster" } },
              { "id": 2, "body": "vergessen auszustempeln", "created_at": "2026-09-17 11:53:30", "user": { "id": 9, "name": "Anna Muster" } }
            ]
        } }
        """;
        var tp = EasyAtWorkTimepunchJson.Lesen(json, Opts)!;
        Assert.Equal(77, tp.Id);
        Assert.True(tp.IsEdited);
        Assert.Equal(2, tp.Comments!.Count);
        Assert.Equal("Anna Muster", tp.Comments[0].EditorDisplayName);
        Assert.Equal(new DateTime(2026, 9, 17, 11, 53, 0, DateTimeKind.Utc), tp.Comments[0].CreatedAt);
        Assert.Equal("vergessen auszustempeln", tp.JoinedComments);
    }

    [Fact]
    public void Kommentare_und_Changelog_im_data_Umschlag()
    {
        const string json = """
        { "id": 78, "employee_id": 5, "in": "2026-09-16T18:01:00Z",
          "comments": { "data": [ { "text": "Pause vergessen", "created_by": 12 } ] },
          "changelog": { "data": [ { "description": "Ein vom 16.9.2026, 20:01 bis zum 16.9.2026, 20:15 geändert" } ] } }
        """;
        var tp = EasyAtWorkTimepunchJson.Lesen(json, Opts)!;
        Assert.Equal("Pause vergessen", tp.JoinedComments);
        Assert.Null(tp.Comments![0].EditorDisplayName);
        Assert.Contains("Ein vom", tp.JoinedChangelog);
    }

    [Fact]
    public void Ohne_Kommentare_leere_Listen()
    {
        var tp = EasyAtWorkTimepunchJson.Lesen("""{ "id": 79, "employee_id": 5 }""", Opts)!;
        Assert.Empty(tp.Comments!);
        Assert.Null(tp.JoinedComments);
    }
}
