# ShaderGraph Stencil Injector

A tool that injects stencil buffer functionality into Unity ShaderGraph shaders.

## Installation

1. Create an `Editor` folder in your Unity project's `Assets` folder if it doesn't exist already
2. Download both files from this repository:
   - `ShaderGraphStencilInjector.cs`
   - `Unity.ShaderGraph.Editor.asmref`
3. Place both files in your project's `Assets/Editor` folder

## Usage

1. Select one or more ShaderGraph files in the Project window
2. Right-click and select "Shader Graph Extensions > Inject Stencil Support"
3. A new shader file will be created with the same name as your ShaderGraph
4. The stencil properties will be exposed in the material inspector

## Features

- Adds stencil buffer support to any ShaderGraph
- Keeps dithering and other ShaderGraph features intact
- Works with "Allow Material Override" option
- Clean UI for controlling stencil parameters

## Requirements

- Unity 2021.3 or newer
- Shader Graph package

## License

[MIT License](LICENSE.md)
