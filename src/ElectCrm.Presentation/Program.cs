using ElectCrm.Application.DependencyInjection;
using ElectCrm.Domain.DependencyInjection;
using ElectCrm.Infrastructure.DependencyInjection;
using ElectCrm.Presentation.Components;
using ElectCrm.Presentation.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDomainServices();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddPresentationServices();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
