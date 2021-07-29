// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AWS.Deploy.Orchestration.Data;

namespace AWS.Deploy.Orchestration.DisplayedResources
{
    public class ElasticLoadBalancerResource : IDisplayedResource
    {
        private readonly IAWSResourceQueryer _awsResourceQueryer;

        public ElasticLoadBalancerResource(IAWSResourceQueryer awsResourceQueryer)
        {
            _awsResourceQueryer = awsResourceQueryer;
        }

        public async Task<Dictionary<string, string>> Execute(string resourceId)
        {
            var loadBalancer = await _awsResourceQueryer.DescribeElasticLoadBalancer(resourceId);
            return new Dictionary<string, string>() {
                { "Endpoint", $"http://{loadBalancer.DNSName}/" }
            };
        }
    }
}
