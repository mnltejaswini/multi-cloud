// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.Azure.ContainerRegistry;
using Microsoft.Azure.Management.ContainerRegistry.Fluent;
using Microsoft.Azure.Management.ContainerRegistry.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace acr_replicate_app
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static async System.Threading.Tasks.Task Run(
            [EventGridTrigger] string eventGridEventAsString, 
            ExecutionContext context, 
            ILogger log)
        {
            if (string.IsNullOrWhiteSpace(eventGridEventAsString))
            {
                log.LogError("Received a null or empty event.");
                return;
            }

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddUserSecrets(Assembly.GetExecutingAssembly()) // application setting on local box
                .AddEnvironmentVariables() // application settings from Azure
                .Build();

            var appConfiguration = config.Get<AppConfiguration>() ?? throw new ArgumentNullException("Configuration is required");

            appConfiguration.Validate();

            var httpContent = new BinaryData(eventGridEventAsString).ToStream();
            var eventGridEvent = EventGridEvent.Parse(BinaryData.FromStream(httpContent));
            if (eventGridEvent.TryGetSystemEventData(out object systemEvent))
            {
                switch (systemEvent)
                {
                    case ContainerRegistryImagePushedEventData pushEvent:
                        log.LogInformation($"Received a push event for artifact '{pushEvent.Target.Repository}:{pushEvent.Target.Tag}' of registry '{pushEvent.Request.Host}'");
                        await ImportImageAsync(pushEvent, appConfiguration, log);
                        break;
                    case ContainerRegistryImageDeletedEventData deletedEvent:
                        log.LogInformation($"Received a delete event for artifact '{deletedEvent.Target.Repository}:{deletedEvent.Target.Digest}' of registry '{deletedEvent.Request.Host}'");
                        await DeleteImageAsync(deletedEvent, appConfiguration, log);
                        break;
                    default:
                        log.LogWarning($"Received an unexpected ACR event data. Expected a push/delete event. Received '{eventGridEvent.EventType}'");
                        break;
                }
            }
            else
            {
                log.LogError("Could not parse the event to ACR Event schema");
            }
        }

        private static async System.Threading.Tasks.Task ImportImageAsync(
            ContainerRegistryImagePushedEventData pushEvent, 
            AppConfiguration configuration,
            ILogger log)
        {
            // Create the resourceId from the target ACR resourceId string
            var targetACRResourceId = ResourceId.FromString(configuration.TargetACRResourceId);

            // Create Azure credentials to talk to target Cloud using the Active directory application
            var credential = new AzureCredentials(
                new ServicePrincipalLoginInformation
                {
                    ClientId = configuration.TargetAzureServicePrincipalClientId,
                    ClientSecret = configuration.TargetAzureServicePrincipalClientKey
                },
                configuration.TargetAzureServicePrincipalTenantId,
                AzureEnvironment.FromName(configuration.TargetAzureEnvironmentName))
                .WithDefaultSubscription(targetACRResourceId.SubscriptionId);

            var builder = RestClient
                .Configure()
                .WithEnvironment(AzureEnvironment.FromName(configuration.TargetAzureEnvironmentName))
                .WithCredentials(credential)
                .Build();

            // Create ACR management client using the Azure credentials
            var _registryClient = new ContainerRegistryManagementClient(builder);
            _registryClient.SubscriptionId = targetACRResourceId.SubscriptionId;

            // Initiate import of the image that is part of the push event into target ACR
            // Configure the pull of image from source ACR using the token
            var imageTag = $"{pushEvent.Target.Repository}:{pushEvent.Target.Tag}";
            var importSource = new ImportSource
            {
                SourceImage = imageTag,
                RegistryUri = pushEvent.Request.Host,
                Credentials = new ImportSourceCredentials
                {
                    Username = configuration.SourceACRPullTokenName,
                    Password = configuration.SourceACRPullTokenPassword,
                }
            };
           
            await _registryClient.Registries.ImportImageAsync(
                   resourceGroupName: targetACRResourceId.ResourceGroupName,
                   registryName: targetACRResourceId.Name,
                   parameters: new ImportImageParametersInner
                   {
                       // Existing Tag will be overwritten with Force option, 
                       // If the desired behavior is to fail the operation instead of overwriting, use ImportMode.NoForce
                       Mode = ImportMode.Force,
                       Source = importSource,
                       TargetTags = new List<string>()
                       {
                           imageTag
                       }
                   });
            log.LogInformation($"Import of '{imageTag}' success to '{configuration.TargetACRResourceId}'");
        }

        private static async System.Threading.Tasks.Task DeleteImageAsync(
            ContainerRegistryImageDeletedEventData deleteEvent,
            AppConfiguration configuration,
            ILogger log)
        {
            // Create the resourceId from the target ACR resourceId string
            var targetACRResourceId = ResourceId.FromString(configuration.TargetACRResourceId);

            // Create Azure credentials to talk to target Cloud using the Active directory application
            var credential = new AzureCredentials(
                new ServicePrincipalLoginInformation
                {
                    ClientId = configuration.TargetAzureServicePrincipalClientId,
                    ClientSecret = configuration.TargetAzureServicePrincipalClientKey
                },
                configuration.TargetAzureServicePrincipalTenantId,
                AzureEnvironment.FromName(configuration.TargetAzureEnvironmentName))
                .WithDefaultSubscription(targetACRResourceId.SubscriptionId);

            var builder = RestClient
                .Configure()
                .WithEnvironment(AzureEnvironment.FromName(configuration.TargetAzureEnvironmentName))
                .WithCredentials(credential)
                .Build();

            // Create ACR management client using the Azure credentials
            var _registryClient = new ContainerRegistryManagementClient(builder);

            _registryClient.SubscriptionId = targetACRResourceId.SubscriptionId;

            // Fetch the target ACR properties to identify its login server.
            var targetRegistry = await _registryClient.Registries.GetAsync(
                resourceGroupName: targetACRResourceId.ResourceGroupName,
                registryName: targetACRResourceId.Name) ?? 
                throw new InvalidOperationException($"'{configuration.TargetACRResourceId}' is not found");

            // Create ACR data plane client using the Azure credentials
            var registryCredentials = new ContainerRegistryCredentials(
                   ContainerRegistryCredentials.LoginMode.TokenAuth,
                   targetRegistry.LoginServer,
                   configuration.TargetAzureServicePrincipalClientId,
                   configuration.TargetAzureServicePrincipalClientKey);

            var client = new AzureContainerRegistryClient(registryCredentials);

            // Invoke Delete of the image that is part of the delete event on the target ACR.
            await client.Manifests.DeleteAsync(
                name: deleteEvent.Target.Repository,
                reference: deleteEvent.Target.Digest);

            log.LogInformation($"Image '{deleteEvent.Target.Repository}:@{deleteEvent.Target.Digest}' deleted from registry '{configuration.TargetACRResourceId}'");
        }
    }
}
