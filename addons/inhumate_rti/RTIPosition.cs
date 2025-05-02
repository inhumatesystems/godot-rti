using System.Collections.Generic;
using Inhumate.RTI.Proto;
using Inhumate.RTI;
using Godot;
using System.Linq;
using System;

namespace Inhumate.GodotRTI {

    [GlobalClass, Icon("res://addons/inhumate_rti/icons/rti_position.svg")]
    public partial class RTIPosition : RTIEntityStateNode<EntityPosition> {

        [Export] public bool Publish = true;
        // [ShowIf("publish")]
        [Export] public float MinPublishInterval = 1f;
        // [ShowIf("publish")]
        [Export] public float MaxPublishInterval = 10f;
        // [ShowIf("publish")]
        [Export] public float PositionThreshold = 0.001f;
        // [ShowIf("publish")]
        [Export] public float RotationThreshold = 0.01f;
        // [ShowIf("publish")]
        [Export] public float VelocityThreshold = 0.1f;

        [Export] public bool Receive = true;
        // [ShowIf("receive")]
        [Export] public bool Interpolate = true;
        // [ShowIf(EConditionOperator.And, new string[] { "receive", "interpolate" })]
        [Export] public float MaxInterpolateInterval = 2f;
        // [Range(0.01f, 10f)]
        // [ShowIf(EConditionOperator.And, new string[] { "receive", "interpolate" })]
        [Export] public float PositionSmoothing = 1f;
        // [Range(0.01f, 10f)]
        // [ShowIf(EConditionOperator.And, new string[] { "receive", "interpolate" })]
        [Export] public float RotationSmoothing = 1f;
        // [ShowIf("receive")]
        [Export] public bool SetBodyFreeze;


        public override string ChannelName => RTIChannel.Position;

        private double lastPublishTime;
        private double lastPositionTime = -1;
        private double previousPositionTime = -1;
        private Vector3 lastPosition;
        private Vector3 previousPosition;
        private Vector3? lastVelocity;
        private Vector3? lastAcceleration;
        private double lastRotationTime = -1;
        private double previousRotationTime = -1;
        private Quaternion lastRotation;
        private Quaternion previousRotation;
        private Vector3? lastAngularVelocity;
        public long ReceiveCount { get; private set; }

        private RigidBody3D body;

        private static bool warnedGeodetic;
        private Node3D node;

        public override void _Ready() {
            base._Ready();
            node = GetParentOrNull<Node3D>();
            if (node == null) {
                GD.PrintErr($"RTIPosition parent {GetParent().Name} is not a Node3D");
                return;
            }
            body = GetParentOrNull<RigidBody3D>();
            lastPosition = node.GlobalPosition;
            lastRotation = node.GlobalBasis.GetRotationQuaternion();
            lastPositionTime = Time.GetTicksMsec() / 1000.0;
            lastRotationTime = Time.GetTicksMsec() / 1000.0;
            if (Entity != null) Entity.OnUpdated += OnUpdated;
        }

        protected void OnUpdated() {
            var data = Entity.LastReceivedData;
            if (Receiving && ReceiveCount == 0 && data != null && data.Position != null) {
                OnStateMessage(data.Position);
            }
        }

        protected override void OnStateMessage(EntityPosition position) {
            ReceiveCount++;
            if (!Receive || !Receiving || !IsInsideTree()) return;
            if (position.Local != null) {
                previousPositionTime = lastPositionTime;
                previousPosition = lastPosition;
                lastPositionTime = Time.GetTicksMsec() / 1000.0;
                if (RTIToLocalPosition != null) {
                    lastPosition = RTIToLocalPosition(position.Local);
                } else {
                    lastPosition = new Vector3(position.Local.X, position.Local.Y, position.Local.Z);
                }
                if (!Interpolate || ReceiveCount == 1) node.GlobalPosition = previousPosition = lastPosition;
            } else if (position.Geodetic != null) {
                if (GeodeticToLocal != null) {
                    previousPositionTime = lastPositionTime;
                    previousPosition = lastPosition;
                    lastPositionTime = Time.GetTicksMsec() / 1000.0;
                    lastPosition = GeodeticToLocal(position.Geodetic);
                    if (!Interpolate || ReceiveCount == 1) node.GlobalPosition = previousPosition = lastPosition;
                } else if (!warnedGeodetic) {
                    GD.Print("Cannot use geodetic position, RTIPosition.GeodeticToLocal not set");
                    warnedGeodetic = true;
                }
            }
            if (position.LocalRotation != null) {
                previousRotationTime = lastRotationTime;
                previousRotation = lastRotation;
                lastRotationTime = Time.GetTicksMsec() / 1000.0;
                lastRotation = new Quaternion(position.LocalRotation.X, position.LocalRotation.Y, position.LocalRotation.Z, position.LocalRotation.W);
                if (!Interpolate || ReceiveCount == 1) {
                    SetNodeRotation(node, lastRotation);
                    previousRotation = lastRotation;
                }
            } else if (position.EulerRotation != null) {
                previousRotationTime = lastRotationTime;
                previousRotation = lastRotation;
                lastRotationTime = Time.GetTicksMsec() / 1000.0;
                if (RTIToLocalEuler != null) {
                    lastRotation = RTIToLocalEuler(position.EulerRotation);
                } else {
                    lastRotation = Quaternion.FromEuler(new Vector3(-position.EulerRotation.Pitch, -position.EulerRotation.Yaw, position.EulerRotation.Roll) * (Mathf.Pi / 180.0f));
                }
                if (!Interpolate || ReceiveCount == 1) {
                    SetNodeRotation(node, lastRotation);
                    previousRotation = lastRotation;
                }
            }
            if (position.Velocity != null) {
                if (RTIToLocalVelocity != null) {
                    lastVelocity = RTIToLocalVelocity(position.Velocity);
                } else {
                    lastVelocity = new Vector3(position.Velocity.Right, position.Velocity.Up, position.Velocity.Forward);
                }
            } else {
                lastVelocity = null;
            }
            if (position.Acceleration != null) {
                if (RTIToLocalVelocity != null) {
                    lastAcceleration = RTIToLocalVelocity(position.Acceleration);
                } else {
                    lastAcceleration = new Vector3(position.Acceleration.Right, position.Acceleration.Up, position.Acceleration.Forward);
                }
            } else {
                lastAcceleration = null;
            }
            if (position.AngularVelocity != null) {
                if (RTIToLocalAngularVelocity != null) {
                    lastAngularVelocity = RTIToLocalAngularVelocity(position.AngularVelocity);
                } else {
                    lastAngularVelocity = new Vector3(-position.AngularVelocity.Pitch, -position.AngularVelocity.Yaw, position.AngularVelocity.Roll);
                }
            } else {
                lastAngularVelocity = null;
            }
        }

        private Vector3 lastVelocityPosition;
        private double lastVelocityTime;
        private double physicsTime;
        public override void _PhysicsProcess(double deltaTime) {
            physicsTime += deltaTime;

            if (Receive && body != null && SetBodyFreeze) {
                body.Freeze = !Publishing;
            }

            if (node == null) return;

            Vector3? localVelocity = null;
            if (body != null && !body.Freeze) {
                localVelocity = body.Transform.Basis.Inverse() * body.LinearVelocity;
            } else if (lastVelocityPosition.LengthSquared() > float.Epsilon && lastVelocityTime > float.Epsilon && physicsTime > lastVelocityTime) {
                Vector3 velocity = (node.GlobalPosition - lastVelocityPosition) / (float)(physicsTime - lastVelocityTime);
                localVelocity = node.Transform.Basis.Inverse() * velocity;
            }
            if (Publish && Publishing && physicsTime - lastPublishTime > MinPublishInterval
                    && (physicsTime - lastPublishTime > MaxPublishInterval
                        || PositionThreshold < float.Epsilon || RotationThreshold < float.Epsilon || VelocityThreshold < float.Epsilon
                        || (node.GlobalPosition - lastPosition).Length() > PositionThreshold
                        || node.GlobalBasis.GetRotationQuaternion().AngleTo(lastRotation) > RotationThreshold
                        || (localVelocity.HasValue && lastVelocity.HasValue && (localVelocity.Value - lastVelocity.Value).Length() > VelocityThreshold)
                    )) {
                lastPublishTime = physicsTime;
                var position = PositionMessageFromNode(node);
                position.Id = Entity.Id;
                if (localVelocity.HasValue) {
                    if (LocalToRTIVelocity != null) {
                        position.Velocity = LocalToRTIVelocity(localVelocity.Value);
                        lastAngularVelocity = Vector3.Zero;
                    } else {
                        position.Velocity = new EntityPosition.Types.VelocityVector {
                            Forward = -localVelocity.Value.Z,
                            Up = localVelocity.Value.Y,
                            Right = localVelocity.Value.X
                        };
                    }
                    lastVelocity = localVelocity;
                }
                if (body != null && !body.Freeze) {
                    Vector3 localAngularVelocity = body.Transform.Basis.Inverse() * body.AngularVelocity * 180.0f / Mathf.Pi;
                    if (LocalToRTIAngularVelocity != null) {
                        position.AngularVelocity = LocalToRTIAngularVelocity(localAngularVelocity);
                    } else {
                        position.AngularVelocity = new EntityPosition.Types.EulerRotation {
                            Roll = localAngularVelocity.Z,
                            Pitch = -localAngularVelocity.X,
                            Yaw = -localAngularVelocity.Y
                        };
                    }
                    lastAngularVelocity = localAngularVelocity;
                } else {
                    lastAngularVelocity = Vector3.Zero;
                }
                PublishState(position);
                lastPosition = node.GlobalPosition;
                lastPositionTime = physicsTime;
                lastRotation = node.GlobalBasis.GetRotationQuaternion();
                lastRotationTime = physicsTime;
            } else if (Receive && Receiving && Interpolate) {
                if (lastAcceleration.HasValue && lastVelocity.HasValue) {
                    lastVelocity += lastAcceleration * (float)deltaTime;
                }
                Vector3 targetPosition = node.GlobalPosition;
                Quaternion targetRotation = node.GlobalBasis.GetRotationQuaternion();
                if (physicsTime - lastPositionTime < MaxInterpolateInterval) {
                    if (lastPositionTime > 0 && lastVelocity.HasValue) {
                        // Interpolate using velocity
                        targetPosition = lastPosition + node.Transform.Basis * (lastVelocity.Value * (float)(physicsTime - lastPositionTime));
                    } else if (lastPositionTime > 0 && previousPositionTime > 0 && lastPositionTime - previousPositionTime > 1e-5f && lastPositionTime - previousPositionTime < MinPublishInterval * 2.5f) {
                        // or else lerp based on last and previous position
                        targetPosition = lastPosition.Lerp(lastPosition + (lastPosition - previousPosition), (float)Mathf.Clamp((physicsTime - lastPositionTime) / (lastPositionTime - previousPositionTime), 0, 1));
                    } else if (lastPositionTime > 0) {
                        // or else just teleport
                        targetPosition = lastPosition;
                    }
                } else {
                    targetPosition = lastPosition;
                    lastVelocity = null;
                    lastAcceleration = null;
                    lastAngularVelocity = null;
                }
                node.GlobalPosition = node.GlobalPosition.Lerp(targetPosition, (float)Mathf.Clamp(deltaTime * 10.0 / PositionSmoothing, 0, 1));

                if (physicsTime - lastRotationTime < MaxInterpolateInterval) {
                    if (lastRotationTime > 0 && lastAngularVelocity.HasValue) {
                        // Interpolate using angular velocity
                        targetRotation = Quaternion.FromEuler(lastAngularVelocity.Value * (float)(Time.GetTicksMsec() / 1000.0 - lastRotationTime)) * lastRotation;
                    } else if (lastRotationTime > 0 && previousRotationTime > 0 && lastRotationTime - previousRotationTime > 1e-5f && lastRotationTime - previousRotationTime < MinPublishInterval * 2.5f) {
                        // or else slerp based on last and previous rotation
                        targetRotation = lastRotation.Slerp(lastRotation * previousRotation.Inverse() * lastRotation, (float)Mathf.Clamp((Time.GetTicksMsec() / 1000.0 - lastRotationTime) / (lastRotationTime - previousRotationTime), 0, 1));
                    } else if (lastRotationTime > 0) {
                        // or else just set rotation
                        targetRotation = lastRotation;
                    }
                } else {
                    targetRotation = lastRotation;
                    lastVelocity = null;
                    lastAcceleration = null;
                    lastAngularVelocity = null;
                }
                SetNodeRotation(node, node.GlobalBasis.GetRotationQuaternion().Slerp(targetRotation, (float)Mathf.Clamp(deltaTime * 10.0 / RotationSmoothing, 0, 1)));
            }
            if (physicsTime - lastVelocityTime >= 10 * deltaTime || physicsTime < lastVelocityTime) {
                lastVelocityPosition = node.GlobalPosition;
                lastVelocityTime = physicsTime;
            }
        }

        private static void SetNodeRotation(Node3D node, Quaternion quat) {
            // some workaround code here because Godot Basis messes with both rotation and scale
            var scale = node.Scale;
            node.GlobalBasis = new Basis(quat);
            node.Scale = scale;
        }

        public static EntityPosition PositionMessageFromNode(Node3D node) {
            var euler = node.GlobalBasis.GetEuler() * 180.0f / Mathf.Pi;
            var position = new EntityPosition();
            if (LocalToRTIPosition != null) {
                position.Local = LocalToRTIPosition(node.GlobalPosition);
            } else {
                position.Local = new EntityPosition.Types.LocalPosition {
                    X = node.GlobalPosition.X,
                    Y = node.GlobalPosition.Y,
                    Z = -node.GlobalPosition.Z
                };
            }
            if (LocalToRTIEuler != null) {
                position.EulerRotation = LocalToRTIEuler(node.GlobalBasis.GetRotationQuaternion());
            } else {
                var quat = node.GlobalBasis.GetRotationQuaternion();
                position.LocalRotation = new EntityPosition.Types.LocalRotation {
                    X = quat.X,
                    Y = quat.Y,
                    Z = -quat.Z,
                    W = -quat.W
                };
                position.EulerRotation = new EntityPosition.Types.EulerRotation {
                    Roll = euler.Z,
                    Pitch = -euler.X,
                    Yaw = -euler.Y
                };
            }
            if (LocalToGeodetic != null) {
                position.Geodetic = LocalToGeodetic(node.GlobalPosition);
            }
            return position;
        }

        public static void ApplyPositionMessageToNode(EntityPosition position, Node3D node) {
            if (position.Local != null) {
                if (RTIToLocalPosition != null) {
                    node.GlobalPosition = RTIToLocalPosition(position.Local);
                } else {
                    node.GlobalPosition = new Vector3(position.Local.X, position.Local.Y, position.Local.Z);
                }
            } else if (position.Geodetic != null) {
                if (GeodeticToLocal != null) {
                    node.GlobalPosition = GeodeticToLocal(position.Geodetic);
                } else if (!warnedGeodetic) {
                    GD.Print("Cannot use geodetic position, RTIPosition.GeodeticToLocal not set");
                    warnedGeodetic = true;
                }
            }
            if (position.EulerRotation != null && RTIToLocalEuler != null) {
                SetNodeRotation(node, RTIToLocalEuler(position.EulerRotation));
            } else if (position.LocalRotation != null) {
                SetNodeRotation(node, new Quaternion(position.LocalRotation.X, position.LocalRotation.Y, -position.LocalRotation.Z, -position.LocalRotation.W));
            } else if (position.EulerRotation != null) {
                SetNodeRotation(node, Quaternion.FromEuler(new Vector3(-position.EulerRotation.Pitch, -position.EulerRotation.Yaw, position.EulerRotation.Roll) * (Mathf.Pi / 180.0f)));
            }
        }

        public delegate EntityPosition.Types.GeodeticPosition LocalToGeodeticConversion(Vector3 local);
        public static LocalToGeodeticConversion LocalToGeodetic;

        public delegate Vector3 GeodeticToLocalConversion(EntityPosition.Types.GeodeticPosition position);
        public static GeodeticToLocalConversion GeodeticToLocal;

        public delegate EntityPosition.Types.LocalPosition LocalToRTIConversion(Vector3 local);
        public static LocalToRTIConversion LocalToRTIPosition;

        public delegate Vector3 RTIToLocalConversion(EntityPosition.Types.LocalPosition rti);
        public static RTIToLocalConversion RTIToLocalPosition;

        public delegate EntityPosition.Types.EulerRotation LocalToRTIEulerConversion(Quaternion local);
        public static LocalToRTIEulerConversion LocalToRTIEuler;

        public delegate Quaternion RTIEulerToLocalConversion(EntityPosition.Types.EulerRotation euler);
        public static RTIEulerToLocalConversion RTIToLocalEuler;

        public delegate EntityPosition.Types.VelocityVector LocalToRTIVelocityConversion(Vector3 local);
        public static LocalToRTIVelocityConversion LocalToRTIVelocity;

        public delegate Vector3 RTIToLocalVelocityConversion(EntityPosition.Types.VelocityVector rti);
        public static RTIToLocalVelocityConversion RTIToLocalVelocity;

        public delegate EntityPosition.Types.EulerRotation LocalToRTIAngularVelocityConversion(Vector3 local);
        public static LocalToRTIAngularVelocityConversion LocalToRTIAngularVelocity;

        public delegate Vector3 RTIToLocalAngularVelocityConversion(EntityPosition.Types.EulerRotation angularEuler);
        public static RTIToLocalAngularVelocityConversion RTIToLocalAngularVelocity;

    }

}
