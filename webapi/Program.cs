﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CopilotChat.WebApi.Controllers;
using CopilotChat.WebApi.Extensions;
using CopilotChat.WebApi.Hubs;
using CopilotChat.WebApi.Services;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace CopilotChat.WebApi;

/// <summary>
/// Chat Copilot Service
/// </summary>
public sealed class Program
{
    /// <summary>
    /// Entry point
    /// </summary>
    /// <param name="args">Web application command-line arguments.</param>
    // ReSharper disable once InconsistentNaming
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Load in configuration settings from appsettings.json, user-secrets, key vaults, etc...
        builder.Host.AddConfiguration();
        builder.WebHost.UseUrls(); // Disables endpoint override warning message when using IConfiguration for Kestrel endpoint.

        // Add in configuration options and required services.
        builder.Services
            .AddSingleton<ILogger>(sp => sp.GetRequiredService<ILogger<Program>>()) // some services require an un-templated ILogger
            .AddOptions(builder.Configuration)
            .AddPersistentChatStore()
            .AddPlugins(builder.Configuration)
            .AddChatCopilotAuthentication(builder.Configuration)
            .AddChatCopilotAuthorization();

        // Configure and add semantic services
        builder.AddBotConfig();
        builder.AddSemanticKernelServices();

        builder.Services.AddWeaviateVectorStore(options: new() { Endpoint = new Uri("http://localhost:8080/v1/") });

        builder.UseGCPSecrets(options =>
            {
                options.ProjectId = builder.Configuration["GCP:ProjectId"];
                options.SecretMappings = new Dictionary<string, string>
                {
                    // Map configuration paths to secret names
                    ["KernelMemory:Services:OpenAI:APIKey"] = "openai-chat-copilot-apikey"
                };
            });

        builder.AddSemanticMemoryServices();

        // Add SignalR as the real time relay service
        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            options.HandshakeTimeout = TimeSpan.FromSeconds(30);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        });

        // Add CORS policy before other services
        builder.Services.AddCorsPolicy(builder.Configuration);

        // Add AppInsights telemetry
        builder.Services
            .AddHttpContextAccessor()
            .AddApplicationInsightsTelemetry(options => { options.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]; })
            .AddSingleton<ITelemetryInitializer, AppInsightsUserTelemetryInitializerService>()
            .AddLogging(logBuilder => logBuilder.AddApplicationInsights())
            .AddSingleton<ITelemetryService, AppInsightsTelemetryService>();

        TelemetryDebugWriter.IsTracingDisabled = Debugger.IsAttached;

        // Add named HTTP clients for IHttpClientFactory
        builder.Services.AddHttpClient();

        // Add in the rest of the services.
        builder.Services
            .AddMaintenanceServices()
            .AddEndpointsApiExplorer()
            .AddSwaggerGen()
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
        builder.Services.AddHealthChecks();

        // Add these services before the app.Build() call
        builder.Services
            .AddSingleton<StartupFileProcessingService>() // Register as a singleton
            .AddHostedService(sp => sp.GetRequiredService<StartupFileProcessingService>())
            .AddScoped<QuestionGenerationPlugin>()
            .AddSingleton<IQuestionRepository, InMemoryQuestionRepository>()
            .AddScoped<ITranscriptSearchService, TranscriptSearchService>()
            .AddSingleton<DocumentTypeProvider>(sp =>
                new DocumentTypeProvider(
                    builder.Configuration.GetValue<bool>("DocumentProcessing:AllowImageOcr")))
            .AddScoped<VectorStoreController>()
            //.AddHostedService<StartupFileProcessingService>()
            .AddHostedService<StartupQuestionGenerationService>()
            .Configure<JsonProcessingOptions>(builder.Configuration.GetSection("JsonProcessing"));

        // Add configuration section for the JSON files directory
        builder.Services.Configure<JsonProcessingOptions>(builder.Configuration.GetSection("JsonProcessing"));

        // Configure middleware and endpoints
        WebApplication app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<MaintenanceMiddleware>();
        app.MapControllers()
            .RequireAuthorization();
        app.MapHealthChecks("/healthz");

        // Add Chat Copilot hub for real time communication
        app.MapHub<MessageRelayHub>("/messageRelayHub");

        // Enable Swagger for development environments.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();

            // Redirect root URL to Swagger UI URL
            app.MapWhen(
                context => context.Request.Path == "/",
                appBuilder =>
                    appBuilder.Run(
                        async context => await Task.Run(() => context.Response.Redirect("/swagger"))));
        }

        // Start the service
        Task runTask = app.RunAsync();

        // Log the health probe URL for users to validate the service is running.
        try
        {
            string? address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
            app.Services.GetRequiredService<ILogger>().LogInformation("Health probe: {0}/healthz", address);
        }
        catch (ObjectDisposedException)
        {
            // We likely failed startup which disposes 'app.Services' - don't attempt to display the health probe URL.
        }

        // Wait for the service to complete.
        await runTask;
    }
}