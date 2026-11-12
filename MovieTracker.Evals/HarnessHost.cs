using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MovieTracker.Evals;

public sealed class HarnessHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<global::Program> factory;

    public HarnessHost()
    {
        this.factory = new WebApplicationFactory<global::Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    public HttpClient CreateClient()
    {
        return this.factory.CreateClient();
    }

    public IConfiguration Configuration
    {
        get { return this.factory.Services.GetRequiredService<IConfiguration>(); }
    }

    public async ValueTask DisposeAsync()
    {
        await this.factory.DisposeAsync();
    }
}
