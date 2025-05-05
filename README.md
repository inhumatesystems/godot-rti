# Inhumate RTI Integration Addon for Godot

## Usage

Add to new project:

1. Download zip-file
1. AssetLib tab, click Import…, select zip-file, check Ignore asset root, click Install
1. Verify `res://addons/inhumate_rti`
1. Project > Tools > C# > Create C# solution
1. `dotnet add package Inhumate.RTI` or `<ItemGroup><PackageReference Include="Inhumate.RTI" Version="1.5.1" /></ItemGroup>`
1. Build project (hammer icon)
1. Project Settings > Plugins tab > check Enabled
