using System.Globalization;
using Godot;
using Inhumate.RTI.Proto;

namespace Inhumate.GodotRTI {

	public partial class RTIRuntimeControlUI : Control {

		[Export] public Button ResetButton;
		[Export] public OptionButton ScenarioDropdown;
		[Export] public Button LoadButton;
		[Export] public Button StartButton;
		[Export] public Button PlayButton;
		[Export] public Button PauseButton;
		[Export] public Button StopButton;
		[Export] public Label StateLabel;
		[Export] public Label TimeLabel;
		[Export] public OptionButton TimeScaleDropdown;

		private static readonly string[] TimeScaleOptions = new string[] {
			"0.1x",
			"0.25x",
			"0.5x",
			"1x",
			"2x",
			"3x",
			"4x",
			"5x",
			"10x",
			"20x",
			"50x",
		};

		protected static RTIConnection RTI => RTIConnection.Instance;

		public override void _EnterTree() {
			bool enabledFromCommandLine = false;
			bool disabledFromCommandLine = false;
			string[] args = OS.GetCmdlineArgs();
			for (int i = 0; i < args.Length; i++) {
				if (args[i] == "--rti-ui") enabledFromCommandLine = true;
				if (args[i] == "--no-rti-ui" || args[i] == "--no-rti") disabledFromCommandLine = true;
			}
			if (disabledFromCommandLine || (!OS.IsDebugBuild() && !enabledFromCommandLine)) {
				QueueFree();
			} else {
				Initialize();
			}
			ProcessMode = ProcessModeEnum.Always;
		}

		private void Initialize() {
			Update();
			RTI.OnConnected += Update;
			RTI.OnDisconnected += Update;
			RTI.StateChanged += (newState) => { Update(); };
			while (ScenarioDropdown.ItemCount > 0) ScenarioDropdown.RemoveItem(0);
			int index = 0;
			foreach (var scenario in RTI.Scenarios) {
				if (scenario != null && !string.IsNullOrWhiteSpace(scenario.Name)) {
					ScenarioDropdown.AddItem(scenario.Name);
					if (scenario.Scene.ResourcePath == GetTree().CurrentScene.SceneFilePath) ScenarioDropdown.Selected = index;
					index++;
				}
			}
			foreach (var name in RTI.ScenarioNames) {
				ScenarioDropdown.AddItem(name);
			}

			ResetButton.Pressed += RTIRuntimeControl.PublishReset;
			LoadButton.Pressed += PublishLoadScenario;
			StartButton.Pressed += RTIRuntimeControl.PublishStart;
			PlayButton.Pressed += RTIRuntimeControl.PublishPlay;
			PauseButton.Pressed += RTIRuntimeControl.PublishPause;
			StopButton.Pressed += RTIRuntimeControl.PublishStop;
			while (TimeScaleDropdown.ItemCount > 0) TimeScaleDropdown.RemoveItem(0);
			foreach (var option in TimeScaleOptions) TimeScaleDropdown.AddItem(option);
			TimeScaleDropdown.Selected = 3;
			TimeScaleDropdown.ItemSelected += (index) => {
				float timeScale = float.Parse(TimeScaleOptions[index].Replace("x", ""), CultureInfo.InvariantCulture);
				RTIRuntimeControl.PublishTimeScale(timeScale);
			};
		}

		private void Update() {
			StateLabel.Text = RTI.State == RuntimeState.Unknown ? "" : RTI.State.ToString();
			if (!RTI.Connected) {
				StateLabel.Text += " NOT CONNECTED";
			}
			ResetButton.Disabled = false;
			ScenarioDropdown.Visible = ScenarioDropdown.ItemCount > 0;
			LoadButton.Visible = ScenarioDropdown.ItemCount > 0;
			ScenarioDropdown.Disabled = LoadButton.Disabled = RTI.State == RuntimeState.Loading || RTI.State == RuntimeState.Ready || RTI.State == RuntimeState.Running || RTI.State == RuntimeState.Playback || RTI.State == RuntimeState.Paused || RTI.State == RuntimeState.PlaybackPaused;
			StartButton.Disabled = RTI.State != RuntimeState.Ready && RTI.State != RuntimeState.Paused && RTI.State != RuntimeState.PlaybackPaused && RTI.State != RuntimeState.Unknown;
			PlayButton.Disabled = RTI.State == RuntimeState.Initial || RTI.State == RuntimeState.Playback || RTI.State == RuntimeState.Running || RTI.State == RuntimeState.Paused;
			PauseButton.Disabled = RTI.State != RuntimeState.Running && RTI.State != RuntimeState.Playback && RTI.State != RuntimeState.Unknown;
			StopButton.Disabled = RTI.State == RuntimeState.Stopped || RTI.State == RuntimeState.PlaybackStopped || RTI.State == RuntimeState.Initial || RTI.State == RuntimeState.Ready;
			TimeScaleDropdown.Disabled = false;
		}

		private double lastUpdatedTime = float.NegativeInfinity;
		public override void _Process(double delta) {
			if (Mathf.Abs(RTI.Time - lastUpdatedTime) > 0.05) {
				TimeLabel.Text = FormatTime(RTI.Time);
				lastUpdatedTime = RTI.Time;
			}
		}

		public void PublishLoadScenario() {
			var index = ScenarioDropdown.GetSelectedId();
			if (index < RTI.Scenarios.Count) {
				RTIRuntimeControl.PublishLoadScenario(RTI.Scenarios[index].Name);
			} else {
				index -= RTI.Scenarios.Count;
				RTIRuntimeControl.PublishLoadScenario(RTI.ScenarioNames[index]);
			}
		}


		public static string FormatTime(double rtiTime) {
			var time = System.TimeSpan.FromSeconds(rtiTime);
			return Mathf.Abs(rtiTime) < 1e-5f
					? "--:--"
					: rtiTime > 3600
					? $"{time:hh\\:mm\\:ss}"
					: RTI.TimeScale < 0.99
					? $"{time:mm\\:ss\\.ff}"
					: $"{time:mm\\:ss}";
		}

	}

}
