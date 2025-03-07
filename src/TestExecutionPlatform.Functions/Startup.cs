using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using Microsoft.Identity.Web;
using System;
using System.IO;
using TestExecutionPlatform.Core.Services;
using TestExecutionPlatform.Core.Services.Containers;
using TestExecutionPlatform.Core.Services.Messaging;
using TestExecutionPlatform.Core.Services.Storage;
using TestExecutionPlatform.Core.Services.Monitoring;
using System.Net.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;

[assembly: FunctionsStartup(typeof(TestExecutionPlatform.Functions.Startup))]

namespace TestExecutionPlatform.Functions
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            builder.Services.AddSingleton<IConfiguration>(configuration);

            // Add HttpClient for webhooks
            builder.Services.AddHttpClient();

            // Configure authentication
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationDefaults)
                .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

            // Configure authorization
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("TestExecutionAdmin", policy =>
                    policy.RequireRole("TestExecutionAdmin"));

                options.AddPolicy("LobAccess", policy =>
                    policy.RequireAssertion(context =>
                        context.User.HasClaim(c =>
                            c.Type == "lob_id" &&
                            !string.IsNullOrEmpty(c.Value))));
            });

            // Add Application Insights
            builder.Services.AddApplicationInsightsTelemetry();

            // Add Configuration Service
            builder.Services.AddSingleton<ConfigurationService>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ConfigurationService>();
                string sqlConnectionString = configuration["SqlConnectionString"];

                return new ConfigurationService(sqlConnectionString, logger);
            });

            // Create and register the appropriate Kubernetes provider
            builder.Services.AddSingleton<IKubernetesProvider>(serviceProvider =>
            {
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var configService = serviceProvider.GetRequiredService<ConfigurationService>();
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<IKubernetesProvider>();

                string provider = configuration["KubernetesConfig:Provider"] ?? "AKS";
                string kubeConfigPath = configuration["KubernetesConfig:KubeConfigPath"];

                if (provider.Equals("aks", StringComparison.OrdinalIgnoreCase))
                {
                    return new AksKubernetesProvider(kubeConfigPath, configService);
                }
                else if (provider.Equals("openshift", StringComparison.OrdinalIgnoreCase))
                {
                    return new OpenShiftKubernetesProvider(kubeConfigPath, configService);
                }
                else
                {
                    throw new ArgumentException($"Unsupported Kubernetes provider: {provider}");
                }
            });

            // Add namespace manager
            builder.Services.AddSingleton<NamespaceManager>();

            // Add Alert Service
            builder.Services.AddSingleton<AlertService>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<AlertService>();
                var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();
                var configService = serviceProvider.GetRequiredService<ConfigurationService>();
                var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                string sendGridApiKey = configuration["Notifications:SendGrid:ApiKey"];
                string alertEmailSender = configuration["Notifications:SendGrid:SenderEmail"];

                return new AlertService(
                    configService,
                    telemetryClient,
                    logger,
                    httpClientFactory.CreateClient(),
                    sendGridApiKey,
                    alertEmailSender);
            });

            // Add Reporting Service
            builder.Services.AddSingleton<ReportingService>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ReportingService>();
                string sqlConnectionString = configuration["SqlConnectionString"];

                return new ReportingService(sqlConnectionString, logger);
            });

            // Add Monitoring Service
            builder.Services.AddSingleton<MonitoringService>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<MonitoringService>();
                var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();
                var kubernetesProvider = serviceProvider.GetRequiredService<IKubernetesProvider>();
                var configService = serviceProvider.GetRequiredService<ConfigurationService>();
                var alertService = serviceProvider.GetRequiredService<AlertService>();

                return new MonitoringService(
                    telemetryClient,
                    kubernetesProvider,
                    configService,
                    alertService,
                    logger);
            });

            // Add Test Result Storage Service
            builder.Services.AddSingleton<TestResultStorageService>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<TestResultStorageService>();
                var configService = serviceProvider.GetRequiredService<ConfigurationService>();
                string storageConnectionString = configuration["Storage:ConnectionString"];
                string containerName = configuration["Storage:TestResultsContainer"] ?? "test-results";

                return new TestResultStorageService(storageConnectionString, containerName, logger, configService);
            });

            // Add Messaging Service
            builder.Services.AddSingleton<IMessagingService>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<KafkaMessagingService>();
                string provider = configuration["Messaging:Provider"] ?? "Kafka";
                string bootstrapServers = configuration["Messaging:Kafka:BootstrapServers"];
                string testResultsTopic = configuration["Messaging:Kafka:TestResultsTopic"];

                if (provider.Equals("Kafka", StringComparison.OrdinalIgnoreCase))
                {
                    return new KafkaMessagingService(bootstrapServers, testResultsTopic, logger);
                }
                else
                {
                    return new MockMessagingService(logger);
                }
            });

            // Add Scheduling Service
            builder.Services.AddSingleton<SchedulingService>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<SchedulingService>();
                string sqlConnectionString = configuration["SqlConnectionString"];

                return new SchedulingService(sqlConnectionString, logger);
            });

            // Add Job Tracking Service
            builder.Services.AddSingleton<JobTrackingService>(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<JobTrackingService>();
                string sqlConnectionString = configuration["SqlConnectionString"];
                var messagingService = serviceProvider.GetRequiredService<IMessagingService>();
                var storageService = serviceProvider.GetRequiredService<TestResultStorageService>();
                var monitoringService = serviceProvider.GetRequiredService<MonitoringService>();

                return new JobTrackingService(sqlConnectionString, messagingService, storageService, monitoringService, logger);
            });

            // Configure TestExecutionService
            builder.Services.AddSingleton<TestExecutionService>(serviceProvider =>
            {
                var provider = serviceProvider.GetRequiredService<IKubernetesProvider>();
                var namespaceManager = serviceProvider.GetRequiredService<NamespaceManager>();
                var telemetry = serviceProvider.GetRequiredService<TelemetryClient>();
                var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<TestExecutionService>();
                string containerRegistry = configuration["KubernetesConfig:ContainerRegistry"];

                return new TestExecutionService(provider, namespaceManager, containerRegistry, telemetry, logger);
            });
        }
    }
}