using System;

namespace BuildTool.Tracking.Editor
{
    public class ProgressBarHandler
    {
        private readonly int _steps;

        public ProgressBarHandler(int steps)
        {
            _steps = Math.Max(1, steps);
        }

        public string Description { get; private set; }
        public int ProgressStep { get; private set; }
        public float Progress => ProgressStep / (float)_steps;
        public bool IsComplete => ProgressStep == _steps;

        public event Action Updated;

        public void StepUpdate(string description)
        {
            Update(description, 1);
        }

        public void Update(string description, int deltaSteps = 0)
        {
            deltaSteps = Math.Max(0, deltaSteps);

            ProgressStep = Math.Clamp(ProgressStep + deltaSteps, 0, _steps);

            if (!string.IsNullOrWhiteSpace(description))
            {
                Description = description;
            }

            Updated?.Invoke();
        }

        public void Step()
        {
            Update(null, 1);
        }

        public void Reset()
        {
            ProgressStep = 0;
            Description = string.Empty;
        }

        public void Complete()
        {
            ProgressStep = _steps;
            Updated?.Invoke();
        }
    }
}
