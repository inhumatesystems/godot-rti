using Godot;

namespace Inhumate.GodotRTI {

	// Scales an entity according to its metadata dimensions

	public partial class RTIEntityDimensions : Node {
		[Export] public Node3D Target;
		[Export] public bool AdjustScale = true;
		[Export] public bool AdjustCenter = true;

		public override void _Ready() {
			var entity = GetParent().FindChildOfType<RTIEntity>();
			if (Target == null) Target = GetParentOrNull<Node3D>();
			if (Target != null && entity != null) {
				if (AdjustScale && entity.Size.Length() > 1e-5f) {
					Target.Scale = new Vector3(
						Target.Scale.X * entity.Size.X,
						Target.Scale.Y * entity.Size.Y,
						Target.Scale.Z * entity.Size.Z
					);
				}

				if (AdjustCenter && entity.Center.Length() > 1e-5f) {
					Target.Position += entity.Center;
				}
			}
		}
	}
}
