// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Tasks;
using Amazon.EC2.Model;
using AWS.Deploy.Common;
using AWS.Deploy.Common.Recipes;
using AWS.Deploy.Orchestration.Data;

namespace AWS.Deploy.CLI.Commands.TypeHints
{
    public class EC2KeyPairCommand : ITypeHintCommand
    {
        private readonly IToolInteractiveService _toolInteractiveService;
        private readonly IAWSResourceQueryer _awsResourceQueryer;
        private readonly IConsoleUtilities _consoleUtilities;

        public EC2KeyPairCommand(IToolInteractiveService toolInteractiveService, IAWSResourceQueryer awsResourceQueryer, IConsoleUtilities consoleUtilities)
        {
            _toolInteractiveService = toolInteractiveService;
            _awsResourceQueryer = awsResourceQueryer;
            _consoleUtilities = consoleUtilities;
        }

        public async Task<object> Execute(Recommendation recommendation, OptionSettingItem optionSetting)
        {
            var currentValue = recommendation.GetOptionSettingValue(optionSetting);
            var keyPairs = await _awsResourceQueryer.ListOfEC2KeyPairs();

            var userInputConfiguration = new UserInputConfiguration<KeyPairInfo>(
                kp => kp.KeyName,
                kp => kp.KeyName.Equals(currentValue)
                )
            {
                AskNewName = true,
                EmptyOption = true,
                CurrentValue = currentValue
            };

            var settingValue = "";

            while (true)
            {
                var userResponse = _consoleUtilities.AskUserToChooseOrCreateNew(keyPairs, "Select key pair to use:", userInputConfiguration);

                if (userResponse.IsEmpty)
                {
                    settingValue = "";
                    break;
                }
                else
                {
                    settingValue = userResponse.SelectedOption?.KeyName ?? userResponse.NewName ??
                        throw new UserPromptForNameReturnedNullException("The user prompt for a new EC2 Key Pair name was null or empty.");
                }

                if (userResponse.CreateNew && !string.IsNullOrEmpty(userResponse.NewName))
                {
                    _toolInteractiveService.WriteLine(string.Empty);
                    _toolInteractiveService.WriteLine("You have chosen to create a new key pair.");
                    _toolInteractiveService.WriteLine("You are required to specify a directory to save the key pair private key.");

                    var answer = _consoleUtilities.AskYesNoQuestion("Do you want to continue?", "false");
                    if (answer == YesNo.No)
                        continue;

                    _toolInteractiveService.WriteLine(string.Empty);
                    _toolInteractiveService.WriteLine($"A new key pair will be created with the name {settingValue}.");

                    var keyPairDirectory = _consoleUtilities.AskForEC2KeyPairSaveDirectory(recommendation.ProjectPath);

                    await _awsResourceQueryer.CreateEC2KeyPair(settingValue, keyPairDirectory);
                }

                break;
            }

            return settingValue ?? "";
        }
    }
}
