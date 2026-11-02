using System;

namespace AIT.Riding
{
    public enum RidePartGroup { Body, Steering, FrontWheel, RearWheel }

    public static class RideVehicleParts
    {
        public static RidePartGroup Classify(string name)
        {
            name = name ?? "";
            // A description such as "leg_shield_with_front_wheel_clearance" is
            // NOT a wheel. Only an actual part-name prefix identifies a wheel.
            if (name.StartsWith("Scooter_front_wheel_", StringComparison.OrdinalIgnoreCase))
                return name.EndsWith("seated_through_axle", StringComparison.OrdinalIgnoreCase)
                    ? RidePartGroup.Steering : RidePartGroup.FrontWheel;
            if (name.StartsWith("Scooter_rear_wheel_", StringComparison.OrdinalIgnoreCase))
                return name.EndsWith("seated_through_axle", StringComparison.OrdinalIgnoreCase)
                    ? RidePartGroup.Body : RidePartGroup.RearWheel;
            foreach (string prefix in SteeringPrefixes)
                if (name.StartsWith("Scooter_" + prefix, StringComparison.OrdinalIgnoreCase))
                    return RidePartGroup.Steering;
            return RidePartGroup.Body;
        }

        static readonly string[] SteeringPrefixes =
        {
            "curved_handlebar", "rubber_handgrip", "grip_rib", "mirror_", "brake_lever",
            "lever_pivot", "control_switch", "instrument", "front_curved_mudguard",
            "front_fender_stay", "front_lower_fork", "front_sliding_stanchion",
            "front_seated_triple_clamp", "front_axle_clamp", "front_disc_brake_caliper", "front_brake_hose"
        };
    }
}
