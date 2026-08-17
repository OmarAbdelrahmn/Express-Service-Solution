using Express_Service;
using Express_Service.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace Accounting.Tests;

public sealed class SwaggerGenerationTests
{
    [Fact]
    public async Task SwaggerV1_GeneratesForEveryController()
    {
        var services = new ServiceCollection();
        var environment = new TestWebHostEnvironment();
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton<IHostEnvironment>(environment);
        services
            .AddControllers()
            .AddApplicationPart(typeof(RiderSalaryImportController).Assembly);
        services.AddEndpointsApiExplorer();
        services.AddSwagger();

        await using var provider = services.BuildServiceProvider();
        var swaggerProvider = provider.GetRequiredService<IAsyncSwaggerProvider>();

        var document = await swaggerProvider.GetSwaggerAsync("v1");

        Assert.Contains("/api/RiderSalaryImport", document.Paths.Keys);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = typeof(RiderSalaryImportController).Assembly.GetName().Name!;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
