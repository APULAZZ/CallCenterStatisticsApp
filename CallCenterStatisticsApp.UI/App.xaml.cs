using CallCenterStatisticsApp.Data;
using CallCenterStatisticsApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace CallCenterStatisticsApp.UI;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlServer(
                        @"Server=SQL;Database=CallCenterStatisticsDb;Trusted_Connection=True;TrustServerCertificate=True;"));

                services.AddSingleton(new MangoApiOptions
                {
                    BaseUrl = "https://app.mango-office.ru",
                    ApiKey = "aoijryxexlw39spfy2cat6ekhafukfhk",
                    ApiSalt = "7aa0b02auj5bja1lno57rr9hphz1q9jt"
                });

                services.AddHttpClient<IMangoApiClient, MangoApiClient>();
                services.AddScoped<MangoDirectorySyncService>();
                services.AddScoped<MangoCallImportService>();
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
                services.AddTransient<MainWindow>();
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