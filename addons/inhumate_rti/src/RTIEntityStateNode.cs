using System.Collections.Generic;
using Google.Protobuf;
using Inhumate.RTI.Proto;
using Godot;

namespace Inhumate.GodotRTI {

    public abstract partial class RTIEntityStateNode<T> : Node where T : IMessage<T>, new() {

        public abstract string ChannelName { get; }

        public bool Publishing {
            get {
                return Entity != null && Entity.Publishing;
            }
        }

        public bool Receiving {
            get {
                return Entity != null && Entity.Receiving;
            }
        }

        protected RTIConnection RTI => RTIConnection.Instance;

        protected RTIEntity Entity;

        // Note that these are not shared between derived classes, because this is a generic class.
        // Unlike the Unreal client equivalent RTIEntityStateComponent.
        private static Dictionary<string, List<RTIEntityStateNode<T>>> instances = new Dictionary<string, List<RTIEntityStateNode<T>>>();
        private static bool subscribed;
        private static Inhumate.RTI.UntypedListener listener;
        private static bool registeredChannel;

        private bool warnedIdNotFound;

        public override void _Ready() {
            Entity = GetParent().FindChildOfType<RTIEntity>();
            if (Entity == null) {
                GD.PrintErr($"{GetType().Name} with owner {GetParent().Name} has no entity");
                return;
            }
            Entity.RegisterCommands(this);
            if (!instances.ContainsKey(Entity.Id)) instances[Entity.Id] = new List<RTIEntityStateNode<T>>();
            if (!instances[Entity.Id].Contains(this)) instances[Entity.Id].Add(this);
            if (!subscribed) {
                if (!registeredChannel) RegisterChannel();
                listener = RTI.Subscribe<T>(ChannelName, (name, id, message) => {
                    if (instances.ContainsKey(id)) {
                        foreach (var instance in instances[id]) instance.OnStateMessage(message);
                    } else {
                        // Allow delaying processing one frame (happens at entity creation sometimes)
                        RTIConnection.QueueOnMainThread(() => {
                            if (instances.ContainsKey(id)) {
                                foreach (var instance in instances[id]) instance.OnStateMessage(message);
                            } else if (!warnedIdNotFound) {
                                GD.PrintErr($"No {this.GetType().Name} for entity {id}");
                                warnedIdNotFound = true;
                            }
                        });
                    }
                });
                subscribed = true;
            }
        }

        public override void _ExitTree() {
            if (Entity != null && instances.ContainsKey(Entity.Id)) {
                instances[Entity.Id].Remove(this);
                if (instances[Entity.Id].Count == 0) instances.Remove(Entity.Id);
            }
            if (subscribed && instances.Count == 0) {
                RTI.Unsubscribe(listener);
                subscribed = false;
            }
        }

        protected abstract void OnStateMessage(T message);

        protected void PublishState(T message) {
            if (!registeredChannel) RegisterChannel();
            RTI.Publish(ChannelName, message);
        }

        private void RegisterChannel() {
            RTI.Client.RegisterChannel(new Channel {
                Name = ChannelName,
                DataType = typeof(T).Name,
                State = true,
                FirstFieldId = true
            });
            registeredChannel = true;
        }

    }

}
