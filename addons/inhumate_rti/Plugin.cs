#if TOOLS
using Godot;
using System;

namespace Inhumate.GodotRTI {
	[Tool]
	public partial class Plugin : EditorPlugin {
		public override void _EnterTree() {
			AddAutoloadSingleton("RTI", "res://addons/inhumate_rti/RTIConnection.cs");
			AddAutoloadSingleton("RTIUI", "res://addons/inhumate_rti/content/rtiui.tscn");
			// Check if RTISettings resource exists, create it if it doesn't
			string settingsPath = "res://rtisettings.tres";
			if (!ResourceLoader.Exists(settingsPath))
			{
				var settings = new RTISettings();
				settings.HomeScene = ResourceLoader.Load<PackedScene>("res://addons/inhumate_rti/content/defaulthome.tscn");
				var error = ResourceSaver.Save(settings, settingsPath);
				if (error != Error.Ok) GD.PrintErr($"Failed to create rtisettings.tres: {error}");
			}
			else
			{
				// Verify the existing resource is of the correct type
				var resource = ResourceLoader.Load(settingsPath);
				if (!(resource is RTISettings)) GD.PrintErr($"Resource at {settingsPath} exists but is not of type RTISettings.");
			}
		}

		public override void _ExitTree() {
			RemoveAutoloadSingleton("RTIUI");
			RemoveAutoloadSingleton("RTI");
		}
	}
}

#endif
