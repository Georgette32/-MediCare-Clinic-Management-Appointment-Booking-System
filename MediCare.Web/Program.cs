using MediCare.Infrastructure;
using MediCare.Infrastructure.DemoData;
using MediCare.Infrastructure.Identity;
using MediCare.Web.Filters;
using MediCare.Web.Middleware;
using MediCare.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddMediCare(builder.Configuration);
builder.Services.ConfigureApplicationCookie(options => AuthCookiePolicy.Apply(options.Cookie, builder.Environment.IsDevelopment()));
builder.Services.AddMediCareRateLimiting(RateLimitingOptions.Load(builder.Configuration));
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddAuthorization();

var app = builder.Build();

await InitialAdminSeeder.SeedAsync(app.Services, InitialAdminSettings.FromConfiguration(builder.Configuration));

// `dotnet run -- --seed-demo` (or the "seed-demo-data" launch profile) adds the demo data set and exits.
if (args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("Demo data can be seeded only in the Development environment.");
    var demo = await DemoDataSeeder.SeedAsync(app.Services);
    Console.WriteLine(demo.Summary);
    Console.WriteLine((await DemoTodayRefresher.RefreshAsync(app.Services)).Summary);
    return;
}

// `dotnet run -- --seed-demo-today` puts a fresh live queue on today's board for the demo doctors and exits.
if (args.Contains("--seed-demo-today", StringComparer.OrdinalIgnoreCase))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("Demo data can be seeded only in the Development environment.");
    Console.WriteLine((await DemoTodayRefresher.RefreshAsync(app.Services)).Summary);
    return;
}

app.UseMiddleware<SecurityHeadersMiddleware>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

public partial class Program { }
