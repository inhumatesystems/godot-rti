using System;
using System.Linq;
using System.Reflection;

namespace Inhumate.GodotRTI {

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RTICommandAttribute : Attribute {
        public string Name { get; private set; }
        public string Description { get; private set; }

        public RTICommandAttribute(string name = "", string description = "") {
            Name = name;
            Description = description;
        }

        public static RTICommandAttribute GetAttribute(MethodInfo method) {
            var attr = method.GetCustomAttributes(typeof(RTICommandAttribute), false).FirstOrDefault() as RTICommandAttribute;
            if (attr == null)
                throw new Exception($"Method {method.Name} does not have an RTICommandAttribute");
            return attr;
        }
    }

}
