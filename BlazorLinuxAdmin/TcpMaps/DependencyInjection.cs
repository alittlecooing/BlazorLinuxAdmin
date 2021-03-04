namespace Microsoft.Extensions.DependencyInjection
{
    using BlazorLinuxAdmin.TcpMaps;

    public static class TcpMapsBuilder
    {
        public static IServiceCollection AddTcpMaps (this IServiceCollection services)
        {
            TcpMapService.AddTcpMaps(services);
            return services;
        }

    }
}
