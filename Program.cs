using ListenLense.Services;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// 1) Add services to DI
builder.Services.AddControllersWithViews();

// 2) Register custom services
builder.Services.AddSingleton<WorkspaceService>();
builder.Services.AddSingleton<GoogleTTSService>();

var app = builder.Build();

// 3) Handle errors in production
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// 4) Serve static files from the default wwwroot
app.UseStaticFiles();

// 5) Also serve files from App_Data/Workspaces under a custom URL path
var workspacesPath = Path.Combine(app.Environment.ContentRootPath, "App_Data", "Workspaces");
if (Directory.Exists(workspacesPath))
{
    // Note: This will serve everything in the "Workspaces" folder
    // at the URL path "/workspace-files"
    app.UseStaticFiles(
        new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(workspacesPath),
            RequestPath = "/workspace-files"
        }
    );
}

// 6) Map default MVC route
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
