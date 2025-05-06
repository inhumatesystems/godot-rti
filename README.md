# Inhumate RTI Integration Addon for Godot

Addon for integrating your Godot-based simulator or application with the RTI (Runtime Infrastructure) of [Inhumate Suite](https://inhumatesystems.com/products/suite/).

Read more in the [documentation](https://docs.inhumatesystems.com/integrations/godot/).

Work in progress.

## Usage

Add to new project:

1. Download [zip-file](https://github.com/inhumatesystems/godot-rti/archive/refs/heads/main.zip)
1. AssetLib tab, click Import…, select zip-file, check Ignore asset root, click Install
1. Verify `res://addons/inhumate_rti`
1. Project > Tools > C# > Create C# solution
1. `dotnet add package Inhumate.RTI` or add to .csproj: `<ItemGroup><PackageReference Include="Inhumate.RTI" Version="1.5.1" /></ItemGroup>`
1. Build project (hammer icon)
1. Project Settings > Plugins tab > check Enabled
