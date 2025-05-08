using Godot;

namespace Inhumate.GodotRTI
{
    public static class GodotExtensions
    {
        public static T FindChildOfType<T>(this Node parent) where T : Node
        {
            foreach (Node child in parent.GetChildren())
            {
                if (child is T typedChild)
                    return typedChild;

                var result = child.FindChildOfType<T>();
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}
