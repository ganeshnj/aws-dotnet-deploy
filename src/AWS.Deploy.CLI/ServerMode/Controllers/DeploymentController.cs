// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using Amazon;
using AWS.Deploy.CLI.Utilities;
using AWS.Deploy.Recipes;
using AWS.Deploy.Orchestration.CDK;
using AWS.Deploy.Orchestration.Data;
using AWS.Deploy.Common;
using AWS.Deploy.CLI.ServerMode.Tasks;
using AWS.Deploy.CLI.ServerMode.Models;
using AWS.Deploy.CLI.ServerMode.Services;
using AWS.Deploy.Orchestration;
using Swashbuckle.AspNetCore.Annotations;
using AWS.Deploy.CLI.ServerMode.Hubs;
using Microsoft.AspNetCore.SignalR;
using AWS.Deploy.CLI.Extensions;
using AWS.Deploy.Orchestration.Utilities;
using Microsoft.AspNetCore.Authorization;
using Amazon.Runtime;
using AWS.Deploy.Common.Recipes;
using AWS.Deploy.Orchestration.DisplayedResources;
using AWS.Deploy.Common.IO;
using AWS.Deploy.Orchestration.LocalUserSettings;
using AWS.Deploy.CLI.Commands;

namespace AWS.Deploy.CLI.ServerMode.Controllers
{
    [Produces("application/json")]
    [ApiController]
    [Route("api/v1/[controller]")]
    public class DeploymentController : ControllerBase
    {
        private readonly IDeploymentSessionStateServer _stateServer;
        private readonly IProjectParserUtility _projectParserUtility;
        private readonly ICloudApplicationNameGenerator _cloudApplicationNameGenerator;
        private readonly IHubContext<DeploymentCommunicationHub, IDeploymentCommunicationHub> _hubContext;

        public DeploymentController(
                        IDeploymentSessionStateServer stateServer,
                        IProjectParserUtility projectParserUtility,
                        ICloudApplicationNameGenerator cloudApplicationNameGenerator,
                        IHubContext<DeploymentCommunicationHub, IDeploymentCommunicationHub> hubContext
                    )
        {
            _stateServer = stateServer;
            _projectParserUtility = projectParserUtility;
            _cloudApplicationNameGenerator = cloudApplicationNameGenerator;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Start a deployment session. A session id will be generated. This session id needs to be passed in future API calls to configure and execute deployment.
        /// </summary>
        [HttpPost("session")]
        [SwaggerOperation(OperationId = "StartDeploymentSession")]
        [SwaggerResponse(200, type: typeof(StartDeploymentSessionOutput))]
        [Authorize]
        public async Task<IActionResult> StartDeploymentSession(StartDeploymentSessionInput input)
        {
            var output = new StartDeploymentSessionOutput(
                Guid.NewGuid().ToString()
                );

            var serviceProvider = CreateSessionServiceProvider(output.SessionId, input.AWSRegion);
            var awsResourceQueryer = serviceProvider.GetRequiredService<IAWSResourceQueryer>();

            var state = new SessionState(
                output.SessionId,
                input.ProjectPath,
                input.AWSRegion,
                (await awsResourceQueryer.GetCallerIdentity()).Account,
                await _projectParserUtility.Parse(input.ProjectPath)
                );

            _stateServer.Save(output.SessionId, state);

            output.DefaultDeploymentName = _cloudApplicationNameGenerator.GenerateValidName(state.ProjectDefinition, new List<CloudApplication>());
            return Ok(output);
        }

        /// <summary>
        /// Closes the deployment session. This removes any session state for the session id.
        /// </summary>
        [HttpDelete("session/<sessionId>")]
        [SwaggerOperation(OperationId = "CloseDeploymentSession")]
        [Authorize]
        public IActionResult CloseDeploymentSession(string sessionId)
        {
            _stateServer.Delete(sessionId);
            return Ok();
        }

        /// <summary>
        /// Gets the list of compatible deployments for the session's project. The list is ordered with the first recommendation in the list being the most compatible recommendation.
        /// </summary>
        [HttpGet("session/<sessionId>/recommendations")]
        [SwaggerOperation(OperationId = "GetRecommendations")]
        [SwaggerResponse(200, type: typeof(GetRecommendationsOutput))]
        [Authorize]
        public async Task<IActionResult> GetRecommendations(string sessionId)
        {
            var state = _stateServer.Get(sessionId);
            if (state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            var orchestrator = CreateOrchestrator(state);

            var output = new GetRecommendationsOutput();

            state.NewRecommendations = await orchestrator.GenerateDeploymentRecommendations();
            foreach (var recommendation in state.NewRecommendations)
            {
                output.Recommendations.Add(new RecommendationSummary(
                    recommendation.Recipe.Id,
                    recommendation.Name,
                    recommendation.ShortDescription
                    ));
            }

            return Ok(output);
        }

        /// <summary>
        /// Gets the list of updatable option setting items for the selected recommendation.
        /// </summary>
        [HttpGet("session/<sessionId>/settings")]
        [SwaggerOperation(OperationId = "GetConfigSettings")]
        [SwaggerResponse(200, type: typeof(GetOptionSettingsOutput))]
        [Authorize]
        public IActionResult GetConfigSettings(string sessionId)
        {
            var state = _stateServer.Get(sessionId);
            if (state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            if (state.SelectedRecommendation == null)
            {
                return NotFound($"A deployment target is not set for Session ID {sessionId}.");
            }

            var orchestrator = CreateOrchestrator(state);

            var configurableOptionSettings = state.SelectedRecommendation.GetConfigurableOptionSettingItems();

            var output = new GetOptionSettingsOutput();
            output.OptionSettings = ListOptionSettingSummary(state.SelectedRecommendation, configurableOptionSettings);

            return Ok(output);
        }

        private List<OptionSettingItemSummary> ListOptionSettingSummary(Recommendation recommendation, IEnumerable<OptionSettingItem> configurableOptionSettings)
        {
            var optionSettingItems = new List<OptionSettingItemSummary>();

            foreach (var setting in configurableOptionSettings)
            {
                var settingSummary = new OptionSettingItemSummary(setting.Id, setting.Name, setting.Description, setting.Type.ToString())
                {
                    TypeHint = setting.TypeHint?.ToString(),
                    Value = recommendation.GetOptionSettingValue(setting),
                    Advanced = setting.AdvancedSetting,
                    Updatable = (!recommendation.IsExistingCloudApplication || setting.Updatable) && recommendation.IsOptionSettingDisplayable(setting),
                    ChildOptionSettings = ListOptionSettingSummary(recommendation, setting.ChildOptionSettings)
                };

                optionSettingItems.Add(settingSummary);
            }

            return optionSettingItems;
        }

        /// <summary>
        /// Applies a value for a list of option setting items on the selected recommendation.
        /// Option setting updates are provided as Key Value pairs with the Key being the JSON path to the leaf node.
        /// Only primitive data types are supported for Value updates. The Value is a string value which will be parsed as its corresponding data type.
        /// </summary>
        [HttpPut("session/<sessionId>/settings")]
        [SwaggerOperation(OperationId = "ApplyConfigSettings")]
        [SwaggerResponse(200, type: typeof(ApplyConfigSettingsOutput))]
        [Authorize]
        public IActionResult ApplyConfigSettings(string sessionId, [FromBody] ApplyConfigSettingsInput input)
        {
            var state = _stateServer.Get(sessionId);
            if (state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            if (state.SelectedRecommendation == null)
            {
                return NotFound($"A deployment target is not set for Session ID {sessionId}.");
            }

            var output = new ApplyConfigSettingsOutput();

            foreach (var updatedSetting in input.UpdatedSettings)
            {
                try
                {
                    var setting = state.SelectedRecommendation.GetOptionSetting(updatedSetting.Key);
                    setting.SetValueOverride(updatedSetting.Value);
                }
                catch (Exception ex)
                {
                    output.FailedConfigUpdates.Add(updatedSetting.Key, ex.Message);
                }
            }

            return Ok(output);
        }

        /// <summary>
        /// Gets the list of existing deployments that are compatible with the session's project.
        /// </summary>
        [HttpGet("session/<sessionId>/deployments")]
        [SwaggerOperation(OperationId = "GetExistingDeployments")]
        [SwaggerResponse(200, type: typeof(GetExistingDeploymentsOutput))]
        [Authorize]
        public async Task<IActionResult> GetExistingDeployments(string sessionId)
        {
            var state = _stateServer.Get(sessionId);
            if (state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            var serviceProvider = CreateSessionServiceProvider(state);

            if(state.NewRecommendations == null)
            {
                await GetRecommendations(sessionId);
            }

            var output = new GetExistingDeploymentsOutput();

            var deployedApplicationQueryer = serviceProvider.GetRequiredService<IDeployedApplicationQueryer>();
            var session = CreateOrchestratorSession(state);
            state.ExistingDeployments = await deployedApplicationQueryer.GetCompatibleApplications(state.NewRecommendations.ToList(), session: session);

            foreach(var deployment in state.ExistingDeployments)
            {
                output.ExistingDeployments.Add(new ExistingDeploymentSummary(
                    deployment.Name,
                    deployment.RecipeId,
                    deployment.LastUpdatedTime,
                    deployment.UpdatedByCurrentUser));
            }

            return Ok(output);
        }

        /// <summary>
        /// Set the target recipe and name for the deployment.
        /// </summary>
        [HttpPost("session/<sessionId>")]
        [SwaggerOperation(OperationId = "SetDeploymentTarget")]
        [Authorize]
        public async Task<IActionResult> SetDeploymentTarget(string sessionId, [FromBody] SetDeploymentTargetInput input)
        {
            var state = _stateServer.Get(sessionId);
            if(state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            if(!string.IsNullOrEmpty(input.NewDeploymentRecipeId) &&
               !string.IsNullOrEmpty(input.NewDeploymentName))
            {
                state.SelectedRecommendation = state.NewRecommendations?.FirstOrDefault(x => string.Equals(input.NewDeploymentRecipeId, x.Recipe.Id));
                if(state.SelectedRecommendation == null)
                {
                    return NotFound($"Recommendation {input.NewDeploymentRecipeId} not found.");
                }

                state.ApplicationDetails.Name = input.NewDeploymentName;
                state.ApplicationDetails.RecipeId = input.NewDeploymentRecipeId;
                state.SelectedRecommendation.AddReplacementToken(DeployCommand.REPLACE_TOKEN_STACK_NAME, input.NewDeploymentName);
            }
            else if(!string.IsNullOrEmpty(input.ExistingDeploymentName))
            {
                var serviceProvider = CreateSessionServiceProvider(state);
                var templateMetadataReader = serviceProvider.GetRequiredService<ITemplateMetadataReader>();

                var existingDeployment = state.ExistingDeployments?.FirstOrDefault(x => string.Equals(input.ExistingDeploymentName, x.Name));
                if (existingDeployment == null)
                {
                    return NotFound($"Existing deployment {input.ExistingDeploymentName} not found.");
                }

                state.SelectedRecommendation = state.NewRecommendations?.FirstOrDefault(x => string.Equals(existingDeployment.RecipeId, x.Recipe.Id));
                if (state.SelectedRecommendation == null)
                {
                    return NotFound($"Recommendation {input.NewDeploymentRecipeId} used in existing deployment {existingDeployment.RecipeId} not found.");
                }

                var existingCloudApplicationMetadata = await templateMetadataReader.LoadCloudApplicationMetadata(input.ExistingDeploymentName);
                state.SelectedRecommendation = state.SelectedRecommendation.ApplyPreviousSettings(existingCloudApplicationMetadata.Settings);

                state.ApplicationDetails.Name = input.ExistingDeploymentName;
                state.ApplicationDetails.RecipeId = existingDeployment.RecipeId;
                state.SelectedRecommendation.AddReplacementToken(DeployCommand.REPLACE_TOKEN_STACK_NAME, input.ExistingDeploymentName);
            }

            return Ok();
        }

        /// <summary>
        /// Checks the missing System Capabilities for a given session.
        /// </summary>
        [HttpPost("session/<sessionId>/compatiblity")]
        [SwaggerOperation(OperationId = "GetCompatibility")]
        [SwaggerResponse(200, type: typeof(GetCompatibilityOutput))]
        [Authorize]
        public async Task<IActionResult> GetCompatibility(string sessionId)
        {
            var state = _stateServer.Get(sessionId);
            if (state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            if (state.SelectedRecommendation == null)
            {
                return NotFound($"A deployment target is not set for Session ID {sessionId}.");
            }

            var output = new GetCompatibilityOutput();
            var serviceProvider = CreateSessionServiceProvider(state);
            var systemCapabilityEvaluator = serviceProvider.GetRequiredService<ISystemCapabilityEvaluator>();

            var capabilities = await systemCapabilityEvaluator.EvaluateSystemCapabilities(state.SelectedRecommendation);

            output.Capabilities = capabilities.Select(x => new SystemCapabilitySummary(x.Name, x.Installed, x.Available)
            {
                InstallationUrl = x.InstallationUrl,
                Message = x.Message
            }).ToList();

            return Ok(output);
        }

        /// <summary>
        /// Begin execution of the deployment.
        /// </summary>
        [HttpPost("session/<sessionId>/execute")]
        [SwaggerOperation(OperationId = "StartDeployment")]
        [Authorize]
        public async Task<IActionResult> StartDeployment(string sessionId)
        {
            var state = _stateServer.Get(sessionId);
            if (state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            var serviceProvider = CreateSessionServiceProvider(state);

            var orchestrator = CreateOrchestrator(state, serviceProvider);

            if (state.SelectedRecommendation == null)
                throw new SelectedRecommendationIsNullException("The selected recommendation is null or invalid.");

            var systemCapabilityEvaluator = serviceProvider.GetRequiredService<ISystemCapabilityEvaluator>();

            var capabilities = await systemCapabilityEvaluator.EvaluateSystemCapabilities(state.SelectedRecommendation);

            var missingCapabilitiesMessage = "";
            foreach (var capability in capabilities)
            {
                missingCapabilitiesMessage = $"{missingCapabilitiesMessage}{capability.GetMessage()}{Environment.NewLine}";
            }

            if (capabilities.Any())
                return Problem($"Unable to start deployment due to missing system capabilities.{Environment.NewLine}{missingCapabilitiesMessage}");

            var task = new DeployRecommendationTask(orchestrator, state.ApplicationDetails, state.SelectedRecommendation);
            state.DeploymentTask = task.Execute();

            return Ok();
        }

        /// <summary>
        /// Gets the status of the deployment.
        /// </summary>
        [HttpGet("session/<sessionId>/execute")]
        [SwaggerOperation(OperationId = "GetDeploymentStatus")]
        [SwaggerResponse(200, type: typeof(GetDeploymentStatusOutput))]
        [Authorize]
        public IActionResult GetDeploymentStatus(string sessionId)
        {
            var state = _stateServer.Get(sessionId);
            if (state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            var output = new GetDeploymentStatusOutput();

            if (state.DeploymentTask == null)
                output.Status = DeploymentStatus.NotStarted;
            else if (state.DeploymentTask.IsCompleted && state.DeploymentTask.Status == TaskStatus.RanToCompletion)
                output.Status = DeploymentStatus.Success;
            else if (state.DeploymentTask.IsCompleted && state.DeploymentTask.Status == TaskStatus.Faulted)
                output.Status = DeploymentStatus.Error;
            else
                output.Status = DeploymentStatus.Executing;

            return Ok(output);
        }

        /// <summary>
        /// Gets information about the displayed resources defined in the recipe definition.
        /// </summary>
        [HttpGet("session/<sessionId>/details")]
        [SwaggerOperation(OperationId = "GetDeploymentDetails")]
        [SwaggerResponse(200, type: typeof(GetDeploymentDetailsOutput))]
        [Authorize]
        public async Task<IActionResult> GetDeploymentDetails(string sessionId)
        {
            var state = _stateServer.Get(sessionId);
            if (state == null)
            {
                return NotFound($"Session ID {sessionId} not found.");
            }

            var serviceProvider = CreateSessionServiceProvider(state);
            var displayedResourcesHandler = serviceProvider.GetRequiredService<IDisplayedResourcesHandler>();

            if (state.SelectedRecommendation == null)
            {
                return NotFound($"A deployment target is not set for Session ID {sessionId}.");
            }

            var displayedResources = await displayedResourcesHandler.GetDeploymentOutputs(state.ApplicationDetails, state.SelectedRecommendation);

            var output = new GetDeploymentDetailsOutput(
                state.ApplicationDetails.StackName,
                displayedResources
                    .Select(x => new DisplayedResourceSummary(x.Id, x.Description, x.Type, x.Data))
                    .ToList());

            return Ok(output);
        }

        private IServiceProvider CreateSessionServiceProvider(SessionState state)
        {
            return CreateSessionServiceProvider(state.SessionId, state.AWSRegion);
        }

        private IServiceProvider CreateSessionServiceProvider(string sessionId, string awsRegion)
        {
            var awsCredentials = HttpContext.User.ToAWSCredentials();
            if(awsCredentials == null)
            {
                throw new FailedToRetrieveAWSCredentialsException("AWS credentials are missing for the current session.");
            }

            var interactiveServices = new SessionOrchestratorInteractiveService(sessionId, _hubContext);
            var services = new ServiceCollection();
            services.AddSingleton<IOrchestratorInteractiveService>(interactiveServices);
            services.AddSingleton<ICommandLineWrapper>(services =>
            {
                var wrapper = new CommandLineWrapper(interactiveServices, true);
                wrapper.RegisterAWSContext(awsCredentials, awsRegion);
                return wrapper;
            });

            services.AddCustomServices();
            var serviceProvider = services.BuildServiceProvider();

            var awsClientFactory = serviceProvider.GetRequiredService<IAWSClientFactory>();

            awsClientFactory.ConfigureAWSOptions(awsOptions =>
            {
                awsOptions.Credentials = awsCredentials;
                awsOptions.Region = RegionEndpoint.GetBySystemName(awsRegion);
            });

            return serviceProvider;
        }

        private OrchestratorSession CreateOrchestratorSession(SessionState state, AWSCredentials? awsCredentials = null)
        {
            return new OrchestratorSession(
                state.ProjectDefinition,
                awsCredentials ?? HttpContext.User.ToAWSCredentials() ??
                    throw new FailedToRetrieveAWSCredentialsException("The tool was not able to retrieve the AWS Credentials."),
                state.AWSRegion,
                state.AWSAccountId);
        }

        private Orchestrator CreateOrchestrator(SessionState state, IServiceProvider? serviceProvider = null, AWSCredentials? awsCredentials = null)
        {
            if(serviceProvider == null)
            {
                serviceProvider = CreateSessionServiceProvider(state);
            }

            var session = CreateOrchestratorSession(state, awsCredentials);

            return new Orchestrator(
                                    session,
                                    serviceProvider.GetRequiredService<IOrchestratorInteractiveService>(),
                                    serviceProvider.GetRequiredService<ICdkProjectHandler>(),
                                    serviceProvider.GetRequiredService<ICDKManager>(),
                                    serviceProvider.GetRequiredService<ICDKVersionDetector>(),
                                    serviceProvider.GetRequiredService<IAWSResourceQueryer>(),
                                    serviceProvider.GetRequiredService<IDeploymentBundleHandler>(),
                                    serviceProvider.GetRequiredService<ILocalUserSettingsEngine>(),
                                    new DockerEngine.DockerEngine(
                                        session.ProjectDefinition,
                                        serviceProvider.GetRequiredService<IFileManager>()),
                                    serviceProvider.GetRequiredService<ICustomRecipeLocator>(),
                                    new List<string> { RecipeLocator.FindRecipeDefinitionsPath() },
                                    serviceProvider.GetRequiredService<IDirectoryManager>()
                                );
        }
    }
}
