// Copyright (c) Microsoft. All rights reserved.

using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Google.Cloud.SecretManager.V1;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

namespace CopilotChat.Shared;

internal static class ConfigurationBuilderExtensions
{
    // ASP.NET env var
    private const string AspnetEnvVar = "ASPNETCORE_ENVIRONMENT";

    public static void AddKMConfigurationSources(
        this IConfigurationBuilder builder,
        bool useAppSettingsFiles = true,
        bool useEnvVars = true,
        bool useSecretManager = true,
        bool useGcpSecretManager = true,
        string? settingsDirectory = null)
    {
        Console.WriteLine("Starting configuration load...");

        // Load env var name, either Development or Production
        var env = Environment.GetEnvironmentVariable(AspnetEnvVar) ?? string.Empty;

        // Detect the folder containing configuration files
        settingsDirectory ??= Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                              ?? Directory.GetCurrentDirectory();
        builder.SetBasePath(settingsDirectory);

        // Add configuration files as sources
        if (useAppSettingsFiles)
        {
            // Add appsettings.json, typically used for default settings, without credentials
            var main = Path.Join(settingsDirectory, "appsettings.json");
            if (!File.Exists(main))
            {
                throw new FileNotFoundException($"appsettings.json not found. Directory: {settingsDirectory}");
            }

            builder.AddJsonFile(main, optional: false);

            // Add appsettings.development.json, used for local overrides and credentials
            if (env.Equals("development", StringComparison.OrdinalIgnoreCase))
            {
                var f1 = Path.Join(settingsDirectory, "appsettings.development.json");
                var f2 = Path.Join(settingsDirectory, "appsettings.Development.json");
                if (File.Exists(f1))
                {
                    builder.AddJsonFile(f1, optional: false);
                }
                else if (File.Exists(f2))
                {
                    builder.AddJsonFile(f2, optional: false);
                }
            }

            // Add appsettings.production.json, used for production settings and credentials
            if (env.Equals("production", StringComparison.OrdinalIgnoreCase))
            {
                var f1 = Path.Join(settingsDirectory, "appsettings.production.json");
                var f2 = Path.Join(settingsDirectory, "appsettings.Production.json");
                if (File.Exists(f1))
                {
                    builder.AddJsonFile(f1, optional: false);
                }
                else if (File.Exists(f2))
                {
                    builder.AddJsonFile(f2, optional: false);
                }
            }
        }

        // Add Secret Manager as source
        if (useSecretManager)
        {
            // GetEntryAssembly method can return null if the library is loaded
            // from an unmanaged application, in which case UserSecrets are not supported.
            var entryAssembly = Assembly.GetEntryAssembly();

            // Support for user secrets. Secret Manager doesn't encrypt the stored secrets and
            // shouldn't be treated as a trusted store. It's for development purposes only.
            // see: https://learn.microsoft.com/aspnet/core/security/app-secrets?#secret-manager
            if (entryAssembly != null && env.Equals("development", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddUserSecrets(entryAssembly, optional: true);
            }
        }

        // Add GCP Secret Manager in production environment
        if (useGcpSecretManager && env.Equals("production", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Console.WriteLine("Attempting to connect to GCP Secret Manager...");
                var secretManagerClient = SecretManagerServiceClient.Create();
                var config = builder.Build();
                var projectId = config["GCP:ProjectId"] ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");

                Console.WriteLine($"Project ID: {projectId}");

                if (!string.IsNullOrEmpty(projectId))
                {
                    // Add each secret you want to load
                    var secrets = new Dictionary<string, string>
                    {
                        { "KernelMemory:Services:OpenAI:APIKey", "openai-chat-copilot-apikey" }  // Map config key to secret name
                    };

                    foreach (var secret in secrets)
                    {
                        try
                        {
                            var secretName = $"projects/{projectId}/secrets/{secret.Value}/versions/latest";
                            Console.WriteLine($"Attempting to access secret: {secretName}");
                            var secretValue = secretManagerClient.AccessSecretVersion(secretName);
                            var value = secretValue.Payload.Data.ToStringUtf8();
                            Console.WriteLine("Successfully retrieved secret");

                            builder.AddInMemoryCollection(new[]
                            {
                        new KeyValuePair<string, string?>(secret.Key, value)
                    });

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error accessing secret: {ex.Message}");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - this allows the application to still start
                // even if GCP Secret Manager is not accessible
                Console.WriteLine($"Failed to load GCP Secrets: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        // Add environment variables as source.
        // Environment variables can override all the settings provided by the previous sources.
        if (useEnvVars)
        {
            builder.AddEnvironmentVariables();

            // Add this debug code
            var config = builder.Build();
            Console.WriteLine($"OpenAI:APIKey value: {config["OpenAI:APIKey"]}");
            Console.WriteLine("Environment Variables:");
            foreach (var envVar in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
            {
                Console.WriteLine($"{envVar.Key} = {envVar.Value}");
            }
        }
    }
}
