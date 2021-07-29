// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;
using AWS.Deploy.Common;
using System.Threading.Tasks;
using AWS.Deploy.Orchestration.Data;

namespace AWS.Deploy.Orchestration.DisplayedResources
{
    public interface IDisplayedResourcesHandler
    {
        Task<List<DisplayedResourceItem>> GetDeploymentOutputs(CloudApplication cloudApplication, Recommendation recommendation);
    }

    public class DisplayedResourcesHandler : IDisplayedResourcesHandler
    {
        private readonly IAWSResourceQueryer _awsResourceQueryer;
        private readonly IDisplayedResourcesFactory _displayedResourcesFactory;

        public DisplayedResourcesHandler(IAWSResourceQueryer awsResourceQueryer, IDisplayedResourcesFactory displayedResourcesFactory)
        {
            _awsResourceQueryer = awsResourceQueryer;
            _displayedResourcesFactory = displayedResourcesFactory;
        }

        /// <summary>
        /// Retrieves the displayed resource data for known resource types by executing specific resource commands.
        /// For unknown resource types, this returns the physical resource ID and type.
        /// </summary>
        public async Task<List<DisplayedResourceItem>> GetDeploymentOutputs(CloudApplication cloudApplication, Recommendation recommendation)
        {
            var displayedResources = new List<DisplayedResourceItem>();

            if (recommendation.Recipe.DisplayedResources == null)
                return displayedResources;

            foreach (var displayedResource in recommendation.Recipe.DisplayedResources)
            {
                var resource = await _awsResourceQueryer.DescribeCloudFormationResource(cloudApplication.StackName, displayedResource.LogicalId);
                Dictionary<string, string> data;
                if (!string.IsNullOrEmpty(resource.ResourceType) && _displayedResourcesFactory.GetResource(resource.ResourceType) is var displayedResourceCommand && displayedResourceCommand != null)
                {
                    data = await displayedResourceCommand.Execute(resource.PhysicalResourceId);
                }
                else
                {
                    data = new Dictionary<string, string>() {
                        { "ID", resource.PhysicalResourceId },
                        { "Type", resource.ResourceType }
                    };
                }
                displayedResources.Add(new DisplayedResourceItem(resource.PhysicalResourceId, displayedResource.Description, resource.ResourceType, data));
            }

            return displayedResources;
        }
    }
}
