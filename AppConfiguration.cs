using System;
using Microsoft.Azure.Management.ResourceManager.Fluent;

namespace acr_replicate_app
{
    /// <summary>
    /// The application settings used by this app.
    /// </summary>
    public class AppConfiguration
    {
        /// <summary>
        /// The target ACR's Azure cloud environment name <see cref="AzureEnvironment"/>
        /// </summary>
        /// <example><see cref="AzureEnvironment.AzureChinaCloud"/> for Azure China Cloud</example>
        /// <example><see cref="AzureEnvironment.AzureGermanCloud"/> for Azure German Cloud</example>
        public string TargetAzureEnvironmentName { get; set; }

        /// <summary>
        /// The Active directory application Id that has Contibutor permissions 
        /// on the target ACR. This application should be created in the target cloud 
        /// and create role assignments for the target ACR.
        /// </summary>
        public string TargetAzureServicePrincipalClientId { get; set; }

        /// <summary>
        /// The application key or client secret of the <see cref="TargetAzureServicePrincipalClientId"/>
        /// </summary>
        public string TargetAzureServicePrincipalClientKey { get; set; }

        /// <summary>
        /// The tenant Id of the application <see cref="TargetAzureServicePrincipalClientId"/>
        /// </summary>
        public string TargetAzureServicePrincipalTenantId { get; set; }

        /// <summary>
        /// The Azure Resource Id of the target ACR in the form
        /// /subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/Microsoft.ContainerRegistry/registries/{registryName}
        /// </summary>
        public string TargetACRResourceId { get; set; }

        /// <summary>
        /// The name of a token that is created for the source ACR. This token will be used to pull the image from ACR 
        /// and hence recommended to have only pull permissions on the ACR.
        /// </summary>
        public string SourceACRPullTokenName { get; set; }

        /// <summary>
        /// The password of the pull token <see cref="SourceACRPullTokenName"/> on source ACR.
        /// </summary>
        public string SourceACRPullTokenPassword { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(TargetAzureEnvironmentName))
            {
                throw new ArgumentException($"'{nameof(TargetAzureEnvironmentName)}' cannot be null or empty");
            }
            if (string.IsNullOrWhiteSpace(TargetAzureServicePrincipalTenantId))
            {
                throw new ArgumentException($"'{nameof(TargetAzureServicePrincipalTenantId)}' cannot be null or empty");
            }
            if (string.IsNullOrWhiteSpace(TargetAzureServicePrincipalClientId))
            {
                throw new ArgumentException($"'{nameof(TargetAzureServicePrincipalClientId)}' cannot be null or empty");
            }
            if (string.IsNullOrWhiteSpace(TargetAzureServicePrincipalClientKey))
            {
                throw new ArgumentException($"'{nameof(TargetAzureServicePrincipalClientKey)}' cannot be null or empty");
            }
            if (string.IsNullOrWhiteSpace(TargetACRResourceId))
            {
                throw new ArgumentException($"'{nameof(TargetACRResourceId)}' cannot be null or empty");
            }
            if (string.IsNullOrWhiteSpace(SourceACRPullTokenName))
            {
                throw new ArgumentException($"'{nameof(SourceACRPullTokenName)}' cannot be null or empty");
            }
            if (string.IsNullOrWhiteSpace(SourceACRPullTokenPassword))
            {
                throw new ArgumentException($"'{nameof(SourceACRPullTokenPassword)}' cannot be null or empty");
            }
        }
    }
}
