// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Threading.Tasks;
using AWS.Deploy.Orchestration.Data;

namespace AWS.Deploy.Orchestration.DisplayedResources
{
    /// <summary>
    /// Interface for displayed resources such as <see cref="ElasticBeanstalkEnvironmentResource"/>
    /// </summary>
    public interface IDisplayedResource
    {
        Task<Dictionary<string, string>> Execute(string resourceId);
    }

    public interface IDisplayedResourcesFactory
    {
        IDisplayedResource? GetResource(string resourceType);
    }

    /// <summary>
    /// Factory class responsible to build and get the displayed resources.
    /// </summary>
    public class DisplayedResourcesFactory : IDisplayedResourcesFactory
    {
        private readonly Dictionary<string, IDisplayedResource> _resources;

        public DisplayedResourcesFactory(IAWSResourceQueryer awsResourceQueryer)
        {
            _resources = new Dictionary<string, IDisplayedResource>
            {
                { "AWS::ElasticBeanstalk::Environment", new ElasticBeanstalkEnvironmentResource(awsResourceQueryer) },
                { "AWS::ElasticLoadBalancingV2::LoadBalancer", new ElasticLoadBalancerResource(awsResourceQueryer) },
                { "AWS::S3::Bucket", new S3BucketResource(awsResourceQueryer) }
            };
        }

        public IDisplayedResource? GetResource(string resourceType)
        {
            if (!_resources.ContainsKey(resourceType))
            {
                return null;
            }

            return _resources[resourceType];
        }
    }
}
