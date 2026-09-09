namespace Drawer.Core;

// Analytic damped spring: frame-rate independent, with velocity retained on retargeting.
public sealed class SpringValue
{
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }

    public void Snap(double target) { Value = Target = target; Velocity = 0; }
    public bool IsSettled(double tolerance) => Math.Abs(Value - Target) < tolerance && Math.Abs(Velocity) < tolerance * 10;

    public void Step(double seconds, double frequency, double damping)
    {
        if (seconds <= 0) return;
        double offset = Value - Target;
        double decay = Math.Exp(-damping * frequency * seconds);
        if (damping >= 1)
        {
            double term = Velocity + frequency * offset;
            Value = Target + (offset + term * seconds) * decay;
            Velocity = (Velocity - frequency * term * seconds) * decay;
        }
        else
        {
            double damped = frequency * Math.Sqrt(1 - damping * damping);
            double cosine = Math.Cos(damped * seconds), sine = Math.Sin(damped * seconds);
            Value = Target + decay * (offset * cosine + (Velocity + damping * frequency * offset) / damped * sine);
            Velocity = decay * (Velocity * cosine - (damping * frequency * Velocity + frequency * frequency * offset) / damped * sine);
        }
    }
}
