using System;

namespace Inhumate.GodotRTI {

    public class RTIParameter {
        public string Name;

        //[Tooltip("User-friendly label. If left blank, same as name.")]
        public string Label;

        public string Description;

        public RTIParameterType Type;

        // [AllowNesting]
        // [ShowIf("type", RTIParameterType.Choice)]
        // [Tooltip("List of choices, separated by pipe (|), semicolon (;) or comma (,)")]
        public string Choices;

        public string DefaultValue;

        public Inhumate.RTI.Proto.Parameter ToProto() {
            var proto = new Inhumate.RTI.Proto.Parameter {
                Name = Name,
                Label = Label,
                Description = Description,
                DefaultValue = DefaultValue,
                Type = Type.ToString().ToLower()
            };
            if (Type == RTIParameterType.Choice && Choices.Length > 0) {
                if (Choices.Contains("|")) {
                    proto.Type += "|" + Choices;
                } else if (Choices.Contains(";")) {
                    proto.Type += "|" + string.Join("|", Choices.Split(';'));
                } else {
                    proto.Type += "|" + string.Join("|", Choices.Split(','));
                }
            }
            return proto;
        }
    }

    public enum RTIParameterType {
        String,
        Text,
        Float,
        Integer,
        Switch,
        Checkbox,
        Choice
    }
}
