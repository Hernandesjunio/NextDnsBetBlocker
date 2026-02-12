using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NextDnsBetBlocker.Core.Interfaces;
using NextDnsBetBlocker.Core.Services;
using NextDnsBetBlocker.FunctionApp;

[assembly: FunctionsStartup(typeof(NextDnsBetBlocker.FunctionApp.Startup))]

namespace NextDnsBetBlocker.FunctionApp;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        var context = builder.GetContext();
        var config = context.Configuration;

        var settings = new FunctionAppSettings();
        config.Bind(settings);

        // HttpClientFactory
        builder.Services.AddHttpClient<INextDnsClient, NextDnsClient>();

        // Azure Storage
        var tableClient = new TableClient(
            new Uri(config.GetValue<string>("TableStorageUri") ?? ""),
            "BlockedDomains",
            new Azure.Data.Tables.TableSharedKeyCredential(
                config.GetValue<string>("StorageAccountName") ?? "",
                config.GetValue<string>("StorageAccountKey") ?? ""));

        var checkpointTableClient = new TableClient(
            new Uri(config.GetValue<string>("TableStorageUri") ?? ""),
            "AgentState",
            new Azure.Data.Tables.TableSharedKeyCredential(
                config.GetValue<string>("StorageAccountName") ?? "",
                config.GetValue<string>("StorageAccountKey") ?? ""));

        builder.Services.AddSingleton(tableClient);
        builder.Services.AddSingleton<IBlockedDomainStore>(sp => 
            new BlockedDomainStore(tableClient, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BlockedDomainStore>>()));
        builder.Services.AddSingleton<ICheckpointStore>(sp => 
            new CheckpointStore(checkpointTableClient, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CheckpointStore>>()));

        // Blob Storage
        var blobClient = new BlobContainerClient(
            new Uri(config.GetValue<string>("BlobStorageUri") ?? ""),
            new Azure.Identity.DefaultAzureCredential());

        builder.Services.AddSingleton(blobClient);
        builder.Services.AddSingleton<IHageziProvider>(sp => 
            new HageziProvider(
                blobClient,
                Path.Combine(Path.GetTempPath(), "hagezi-gambling-domains.txt"),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HageziProvider>>()));

        // Allowlist (from blob or temp)
        var allowlistPath = Path.Combine(Path.GetTempPath(), "allowlist.txt");
        builder.Services.AddSingleton<IAllowlistProvider>(sp =>
            new AllowlistProvider(allowlistPath, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AllowlistProvider>>()));

        // Classifier and Pipeline
        builder.Services.AddSingleton<IBetClassifier, BetClassifier>();
        builder.Services.AddSingleton<IBetBlockerPipeline>(sp =>
            new BetBlockerPipeline(
                sp.GetRequiredService<INextDnsClient>(),
                sp.GetRequiredService<ICheckpointStore>(),
                sp.GetRequiredService<IBlockedDomainStore>(),
                sp.GetRequiredService<IHageziProvider>(),
                sp.GetRequiredService<IAllowlistProvider>(),
                sp.GetRequiredService<IBetClassifier>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BetBlockerPipeline>>(),
                settings.RateLimitPerSecond));

        builder.Services.AddSingleton(settings);
    }
}
