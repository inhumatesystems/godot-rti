using Godot;

// TODO this is just a stub

namespace Inhumate.GodotRTI {

    public partial class RTIInjectable : Node {

        public void Publish() {}
        public void PublishInjections() {}
        public void Inject(Inhumate.RTI.Proto.InjectionOperation.Types.Inject inject) {}

        public Inhumate.RTI.Proto.Injection GetInjection(string id) { return null; }

        public void DisableInjection(Inhumate.RTI.Proto.Injection injection) {}
        public void EnableInjection(Inhumate.RTI.Proto.Injection injection) {}
        public void StartInjection(Inhumate.RTI.Proto.Injection injection) {}
        public void EndInjection(Inhumate.RTI.Proto.Injection injection) {}
        public void StopInjection(Inhumate.RTI.Proto.Injection injection) {}
        public void CancelInjection(Inhumate.RTI.Proto.Injection injection) {}
        public void ScheduleInjection(double time, Inhumate.RTI.Proto.Injection injection) {}
        public void UpdateTitle(string title, Inhumate.RTI.Proto.Injection injection) {}
    }

}
