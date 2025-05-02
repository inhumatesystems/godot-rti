using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Inhumate.RTI;
using Inhumate.RTI.Proto;
using Godot;

namespace Inhumate.GodotRTI {

	public partial class RTISpawner : Node {
		public const string SpawnAllocationChannel = "rti/spawn";

		[Export] public Godot.Collections.Dictionary<string, PackedScene> SpawnableEntities = new();

		[Export] public PackedScene Unknown;
		[Export] public PackedScene Player;

		[Export] public Godot.Collections.Array<Node3D> PlayerSpawnPoints = new();

		private Node3D player;
		private Dictionary<int, string> spawnPointAllocations = new();
		private int allocatedSpawnPointIndex = -1;
		private Node3D allocatedSpawnPoint { get { return allocatedSpawnPointIndex >= 0 ? PlayerSpawnPoints[allocatedSpawnPointIndex] : null; } }
		private Inhumate.RTI.UntypedListener spawnListener;
		private double startTime;

		public bool requestUpdatesOnStart = true;

		[Signal] public delegate void OnEntityCreatedEventHandler(string entityId);
		[Signal] public delegate void OnSpawnPlayerEventHandler(string entityId);

		protected static RTIConnection RTI => RTIConnection.Instance;
		private Inhumate.RTI.UntypedListener listener;

		public override void _EnterTree() {
			GD.Seed((ulong)RTI.ClientId.GetHashCode());
			listener = RTI.Subscribe<Inhumate.RTI.Proto.Entity>(RTIChannel.Entity, (channel, message) => {
				OnEntity(message);
			});
		}

		public override void _ExitTree() {
			RTI.Unsubscribe(listener);
		}

		public override void _Ready() {
			RTI.OnStart += OnStart;
			if (RTI.State == RuntimeState.Unknown) OnStart();
			if (PlayerSpawnPoints.Count > 1) {
				spawnListener = RTI.Subscribe(SpawnAllocationChannel, OnSpawnAllocation);
				RTI.Publish(SpawnAllocationChannel, "?");
				_ = RandomDelayAllocateSpawnPoint();
			}
		}

		void OnDestroy() {
			RTI.OnStart -= OnStart;
			if (spawnListener != null) RTI.Unsubscribe(spawnListener);
		}

		void OnStart() {
			startTime = Time.GetTicksMsec() / 1000.0;
			if (player == null && Player != null && PlayerSpawnPoints.Count == 1) {
				SpawnPlayer(PlayerSpawnPoints[0]);
			} else if (player == null && Player != null && PlayerSpawnPoints.Count == 0) {
				GD.PrintErr("RTISpawner has no spawn points");
			}
			if (requestUpdatesOnStart) {
				RTI.WhenConnected(RequestUpdates);
			}
		}

		public override void _Process(double deltaTime) {
			if ((RTI.State == RuntimeState.Running || RTI.State == RuntimeState.Unknown) && player == null && Player != null && PlayerSpawnPoints.Count > 1) {
				if (allocatedSpawnPoint == null && Time.GetTicksMsec() / 1000.0 - startTime > 2f && spawnPointAllocations.Count < PlayerSpawnPoints.Count) {
					GD.PrintErr("Spawn point allocation timeout");
					AllocateSpawnPoint();
				}
				if (allocatedSpawnPoint != null) {
					SpawnPlayer(allocatedSpawnPoint);
				}
			}
		}

		void SpawnPlayer(Node3D spawnPoint) {
			if (RTI.DebugEntities) GD.Print($"Spawning player at {spawnPoint.Name}");
			player = Player.Instantiate<Node3D>();
			player.GlobalTransform = spawnPoint.GlobalTransform;

			RTIEntity entity = player.FindChildOfType<RTIEntity>();
			if (entity == null) {
				GD.PrintErr($"Player {player.Name} has no RTIEntity");
			} else {
				entity.Spawned = true;
			}
			AddChild(player);
            EmitSignal(SignalName.OnSpawnPlayer, entity?.Id);
		}

		private async Task RandomDelayAllocateSpawnPoint() {
			await Task.Delay((int)(GD.RandRange(0.2, 0.5) * 1000));
			AllocateSpawnPoint();
		}

		private void AllocateSpawnPoint() {
			for (int i = 0; i < PlayerSpawnPoints.Count; i++) {
				if (!spawnPointAllocations.ContainsKey(i)) {
					RTI.Publish(SpawnAllocationChannel, $"{i} {RTI.ClientId}");
					if (RTI.DebugEntities) GD.Print($"Allocating spawn point {i}");
					allocatedSpawnPointIndex = i;
					break;
				}
			}
		}

		private void RequestUpdates() {
			RTI.Publish(RTIChannel.EntityOperation, new EntityOperation {
				RequestUpdate = new Google.Protobuf.WellKnownTypes.Empty()
			});
		}

		protected void OnSpawnAllocation(string channel, object message) {
			if (message.ToString() == "?") {
				if (allocatedSpawnPoint != null) RTI.Publish(SpawnAllocationChannel, $"{allocatedSpawnPointIndex} {RTI.ClientId}");
			} else {
				var parts = message.ToString().Split(' ');
				var index = int.Parse(parts[0]);
				var clientId = parts[1];
				if (index == allocatedSpawnPointIndex && clientId != RTI.ClientId) {
					GD.Print($"Spawn point conflict with {clientId}");
					if (clientId.GetHashCode() < RTI.ClientId.GetHashCode()) _ = RandomDelayAllocateSpawnPoint();
				}
				if (RTI.DebugEntities && clientId != RTI.ClientId) GD.Print($"Received spawn point allocation {index} {clientId}");
				spawnPointAllocations[index] = clientId;
				if (allocatedSpawnPoint == null && spawnPointAllocations.Count == PlayerSpawnPoints.Count) {
					GD.Print("All spawn points are allocated");
				}
			}
		}

		protected void OnEntity(Entity message) {
			var id = message.Id;
			var entity = RTI.GetEntityById(id);
			if (entity != null) {
				if (!entity.Persistent && !entity.Owned) {
					if (message.Deleted) {
						if (RTI.DebugEntities) GD.Print($"Destroy deleted entity {message.Id}: {entity.Name}");
						entity.Deleted = true;
						if (entity.GetParent() != null) entity.GetParent().QueueFree();
						RTI.UnregisterEntity(entity);
					} else {
						if (RTI.DebugEntities) GD.Print($"Update entity {id}: {entity.Name}");
						entity.SetPropertiesFromEntityData(message);
						entity.InvokeOnUpdated(message);
					}
				}
			} else if (!message.Deleted) {
				CreateEntity(message);
			}
		}

		protected void CreateEntity(Entity data) {
			if (string.IsNullOrWhiteSpace(data.Id)) {
				GD.Print($"Received entity with no id");
				return;
			}
			PackedScene prefab = null;
			if (SpawnableEntities.ContainsKey(data.Type)) {
				if (RTI.DebugEntities) GD.Print($"Create entity id {data.Id} type {data.Type}");
				prefab = SpawnableEntities[data.Type];
			}
			if (prefab == null) {
				foreach (var key in SpawnableEntities.Keys) {
					if (key.Contains("*") && key.Substring(0, key.IndexOf("*")) == data.Type.Substring(0, key.IndexOf("*"))) {
						if (RTI.DebugEntities) GD.Print($"Create entity id {data.Id} type {data.Type} (matching {key})");
						prefab = SpawnableEntities[key];
					}
				}
			}
			if (prefab == null && Unknown != null) {
				GD.Print($"Create entity id {data.Id} unknown type {data.Type}");
				prefab = Unknown;
			}
			if (prefab == null) {
				GD.Print($"Can't create entity id {data.Id} unknown type {data.Type}");
				return;
			}
			var node = prefab.Instantiate<Node3D>();

			RTIEntity entity = node.FindChildOfType<RTIEntity>();
			if (entity == null) {
				GD.PrintErr($"Spawned {node.Name} has no RTIEntity");
			} else {
				entity.Spawned = true;
				entity.Id = data.Id;
				RTI.RegisterEntity(entity);
				entity.SetPropertiesFromEntityData(data);
				entity.Published = true;
				entity.Owned = data.OwnerClientId == RTI.ClientId;
				entity.OwnerClientId = data.OwnerClientId;
				node.Name = $"{entity.Type} {entity.Id}";
				if (data.Position != null) RTIPosition.ApplyPositionMessageToNode(data.Position, entity.GetOwner<Node3D>());
				AddChild(node);
				entity.InvokeOnCreated(data);
                EmitSignal(SignalName.OnEntityCreated, entity.Id);
			}
		}
	}

}
