// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.\r
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Deploy.Common.Recipes.Validation
{
    /// <summary>
    /// The validator is typically used with OptionSettingItems which have a numeric type.
    /// The minimum and maximum values are specified in the deployment recipe
    /// and this validator checks if the set value of the OptionSettingItem falls within this range or not.
    /// </summary>
    public class RangeValidator : IOptionSettingItemValidator
    {
        private static readonly string defaultValidationFailedMessage =
            "Value must be greater than or equal to {{Min}} and less than or equal to {{Max}}";

        public int Min { get; set; } = int.MinValue;
        public int Max { get;set; } = int.MaxValue;

        /// <summary>
        /// Supports replacement tokens {{Min}} and {{Max}}
        /// </summary>
        public string ValidationFailedMessage { get; set; } = defaultValidationFailedMessage;

        public ValidationResult Validate(object input)
        {
            if (int.TryParse(input?.ToString(), out var result) &&
                result >= Min &&
                result <= Max)
            {
                return ValidationResult.Valid();
            }

            var message =
                ValidationFailedMessage
                    .Replace("{{Min}}", Min.ToString())
                    .Replace("{{Max}}", Max.ToString());

            return ValidationResult.Failed(message);
        }
    }
}
