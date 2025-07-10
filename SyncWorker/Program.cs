using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncWorker;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<Worker>();
        services.Configure<HostOptions>(options =>
        {
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        });
    })
    .Build();

host.Run();
