using Vehicles;

namespace AirstrikeMod
{
    public class LaunchProtocol_PreparedRunwayDirectional : DirectionalTakeoff
    {
        public LaunchProtocol_PreparedRunwayDirectional()
        {
        }

        public LaunchProtocol_PreparedRunwayDirectional(
            LaunchProtocol_PreparedRunwayDirectional reference, VehiclePawn vehicle)
            : base(reference, vehicle)
        {
        }

        public override string FailLaunchMessage
        {
            get
            {
                var reason = vehicle?.GetComp<CompPreparedRunway>()?.LastFailureReason;
                return string.IsNullOrEmpty(reason) ? base.FailLaunchMessage : reason;
            }
        }
    }
}
