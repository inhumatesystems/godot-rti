using Inhumate.RTI;
using Inhumate.RTI.Proto;

namespace Inhumate.GodotRTI {
    public class RTIRuntimeControl {

        protected static RTIConnection RTI => RTIConnection.Instance;

        public static void PublishReset() {
            PublishAndReceiveIfNotConnected(new RuntimeControl {
                Reset = new Google.Protobuf.WellKnownTypes.Empty()
            });
        }

        public static void PublishLoadScenario(string scenarioName) {
            PublishAndReceiveIfNotConnected(new RuntimeControl {
                LoadScenario = new RuntimeControl.Types.LoadScenario {
                    Name = scenarioName
                }
            });
        }

        public static void PublishStart() {
            PublishAndReceiveIfNotConnected(new RuntimeControl {
                Start = new Google.Protobuf.WellKnownTypes.Empty()
            });
        }

        public static void PublishPlay() {
            PublishAndReceiveIfNotConnected(new RuntimeControl {
                Play = new Google.Protobuf.WellKnownTypes.Empty()
            });
        }

        public static void PublishPause() {
            PublishAndReceiveIfNotConnected(new RuntimeControl {
                Pause = new Google.Protobuf.WellKnownTypes.Empty()
            });
        }

        public static void PublishEnd() {
            PublishAndReceiveIfNotConnected(new RuntimeControl {
                End = new Google.Protobuf.WellKnownTypes.Empty()
            });

        }

        public static void PublishStop() {
            PublishAndReceiveIfNotConnected(new RuntimeControl {
                Stop = new Google.Protobuf.WellKnownTypes.Empty()
            });
        }

        public static void PublishTimeScale(float timeScale) {
            PublishAndReceiveIfNotConnected(new RuntimeControl {
                SetTimeScale = new RuntimeControl.Types.SetTimeScale {
                    TimeScale = timeScale
                }
            });
        }

        private static void PublishAndReceiveIfNotConnected(RuntimeControl message) {
            RTI.Publish(RTIChannel.Control, message);
            if (!RTI.Connected) RTI.OnRuntimeControl("internal", message);
        }
    }
}
