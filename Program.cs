using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Datenbank
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Controller / API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// DB-Schema-Migrations und Seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Schema: neue Spalten
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee_import_snapshot
        ADD COLUMN IF NOT EXISTS job_title TEXT;
    ");

    // Neue Job-Gruppen: 2. Assistent, 1. Assistent, Restaurant Manager
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO job_group (code, sort_order, is_active)
        SELECT 'ASST_2', 5, true WHERE NOT EXISTS (SELECT 1 FROM job_group WHERE code = 'ASST_2');

        INSERT INTO job_group (code, sort_order, is_active)
        SELECT 'ASST_1', 6, true WHERE NOT EXISTS (SELECT 1 FROM job_group WHERE code = 'ASST_1');

        INSERT INTO job_group (code, sort_order, is_active)
        SELECT 'REST_MANAGER', 7, true WHERE NOT EXISTS (SELECT 1 FROM job_group WHERE code = 'REST_MANAGER');
    ");

    // Deutsche Bezeichnungen für neue Job-Gruppen
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO app_text (module, text_key, language_code, content, is_active)
        SELECT 'JOB_GROUP', 'ASST_2.NAME', 'de', '2. Assistent/in', true
        WHERE NOT EXISTS (SELECT 1 FROM app_text WHERE module = 'JOB_GROUP' AND text_key = 'ASST_2.NAME' AND language_code = 'de');

        INSERT INTO app_text (module, text_key, language_code, content, is_active)
        SELECT 'JOB_GROUP', 'ASST_1.NAME', 'de', '1. Assistent/in', true
        WHERE NOT EXISTS (SELECT 1 FROM app_text WHERE module = 'JOB_GROUP' AND text_key = 'ASST_1.NAME' AND language_code = 'de');

        INSERT INTO app_text (module, text_key, language_code, content, is_active)
        SELECT 'JOB_GROUP', 'REST_MANAGER.NAME', 'de', 'Restaurant Manager/in', true
        WHERE NOT EXISTS (SELECT 1 FROM app_text WHERE module = 'JOB_GROUP' AND text_key = 'REST_MANAGER.NAME' AND language_code = 'de');
    ");

    // Mindestlöhne 2026 – FIX-M / monatlich
    // 2. Assistent: Ia–IIIb = 4800, IV = 5293
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO minimum_wage_rule_new
            (job_group_code, employment_model_code, education_level_id, salary_type, amount, valid_from, is_active)
        SELECT 'ASST_2', 'FIX-M', el.id, 'monthly',
               CASE WHEN el.code = 'IV' THEN 5293.00 ELSE 4800.00 END,
               '2026-01-01', true
        FROM education_level el
        WHERE NOT EXISTS (
            SELECT 1 FROM minimum_wage_rule_new r
            WHERE r.job_group_code = 'ASST_2'
              AND r.employment_model_code = 'FIX-M'
              AND r.education_level_id = el.id
        );
    ");

    // 1. Assistent: Ia–IIIb = 5200, IV = 5293
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO minimum_wage_rule_new
            (job_group_code, employment_model_code, education_level_id, salary_type, amount, valid_from, is_active)
        SELECT 'ASST_1', 'FIX-M', el.id, 'monthly',
               CASE WHEN el.code = 'IV' THEN 5293.00 ELSE 5200.00 END,
               '2026-01-01', true
        FROM education_level el
        WHERE NOT EXISTS (
            SELECT 1 FROM minimum_wage_rule_new r
            WHERE r.job_group_code = 'ASST_1'
              AND r.employment_model_code = 'FIX-M'
              AND r.education_level_id = el.id
        );
    ");

    // Restaurant Manager: alle = 6100
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO minimum_wage_rule_new
            (job_group_code, employment_model_code, education_level_id, salary_type, amount, valid_from, is_active)
        SELECT 'REST_MANAGER', 'FIX-M', el.id, 'monthly', 6100.00, '2026-01-01', true
        FROM education_level el
        WHERE NOT EXISTS (
            SELECT 1 FROM minimum_wage_rule_new r
            WHERE r.job_group_code = 'REST_MANAGER'
              AND r.employment_model_code = 'FIX-M'
              AND r.education_level_id = el.id
        );
    ");
}

// Statische Dateien / Startseite
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

app.MapControllers();

app.Run();