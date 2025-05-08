using Godot;

namespace Inhumate.GodotRTI {

	[GlobalClass]
	public partial class RTISettings : Resource {

		[Export] public PackedScene HomeScene;

		[Export] public Godot.Collections.Array<RTIScenario> Scenarios = new();

		[Export] public bool AutoConnect = true;

		[Export] public bool Polling = true;

		//[Tooltip("URL of RTI broker to connect to. Leave blank for default. May be overridden by RTI_URL environment variable or command-line.")]
		[Export] public string Url;

		//[Tooltip("Secret to use when connecting. Leave blank for default. May be overridden by RTI_SECRET environment variable or command-line.")]
		[Export] public string Secret;

		//[Tooltip("Automatically load scenario and start if other clients are running")]
		[Export] public bool LateJoin;

		[Export] public bool DebugConnection;
		[Export] public bool DebugChannels;
		[Export] public bool DebugRuntimeControl;
		[Export] public bool DebugEntities;

	}

}
