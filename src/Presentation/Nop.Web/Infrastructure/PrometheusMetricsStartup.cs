using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Web.Infrastructure.Metrics;
using Prometheus;

namespace Nop.Web.Infrastructure;

public partial class PrometheusMetricsStartup : INopStartup
{
    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<IntegrationMetricsHostedService>();
    }

    public virtual void Configure(IApplicationBuilder application)
    {
        application.UseMetricServer();
        application.UseHttpMetrics();
    }

    public int Order => 450;
}
