using MediCare.Application;
using MediCare.Application.Common.Security;
using MediCare.Application.Interfaces.Identity;
using MediCare.Application.Interfaces.Services;
using MediCare.Infrastructure;
using MediCare.Infrastructure.Persistence;
using MediCare.Web.Authorization;
using MediCare.Web.Middleware;
using MediCare.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAppUrlProvider, AppUrlProvider>();
builder.Services.AddScoped<IAuthorizationHandler, ApprovedDoctorAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(Roles.Admin));
    options.AddPolicy(Policies.PatientOnly, policy => policy.RequireRole(Roles.Patient));
    options.AddPolicy(Policies.ApprovedDoctor, policy =>
    {
        policy.RequireRole(Roles.Doctor);
        policy.AddRequirements(new ApprovedDoctorRequirement());
    });
});

builder.Services.AddControllersWithViews(options =>
{
    // Every state-changing request must carry a valid antiforgery token.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<ActiveUserMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

if (builder.Configuration.GetValue("Seed:Enabled", true))
{
    var seedOptions = builder.Configuration.GetSection("Seed").Get<SeedOptions>() ?? new SeedOptions();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MediCare.Seeding");
    await DatabaseSeeder.SeedAsync(app.Services, seedOptions, logger);
}

app.Run();
