# Input Actions Generator
A tool to generate C# for your S&box gamemode's input actions

## What this does
This tool takes a gamemode package and reads its input settings. From that it constructs a C# class of input action data that can be used in your gamemode's code-base. The data contains the following items:
* Name - The name you gave the input, used in `Input.*` calls.
* GroupName - The name you gave to group the input with others.
* KeyboardCode - The keyboard code you gave the input as a `string`.
* GamepadCode - The gamepad code you gave the input as a `Gamepad.Code`.

This data is implicited converted to its name so that it can be used seamlessly with the [Input](https://asset.party/api/Sandbox.Input) API.

### Feature Showcase
https://github.com/peter-r-g/Sbox-InputActionsGenerator/assets/11802285/5689f673-9d51-411c-951f-4cf3ffea5c79

## Installation
You can install the tool by downloading this repo or using [Xezno](https://github.com/xezno)s [Tool Manager](https://github.com/xezno/sbox-tools-manager)

## License
Distributed under the MIT License. See the [license](https://github.com/peter-r-g/Sbox-InputActionsGenerator/blob/master/LICENSE.md) for more information.
