using System;
using System.Linq;
using System.Reflection;
using Inhumate.RTI.Proto;

namespace Inhumate.GodotRTI {

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class RTICommandArgumentAttribute : Attribute {
        public string Name { get; private set; }
        public string DefaultValue { get; private set; }
        public string Type { get; private set; }
        public string Description { get; private set; }
        public bool Required { get; private set; }


        public RTICommandArgumentAttribute(string name, string defaultValue = "", string type = "string", string description = "", bool required = false) {
            Name = name;
            DefaultValue = defaultValue;
            Type = type;
            Description = description;
            Required = required;
        }

        public void AddToCommand(Command command) {
            command.Argument(new Parameter {
                Name = Name,
                DefaultValue = DefaultValue,
                Type = Type,
                Description = Description,
                Required = Required
            });
        }

        public static RTICommandArgumentAttribute GetAttribute(MethodInfo method) {
            var attr = method.GetCustomAttributes(typeof(RTICommandArgumentAttribute), false).FirstOrDefault() as RTICommandArgumentAttribute;
            if (attr == null)
                throw new Exception($"Method {method.Name} does not have an RTICommandArgumentAttribute");
            return attr;
        }
    }

}
