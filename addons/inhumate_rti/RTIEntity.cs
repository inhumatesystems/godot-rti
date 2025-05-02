using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Inhumate.RTI;
using Inhumate.RTI.Proto;

namespace Inhumate.GodotRTI {

    [GlobalClass, Icon("res://addons/inhumate_rti/icons/rti_entity.svg")]
    public partial class RTIEntity : Node {

        [Export] public string Type = "";
        [Export] public EntityCategory Category;
        [Export] public EntityDomain Domain;
        [Export] public LVCCategory Lvc;
        [Export] public Vector3 Center;
        [Export] public Vector3 Size;
        [Export] public Godot.Color Color;
        [Export] public bool TitleFromName = true;
        //[HideIf("titleFromName")]
        [Export] public string Title;
        //[Tooltip("Unique ID used to identify this entity. Leave blank to generate a random ID when the entity is created.")]
        [Export] public string Id;
        //[Tooltip("Interval in seconds between periodic publishing. Set to 0 to only publish on create/destroy/update requests.")]
        [Export] public float PublishInterval = 0f;

        public bool Persistent { get; internal set; }
        public bool Published { get; internal set; }
        public bool Deleted { get; internal set; }
        public bool Owned { get; internal set; } = true;
        public string OwnerClientId { get; internal set; }
        public double LastOwnershipChangeTime { get; internal set; }
        internal bool Spawned;
        public Entity LastReceivedData { get; private set; }

        // TODO temp testing - how to handle? Was isActiveAndEnabled/gameObject.activeInHierarchy in Unity
        public bool Enabled = true;

        public bool Publishing {
            get {
                return Owned && Published && (RTI.State == RuntimeState.Running || RTI.State == RuntimeState.Unknown);
            }
        }

        public bool Receiving {
            get {
                return !Publishing && (!Owned || (RTI.State != RuntimeState.Running && RTI.State != RuntimeState.Unknown));
            }
        }

        public Client OwnerClient {
            get {
                return OwnerClientId != null ? RTI.Client.GetClient(OwnerClientId) : null;
            }
        }

        [Signal] public delegate void OnCreatedEventHandler();
        [Signal] public delegate void OnUpdatedEventHandler();
        [Signal] public delegate void OnOwnershipChangedEventHandler();

        public string CommandsChannelName => $"{RTIChannel.Commands}/{Id}";
        public Command[] Commands => commands.Values.ToArray();
        private Dictionary<string, Command> commands = new Dictionary<string, Command>();
        private Dictionary<string, RTIConnection.CommandHandler> commandHandlers = new Dictionary<string, RTIConnection.CommandHandler>();
        private UntypedListener commandsListener;
        private UntypedListener ownCommandsListener;

        private Node3D node;
        private RTIPosition position;

        protected static RTIConnection RTI => RTIConnection.Instance;

        private bool updateRequested;
        public void RequestUpdate() {
            updateRequested = true;
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
                                GD.PrintErr($"Invalid entity command method {method.Name} in {node.GetType().Name}: parameters");
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
                                GD.PrintErr($"Invalid entity command method {method.Name} in {node.GetType().Name}: parameters");
                            }
                        } else {
                            GD.PrintErr($"Invalid entity command method {method.Name} in {node.GetType().Name}: return type");
                        }
                    }
                }
        }

        public bool RegisterCommand(Command command, RTIConnection.DefaultResponseCommandHandler handler) {
            return RegisterCommand(command, (cmd, exe) => {
                handler(cmd, exe);
                return new CommandResponse();
            });
        }

        public bool RegisterCommand(Command command, RTIConnection.CommandHandler handler) {
            if (commandsListener == null) {
                RTI.Client.RegisterChannel(new Channel {
                    Name = CommandsChannelName,
                    DataType = typeof(Commands).Name,
                    Ephemeral = true
                });
                commandsListener = RTI.Subscribe<Commands>(CommandsChannelName, OnCommandsMessage);
                ownCommandsListener = RTI.Subscribe<Commands>(RTI.Client.OwnChannelPrefix + CommandsChannelName, OnCommandsMessage);
            }
            var name = command.Name.ToLower();
            if (commands.ContainsKey(name) && commands[name] != command) {
                return false;
            } else {
                commands[name] = command;
                commandHandlers[name] = handler;
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
            if (string.IsNullOrEmpty(transactionId) || !RTI.Connected) return;
            response.TransactionId = transactionId;
            RTI.Publish(CommandsChannelName, new Commands { Response = response });
        }

        public void AssumeOwnership() {
            if (Owned) return;
            RTI.Publish(RTIChannel.EntityOperation, new EntityOperation {
                AssumeOwnership = new EntityOperation.Types.EntityClient {
                    EntityId = Id,
                    ClientId = RTI.ClientId
                }
            });
            Owned = true;
            OwnerClientId = RTI.ClientId;
            InvokeOnOwnershipChanged();
        }

        public void ReleaseOwnership(string newOwnerClientId = null) {
            if (!Owned) return;
            RTI.Publish(RTIChannel.EntityOperation, new EntityOperation {
                ReleaseOwnership = new EntityOperation.Types.EntityClient {
                    EntityId = Id,
                    ClientId = RTI.ClientId
                }
            });
            Owned = false;
            OwnerClientId = newOwnerClientId;
            InvokeOnOwnershipChanged();
        }

        public override void _EnterTree() {
            if (RTI.State == RuntimeState.Loading || (RTI.Time < float.Epsilon && RTI.State != RuntimeState.Running && !Spawned)) {
                Persistent = true;
            }
            if (string.IsNullOrWhiteSpace(Id)) Id = GenerateId();
            OwnerClientId = RTI.ClientId;
            node = GetParentOrNull<Node3D>();
            if (node != null) position = node.FindChildOfType<RTIPosition>();
        }

        public override void _Ready() {
            if (RTI.GetEntityById(Id) == null) {
                RTI.RegisterEntity(this);
            }
        }

        string GenerateId() {
            if (Persistent) {
                string path = "";
                var current = GetParent();
                while (current != null) {
                    path = "/" + current.Name.ToString().Trim().Replace(" (", "").Replace(")", "").Replace(" ", "_") + path;
                    current = current.GetParent();
                }
                return RTI.Application + path;
            } else {
                return Guid.NewGuid().ToString();
            }
        }

        private double lastPublishTime;

        public override void _Process(double deltaTime) {
            if (Owned && !Deleted && RTI.Connected && RTI.persistentEntityOwnerClientId != null && (RTI.State == RuntimeState.Running || RTI.State == RuntimeState.Unknown)) {
                if (!Published) {
                    if (Persistent && RTI.persistentEntityOwnerClientId != null && !RTI.IsPersistentEntityOwner) {
                        Owned = false;
                        OwnerClientId = RTI.persistentEntityOwnerClientId;
                        InvokeOnOwnershipChanged();
                    } else {
                        if (RTI.DebugEntities) GD.Print($"RTI publish entity {Id}");
                        Publish();
                    }
                    Published = true;
                } else if (updateRequested || (PublishInterval > 1e-5f && Time.GetTicksMsec() / 1000.0 - lastPublishTime > PublishInterval)) {
                    updateRequested = false;
                    Publish();
                }
            } else if (Owned && RTI.State == RuntimeState.Playback) {
                if (!Published && GetParent() != null) GetParent().QueueFree();
            }
        }

        void OnCommandsMessage(string channelName, Commands message) {
            if (!Enabled || !Owned) return;
            switch (message.WhichCase) {
                case Inhumate.RTI.Proto.Commands.WhichOneofCase.RequestCommands:
                    foreach (var command in commands.Values) {
                        RTI.Publish(channelName, new Commands { Command = command });
                    }
                    break;
                case Inhumate.RTI.Proto.Commands.WhichOneofCase.Execute: {
                        CommandResponse response = null;
                        if (commands.TryGetValue(message.Execute.Name.ToLower(), out Command command) && commandHandlers.TryGetValue(message.Execute.Name.ToLower(), out RTIConnection.CommandHandler handler)) {
                            response = handler(command, message.Execute);
                        } else {
                            response = new CommandResponse { Failed = true, Message = $"Unknown command {message.Execute.Name}" };
                        }
                        if (response != null && !string.IsNullOrEmpty(message.Execute.TransactionId)) {
                            response.TransactionId = message.Execute.TransactionId;
                            RTI.Publish(channelName, new Commands { Response = response });
                        }
                        break;
                    }
            }
        }

        public void ExecuteCommandInternal(string name, ExecuteCommand executeCommand = null) {
            name = name.ToLower();
            if (executeCommand == null) executeCommand = new ExecuteCommand { Name = name };
            if (commands.TryGetValue(name, out Command command) && commandHandlers.TryGetValue(name, out RTIConnection.CommandHandler handler)) {
                var response = handler(command, executeCommand);
                if (response.Failed) {
                    GD.Print($"Command {name} failed: {response.Message}");
                } else if (!string.IsNullOrEmpty(response.Message)) {
                    GD.Print($"Command {name}: {response.Message}");
                }
            } else {
                GD.Print($"Unknown entity command {name}");
            }
        }

        // TODO see Enabled above

        // void OnEnable() {
        //     if (owned && published && !deleted && RTI.Connected) Publish();
        // }

        // void OnDisable() {
        //     if (owned && published && !deleted && Enabled && RTI.Connected) Publish();
        // }

        void Publish() {
            lastPublishTime = Time.GetTicksMsec() / 1000.0;
            RTI.Publish(RTIChannel.Entity, EntityData);
        }

        public override void _Notification(int what) {
            if (what == NotificationWMCloseRequest) {
                if (Published && Owned && (!Persistent || RTI.Quitting)) _ExitTree();
            }
        }

        public override void _ExitTree() {
            bool hopefullyOtherClientTakesOver = RTI.Quitting && Persistent && Publishing && RTI.Client.KnownClients.Count(c => c.Application == RTI.Client.Application) > 1;
            if (Published && Owned && RTI.Connected && !hopefullyOtherClientTakesOver) {
                if (RTI.DebugEntities) GD.Print($"RTI publish deleted entity {Id}");
                Deleted = true;
                Publish();
            }
            if (commandsListener != null) {
                RTI.Unsubscribe(commandsListener);
                commandsListener = null;
            }
            if (ownCommandsListener != null) {
                RTI.Unsubscribe(ownCommandsListener);
                ownCommandsListener = null;
            }
            RTI.UnregisterEntity(this);
        }

        // public Bounds GetBoundsFromRenderers() {
        //     var b = new Bounds(Vector3.zero, Vector3.zero);
        //     RecurseEncapsulate(transform, ref b);
        //     return b;

        //     void RecurseEncapsulate(Transform child, ref Bounds bounds) {
        //         var mesh = child.GetComponent<MeshFilter>();
        //         if (mesh && mesh.sharedMesh) {
        //             var lsBounds = mesh.sharedMesh.bounds;
        //             var wsMin = child.TransformPoint(lsBounds.center - lsBounds.extents);
        //             var wsMax = child.TransformPoint(lsBounds.center + lsBounds.extents);
        //             bounds.Encapsulate(transform.InverseTransformPoint(wsMin));
        //             bounds.Encapsulate(transform.InverseTransformPoint(wsMax));
        //         }
        //         foreach (Transform grandChild in child.transform) {
        //             RecurseEncapsulate(grandChild, ref bounds);
        //         }
        //     }
        // }

        // public Bounds GetBoundsFromColliders() {
        //     var b = new Bounds(Vector3.zero, Vector3.zero);
        //     RecurseEncapsulate(transform, ref b);
        //     return b;

        //     void RecurseEncapsulate(Transform child, ref Bounds bounds) {
        //         var collider = child.GetComponent<Collider>();
        //         if (collider) {
        //             if (collider is BoxCollider) {
        //                 BoxCollider box = (BoxCollider)collider;
        //                 bounds.Encapsulate(box.center - box.size / 2);
        //                 bounds.Encapsulate(box.center + box.size / 2);
        //             } else {
        //                 bounds.Encapsulate(transform.InverseTransformPoint(collider.bounds.center - collider.bounds.extents));
        //                 bounds.Encapsulate(transform.InverseTransformPoint(collider.bounds.center + collider.bounds.extents));
        //             }
        //         }
        //         foreach (Transform grandChild in child.transform) {
        //             RecurseEncapsulate(grandChild, ref bounds);
        //         }
        //     }
        // }

        internal Entity EntityData {
            get {
                Inhumate.RTI.Proto.Color col = null;
                if (Color.A > float.Epsilon || Color.R > float.Epsilon || Color.G > float.Epsilon || Color.B > float.Epsilon) {
                    col = new Inhumate.RTI.Proto.Color {
                        Red = (int)Math.Round(Color.R * 255),
                        Green = (int)Math.Round(Color.G * 255),
                        Blue = (int)Math.Round(Color.B * 255)
                    };
                }
                return new Entity {
                    Id = Id,
                    OwnerClientId = RTI.ClientId,
                    Type = Type,
                    Category = Category,
                    Domain = Domain,
                    Lvc = Lvc,
                    Dimensions = Size.Length() < 1e-5 && Center.Length() < 1e-5
                        ? null
                        : new Entity.Types.Dimensions {
                            Length = Size.Z,
                            Width = Size.X,
                            Height = Size.Y,
                            Center = new EntityPosition.Types.LocalPosition {
                                X = Center.X,
                                Y = Center.Y,
                                Z = -Center.Z
                            }
                        },
                    Color = col,
                    Title = TitleFromName ? GetParent().Name.ToString() : !string.IsNullOrWhiteSpace(Title) ? Title : "",
                    Position = position != null ? RTIPosition.PositionMessageFromNode(node) : null,
                    Disabled = !Enabled,
                    Deleted = Deleted
                };
            }
        }

        internal void SetPropertiesFromEntityData(Entity data) {
            LastReceivedData = data;
            Id = data.Id;
            OwnerClientId = data.OwnerClientId;
            Deleted = data.Deleted;
            Type = data.Type;
            Category = data.Category;
            Domain = data.Domain;
            Lvc = data.Lvc;
            if (data.Dimensions != null) {
                Size = new Vector3(data.Dimensions.Width, data.Dimensions.Height, data.Dimensions.Length);
                if (data.Dimensions.Center != null) {
                    Center = new Vector3(data.Dimensions.Center.X, data.Dimensions.Center.Y, data.Dimensions.Center.Z);
                }
            }
            if (data.Color != null) {
                Color = new Godot.Color(data.Color.Red / 255f, data.Color.Green / 255f, data.Color.Blue / 255f);
            }
            if (data.Disabled && Enabled) Enabled = false;
            if (!data.Disabled && !Enabled) Enabled = true;
        }

        internal void InvokeOnCreated(Entity data) {
            EmitSignal(SignalName.OnCreated);
        }

        internal void InvokeOnUpdated(Entity data) {
            EmitSignal(SignalName.OnUpdated);
        }

        internal void InvokeOnOwnershipChanged() {
            EmitSignal(SignalName.OnOwnershipChanged);
        }

    }

}
