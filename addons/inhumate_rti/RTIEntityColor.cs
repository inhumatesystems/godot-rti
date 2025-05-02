using Godot;

namespace Inhumate.GodotRTI {
    
    // Sets a MeshInstance3D material color based on entity metadata

    public partial class RTIEntityColor : Node {
        [Export] public MeshInstance3D Renderer;

        public override void _Ready() {
            if (Renderer == null) Renderer = GetParent().FindChildOfType<MeshInstance3D>(); ;
            if (Renderer != null) {
                var entity = GetParent().FindChildOfType<RTIEntity>(); ;

                if (entity.Color.A > 1e-5f || entity.Color.R > 1e-5f || entity.Color.G > 1e-5f || entity.Color.B > 1e-5f) {
                    // Duplicate the material to avoid changing the shared resource
                    var material = Renderer.GetActiveMaterial(0)?.Duplicate() as StandardMaterial3D;

                    if (material != null) {
                        material.AlbedoColor = entity.Color;
                        Renderer.SetSurfaceOverrideMaterial(0, material);
                    }
                }
            }
        }
    }
}
