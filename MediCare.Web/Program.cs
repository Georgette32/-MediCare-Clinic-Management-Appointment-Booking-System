using MediCare.Infrastructure;
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
