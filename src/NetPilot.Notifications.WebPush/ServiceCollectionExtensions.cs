using Lib.Net.Http.WebPush;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetPilot.Core.Notifications;

namespace NetPilot.Notifications.WebPush;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNetPilotWebPush(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new NotificationOptions();
        configuration.GetSection("NetPilot:Notifications").Bind(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton(sp => new PushServiceClient(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<WebPushChannel>();
        services.AddSingleton<INotificationChannel>(sp => sp.GetRequiredService<WebPushChannel>());
        services.AddSingleton<NotificationDispatcher>();
        services.AddSingleton<NotificationService>();
        return services;
    }
}
