using System.Text;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// === Конфигурация ===
const int VariantN = 22;
int cacheTtlSeconds = 2 * VariantN + 240; // 284
int defaultPageSize = 20;

// Вставь сюда свою строку подключения (оставлена та, что ты присылал)
string connectionString = "Server=db27595.public.databaseasp.net; Database=db27595; User Id=db27595; Password=r@8JQb_26Ad#; Encrypt=True; TrustServerCertificate=True; MultipleActiveResultSets=True;";

app.UseSession();

// Гарантируем UTF-8
app.Use(async (ctx, next) =>
{
    if (!ctx.Response.HasStarted)
        ctx.Response.Headers["Content-Type"] = "text/html; charset=utf-8";
    await next();
});

// === Вспомогательные HTML-шаблоны ===
string LayoutHead() => @"<!doctype html>
<html lang='ru'>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <title>Lab3</title>
  <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css' rel='stylesheet'/>
</head>
<body class='bg-dark text-light'>
<div class='container py-3'>";

string LayoutFooter() => @"</div>
<script src='https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/js/bootstrap.bundle.min.js'></script>
</body></html>";

string Nav() => @"
<nav class='navbar navbar-expand-lg navbar-dark bg-secondary rounded mb-3'>
  <div class='container-fluid'>
    <a class='navbar-brand' href='/'>Lab3</a>
    <div class='collapse navbar-collapse'>
      <ul class='navbar-nav me-auto mb-2 mb-lg-0'>
        <li class='nav-item'><a class='nav-link' href='/info'>Info</a></li>
        <li class='nav-item'><a class='nav-link' href='/table/Clients'>Clients</a></li>
        <li class='nav-item'><a class='nav-link' href='/clients/add'>Add Client</a></li>
        <li class='nav-item'><a class='nav-link' href='/searchform1'>Form1 (Cookie)</a></li>
        <li class='nav-item'><a class='nav-link' href='/searchform2'>Form2 (Session)</a></li>
      </ul>
    </div>
  </div>
</nav>";

// === HOME -> redirect to Clients table ===
app.MapGet("/", ctx =>
{
    ctx.Response.Redirect("/table/Clients");
    return Task.CompletedTask;
});

// === INFO ===
app.MapGet("/info", async ctx =>
{
    var sb = new StringBuilder();
    sb.Append(LayoutHead());
    sb.Append(Nav());
    sb.Append("<h3>Info</h3>");
    sb.Append($"<p><strong>IP:</strong> {ctx.Connection.RemoteIpAddress}</p>");
    sb.Append($"<p><strong>User-Agent:</strong> {ctx.Request.Headers["User-Agent"]}</p>");
    sb.Append(LayoutFooter());
    await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
});

// === TABLE with search + pagination ===
// /table/Clients?page=1&size=20&search=ivan
app.MapGet("/table/{table}", async (HttpContext ctx, string table, IMemoryCache cache) =>
{
    // normalize
    table = table.Trim();

    int page = 1;
    if (int.TryParse(ctx.Request.Query["page"], out var p) && p > 0) page = p;
    int size = defaultPageSize;
    if (int.TryParse(ctx.Request.Query["size"], out var s) && s > 0) size = s;
    string search = ctx.Request.Query["search"].ToString() ?? "";

    // Validate table existence
    using (var chk = new SqlConnection(connectionString))
    {
        var exists = await chk.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @t",
            new { t = table });
        if (exists == 0)
        {
            var notfound = new StringBuilder();
            notfound.Append(LayoutHead()).Append(Nav());
            notfound.Append($"<div class='alert alert-danger'>Таблица <b>{table}</b> не найдена в базе.</div>");
            notfound.Append(LayoutFooter());
            await ctx.Response.WriteAsync(notfound.ToString(), Encoding.UTF8);
            return;
        }
    }

    // Build cache key for this query (cache only page=1, size=20, no search — keep cache simple)
    string cacheKey = $"tbl_{table}_p{page}_s{size}_q{search}";
    bool fromCache = cache.TryGetValue(cacheKey, out (IEnumerable<dynamic> data, int totalCount)? cached);

    IEnumerable<dynamic> rows;
    int totalCount;

    if (fromCache && cached != null)
    {
        rows = cached.Value.data;
        totalCount = cached.Value.totalCount;
    }
    else
    {
        using var conn = new SqlConnection(connectionString);
        // Build WHERE if search provided
        var where = "";
        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search))
        {
            where = "WHERE FirstName LIKE @q OR LastName LIKE @q OR PhoneNumber LIKE @q OR Email LIKE @q";
            parameters.Add("q", $"%{search}%");
        }

        // total count
        var sqlCount = $"SELECT COUNT(*) FROM [{table}] {where}";
        totalCount = await conn.ExecuteScalarAsync<int>(sqlCount, parameters);

        // paging using OFFSET-FETCH
        var offset = (page - 1) * size;
        var sql = $@"SELECT *
FROM [{table}]
{where}
ORDER BY ClientID
OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY";
        parameters.Add("offset", offset);
        parameters.Add("size", size);

        var result = await conn.QueryAsync(sql, parameters);
        rows = result.ToList();

        // cache only "no search" first page to satisfy requirement caching 20 records from table
        // but we can cache any query — cache for cacheTtlSeconds
        cache.Set(cacheKey, (rows, totalCount), TimeSpan.FromSeconds(cacheTtlSeconds));
    }

    // Render HTML with pagination controls
    var sb = new StringBuilder();
    sb.Append(LayoutHead());
    sb.Append(Nav());

    sb.Append("<div class='card bg-dark text-light'><div class='card-body'>");
    sb.Append($"<h4>Таблица: <span class='badge bg-info text-dark ms-2'>{table}</span></h4>");
    sb.Append($"<p>Показано: {((rows as ICollection<dynamic>)?.Count ?? rows.Count())} из {totalCount} (источник: {(fromCache ? "кэш" : "бд")})</p>");

    // Search form (keeps current search)
    sb.Append($@"
<form class='row g-2 mb-3' method='get' action='/table/{table}'>
  <div class='col-auto'>
    <input class='form-control' name='search' placeholder='Поиск по имени, фамилии, телефону, email' value='{System.Net.WebUtility.HtmlEncode(search)}'/>
  </div>
  <div class='col-auto'>
    <select class='form-select' name='size'>
      <option value='10' {(size == 10 ? "selected" : "")}>10</option>
      <option value='20' {(size == 20 ? "selected" : "")}>20</option>
      <option value='50' {(size == 50 ? "selected" : "")}>50</option>
    </select>
  </div>
  <div class='col-auto'>
    <button class='btn btn-primary' type='submit'>Найти</button>
  </div>
</form>
");

    // Table
    sb.Append("<div class='table-responsive'><table class='table table-sm table-dark table-striped'>");
    // header
    if (rows.Any())
    {
        var first = (IDictionary<string, object>)rows.First();
        sb.Append("<thead><tr>");
        foreach (var col in first.Keys) sb.Append($"<th>{col}</th>");
        sb.Append("</tr></thead>");
        sb.Append("<tbody>");
        foreach (var r in rows)
        {
            var dict = (IDictionary<string, object>)r;
            sb.Append("<tr>");
            foreach (var col in first.Keys)
            {
                var v = dict[col];
                sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(v?.ToString() ?? "")}</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody>");
    }
    else
    {
        sb.Append("<tr><td>Нет данных</td></tr>");
    }
    sb.Append("</table></div>");

    // Pagination controls
    int totalPages = (int)Math.Ceiling((double)totalCount / size);
    sb.Append("<nav><ul class='pagination'>");
    string baseUrl = $"/table/{table}?search={System.Net.WebUtility.UrlEncode(search)}&size={size}&page=";
    int prev = Math.Max(1, page - 1);
    int next = Math.Min(totalPages == 0 ? 1 : totalPages, page + 1);

    sb.Append($"<li class='page-item {(page == 1 ? "disabled" : "")}'><a class='page-link' href='{baseUrl}{prev}'>Previous</a></li>");
    for (int i = 1; i <= totalPages; i++)
    {
        if (i > 10 && Math.Abs(i - page) > 3 && i != totalPages) // compact
        {
            if (i == page - 4 || i == page + 4) sb.Append("<li class='page-item disabled'><span class='page-link'>...</span></li>");
            continue;
        }
        sb.Append($"<li class='page-item {(i == page ? "active" : "")}'><a class='page-link' href='{baseUrl}{i}'>{i}</a></li>");
    }
    sb.Append($"<li class='page-item {(page >= totalPages ? "disabled" : "")}'><a class='page-link' href='{baseUrl}{next}'>Next</a></li>");
    sb.Append("</ul></nav>");

    sb.Append("</div></div>"); // card
    sb.Append(LayoutFooter());

    await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
});

// === ADD CLIENT (GET form + POST handler) ===
app.MapGet("/clients/add", async ctx =>
{
    var sb = new StringBuilder();
    sb.Append(LayoutHead());
    sb.Append(Nav());
    sb.Append(@"
<div class='card bg-dark text-light'>
  <div class='card-body'>
    <h4>Добавить клиента</h4>
    <form method='post' action='/clients/add'>
      <div class='mb-2'>
        <label class='form-label'>FirstName*</label>
        <input class='form-control' name='FirstName' required />
      </div>
      <div class='mb-2'>
        <label class='form-label'>LastName*</label>
        <input class='form-control' name='LastName' required />
      </div>
      <div class='mb-2'>
        <label class='form-label'>BirthDate* (YYYY-MM-DD)</label>
        <input class='form-control' name='BirthDate' type='date' required />
      </div>
      <div class='mb-2'>
        <label class='form-label'>Gender* (M/F)</label>
        <select class='form-select' name='Gender' required>
          <option value='M'>M</option>
          <option value='F'>F</option>
        </select>
      </div>
      <div class='mb-2'>
        <label class='form-label'>PhoneNumber* (unique)</label>
        <input class='form-control' name='PhoneNumber' required />
      </div>
      <div class='mb-2'>
        <label class='form-label'>Email (unique or empty)</label>
        <input class='form-control' name='Email' />
      </div>
      <button class='btn btn-success' type='submit'>Добавить</button>
    </form>
  </div>
</div>");
    sb.Append(LayoutFooter());
    await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
});

app.MapPost("/clients/add", async ctx =>
{
    var form = await ctx.Request.ReadFormAsync();
    var fn = form["FirstName"].ToString().Trim();
    var ln = form["LastName"].ToString().Trim();
    var bdStr = form["BirthDate"].ToString().Trim();
    var gender = form["Gender"].ToString().Trim().ToUpper();
    var phone = form["PhoneNumber"].ToString().Trim();
    var email = string.IsNullOrWhiteSpace(form["Email"]) ? null : form["Email"].ToString().Trim();

    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(fn)) errors.Add("FirstName обязателен");
    if (string.IsNullOrWhiteSpace(ln)) errors.Add("LastName обязателен");
    if (!DateTime.TryParse(bdStr, out var birth)) errors.Add("BirthDate неверного формата");
    if (!(gender == "M" || gender == "F")) errors.Add("Gender должен быть 'M' или 'F'");
    if (string.IsNullOrWhiteSpace(phone)) errors.Add("PhoneNumber обязателен");

    if (errors.Any())
    {
        var sb = new StringBuilder();
        sb.Append(LayoutHead()).Append(Nav());
        sb.Append("<div class='alert alert-danger'><ul>");
        foreach (var e in errors) sb.Append($"<li>{e}</li>");
        sb.Append("</ul></div>");
        sb.Append("<a class='btn btn-secondary' href='/clients/add'>Назад</a>");
        sb.Append(LayoutFooter());
        await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
        return;
    }

    // Check uniqueness phone and email
    using (var conn = new SqlConnection(connectionString))
    {
        await conn.OpenAsync();
        var pcount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Clients WHERE PhoneNumber = @phone", new { phone });
        if (pcount > 0)
        {
            var sb = new StringBuilder();
            sb.Append(LayoutHead()).Append(Nav());
            sb.Append("<div class='alert alert-danger'>Клиент с таким телефонным номером уже существует.</div>");
            sb.Append("<a class='btn btn-secondary' href='/clients/add'>Назад</a>");
            sb.Append(LayoutFooter());
            await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
            return;
        }
        if (!string.IsNullOrWhiteSpace(email))
        {
            var ecount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Clients WHERE Email = @email", new { email });
            if (ecount > 0)
            {
                var sb = new StringBuilder();
                sb.Append(LayoutHead()).Append(Nav());
                sb.Append("<div class='alert alert-danger'>Клиент с таким email уже существует.</div>");
                sb.Append("<a class='btn btn-secondary' href='/clients/add'>Назад</a>");
                sb.Append(LayoutFooter());
                await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
                return;
            }
        }

        // Insert (omit ClientID and RegistrationDate -> defaults)
        var sql = @"INSERT INTO Clients (FirstName, LastName, MiddleName, BirthDate, Gender, PhoneNumber, Email)
                    VALUES (@fn, @ln, NULL, @birth, @gender, @phone, @email)";
        var res = await conn.ExecuteAsync(sql, new { fn, ln, birth, gender, phone, email });
        // Invalidate cache for Clients
        var cache = ctx.RequestServices.GetRequiredService<IMemoryCache>();
        // remove all keys starting with tbl_Clients (simple approach: remove known page sizes)
        cache.Remove($"tbl_Clients_p1_s{defaultPageSize}_q");
        // simpler: clear specific known cache keys — you can extend as needed
    }

    // redirect to clients list (first page)
    ctx.Response.Redirect("/table/Clients");
});

// === Forms: Form1 (Cookies) and Form2 (Session) — now both redirect to table with search ===
// Form1: cookie storage + search
app.MapGet("/searchform1", async ctx =>
{
    var text = ctx.Request.Cookies["sf1_text"] ?? "";
    var gender = ctx.Request.Cookies["sf1_gender"] ?? "";
    var role = ctx.Request.Cookies["sf1_role"] ?? "";

    var sb = new StringBuilder();
    sb.Append(LayoutHead()).Append(Nav());
    sb.Append("<h4>Form1 — state in Cookies (при отправке выполняет поиск)</h4>");
    sb.Append($@"
<form method='post' action='/searchform1'>
  <div class='mb-2'><input class='form-control' name='text' placeholder='search' value='{System.Net.WebUtility.HtmlEncode(text)}'/></div>
  <div class='mb-2'>
    <select class='form-select' name='role'>
      <option value='any' {(role == "any" ? "selected" : "")}>any</option>
      <option value='client' {(role == "client" ? "selected" : "")}>client</option>
      <option value='admin' {(role == "admin" ? "selected" : "")}>admin</option>
    </select>
  </div>
  <div class='mb-2'>
    <input class='form-check-input' type='radio' name='gender' value='M' {(gender == "M" ? "checked" : "")}/> M
    <input class='form-check-input' type='radio' name='gender' value='F' {(gender == "F" ? "checked" : "")}/> F
  </div>
  <button class='btn btn-primary' type='submit'>Search (save cookie & go)</button>
</form>");
    sb.Append(LayoutFooter());
    await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
});

app.MapPost("/searchform1", async ctx =>
{
    var form = await ctx.Request.ReadFormAsync();
    var text = form["text"].ToString();
    var gender = form["gender"].ToString();
    var role = form["role"].ToString();

    ctx.Response.Cookies.Append("sf1_text", text, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(7) });
    ctx.Response.Cookies.Append("sf1_gender", gender, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(7) });
    ctx.Response.Cookies.Append("sf1_role", role, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(7) });

    // redirect to clients table with search param
    var q = System.Net.WebUtility.UrlEncode(text);
    ctx.Response.Redirect($"/table/Clients?search={q}&page=1&size={defaultPageSize}");
});

// Form2: session storage + search
app.MapGet("/searchform2", async ctx =>
{
    var text = ctx.Session.GetString("sf2_text") ?? "";
    var gender = ctx.Session.GetString("sf2_gender") ?? "";
    var role = ctx.Session.GetString("sf2_role") ?? "";

    var sb = new StringBuilder();
    sb.Append(LayoutHead()).Append(Nav());
    sb.Append("<h4>Form2 — state in Session (при отправке выполняет поиск)</h4>");
    sb.Append($@"
<form method='post' action='/searchform2'>
  <div class='mb-2'><input class='form-control' name='text' placeholder='search' value='{System.Net.WebUtility.HtmlEncode(text)}'/></div>
  <div class='mb-2'>
    <select class='form-select' name='role'>
      <option value='any' {(role == "any" ? "selected" : "")}>any</option>
      <option value='client' {(role == "client" ? "selected" : "")}>client</option>
      <option value='admin' {(role == "admin" ? "selected" : "")}>admin</option>
    </select>
  </div>
  <div class='mb-2'>
    <input class='form-check-input' type='radio' name='gender' value='M' {(gender == "M" ? "checked" : "")}/> M
    <input class='form-check-input' type='radio' name='gender' value='F' {(gender == "F" ? "checked" : "")}/> F
  </div>
  <button class='btn btn-primary' type='submit'>Search (save session & go)</button>
</form>");
    sb.Append(LayoutFooter());
    await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
});

app.MapPost("/searchform2", async ctx =>
{
    var form = await ctx.Request.ReadFormAsync();
    var text = form["text"].ToString();
    var gender = form["gender"].ToString();
    var role = form["role"].ToString();

    ctx.Session.SetString("sf2_text", text);
    ctx.Session.SetString("sf2_gender", gender);
    ctx.Session.SetString("sf2_role", role);

    var q = System.Net.WebUtility.UrlEncode(text);
    ctx.Response.Redirect($"/table/Clients?search={q}&page=1&size={defaultPageSize}");
});

// Fallback 404
app.MapFallback(async ctx =>
{
    var sb = new StringBuilder();
    sb.Append(LayoutHead()).Append(Nav());
    sb.Append("<div class='alert alert-warning'>Page Not Found</div>");
    sb.Append(LayoutFooter());
    await ctx.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
});

app.Run();
