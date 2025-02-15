// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
using AWS.Deploy.Common.Recipes;
using AWS.Deploy.Common.Recipes.Validation;

namespace AWS.Deploy.Common
{
    public class ProjectFileNotFoundException : Exception
    {
        public ProjectFileNotFoundException(string projectPath)
            : base($"A project was not found at the path {projectPath}.")
        {
            Path = projectPath;
        }

        public string Path { get; set; }
    }

    /// <summary>
    /// Common exception if the user has passed invalid input from the command line
    /// </summary>
    [AWSDeploymentExpectedException]
    public class InvalidCliArgumentException : Exception
    {
        public InvalidCliArgumentException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Throw if the user attempts to deploy a <see cref="RecipeDefinition"/> but the recipe definition is invalid
    /// </summary>
    [AWSDeploymentExpectedException]
    public class InvalidRecipeDefinitionException : Exception
    {
        public InvalidRecipeDefinitionException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Throw if the user attempts to deploy a <see cref="ProjectDefinition"/> but the project definition is invalid
    /// </summary>
    [AWSDeploymentExpectedException]
    public class InvalidProjectDefinitionException : Exception
    {
        public InvalidProjectDefinitionException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown if there is a missing System Capability.
    /// </summary>
    [AWSDeploymentExpectedException]
    public class MissingSystemCapabilityException : Exception
    {
        public MissingSystemCapabilityException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Throw if Recommendation Engine is unable to generate
    /// recommendations for a given target context
    /// </summary>
    [AWSDeploymentExpectedException]
    public class FailedToGenerateAnyRecommendations : Exception
    {
        public FailedToGenerateAnyRecommendations(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Throw if a value is set that is not part of the allowed values
    /// of an option setting item
    /// </summary>
    [AWSDeploymentExpectedException]
    public class InvalidOverrideValueException : Exception
    {
        public InvalidOverrideValueException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Throw if there is a parse error reading the existing Cloud Application's metadata
    /// </summary>
    [AWSDeploymentExpectedException]
    public class ParsingExistingCloudApplicationMetadataException : Exception
    {
        public ParsingExistingCloudApplicationMetadataException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Throw if Orchestrator is unable to create
    /// the deployment bundle.
    /// </summary>
    [AWSDeploymentExpectedException]
    public class FailedToCreateDeploymentBundleException : Exception
    {
        public FailedToCreateDeploymentBundleException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Throw if Option Setting Item does not exist
    /// </summary>
    [AWSDeploymentExpectedException]
    public class OptionSettingItemDoesNotExistException : Exception
    {
        public OptionSettingItemDoesNotExistException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    [AWSDeploymentExpectedException]
    public class InvalidValidatorTypeException : Exception
    {
        public InvalidValidatorTypeException(string? message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown if <see cref="OptionSettingItem.SetValueOverride"/> is given an invalid value.
    /// </summary>
    [AWSDeploymentExpectedException]
    public class ValidationFailedException : Exception
    {
        public ValidationFailedException(string? message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception thrown if Project Path contains an invalid path
    /// </summary>
    [AWSDeploymentExpectedException]
    public class InvalidProjectPathException : Exception
    {
        public InvalidProjectPathException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// Throw if an invalid <see cref="UserDeploymentSettings"/> is used.
    /// </summary>
    [AWSDeploymentExpectedException]
    public class InvalidUserDeploymentSettingsException : Exception
    {
        public InvalidUserDeploymentSettingsException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception is thrown if we cannot retrieve deployment bundle definitions
    /// </summary>
    [AWSDeploymentExpectedException]
    public class NoDeploymentBundleDefinitionsFoundException : Exception
    {
        public NoDeploymentBundleDefinitionsFoundException(string message, Exception? innerException = null) : base(message, innerException) { }
    }
    
    /// Exception thrown if a failure occured while trying to update the deployment manifest file.
    /// </summary>
    [AWSDeploymentExpectedException]
    public class FailedToUpdateDeploymentManifestFileException : Exception
    {
        public FailedToUpdateDeploymentManifestFileException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    /// <summary>
    /// Indicates a specific strongly typed Exception can be anticipated.
    /// Whoever throws this error should also present the user with helpful information
    /// on what wrong and how to fix it.  This is the preferred UX.
    /// <para />
    /// Conversely, if an Exceptions not marked with attribute reaches the entry point
    /// application (ie Program.cs) than all exception details will likely be presented to
    /// user.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class AWSDeploymentExpectedExceptionAttribute : Attribute { }

    public static class ExceptionExtensions
    {
        /// <summary>
        /// True if the <paramref name="e"/> is decorated with
        /// <see cref="AWSDeploymentExpectedExceptionAttribute"/>.
        /// </summary>
        public static bool IsAWSDeploymentExpectedException(this Exception e) =>
            null != e?.GetType()
                .GetCustomAttribute(typeof(AWSDeploymentExpectedExceptionAttribute), inherit: true);

        public static string PrettyPrint(this Exception? e)
        {
            if (null == e)
                return string.Empty;

            return $"{Environment.NewLine}{e.Message}{Environment.NewLine}{e.StackTrace}{PrettyPrint(e.InnerException)}";
        }
    }
}
