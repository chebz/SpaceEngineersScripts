using System;

namespace IngameScript
{
    /// <summary>
    ///     Discrete time PID controller class.
    ///     credit: https://github.com/Whiplash141
    ///     Last edited: 2022/08/11 - Whiplash141
    /// </summary>
    public class PID
    {
        #region Fields
        private double _errorSum;
        private bool _firstRun = true;
        private double _inverseTimeStep;
        private double _lastError;

        private double _timeStep;
        #endregion

        #region Properties
        public double Kp { get; set; }
        public double Ki { get; set; }
        public double Kd { get; set; }
        public double Value { get; private set; }
        public double TimeStep 
        { 
            get 
            {
                return _timeStep;
            }
            set
            {
                _timeStep = value;
                _inverseTimeStep = 1 / _timeStep;
            }
        }
        #endregion

        #region Constructors
        public PID(double kp, double ki, double kd, double timeStep)
        {
            Kp = kp;
            Ki = ki;
            Kd = kd;
            _timeStep = timeStep;
            _inverseTimeStep = 1 / _timeStep;
        }
        #endregion

        #region Methods
        protected virtual double GetIntegral(double currentError, double errorSum, double timeStep)
        {
            return errorSum + currentError * timeStep;
        }

        public double Control(double error)
        {
            //Compute derivative term
            var errorDerivative = (error - _lastError) * _inverseTimeStep;

            if (_firstRun)
            {
                errorDerivative = 0;
                _firstRun = false;
            }

            //Get error sum
            _errorSum = GetIntegral(error, _errorSum, _timeStep);

            //Store this error as last error
            _lastError = error;

            //Construct output
            Value = Kp * error + Ki * _errorSum + Kd * errorDerivative;
            return Value;
        }

        public double Control(double error, double timeStep)
        {
            if (timeStep != _timeStep)
            {
                _timeStep = timeStep;
                _inverseTimeStep = 1 / _timeStep;
            }

            return Control(error);
        }

        public virtual void Reset()
        {
            _errorSum = 0;
            _lastError = 0;
            _firstRun = true;
        }
        #endregion
    }

}