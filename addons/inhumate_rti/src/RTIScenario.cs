using Godot;

namespace Inhumate.GodotRTI {

    [GlobalClass]
    public partial class RTIScenario : Resource {

        [Export] public string Name;
        [Export] public PackedScene Scene;
        [Export] public string Description;

        // TODO [Export]
        public RTIParameter[] Parameters = new RTIParameter[] {};

        public Inhumate.RTI.Proto.Scenario ToProto() {
            var proto = new Inhumate.RTI.Proto.Scenario {
                Name = Name,
                Description = Description
            };
            foreach (var parameter in Parameters) proto.Parameters.Add(parameter.ToProto());
            return proto;
        }

    }

}
