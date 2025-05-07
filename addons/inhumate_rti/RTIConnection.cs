using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inhumate.RTI;
using Inhumate.RTI.Proto;
using Google.Protobuf;
using System.Reflection;
using Godot;

namespace Inhumate.GodotRTI {

	public partial class RTIConnection : Node {

		// Not to be added to a scene. Add as auto-load.

		public const string IntegrationVersion = "0.0.1-dev-version";

		public RTISettings Settings = new RTISettings();
		public bool DebugConnection => Settings.DebugConnection;
		public bool DebugChannels => Settings.DebugChannels;
		public bool DebugRuntimeControl => Settings.DebugRuntimeControl;
		public bool DebugEntities => Settings.DebugEntities;

		public Godot.Collections.Array<RTIScenario> Scenarios = new();
		public Godot.Collections.Array<string> ScenarioNames { get; set; } = new();
		public RTIScenario Scenario { get; private set; }
		private Godot.Collections.Dictionary<string, string> scenarioParameterValues = new();
		public string LastScenarioName { get; private set; }

		public int MaxPollCount = 100;

		public string TimeSyncMasterClientId { get; private set; }
		private double lastTimeSyncTime;
		public bool IsTimeSyncMaster => rti != null && TimeSyncMasterClientId == rti.ClientId;
		private bool inhibitTimeSyncMaster;

		private static int _mainThreadId;
		public static RTIConnection Instance {
			get {
				if (_instance == null && !_quitting) {
					_mainThreadId = Thread.CurrentThread.ManagedThreadId;
					var conn = new RTIConnection();
					conn.Initialize();
					var sceneTree = Engine.GetMainLoop() as SceneTree;
					if (sceneTree?.CurrentScene != null) {
						if (sceneTree.CurrentScene.IsNodeReady()) {
							sceneTree.CurrentScene.AddChild(conn);
							//conn.Owner = sceneTree.CurrentScene;
						} else {
							sceneTree.CurrentScene.Ready += () => {
								sceneTree.CurrentScene.AddChild(conn);
							};
						}
					}
					_instance = conn;
				}
				return _instance;
			}
		}

		public RTIClient Client => rti;
		private RTIClient rti;
		public string Application => Client.Application;
        public string ApplicationVersion => Client.ApplicationVersion;
		public string ClientId => Client.ClientId;

		public bool Connected => connected;
		private bool connected;
		public bool WasEverConnected => everConnected;
		private bool everConnected;
		public bool Quitting { get; private set; }
		private static bool _quitting;
		public double Time { get; private set; }
		public double TimeScale { get; private set; } = 1;
		public int PollCount { get; private set; }
		public RuntimeState State {
			get { return rti != null ? rti.State : RuntimeState.Unknown; }
			set { if (rti != null) rti.State = value; }
		}

        [Signal] public delegate void TimeScaleChangedEventHandler(double timeScale);
		[Signal] public delegate void OnStartEventHandler();
		[Signal] public delegate void OnStopEventHandler();
		[Signal] public delegate void OnResetEventHandler();
		[Signal] public delegate void StateChangedEventHandler(string newStateStr);
        [Signal] public delegate void OnConnectedEventHandler();
		[Signal] public delegate void OnConnectedOnceEventHandler();
		[Signal] public delegate void OnDisconnectedEventHandler();
        [Signal] public delegate void OnLoadScenarioEventHandler(string scenarioName, Godot.Collections.Dictionary<string, string> parameterValues);
		[Signal] public delegate void OnEntityOwnershipReleasedEventHandler(string entityId);
		[Signal] public delegate void OnEntityOwnershipAssumedEventHandler(string entityId);

        // TODO these don't work as signals - we can't null-check and HasConnections() seems to always return true
		public event Action CustomStop;
		public event Action CustomReset;
		public event Action<RuntimeControl.Types.LoadScenario> CustomLoadScenario;

		private Dictionary<string, RTIEntity> entities = new Dictionary<string, RTIEntity>();
		public IEnumerable<RTIEntity> Entities => entities.Values;
		private Dictionary<string, RTIGeometry> geometries = new Dictionary<string, RTIGeometry>();
		private Dictionary<string, RTIInjectable> injectables = new Dictionary<string, RTIInjectable>();

		public Command[] Commands => commands.Values.ToArray();
		private Dictionary<string, Command> commands = new Dictionary<string, Command>();
		public delegate CommandResponse CommandHandler(Command command, ExecuteCommand exec);
		private Dictionary<string, CommandHandler> commandHandlers = new Dictionary<string, CommandHandler>();
		private Dictionary<string, string> commandTransactionChannel = new Dictionary<string, string>();

		private bool inhibitConnect;

		public string persistentEntityOwnerClientId { get; private set; }
		public bool IsPersistentEntityOwner => rti != null && persistentEntityOwnerClientId == rti.ClientId;
		public string persistentGeometryOwnerClientId { get; private set; }
		public bool IsPersistentGeometryOwner => rti != null && persistentGeometryOwnerClientId == rti.ClientId;

		public string LastErrorChannel { get; private set; }
		public Exception LastError { get; private set; }

		private RuntimeControl.Types.LoadScenario receivedCurrentScenario;

		private string homeScenePath => Settings.HomeScene?.ResourcePath;
        private string initialScenePath;

		public bool IsHomeScene { get {
			return GetTree().CurrentScene.SceneFilePath == homeScenePath;
		}}

		public void WhenConnected(OnConnectedEventHandler handler) {
			if (connected) handler?.Invoke();
			else OnConnected += handler;
		}

		public void WhenConnectedOnce(OnConnectedOnceEventHandler handler) {
			if (connected) handler?.Invoke();
			else OnConnectedOnce += handler;
		}

		public void Publish(string channelName, string message) {
			if (Quitting) return;
			if (connected) rti.Publish(channelName, message);
		}

		public void Publish<T>(string channelName, T message) where T : IMessage<T>, new() {
			if (Quitting) return;
			if (connected) rti.Publish(channelName, message);
		}

		public void PublishJson(string channelName, object message) {
			if (Quitting) return;
			if (connected) rti.PublishJson(channelName, message);
		}

		public UntypedListener Subscribe<T>(string channelName, TypedListener<T> callback) where T : IMessage<T>, new() {
			if (Quitting) return null;
			return rti.Subscribe<T>(channelName, (name, data) => {
				RunOrQueue(() => { callback(name, data); });
			});
		}

		public UntypedListener Subscribe<T>(string channelName, TypedIdListener<T> callback) where T : IMessage<T>, new() {
			if (Quitting) return null;
			return rti.Subscribe<T>(channelName, (name, id, data) => {
				RunOrQueue(() => { callback(name, id, data); });
			});
		}

		public UntypedListener Subscribe(string channelName, UntypedListener callback) {
			if (Quitting) return null;
			return rti.Subscribe(channelName, (name, data) => {
				RunOrQueue(() => { callback(name, data); });
			});
		}

		public UntypedListener SubscribeJson<T>(string channelName, TypedListener<T> callback) {
			if (Quitting) return null;
			return rti.SubscribeJson<T>(channelName, (name, data) => {
				RunOrQueue(() => { callback(name, data); });
			});
		}

		public void Unsubscribe(UntypedListener listener) {
			if (Quitting) return;
			rti.Unsubscribe(listener);
		}

		public void Unsubscribe(string channelName) {
			if (Quitting) return;
			if (DebugChannels) GD.Print($"RTI unsubscribe {channelName}");
			rti.Unsubscribe(channelName);
		}

		public override void _EnterTree() {
			_mainThreadId = Thread.CurrentThread.ManagedThreadId;
			ProcessMode = ProcessModeEnum.Always;
			Quitting = false;
			if (_instance != null && _instance != this) {
				GD.PrintErr($"Multiple instances of RTIConnection - destroying this one");
				QueueFree();
				return;
			}
			_instance = this;
			if (rti == null) Initialize();
		}

		public override void _Notification(int what) {
			if (what == NotificationWMCloseRequest) {
				Quitting = true;
				_quitting = true;
			}
		}

		public override void _ExitTree() {
			if (rti != null) {
				rti.OnConnected -= OnRtiConnected;
				rti.OnDisconnected -= OnRtiDisconnected;
				rti.OnError -= OnRtiError;
			}
			Disconnect();
			if (_instance == this) {
				_instance = null;
			}
		}

		public override void _Ready() {
			base._Ready();
            initialScenePath = GetTree().CurrentScene.SceneFilePath;
			if (rti != null && IsHomeScene) rti.State = RuntimeState.Initial;
		}

		private void Initialize() {
			string clientId = null;

			string settingsPath = "res://rtisettings.tres";
			if (ResourceLoader.Exists(settingsPath)) {
				var loadedSettings = ResourceLoader.Load<RTISettings>(settingsPath);
				if (loadedSettings != null) {
					Settings = loadedSettings;
				} else {
					GD.PrintErr($"Failed to load RTI settings from {settingsPath}");
				}
			} else {
                GD.Print("Configure Inhumate RTI by adding an RTISettings resource at res://rtisettings.tres");
            }
			Scenarios = Settings.Scenarios;

			string[] args = OS.GetCmdlineArgs();
			for (int i = 0; i < args.Length; i++) {
				if (args[i] == "--rti" && i < args.Length - 2) {
					Settings.Url = args[i + 1];
				} else if (args[i] == "--rti-client-id" && i < args.Length - 2) {
					clientId = args[i + 1];
				} else if (args[i] == "--rti-secret" && i < args.Length - 2) {
					Settings.Secret = args[i + 1];
				} else if (args[i] == "--no-rti") {
					inhibitConnect = true;
				} else if (args[i] == "--rti-no-time-sync-master") {
					inhibitTimeSyncMaster = true;
				} else if (args[i] == "--rti-debug") {
					Settings.DebugChannels = true;
					Settings.DebugConnection = true;
					Settings.DebugEntities = true;
					Settings.DebugRuntimeControl = true;
				} else if (args[i] == "--rti-debug-channels") {
					Settings.DebugChannels = true;
				} else if (args[i] == "--rti-debug-connection") {
					Settings.DebugConnection = true;
				} else if (args[i] == "--rti-debug-entities") {
					Settings.DebugEntities = true;
				} else if (args[i] == "--rti-debug-runtime-control") {
					Settings.DebugRuntimeControl = true;
				}
			}

			try {
				var frameworkVer = System.Environment.Version;
				var runtimeVer = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
				var versionInfo = Engine.GetVersionInfo();
				rti = new RTIClient(Settings.Url, false) {
					Application = ProjectSettings.GetSetting("application/config/name").ToString() + (Engine.IsEditorHint() ? " (Editor)" : ""),
					ApplicationVersion = ProjectSettings.GetSetting("application/config/version").ToString(),
					IntegrationVersion = IntegrationVersion,
					EngineVersion = $"Godot {versionInfo["string"].ToString()}",
					Capabilities = {
						RTICapability.RuntimeControl,
						RTICapability.Scenario,
						RTICapability.TimeScale
					}
				};
				if (!string.IsNullOrWhiteSpace(clientId)) rti.ClientId = clientId;
				if (!string.IsNullOrWhiteSpace(Settings.Secret)) rti.Secret = Settings.Secret;
				Subscribe<RuntimeControl>(RTIChannel.Control, OnRuntimeControl);
				Subscribe<RuntimeControl>(rti.OwnChannelPrefix + RTIChannel.Control, OnRuntimeControl);
				Subscribe<Scenarios>(RTIChannel.Scenarios, OnScenarios);
				rti.RegisterChannel(new Channel {
					Name = RTIChannel.Entity,
					DataType = typeof(Entity).Name,
					State = true,
					FirstFieldId = true
				});
				Subscribe<Entity>(RTIChannel.Entity, OnEntity);
				rti.RegisterChannel(new Channel {
					Name = RTIChannel.EntityOperation,
					DataType = typeof(EntityOperation).Name,
					Ephemeral = true,
				});
				Subscribe<EntityOperation>(RTIChannel.EntityOperation, OnEntityOperation);
				rti.RegisterChannel(new Channel {
					Name = RTIChannel.Geometry,
					DataType = typeof(Geometry).Name,
					State = true,
					FirstFieldId = true
				});
				rti.RegisterChannel(new Channel {
					Name = RTIChannel.GeometryOperation,
					DataType = typeof(GeometryOperation).Name,
					Ephemeral = true,
				});
				Subscribe<GeometryOperation>(RTIChannel.GeometryOperation, OnGeometryOperation);
				Subscribe<InjectableOperation>(RTIChannel.InjectableOperation, OnInjectableOperation);
				Subscribe<InjectionOperation>(RTIChannel.InjectionOperation, OnInjectionOperation);
				rti.RegisterChannel(new Channel {
					Name = RTIChannel.Injection,
					DataType = typeof(Injection).Name,
					State = true,
					FirstFieldId = true
				});
				Subscribe<Commands>(RTIChannel.Commands, OnCommands);
				Subscribe<Commands>(rti.OwnChannelPrefix + RTIChannel.Commands, OnCommands);
				rti.RegisterChannel(new Channel {
					Name = RTIChannel.Commands,
					DataType = typeof(Commands).Name,
					Ephemeral = true
				});
				Subscribe(RTIChannel.ClientDisconnect, OnClientDisconnect);
				rti.OnConnected += OnRtiConnected;
				rti.OnDisconnected += OnRtiDisconnected;
				rti.OnError += OnRtiError;
				if (Settings.AutoConnect) Connect();
			} catch (AggregateException ex) {
				if (ex.InnerExceptions.Count == 1) {
					GD.PrintErr($"RTI connection failed: {ex.InnerException.Message}");
				} else {
					GD.PrintErr($"RTI connection failed: {ex.Message}");
				}
			} catch (Exception ex) {
				GD.PrintErr($"RTI initialization failed: {ex.Message}");
			}
		}

		private void OnRtiConnected() {
			connected = true;
			if (Settings.DebugConnection) GD.Print("RTI connected");
			if (persistentEntityOwnerClientId == null) QueryPersistentEntityOwner();
			if (persistentGeometryOwnerClientId == null) QueryPersistenGeometryOwner();
			if (Settings.LateJoin && State == RuntimeState.Initial) {
				Publish(RTIChannel.Clients, new Clients { RequestClients = new Google.Protobuf.WellKnownTypes.Empty() });
				Publish(RTIChannel.Control, new RuntimeControl { RequestCurrentScenario = new Google.Protobuf.WellKnownTypes.Empty() });
			}
			RunOrQueue(() => {
                EmitSignal(SignalName.OnConnected);
                if (!everConnected) EmitSignal(SignalName.OnConnectedOnce);
                everConnected = true;
			});
		}

		public void ClaimPersistentEntityOwnership() {
			persistentEntityOwnerClientId = rti.ClientId;
			PublishClaimPersistentEntityOwnership();
		}

		public void ClaimPersistentGeometryOwnership() {
			persistentGeometryOwnerClientId = rti.ClientId;
			PublishClaimPersistentGeometryOwnership();
		}

		private void QueryPersistentEntityOwner() {
			Publish(RTIChannel.EntityOperation, new EntityOperation {
				RequestPersistentOwnership = new EntityOperation.Types.ApplicationClient {
					Application = rti.Application,
					ClientId = rti.ClientId
				}
			});
			RunOrQueue(() => { _ = RandomDelayClaimPersistentEntityOwnership(); });
		}

		private void QueryPersistenGeometryOwner() {
			Publish(RTIChannel.Geometry, new GeometryOperation {
				RequestPersistentOwnership = new GeometryOperation.Types.ApplicationClient {
					Application = rti.Application,
					ClientId = rti.ClientId
				}
			});
			RunOrQueue(() => { _ = RandomDelayClaimPersistentGeometryOwnership(); });
		}

		private void OnRtiDisconnected() {
			if (DebugConnection) GD.Print($"RTI disconnected");
			connected = false;
			RunOrQueue(() => { EmitSignal(SignalName.OnDisconnected); });
		}

		private bool warnedConnection = false;
		private void OnRtiError(string channel, Exception ex) {
			if (!Quitting && (channel != "connection" || everConnected || !warnedConnection)) {
				GD.Print($"RTI error {channel} {ex}");
				if (channel == "connection") warnedConnection = true;
			}
			LastErrorChannel = channel;
			LastError = ex;
		}

		public void Connect() {
			if (inhibitConnect) {
				if (DebugConnection) GD.Print("RTI connection inhibited");
			} else {
				if (rti == null) Initialize();
				if (DebugConnection) GD.Print($"RTI connecting {rti.Application} {rti.ClientId} to {rti.Url}");
				rti.Connect();
				rti.Polling = Settings.Polling;
			}
		}

		public void OnRuntimeControl(string channelName, RuntimeControl message) {
			switch (message.ControlCase) {
				case RuntimeControl.ControlOneofCase.LoadScenario:
					scenarioParameterValues.Clear();
					foreach (var pair in message.LoadScenario.ParameterValues) {
						scenarioParameterValues[pair.Key] = pair.Value;
                    }
                    EmitSignal(SignalName.OnLoadScenario, message.LoadScenario.Name, scenarioParameterValues);
					if (CustomLoadScenario != null) {
						CustomLoadScenario(message.LoadScenario);
					} else {
						RTIScenario scenarioToLoad = null;
						foreach (var scenario in Scenarios) {
							if (scenario != null && scenario.Name == message.LoadScenario.Name) {
								scenarioToLoad = scenario;
								break;
							}
						}
						if (scenarioToLoad == null) {
							GD.PrintErr($"Scenario {message.LoadScenario.Name} not found");
							rti.PublishError($"Scenario {message.LoadScenario.Name} not found", RuntimeState.Loading);
						} else if (Scenario == scenarioToLoad && rti.State == RuntimeState.Playback) {
							if (DebugRuntimeControl) GD.Print($"Scenario already loaded for playback: {scenarioToLoad.Name}");
						} else {
							if (DebugRuntimeControl) GD.Print($"Load scenario {scenarioToLoad.Name}");
							Scenario = scenarioToLoad;
							if (GetTree().CurrentScene.Name == scenarioToLoad.Scene.ResourceName && rti.State == RuntimeState.Playback) {
								if (DebugRuntimeControl) GD.Print($"Scene already loaded for playback: {GetTree().CurrentScene.Name}");
							} else {
								var previousState = rti.State;
								rti.State = RuntimeState.Loading;
								Engine.TimeScale = 1;
								GetTree().ChangeSceneToPacked(scenarioToLoad.Scene);
								if (DebugRuntimeControl) GD.Print("Scene loaded");
								Time = 0;
								if (previousState == RuntimeState.Playback) {
									rti.State = RuntimeState.Playback;
									Engine.TimeScale = TimeScale;
								} else {
									rti.State = RuntimeState.Ready;
									// workaround: setting timescale 0 here seems to mess up physics
									Engine.TimeScale = 0.001;
								}
							}
						}
					}
					LastScenarioName = message.LoadScenario.Name;
					break;
				case RuntimeControl.ControlOneofCase.Start:
					if (DebugRuntimeControl) GD.Print("Start");
					GD.Print($"Current scene: {GetTree().CurrentScene.SceneFilePath}");
					if (CustomLoadScenario == null && IsHomeScene) {
						GD.Print("Cannot start - no scene loaded");
						rti.PublishError("No scene loaded", RuntimeState.Running);
					} else {
						if (rti.State != RuntimeState.Paused && rti.State != RuntimeState.PlaybackPaused) {
							Time = 0;
							TimeSyncMasterClientId = null;
						}
						rti.State = RuntimeState.Running;
						Engine.TimeScale = TimeScale;
						EmitSignal(SignalName.OnStart);
					}
					break;
				case RuntimeControl.ControlOneofCase.Pause:
					if (DebugRuntimeControl) GD.Print("Pause");
					rti.State = rti.State == RuntimeState.Playback ? RuntimeState.PlaybackPaused : RuntimeState.Paused;
					Engine.TimeScale = 0;
					break;
				case RuntimeControl.ControlOneofCase.End:
					if (rti.State == RuntimeState.Running) {
						if (DebugRuntimeControl) GD.Print("End");
						rti.State = RuntimeState.End;
						Engine.TimeScale = 0;
					} else if (rti.State == RuntimeState.Playback) {
						if (DebugRuntimeControl) GD.Print("End playback");
						rti.State = RuntimeState.PlaybackEnd;
						Engine.TimeScale = 0;
					} else {
						if (DebugRuntimeControl) GD.Print($"Unexpected end from state {rti.State}");
					}
					break;
				case RuntimeControl.ControlOneofCase.Play:
					if (DebugRuntimeControl) GD.Print("Play");
					if (rti.State != RuntimeState.PlaybackPaused) Time = 0;
					rti.State = RuntimeState.Playback;
					Engine.TimeScale = TimeScale;
					TimeSyncMasterClientId = null;
					break;
				case RuntimeControl.ControlOneofCase.SetTimeScale:
					if (DebugRuntimeControl) GD.Print($"Time scale {message.SetTimeScale.TimeScale}");
					TimeScale = (float)message.SetTimeScale.TimeScale;
					if (rti.State == RuntimeState.Running || rti.State == RuntimeState.Playback || rti.State == RuntimeState.Unknown) Engine.TimeScale = TimeScale;
                    EmitSignal(SignalName.TimeScaleChanged, TimeScale);
					break;
				case RuntimeControl.ControlOneofCase.Seek:
					if (DebugRuntimeControl) GD.Print($"Seek {message.Seek.Time}");
					Time = (float)message.Seek.Time;
					break;
				case RuntimeControl.ControlOneofCase.Stop:
					var stateBefore = rti.State;
					EmitSignal(SignalName.OnStop);
					if (CustomStop != null) {
						CustomStop?.Invoke();
					} else {
						rti.State = RuntimeState.Stopping;
						var toRemove = new List<string>();
						foreach (var id in entities.Keys) {
							var entity = entities[id];
							if (entity != null && entity.GetParent() != null && !entity.Persistent) {
								entity.GetParent().QueueFree();
								toRemove.Add(id);
							}
						}
						foreach (var id in toRemove) {
							entities.Remove(id);
						}
						foreach (var injectable in injectables.Values) {
							if (injectable != null && injectable.GetParent() != null) injectable.GetParent().QueueFree();
						}
						injectables.Clear();
						Engine.TimeScale = 0;
						TimeSyncMasterClientId = null;
					}
					switch (stateBefore) {
						case RuntimeState.Playback:
						case RuntimeState.PlaybackPaused:
						case RuntimeState.PlaybackEnd:
							rti.State = RuntimeState.PlaybackStopped;
							break;
						default:
							rti.State = RuntimeState.Stopped;
							break;
					}
					break;
				case RuntimeControl.ControlOneofCase.Reset:
					EmitSignal(SignalName.OnReset);
					if (CustomReset != null) {
                        CustomReset?.Invoke();
						rti.State = RuntimeState.Initial;
					} else {
                        if (DebugRuntimeControl) GD.Print("Reset");
                        entities.Clear();
                        geometries.Clear();
                        injectables.Clear();
                        scenarioParameterValues.Clear();
                        receivedCurrentScenario = null;
                        TimeSyncMasterClientId = null;
                        GetTree().ChangeSceneToFile(homeScenePath ?? initialScenePath);
                        rti.State = homeScenePath != null ? RuntimeState.Initial : RuntimeState.Unknown;
                        Engine.TimeScale = 1;
                        Time = 0;
                        Scenario = null;
					}
					break;
				case RuntimeControl.ControlOneofCase.TimeSync: {
						var wasTimeSyncMaster = IsTimeSyncMaster;
						TimeSyncMasterClientId = message.TimeSync.MasterClientId;
						var diff = Time - (float)message.TimeSync.Time;
						if (Math.Abs(diff) > 0.5f) {
							if (DebugRuntimeControl) GD.Print($"Time sync diff {diff}");
							Time = (float)message.TimeSync.Time;
						}
						if (Mathf.Abs(TimeScale - (float)message.TimeSync.TimeScale) > 0.01f) {
							if (DebugRuntimeControl) GD.Print($"Sync time scale to {message.TimeSync.TimeScale}");
							TimeScale = (float)message.TimeSync.TimeScale;
							if (rti.State == RuntimeState.Running || rti.State == RuntimeState.Playback) Engine.TimeScale = TimeScale;
                            EmitSignal(SignalName.TimeScaleChanged, TimeScale);
						}
						if (wasTimeSyncMaster && !IsTimeSyncMaster && DebugRuntimeControl) GD.Print($"Giving up time sync master to {TimeSyncMasterClientId}");
						break;
					}
				case RuntimeControl.ControlOneofCase.RequestCurrentScenario: {
						if (Scenario != null) {
							var currentScenario = new RuntimeControl.Types.LoadScenario {
								Name = Scenario.Name
							};
							foreach (var pair in scenarioParameterValues) {
								currentScenario.ParameterValues.Add(pair.Key, pair.Value);
							}
							Publish(RTIChannel.Control, new RuntimeControl {
								CurrentScenario = currentScenario
							});
						}
						break;
					}
				case RuntimeControl.ControlOneofCase.CurrentScenario: {
						receivedCurrentScenario = message.CurrentScenario;
						break;
					}
			}
			if (rti.State != lastState) EmitSignal(SignalName.StateChanged, rti.State.ToString());
			lastState = rti.State;
		}
		private RuntimeState lastState;

		private Dictionary<string, bool> mentionedScenario = new Dictionary<string, bool>();

		private void OnScenarios(string channelName, Scenarios message) {
			if (message.WhichCase == RTI.Proto.Scenarios.WhichOneofCase.RequestScenarios) {
				mentionedScenario.Clear();
				_ = RandomDelayPublishScenarios();
			} else if (message.WhichCase == RTI.Proto.Scenarios.WhichOneofCase.Scenario) {
				mentionedScenario[message.Scenario.Name] = true;
			}
		}

		// Using a random delay to avoid multiple instances of same simulator talking in each others mouths
		private async Task RandomDelayPublishScenarios() {
			await Task.Delay((int)(GD.RandRange(0.01, 0.1) * 1000));
			if (Scenarios.Count > 0 || ScenarioNames.Count > 0) {
				foreach (var scenario in Scenarios) {
					if (scenario != null && !mentionedScenario.ContainsKey(scenario.Name)) {
						rti.Publish(RTIChannel.Scenarios, new Scenarios {
							Scenario = scenario.ToProto()
						});
						mentionedScenario[scenario.Name] = true;
					}
				}
				foreach (var scenarioName in ScenarioNames) {
					if (!mentionedScenario.ContainsKey(scenarioName)) {
						rti.Publish(RTIChannel.Scenarios, new Scenarios {
							Scenario = new Inhumate.RTI.Proto.Scenario { Name = scenarioName }
						});
						mentionedScenario[scenarioName] = true;
					}
				}
			}
		}

		private void OnEntity(string channelName, Entity message) {
			var entity = GetEntityById(message.Id);
			if (entity != null && entity.Persistent && !entity.Owned) {
				if (message.Deleted) {
					if (DebugEntities) GD.Print($"Destroy deleted persistent entity {message.Id}: {entity.Name}", this);
					if (entity.GetParent() != null) entity.GetParent().QueueFree();
					UnregisterEntity(entity);
				} else {
					if (DebugEntities) GD.Print($"Update persistent entity {message.Id}: {entity.Name}", this);
					entity.SetPropertiesFromEntityData(message);
					entity.InvokeOnUpdated(message);
				}
			}
		}

		private void OnEntityOperation(string channelName, EntityOperation message) {
			switch (message.WhichCase) {
				case EntityOperation.WhichOneofCase.RequestUpdate: {
						foreach (var ent in entities.Values) {
							if (ent.Publishing) ent.RequestUpdate();
						}
						break;
					}
				case EntityOperation.WhichOneofCase.TransferOwnership: {
						var entity = GetEntityById(message.TransferOwnership.EntityId);
						if (entity != null) {
							if (entity.Owned && entity.OwnerClientId == ClientId && message.TransferOwnership.ClientId != ClientId) {
								if (DebugEntities) GD.Print($"Transfer entity {entity.Id} - releasing ownership");
								entity.ReleaseOwnership(message.TransferOwnership.ClientId);
                                EmitSignal(SignalName.OnEntityOwnershipReleased, entity.Id);
							} else if (!entity.Owned && entity.OwnerClientId != ClientId && message.TransferOwnership.ClientId == ClientId) {
								if (DebugEntities) GD.Print($"Transfer entity {entity.Id} - assuming ownership");
								entity.AssumeOwnership();
                                EmitSignal(SignalName.OnEntityOwnershipAssumed, entity.Id);
							} else if (entity.Owned) {
								GD.Print($"Weird ownership transfer of owned entity {entity.Id} to {message.TransferOwnership.ClientId}");
							}
						}
						break;
					}
				case EntityOperation.WhichOneofCase.AssumeOwnership: {
						var entity = GetEntityById(message.AssumeOwnership.EntityId);
						if (entity != null) {
							if (entity.Owned && message.AssumeOwnership.ClientId != ClientId && entity.OwnerClientId == ClientId) {
								entity.ReleaseOwnership(message.AssumeOwnership.ClientId);
                                EmitSignal(SignalName.OnEntityOwnershipReleased, entity.Id);
							}
							entity.LastOwnershipChangeTime = Godot.Time.GetTicksMsec() / 1000.0;
						}
						break;
					}
				case EntityOperation.WhichOneofCase.ReleaseOwnership: {
						var entity = GetEntityById(message.ReleaseOwnership.EntityId);
						if (entity != null) {
							if (entity.OwnerClientId == message.ReleaseOwnership.ClientId) entity.OwnerClientId = null;
							entity.LastOwnershipChangeTime = Godot.Time.GetTicksMsec() / 1000.0;
						}
						break;
					}
				case EntityOperation.WhichOneofCase.RequestPersistentOwnership:
					if (IsPersistentEntityOwner && message.RequestPersistentOwnership.Application == rti.Application) {
						PublishClaimPersistentEntityOwnership();
					}
					break;
				case EntityOperation.WhichOneofCase.ClaimPersistentOwnership:
					if (message.ClaimPersistentOwnership.Application == rti.Application) {
						persistentEntityOwnerClientId = message.ClaimPersistentOwnership.ClientId;
					}
					break;
			}
		}

		private async Task RandomDelayClaimPersistentEntityOwnership() {
			await Task.Delay((int)(GD.RandRange(0.2, 0.5) * 1000));
			if (persistentEntityOwnerClientId == null) {
				if (DebugRuntimeControl) GD.Print($"RTI claiming persistent entity ownership for {rti.Application}");
				persistentEntityOwnerClientId = rti.ClientId;
				PublishClaimPersistentEntityOwnership();
				Publish(RTIChannel.Clients, new Clients { RequestClients = new Google.Protobuf.WellKnownTypes.Empty() });
				foreach (var entity in entities.Values) {
					if (entity.Persistent && string.IsNullOrEmpty(entity.OwnerClientId)) {
						GD.Print($"Re-assume ownership of persistent entity {entity.Id}");
						entity.Owned = true;
						entity.OwnerClientId = ClientId;
						Publish(RTIChannel.EntityOperation, new EntityOperation {
							AssumeOwnership = new EntityOperation.Types.EntityClient {
								EntityId = entity.Id,
								ClientId = ClientId
							}
						});
						entity.InvokeOnOwnershipChanged();
					}
				}
			}
		}

		private void PublishClaimPersistentEntityOwnership() {
			rti.Publish(RTIChannel.EntityOperation, new EntityOperation {
				ClaimPersistentOwnership = new EntityOperation.Types.ApplicationClient {
					Application = rti.Application,
					ClientId = rti.ClientId
				}
			});
		}

		public void RegisterEntity(RTIEntity entity) {
			entities[entity.Id] = entity;
		}

		public void UnregisterEntity(RTIEntity entity) {
			entities.Remove(entity.Id);
		}

		public RTIEntity GetEntityById(string id) {
			if (entities.ContainsKey(id)) return entities[id];
			return null;
		}

		public IEnumerable<RTIEntity> GetEntitiesByType(string type) {
			return entities.Values.Where(entity => entity.EntityData.Type.ToLower() == type.ToLower());
		}

		private void OnGeometryOperation(string channelName, GeometryOperation message) {
			switch (message.WhichCase) {
				case GeometryOperation.WhichOneofCase.RequestUpdate: {
						foreach (var geometry in geometries.Values) {
							geometry.RequestUpdate();
						}
						break;
					}
				case GeometryOperation.WhichOneofCase.RequestPersistentOwnership:
					if (IsPersistentGeometryOwner && message.RequestPersistentOwnership.Application == rti.Application) {
						PublishClaimPersistentGeometryOwnership();
					}
					break;
				case GeometryOperation.WhichOneofCase.ClaimPersistentOwnership:
					if (message.ClaimPersistentOwnership.Application == rti.Application) {
						persistentGeometryOwnerClientId = message.ClaimPersistentOwnership.ClientId;
					}
					break;
			}
		}

		private async Task RandomDelayClaimPersistentGeometryOwnership() {
			await Task.Delay((int)(GD.RandRange(0.2, 0.5) * 1000));
			if (persistentGeometryOwnerClientId == null) {
				if (DebugRuntimeControl) GD.Print($"RTI claiming persistent geometry ownership for {rti.Application}");
				persistentGeometryOwnerClientId = rti.ClientId;
				PublishClaimPersistentGeometryOwnership();
				Publish(RTIChannel.Clients, new Clients { RequestClients = new Google.Protobuf.WellKnownTypes.Empty() });
				foreach (var geometry in geometries.Values) {
					if (geometry.Persistent) geometry.Owned = true;
				}
			}
		}

		private void PublishClaimPersistentGeometryOwnership() {
			rti.Publish(RTIChannel.GeometryOperation, new GeometryOperation {
				ClaimPersistentOwnership = new GeometryOperation.Types.ApplicationClient {
					Application = rti.Application,
					ClientId = rti.ClientId
				}
			});
		}

		public bool RegisterGeometry(RTIGeometry geometry) {
			if (geometries.ContainsKey(geometry.Id) && geometries[geometry.Id] != geometry) {
				return false;
			} else {
				geometries[geometry.Id] = geometry;
				return true;
			}
		}

		public void UnregisterGeometry(RTIGeometry geometry) {
			if (geometries.ContainsKey(geometry.Id) && geometries[geometry.Id] == geometry) {
				geometries.Remove(geometry.Id);
			}
		}

		public RTIGeometry GetGeometryById(string id) {
			if (geometries.ContainsKey(id)) return geometries[id];
			return null;
		}

		private void OnInjectableOperation(string channelName, InjectableOperation message) {
			if (message.WhichCase == InjectableOperation.WhichOneofCase.RequestUpdate) {
				foreach (var injectable in injectables.Values) injectable.Publish();
			}
		}

		private void OnInjectionOperation(string channelName, InjectionOperation message) {
			if (message.WhichCase == InjectionOperation.WhichOneofCase.RequestUpdate) {
				foreach (var injectable in injectables.Values) injectable.PublishInjections();
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.Inject) {
				var id = message.Inject.Injectable.ToLower();
				if (injectables.ContainsKey(id)) {
					injectables[id].Inject(message.Inject);
				} else {
					GD.Print($"Unknown injectable: {message.Inject.Injectable}");
				}
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.Disable) {
				if (GetInjectableAndInjection(message.Disable, out RTIInjectable injectable, out Injection injection)) {
					injectable.DisableInjection(injection);
				}
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.Enable) {
				if (GetInjectableAndInjection(message.Enable, out RTIInjectable injectable, out Injection injection)) {
					injectable.EnableInjection(injection);
				}
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.Start) {
				if (GetInjectableAndInjection(message.Start, out RTIInjectable injectable, out Injection injection)) {
					injectable.StartInjection(injection);
				}
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.End) {
				if (GetInjectableAndInjection(message.End, out RTIInjectable injectable, out Injection injection)) {
					injectable.EndInjection(injection);
				}
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.Stop) {
				if (GetInjectableAndInjection(message.Stop, out RTIInjectable injectable, out Injection injection)) {
					injectable.StopInjection(injection);
				}
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.Cancel) {
				if (GetInjectableAndInjection(message.Cancel, out RTIInjectable injectable, out Injection injection)) {
					injectable.CancelInjection(injection);
				}
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.Schedule) {
				if (GetInjectableAndInjection(message.Schedule.InjectionId, out RTIInjectable injectable, out Injection injection)) {
					injectable.ScheduleInjection(message.Schedule.EnableTime, injection);
				}
			} else if (message.WhichCase == InjectionOperation.WhichOneofCase.UpdateTitle) {
				if (GetInjectableAndInjection(message.UpdateTitle.InjectionId, out RTIInjectable injectable, out Injection injection)) {
					injectable.UpdateTitle(message.UpdateTitle.Title, injection);
				}
			}
		}

		public bool GetInjectableAndInjection(string injectionId, out RTIInjectable injectable, out Injection injection) {
			injectable = null;
			injection = null;
			foreach (var able in injectables.Values) {
				var on = able.GetInjection(injectionId);
				if (on != null) {
					injectable = able;
					injection = on;
					return true;
				}
			}
			return false;
		}

		public bool RegisterInjectable(RTIInjectable injectable) {
			var id = injectable.Name.ToString().ToLower();
			if (injectables.ContainsKey(id) && injectables[id] != injectable) {
				return false;
			} else {
				injectables[id] = injectable;
				return true;
			}
		}

		public void UnregisterInjectable(RTIInjectable injectable) {
			var id = injectable.Name.ToString().ToLower();
			if (injectables.ContainsKey(id) && injectables[id] == injectable) {
				injectables.Remove(id);
			}
		}

		private void OnCommands(string channelName, Commands message) {
			switch (message.WhichCase) {
				case Inhumate.RTI.Proto.Commands.WhichOneofCase.RequestCommands:
					foreach (var command in commands.Values) {
						Publish(channelName, new Commands { Command = command });
					}
					break;
				case Inhumate.RTI.Proto.Commands.WhichOneofCase.Execute: {
						CommandResponse response = null;
						var name = message.Execute.Name.ToLower();
						var specific = false;
						// Allow command names with prefixed application name, e.g. mysim/dostuff
						if (name.StartsWith(Client.Application.ToLower() + "/")) {
							name = name.Substring(Client.Application.Length + 1);
							specific = true;
						}
						if (commands.TryGetValue(name, out Command command) && commandHandlers.TryGetValue(name, out CommandHandler handler)) {
							response = handler(command, message.Execute);
						} else if (specific) {
							response = new CommandResponse { Failed = true, Message = $"Unknown command {name}" };
						}
						if (!string.IsNullOrEmpty(message.Execute.TransactionId)) {
							if (response != null) {
								response.TransactionId = message.Execute.TransactionId;
								Publish(channelName, new Commands { Response = response });
							} else {
								commandTransactionChannel[message.Execute.TransactionId] = channelName;
							}
						}
						break;
					}
			}
		}

		public void ExecuteCommandInternal(string name, ExecuteCommand executeCommand = null) {
			name = name.ToLower();
			if (executeCommand == null) executeCommand = new ExecuteCommand { Name = name };
			if (commands.TryGetValue(name, out Command command) && commandHandlers.TryGetValue(name, out CommandHandler handler)) {
				var response = handler(command, executeCommand);
				if (response.Failed) {
					GD.Print($"Command {name} failed: {response.Message}", this);
				} else if (!string.IsNullOrEmpty(response.Message)) {
					GD.Print($"Command {name}: {response.Message}", this);
				}
			} else {
				GD.Print($"Unknown command {name}", this);
			}
		}

		public void RegisterCommands(Node node) {
			var methods = node.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (var method in methods) {
				var commandAttribute = method.GetCustomAttribute<RTICommandAttribute>();
				if (commandAttribute != null) {
					var command = new Command { Name = commandAttribute.Name };
					if (string.IsNullOrWhiteSpace(command.Name)) command.Name = method.Name;
					var argumentAttributes = method.GetCustomAttributes<RTICommandArgumentAttribute>();
					foreach (var argumentAttribute in argumentAttributes) {
						argumentAttribute.AddToCommand(command);
					}
					if (typeof(CommandResponse).IsAssignableFrom(method.ReturnType)) {
						if (method.GetParameters().Length == 2) {
							RegisterCommand(command, (cmd, exe) => {
								return (CommandResponse)method.Invoke(node, new object[] { cmd, exe });
							});
						} else if (method.GetParameters().Length == 0) {
							RegisterCommand(command, (cmd, exe) => {
								return (CommandResponse)method.Invoke(node, new object[] { });
							});
						} else {
							GD.PrintErr($"Invalid command method {method.Name} in {node.GetType().Name}: parameters");
						}
					} else if (method.ReturnType == typeof(void)) {
						if (method.GetParameters().Length == 2) {
							RegisterCommand(command, (cmd, exe) => {
								method.Invoke(node, new object[] { cmd, exe });
								return new CommandResponse();
							});
						} else if (method.GetParameters().Length == 0) {
							RegisterCommand(command, (cmd, exe) => {
								method.Invoke(node, new object[] { });
								return new CommandResponse();
							});
						} else {
							GD.PrintErr($"Invalid command method {method.Name} in {node.GetType().Name}: parameters");
						}
					} else {
						GD.PrintErr($"Invalid command method {method.Name} in {node.GetType().Name}: return type");
					}
				}
			}
		}

		public delegate void DefaultResponseCommandHandler(Command command, ExecuteCommand exec);
		public bool RegisterCommand(Command command, DefaultResponseCommandHandler handler) {
			return RegisterCommand(command, (cmd, exe) => {
				handler(cmd, exe);
				return new CommandResponse();
			});
		}

		public bool RegisterCommand(Command command, CommandHandler handler) {
			var name = command.Name.ToLower();
			if (commands.ContainsKey(name) && commands[name] != command) {
				return false;
			} else {
				commands[name] = command;
				commandHandlers[name] = handler;
				WhenConnectedOnce(() => Publish(RTIChannel.Commands, new Commands { Command = command }));
				return true;
			}
		}

		public void UnregisterCommand(Command command) {
			UnregisterCommand(command.Name);
		}

		public void UnregisterCommand(string name) {
			commands.Remove(name.ToLower());
			commandHandlers.Remove(name.ToLower());
		}

		public void PublishCommandResponse(ExecuteCommand exec, CommandResponse response) {
			PublishCommandResponse(exec.TransactionId, response);
		}

		public void PublishCommandResponse(string transactionId, CommandResponse response) {
			if (string.IsNullOrEmpty(transactionId) || !Connected) return;
			response.TransactionId = transactionId;
			var channel = RTIChannel.Commands;
			commandTransactionChannel.TryGetValue(transactionId, out channel);
			commandTransactionChannel.Remove(transactionId);
			Publish(channel, new Commands { Response = response });
		}

		public string GetScenarioParameterValue(string name) {
			if (scenarioParameterValues.ContainsKey(name)) return scenarioParameterValues[name];
			if (Scenario != null) {
				var parameter = Scenario.Parameters.Where(p => p.Name == name).FirstOrDefault();
				if (parameter != null) return parameter.DefaultValue;
			}
			return null;
		}

		public void OnClientDisconnect(string channel, object clientId) {
			if (clientId == null) return;
			if (TimeSyncMasterClientId == clientId.ToString()) {
				GD.Print("Time sync master disconnected");
				TimeSyncMasterClientId = null;
			}
			if (persistentEntityOwnerClientId == clientId.ToString()) {
				GD.Print("Persistent entity owner disconnected");
				foreach (var entity in entities.Values) {
					if (entity.Persistent && entity.OwnerClientId == clientId.ToString()) {
						entity.OwnerClientId = null;
					}
				}
				persistentEntityOwnerClientId = null;
				QueryPersistentEntityOwner();
			}
			if (persistentGeometryOwnerClientId == clientId.ToString()) {
				GD.Print("Persistent geometry owner disconnected");
				persistentGeometryOwnerClientId = null;
				QueryPersistenGeometryOwner();
			}
		}

		public void Disconnect() {
			if (connected && DebugConnection) GD.Print($"RTI Disconnect");
			connected = false;
			if (rti != null) {
				rti.Disconnect();
			}
			if (LastErrorChannel == "connection") {
				LastError = null;
				LastErrorChannel = null;
			}
			EmitSignal(SignalName.OnDisconnected);
            everConnected = false;
		}

		public override void _Process(double delta) {
			if (rti != null && rti.Polling) {
				PollCount = rti.Poll(MaxPollCount);
			}
			if (rti != null && (rti.State == RuntimeState.Running || rti.State == RuntimeState.Playback || rti.State == RuntimeState.Unknown)) {
				Time += delta;
			}
			if (_queued) {
				lock (_backlog) {
					var tmp = _actions;
					_actions = _backlog;
					_backlog = tmp;
					_queued = false;
				}

				foreach (var action in _actions)
					action?.Invoke();

				_actions.Clear();
			}
			if (!inhibitTimeSyncMaster && rti != null && rti.State == RuntimeState.Running
					&& ((TimeSyncMasterClientId == null && Time >= 2f && Mathf.Abs(Time - lastTimeSyncTime) >= 1f + GD.RandRange(0.5f, 1.5f))
					|| (TimeSyncMasterClientId == rti.ClientId && Mathf.Abs(Time - lastTimeSyncTime) >= 1f))) {
				lastTimeSyncTime = Time;
				if (rti.IsConnected) {
					rti.Publish(RTIChannel.Control, new RuntimeControl {
						TimeSync = new RuntimeControl.Types.TimeSync {
							Time = Time,
							TimeScale = TimeScale,
							MasterClientId = rti.ClientId
						}
					});
					if (DebugRuntimeControl && TimeSyncMasterClientId != rti.ClientId) GD.Print($"Claiming time sync master");
				}
			}
			if (Settings.LateJoin && State == RuntimeState.Initial && rti != null && receivedCurrentScenario != null) {
				var siblings = rti.KnownClients.Where(c => c.Application == rti.Application && c.Id != rti.ClientId);
				foreach (var sibling in siblings) {
					if (sibling.State >= RuntimeState.Loading) {
						if (DebugRuntimeControl) GD.Print($"Late join load scenario");
						OnRuntimeControl("internal", new RuntimeControl {
							LoadScenario = receivedCurrentScenario
						});
						break;
					}
				}
			}
			if (Settings.LateJoin && State == RuntimeState.Ready) {
				var siblings = rti.KnownClients.Where(c => c.Application == rti.Application && c.Id != rti.ClientId);
				foreach (var sibling in siblings) {
					if (sibling.State == RuntimeState.Running) {
						if (DebugRuntimeControl) GD.Print($"Late join start");
						OnRuntimeControl("internal", new RuntimeControl {
							Start = new Google.Protobuf.WellKnownTypes.Empty()
						});
					}
					break;
				}
			}
		}

		public void RunOrQueue(Action action) {
			if (rti.Polling && Thread.CurrentThread.ManagedThreadId == _mainThreadId) action?.Invoke();
			else QueueOnMainThread(action);
		}

		// as inspired from ThreadDispatcher.cs from common

		public static void QueueOnMainThread(Action action) {
			lock (_backlog) {
				_backlog.Add(action);
				_queued = true;
			}
		}

		static volatile bool _queued = false;
		static List<Action> _backlog = new List<Action>(8);
		static List<Action> _actions = new List<Action>(8);

		static RTIConnection _instance;
	}

}
