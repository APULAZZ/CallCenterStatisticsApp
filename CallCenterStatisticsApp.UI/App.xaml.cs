using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using CallCenterStatisticsApp.UI.Views;

namespace CallCenterStatisticsApp.UI;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
            })
            .ConfigureServices((context, services) =>
            {
                var connectionString = context.Configuration.GetConnectionString("CallCenterStatistics")
                    ?? @"Server=(localdb)\MSSQLLocalDB;Database=CallCenterStatisticsDb;Trusted_Connection=True;TrustServerCertificate=True;";

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlServer(connectionString));

                services.AddSingleton(new MangoApiOptions
                {
                    BaseUrl = context.Configuration["MangoApi:BaseUrl"] ?? "https://app.mango-office.ru",
                    ApiKey = context.Configuration["MangoApi:ApiKey"]
                        ?? Environment.GetEnvironmentVariable("MANGO_API_KEY")
                        ?? string.Empty,
                    ApiSalt = context.Configuration["MangoApi:ApiSalt"]
                        ?? Environment.GetEnvironmentVariable("MANGO_API_SALT")
                        ?? string.Empty
                });
                services.AddSingleton<ThemeService>();
                services.AddSingleton<BusyService>();

                services.AddHttpClient<IMangoApiClient, MangoApiClient>();
                services.AddScoped<MangoDirectorySyncService>();
                services.AddScoped<MangoCallImportService>();
                services.AddScoped<MangoSynchronizationService>();
                services.AddScoped<CallStatisticsService>();
                services.AddTransient<MangoImportWindow>();
                services.AddTransient<EmployeeStatisticsWindow>();
                services.AddTransient<GroupStatisticsWindow>();
                services.AddTransient<MangoApiTestWindow>();
                services.AddTransient<CallLogWindow>();
                services.AddTransient<EmployeeStatisticsWindow>();
                services.AddTransient<GroupStatisticsWindow>();
                services.AddScoped<MangoCallTopicEnrichmentService>();
                services.AddTransient<TopicEnrichmentWindow>();
                services.AddTransient<DashboardPage>();
                services.AddTransient<JournalPage>();
                services.AddTransient<EmployeeStatisticsPage>();
                services.AddTransient<GroupStatisticsPage>();
                services.AddTransient<ImportPage>();
                services.AddTransient<TopicEnrichmentPage>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<MainWindow>();
                services.AddTransient<CallDetailsWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost.StartAsync();

        using (var scope = AppHost.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await AppHost.StopAsync();
        AppHost.Dispose();
        base.OnExit(e);
    }
}
