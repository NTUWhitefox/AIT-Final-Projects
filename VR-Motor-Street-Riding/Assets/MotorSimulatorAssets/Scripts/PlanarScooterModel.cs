using UnityEngine;

namespace AIT.Riding
{
    // Fixed-step, intentionally stable, no balancing or countersteering model.
    // Visual lean never feeds back into trajectory or tracked head pose.
    public sealed class PlanarScooterModel
    {
        public Vector3 position;
        public float yaw, speed, steeringDegrees, yawRate, lateralAcceleration, acceleration;

        public void Reset(Vector3 p, float heading)
        {
            position = p; yaw = heading;
            speed = steeringDegrees = yawRate = lateralAcceleration = acceleration = 0;
        }

        public void Step(RideInputFrame input, RideTestSettings s, float capKph, float dt)
        {
            if (dt <= 0) return;
            float previousSpeed = speed;
            float drag = s.coastingDeceleration + s.quadraticDrag * speed * speed;
            float reverse = Mathf.Clamp01(input.reverse);
            // Braking always reduces the magnitude; it never selects reverse.
            if (input.brake > .02f)
                speed = Mathf.MoveTowards(speed, 0, (Mathf.Clamp01(input.brake) * s.brakingDeceleration + drag) * dt);
            else if (reverse > .02f)
            {
                // Direction changes pass through a stationary step.
                if (speed > 0) speed = Mathf.MoveTowards(speed, 0, s.brakingDeceleration * dt);
                else speed = Mathf.MoveTowards(speed, -reverse * s.maximumReverseSpeedKph / 3.6f, s.reverseAcceleration * dt);
            }
            else if (speed < 0)
                speed = Mathf.MoveTowards(speed, 0, s.brakingDeceleration * dt); // release-to-stop walking reverse
            else
                speed = Mathf.Clamp(speed + (Mathf.Clamp01(input.throttle) * s.acceleration - drag) * dt,
                    0, Mathf.Min(s.maximumSpeedKph, capKph) / 3.6f);
            acceleration = (speed - previousSpeed) / dt;
            float magnitude = Mathf.Abs(speed);
            // Low-speed lock is independent of the global maximum driving speed.
            float fraction = Mathf.SmoothStep(0, 1, magnitude / Mathf.Max(.1f, s.steeringTransitionSpeedKph / 3.6f));
            float maxSteer = Mathf.Lerp(s.lowSpeedSteeringDegrees, s.highSpeedSteeringDegrees, fraction);
            steeringDegrees = Mathf.MoveTowards(steeringDegrees,
                Mathf.Clamp(input.steering, -1, 1) * maxSteer, s.steeringRateDegreesPerSecond * dt);
            float requestedRate = speed * Mathf.Tan(steeringDegrees * Mathf.Deg2Rad) / Mathf.Max(0.5f, s.wheelbase);
            float rateLimit = Mathf.Min(Mathf.Lerp(s.lowSpeedYawDegreesPerSecond, s.maximumYawDegreesPerSecond, fraction) * Mathf.Deg2Rad,
                s.maximumLateralAcceleration / Mathf.Max(magnitude, 0.1f));
            yawRate = Mathf.Clamp(requestedRate, -rateLimit, rateLimit);
            lateralAcceleration = speed * yawRate;
            // Midpoint integration reduces direction bias on sustained turns.
            float midYaw = yaw + yawRate * Mathf.Rad2Deg * dt * 0.5f;
            position += Quaternion.Euler(0, midYaw, 0) * Vector3.forward * speed * dt;
            yaw = Mathf.Repeat(yaw + yawRate * Mathf.Rad2Deg * dt, 360);
        }

        public void Stop()
        {
            speed = yawRate = lateralAcceleration = acceleration = 0;
        }
    }
}
