namespace FuelFlow.Infra;

/// <summary>
/// FuelFlow Infrastructure Stack - Azure Resources
/// Region: West Europe | App Service + PostgreSQL + Redis + Blob Storage + Key Vault + Monitoring
/// </summary>
public class FuelFlowStack : TerraformStack
{
    public FuelFlowStack(Construct scope, string id, FuelFlowStackConfig config) : base(scope, id)
    {
        // ============================================================================
        // PROVIDER
        // ============================================================================

        _ = new AzurermProvider(this, "azurerm", new AzurermProviderConfig
        {
            Features = new AzurermProviderFeatures(),
            SkipProviderRegistration = true
        });

        DataAzurermClientConfig clientConfig = new(this, "current");

        // ============================================================================
        // RESOURCE GROUP
        // ============================================================================

        ResourceGroup resourceGroup = new(this, "rg", new ResourceGroupConfig
        {
            Name = $"{config.ResourcePrefix}-rg",
            Location = config.Location,
            Tags = config.Tags
        });

        // ============================================================================
        // LOG ANALYTICS WORKSPACE
        // ============================================================================

        LogAnalyticsWorkspace logAnalytics = new(this, "logs", new LogAnalyticsWorkspaceConfig
        {
            Name = $"{config.ResourcePrefix}-logs",
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            Sku = "PerGB2018",
            RetentionInDays = config.LogRetentionDays,
            Tags = config.Tags
        });

        // ============================================================================
        // APPLICATION INSIGHTS
        // ============================================================================

        ApplicationInsights appInsights = new(this, "insights", new ApplicationInsightsConfig
        {
            Name = $"{config.ResourcePrefix}-insights",
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            ApplicationType = "Node.JS",
            WorkspaceId = logAnalytics.Id,
            SamplingPercentage = 0, // Adaptive sampling (0 = auto)
            Tags = config.Tags
        });

        // ============================================================================
        // KEY VAULT
        // ============================================================================

        KeyVault keyVault = new(this, "kv", new KeyVaultConfig
        {
            Name = config.KeyVaultName,
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            TenantId = clientConfig.TenantId,
            SkuName = "standard",
            EnableRbacAuthorization = true,
            SoftDeleteRetentionDays = 7,
            Tags = config.Tags
        });

        // Key Vault secret names
        const string secretNamePostgres = "PostgresConnectionString";
        const string secretNameRedis = "RedisConnectionString";
        const string secretNameStripeSecret = "StripeSecretKey";
        const string secretNameStripeWebhook = "StripeWebhookSecret";
        const string secretNameGemini = "GeminiApiKey";
        const string secretNameTwilioSid = "TwilioAccountSid";
        const string secretNameTwilioToken = "TwilioAuthToken";
        const string secretNameTwilioPhone = "TwilioPhoneNumber";
        const string secretNameSession = "SessionSecret";
        const string secretNameQrEncryption = "QrEncryptionKey";
        const string secretNameStorage = "StorageConnectionString";

        // Helper to create Key Vault reference for App Settings
        string KeyVaultRef(string secretName) => $"@Microsoft.KeyVault(VaultName={keyVault.Name};SecretName={secretName})";

        // ============================================================================
        // STORAGE ACCOUNT (for Blob Storage)
        // ============================================================================

        StorageAccount storageAccount = new(this, "storage", new StorageAccountConfig
        {
            Name = config.StorageAccountName,
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            AccountTier = "Standard",
            AccountReplicationType = config.StorageReplicationType,
            AllowNestedItemsToBePublic = false,
            MinTlsVersion = "TLS1_2",
            BlobProperties = new StorageAccountBlobProperties
            {
                DeleteRetentionPolicy = new StorageAccountBlobPropertiesDeleteRetentionPolicy
                {
                    Days = 7
                }
            },
            Tags = config.Tags
        });

        // Blob container for voucher PDFs
        _ = new StorageContainer(this, "vouchers-container", new StorageContainerConfig
        {
            Name = "vouchers",
            StorageAccountName = storageAccount.Name,
            ContainerAccessType = "private"
        });

        // Blob container for uploads
        _ = new StorageContainer(this, "uploads-container", new StorageContainerConfig
        {
            Name = "uploads",
            StorageAccountName = storageAccount.Name,
            ContainerAccessType = "private"
        });

        // ============================================================================
        // REDIS CACHE
        // ============================================================================

        RedisCache redisCache = new(this, "redis", new RedisCacheConfig
        {
            Name = $"{config.ResourcePrefix}-redis",
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            Capacity = config.RedisCapacity,
            Family = "C",
            SkuName = config.RedisSkuName,
            EnableNonSslPort = false,
            MinimumTlsVersion = "1.2",
            RedisConfiguration = new RedisCacheRedisConfiguration(),
            Tags = config.Tags
        });

        // ============================================================================
        // APP SERVICE PLAN
        // ============================================================================

        ServicePlan appServicePlan = new(this, "plan", new ServicePlanConfig
        {
            Name = $"{config.ResourcePrefix}-plan",
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            OsType = "Linux",
            SkuName = config.AppServiceSkuName,
            Tags = config.Tags
        });

        // ============================================================================
        // APP SERVICE (Linux Web App - Node.js)
        // ============================================================================

        LinuxWebApp appService = new(this, "app", new LinuxWebAppConfig
        {
            Name = $"{config.ResourcePrefix}-app",
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            ServicePlanId = appServicePlan.Id,
            HttpsOnly = true,
            ClientAffinityEnabled = false,
            Identity = new LinuxWebAppIdentity
            {
                Type = "SystemAssigned"
            },
            SiteConfig = new LinuxWebAppSiteConfig
            {
                AlwaysOn = true,
                Http2Enabled = true,
                MinimumTlsVersion = "1.2",
                FtpsState = "Disabled",
                HealthCheckPath = "/api/health",
                HealthCheckEvictionTimeInMin = 5,
                ApplicationStack = new LinuxWebAppSiteConfigApplicationStack
                {
                    NodeVersion = "20-lts"
                },
                // Startup command: run migrations then start server
                AppCommandLine = "npm run start"
            },
            Logs = new LinuxWebAppLogs
            {
                ApplicationLogs = new LinuxWebAppLogsApplicationLogs
                {
                    FileSystemLevel = "Information"
                },
                HttpLogs = new LinuxWebAppLogsHttpLogs
                {
                    FileSystem = new LinuxWebAppLogsHttpLogsFileSystem
                    {
                        RetentionInDays = 7,
                        RetentionInMb = 35
                    }
                }
            },
            AppSettings = new Dictionary<string, string>
            {
                // ===== Azure App Service Configuration =====
                ["WEBSITE_NODE_DEFAULT_VERSION"] = "~20",
                ["SCM_DO_BUILD_DURING_DEPLOYMENT"] = "true",

                // ===== Application Configuration =====
                ["NODE_ENV"] = "production",
                ["PORT"] = "8080", // Azure App Service expects 8080
                ["USE_REFACTORED_ARCHITECTURE"] = "true",
                ["FRONTEND_URL"] = $"https://{config.ResourcePrefix}-app.azurewebsites.net",

                // ===== Session Configuration =====
                ["SESSION_SECRET"] = KeyVaultRef(secretNameSession),
                ["SESSION_SECURE"] = "true", // Enable secure cookies in production

                // ===== Application Insights =====
                ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = appInsights.ConnectionString,
                ["APPLICATIONINSIGHTS_CONNECTIONSTRING"] = appInsights.ConnectionString, // Alias
                ["ApplicationInsightsAgent_EXTENSION_VERSION"] = "~3",

                // ===== Database (from Key Vault) =====
                ["DATABASE_URL"] = KeyVaultRef(secretNamePostgres),

                // ===== Redis (from Key Vault) =====
                ["REDIS_URL"] = KeyVaultRef(secretNameRedis),

                // ===== Stripe =====
                ["STRIPE_SECRET_KEY"] = KeyVaultRef(secretNameStripeSecret),
                ["STRIPE_PUBLISHABLE_KEY"] = config.StripePublishableKey, // Not secret, needed client-side
                ["STRIPE_WEBHOOK_SECRET"] = KeyVaultRef(secretNameStripeWebhook),

                // ===== Gemini AI =====
                ["GEMINI_API_KEY"] = KeyVaultRef(secretNameGemini),

                // ===== Twilio SMS =====
                ["TWILIO_ACCOUNT_SID"] = KeyVaultRef(secretNameTwilioSid),
                ["TWILIO_AUTH_TOKEN"] = KeyVaultRef(secretNameTwilioToken),
                ["TWILIO_PHONE_NUMBER"] = KeyVaultRef(secretNameTwilioPhone),

                // ===== QR Encryption =====
                ["QR_ENCRYPTION_KEY"] = KeyVaultRef(secretNameQrEncryption),

                // ===== Azure Storage =====
                ["AZURE_STORAGE_CONNECTION_STRING"] = KeyVaultRef(secretNameStorage),
                ["AZURE_STORAGE_CONTAINER_VOUCHERS"] = "vouchers",
                ["AZURE_STORAGE_CONTAINER_UPLOADS"] = "uploads"
            },
            Tags = config.Tags
        });

        // ============================================================================
        // KEY VAULT ACCESS FOR APP SERVICE
        // ============================================================================

        // Grant App Service managed identity access to read Key Vault secrets
        _ = new RoleAssignment(this, "kv-app-access", new RoleAssignmentConfig
        {
            Scope = keyVault.Id,
            RoleDefinitionName = "Key Vault Secrets User",
            PrincipalId = appService.Identity.PrincipalId
        });

        // ============================================================================
        // POSTGRESQL FLEXIBLE SERVER
        // ============================================================================

        PostgresqlFlexibleServer postgresServer = new(this, "db", new PostgresqlFlexibleServerConfig
        {
            Name = $"{config.ResourcePrefix}-db",
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            Version = "16",
            AdministratorLogin = config.PostgresAdminLogin,
            AdministratorPassword = config.PostgresAdminPassword,
            SkuName = "B_Standard_B1ms",
            StorageMb = config.PostgresStorageMb,
            BackupRetentionDays = 7,
            GeoRedundantBackupEnabled = false,
            Zone = "1",
            Tags = config.Tags
        });

        // PostgreSQL Database
        _ = new PostgresqlFlexibleServerDatabase(this, "db-fuelflow", new PostgresqlFlexibleServerDatabaseConfig
        {
            Name = "fuelflow",
            ServerId = postgresServer.Id,
            Charset = "UTF8",
            Collation = "en_US.utf8"
        });

        // PostgreSQL Firewall - Allow Azure Services
        _ = new PostgresqlFlexibleServerFirewallRule(this, "db-fw-azure", new PostgresqlFlexibleServerFirewallRuleConfig
        {
            Name = "AllowAzureServices",
            ServerId = postgresServer.Id,
            StartIpAddress = "0.0.0.0",
            EndIpAddress = "0.0.0.0"
        });

        // PostgreSQL Firewall - Allow specified IPs
        if (!string.IsNullOrEmpty(config.AllowedIpAddresses))
        {
            if (config.AllowedIpAddresses == "0.0.0.0")
            {
                _ = new PostgresqlFlexibleServerFirewallRule(this, "db-fw-allow-all", new PostgresqlFlexibleServerFirewallRuleConfig
                {
                    Name = "AllowAll",
                    ServerId = postgresServer.Id,
                    StartIpAddress = "0.0.0.0",
                    EndIpAddress = "255.255.255.255"
                });
            }
            else
            {
                string[] ips = config.AllowedIpAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < ips.Length; i++)
                {
                    string ip = ips[i].Trim();
                    _ = new PostgresqlFlexibleServerFirewallRule(this, $"db-fw-ip-{i}", new PostgresqlFlexibleServerFirewallRuleConfig
                    {
                        Name = $"AllowIP-{i}",
                        ServerId = postgresServer.Id,
                        StartIpAddress = ip,
                        EndIpAddress = ip
                    });
                }
            }
        }

        // ============================================================================
        // KEY VAULT SECRETS
        // ============================================================================

        // PostgreSQL Connection String
        string postgresConnectionString = $"postgresql://{config.PostgresAdminLogin}:{config.PostgresAdminPassword}@{postgresServer.Fqdn}:5432/fuelflow?sslmode=require";

        _ = new KeyVaultSecret(this, "secret-postgres", new KeyVaultSecretConfig
        {
            Name = secretNamePostgres,
            Value = postgresConnectionString,
            KeyVaultId = keyVault.Id
        });

        // Redis Connection String
        string redisConnectionString = $"rediss://:{redisCache.PrimaryAccessKey}@{redisCache.Hostname}:6380";

        _ = new KeyVaultSecret(this, "secret-redis", new KeyVaultSecretConfig
        {
            Name = secretNameRedis,
            Value = redisConnectionString,
            KeyVaultId = keyVault.Id
        });

        // Stripe Secrets
        _ = new KeyVaultSecret(this, "secret-stripe-key", new KeyVaultSecretConfig
        {
            Name = secretNameStripeSecret,
            Value = config.StripeSecretKey,
            KeyVaultId = keyVault.Id
        });

        _ = new KeyVaultSecret(this, "secret-stripe-webhook", new KeyVaultSecretConfig
        {
            Name = secretNameStripeWebhook,
            Value = config.StripeWebhookSecret,
            KeyVaultId = keyVault.Id
        });

        // Gemini API Key
        _ = new KeyVaultSecret(this, "secret-gemini", new KeyVaultSecretConfig
        {
            Name = secretNameGemini,
            Value = config.GeminiApiKey,
            KeyVaultId = keyVault.Id
        });

        // Twilio Secrets
        _ = new KeyVaultSecret(this, "secret-twilio-sid", new KeyVaultSecretConfig
        {
            Name = secretNameTwilioSid,
            Value = config.TwilioAccountSid,
            KeyVaultId = keyVault.Id
        });

        _ = new KeyVaultSecret(this, "secret-twilio-token", new KeyVaultSecretConfig
        {
            Name = secretNameTwilioToken,
            Value = config.TwilioAuthToken,
            KeyVaultId = keyVault.Id
        });

        _ = new KeyVaultSecret(this, "secret-twilio-phone", new KeyVaultSecretConfig
        {
            Name = secretNameTwilioPhone,
            Value = config.TwilioPhoneNumber,
            KeyVaultId = keyVault.Id
        });

        // Session & Encryption Secrets
        _ = new KeyVaultSecret(this, "secret-session", new KeyVaultSecretConfig
        {
            Name = secretNameSession,
            Value = config.SessionSecret,
            KeyVaultId = keyVault.Id
        });

        _ = new KeyVaultSecret(this, "secret-qr-encryption", new KeyVaultSecretConfig
        {
            Name = secretNameQrEncryption,
            Value = config.QrEncryptionKey,
            KeyVaultId = keyVault.Id
        });

        // Storage Connection String
        _ = new KeyVaultSecret(this, "secret-storage", new KeyVaultSecretConfig
        {
            Name = secretNameStorage,
            Value = storageAccount.PrimaryConnectionString,
            KeyVaultId = keyVault.Id
        });

        // ============================================================================
        // MONITORING & ALERTS
        // ============================================================================

        MonitorActionGroup actionGroup = new(this, "alerts", new MonitorActionGroupConfig
        {
            Name = $"{config.ResourcePrefix}-alerts",
            ResourceGroupName = resourceGroup.Name,
            ShortName = "FuelAlerts",
            Enabled = true,
            EmailReceiver = new[]
            {
                new MonitorActionGroupEmailReceiver
                {
                    Name = "Admin",
                    EmailAddress = config.AlertEmail,
                    UseCommonAlertSchema = true
                }
            },
            Tags = config.Tags
        });

        // Alert: HTTP 5xx errors > 10 in 5 min
        _ = new MonitorMetricAlert(this, "alert-5xx", new MonitorMetricAlertConfig
        {
            Name = $"{config.ResourcePrefix}-alert-5xx",
            ResourceGroupName = resourceGroup.Name,
            Scopes = new[] { appService.Id },
            Severity = 0, // Critical
            Frequency = "PT1M",
            WindowSize = "PT5M",
            Criteria = new[]
            {
                new MonitorMetricAlertCriteria
                {
                    MetricNamespace = "Microsoft.Web/sites",
                    MetricName = "Http5xx",
                    Aggregation = "Total",
                    Operator = "GreaterThan",
                    Threshold = 10
                }
            },
            Action = new[]
            {
                new MonitorMetricAlertAction { ActionGroupId = actionGroup.Id }
            },
            Tags = config.Tags
        });

        // Alert: High CPU > 80%
        _ = new MonitorMetricAlert(this, "alert-cpu", new MonitorMetricAlertConfig
        {
            Name = $"{config.ResourcePrefix}-alert-cpu",
            ResourceGroupName = resourceGroup.Name,
            Scopes = new[] { appServicePlan.Id },
            Severity = 2, // Warning
            Frequency = "PT1M",
            WindowSize = "PT5M",
            Criteria = new[]
            {
                new MonitorMetricAlertCriteria
                {
                    MetricNamespace = "Microsoft.Web/serverfarms",
                    MetricName = "CpuPercentage",
                    Aggregation = "Average",
                    Operator = "GreaterThan",
                    Threshold = 80
                }
            },
            Action = new[]
            {
                new MonitorMetricAlertAction { ActionGroupId = actionGroup.Id }
            },
            Tags = config.Tags
        });

        // Alert: High Memory > 85%
        _ = new MonitorMetricAlert(this, "alert-memory", new MonitorMetricAlertConfig
        {
            Name = $"{config.ResourcePrefix}-alert-memory",
            ResourceGroupName = resourceGroup.Name,
            Scopes = new[] { appServicePlan.Id },
            Severity = 2, // Warning
            Frequency = "PT1M",
            WindowSize = "PT5M",
            Criteria = new[]
            {
                new MonitorMetricAlertCriteria
                {
                    MetricNamespace = "Microsoft.Web/serverfarms",
                    MetricName = "MemoryPercentage",
                    Aggregation = "Average",
                    Operator = "GreaterThan",
                    Threshold = 85
                }
            },
            Action = new[]
            {
                new MonitorMetricAlertAction { ActionGroupId = actionGroup.Id }
            },
            Tags = config.Tags
        });

        // Alert: Slow Response Time > 2s
        _ = new MonitorMetricAlert(this, "alert-response", new MonitorMetricAlertConfig
        {
            Name = $"{config.ResourcePrefix}-alert-response",
            ResourceGroupName = resourceGroup.Name,
            Scopes = new[] { appService.Id },
            Severity = 2, // Warning
            Frequency = "PT1M",
            WindowSize = "PT5M",
            Criteria = new[]
            {
                new MonitorMetricAlertCriteria
                {
                    MetricNamespace = "Microsoft.Web/sites",
                    MetricName = "HttpResponseTime",
                    Aggregation = "Average",
                    Operator = "GreaterThan",
                    Threshold = 2
                }
            },
            Action = new[]
            {
                new MonitorMetricAlertAction { ActionGroupId = actionGroup.Id }
            },
            Tags = config.Tags
        });

        // Alert: Database Connection Failed
        _ = new MonitorMetricAlert(this, "alert-db", new MonitorMetricAlertConfig
        {
            Name = $"{config.ResourcePrefix}-alert-db",
            ResourceGroupName = resourceGroup.Name,
            Scopes = new[] { postgresServer.Id },
            Severity = 0, // Critical
            Frequency = "PT1M",
            WindowSize = "PT5M",
            Criteria = new[]
            {
                new MonitorMetricAlertCriteria
                {
                    MetricNamespace = "Microsoft.DBforPostgreSQL/flexibleServers",
                    MetricName = "connections_failed",
                    Aggregation = "Total",
                    Operator = "GreaterThan",
                    Threshold = 5
                }
            },
            Action = new[]
            {
                new MonitorMetricAlertAction { ActionGroupId = actionGroup.Id }
            },
            Tags = config.Tags
        });

        // Alert: Redis Connection Failed
        _ = new MonitorMetricAlert(this, "alert-redis", new MonitorMetricAlertConfig
        {
            Name = $"{config.ResourcePrefix}-alert-redis",
            ResourceGroupName = resourceGroup.Name,
            Scopes = new[] { redisCache.Id },
            Severity = 0, // Critical
            Frequency = "PT1M",
            WindowSize = "PT5M",
            Criteria = new[]
            {
                new MonitorMetricAlertCriteria
                {
                    MetricNamespace = "Microsoft.Cache/redis",
                    MetricName = "errors",
                    Aggregation = "Total",
                    Operator = "GreaterThan",
                    Threshold = 5
                }
            },
            Action = new[]
            {
                new MonitorMetricAlertAction { ActionGroupId = actionGroup.Id }
            },
            Tags = config.Tags
        });

        // ============================================================================
        // OUTPUTS
        // ============================================================================

        _ = new TerraformOutput(this, "app_service_name", new TerraformOutputConfig
        {
            Value = appService.Name
        });

        _ = new TerraformOutput(this, "app_service_url", new TerraformOutputConfig
        {
            Value = $"https://{appService.DefaultHostname}"
        });

        _ = new TerraformOutput(this, "key_vault_name", new TerraformOutputConfig
        {
            Value = keyVault.Name
        });

        _ = new TerraformOutput(this, "key_vault_uri", new TerraformOutputConfig
        {
            Value = keyVault.VaultUri
        });

        _ = new TerraformOutput(this, "postgres_server_name", new TerraformOutputConfig
        {
            Value = postgresServer.Name
        });

        _ = new TerraformOutput(this, "postgres_server_fqdn", new TerraformOutputConfig
        {
            Value = postgresServer.Fqdn
        });

        _ = new TerraformOutput(this, "redis_hostname", new TerraformOutputConfig
        {
            Value = redisCache.Hostname
        });

        _ = new TerraformOutput(this, "storage_account_name", new TerraformOutputConfig
        {
            Value = storageAccount.Name
        });

        _ = new TerraformOutput(this, "app_insights_connection_string", new TerraformOutputConfig
        {
            Value = appInsights.ConnectionString,
            Sensitive = true
        });

        _ = new TerraformOutput(this, "log_analytics_workspace_id", new TerraformOutputConfig
        {
            Value = logAnalytics.Id
        });

        _ = new TerraformOutput(this, "resource_group_name", new TerraformOutputConfig
        {
            Value = resourceGroup.Name
        });
    }
}
