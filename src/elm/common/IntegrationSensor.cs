﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace hobd
{
    internal struct SensorData
    {
        public double Value { get; set; }
        public long TimeStamp { get; set; }
    }

    internal class CircularBuffer
    {
        private SensorData[] _buffer;
        private int _bufferPtr;
        private object _syncObject;
        private int _bufferSize;

        public CircularBuffer(int bufferSize)
        {
            InitializeBuffer(bufferSize);
        }

        public void Add(SensorData sensorData)
        {
            lock (_syncObject)
            {
                _buffer[_bufferPtr] = sensorData;
                ShiftPtr(ref _bufferPtr);
            }
        }

        public SensorData[] Get()
        {
            var resBuf= new SensorData[_bufferSize];

            lock (_syncObject)
            {
                int count = (_bufferSize < _buffer.Count()) ? _bufferSize : _buffer.Count();
                int shift = _bufferPtr;

                for (int i = 0; i < count; i++)
                {
                    resBuf[i]=_buffer[shift];
                    ShiftPtr(ref shift);
                }
            }
            return resBuf;
        }

        private void InitializeBuffer(int bufferSize)
        {
            if (bufferSize < 1)
            {
                var msg = String.Format("BufferSize must be positive. Current size is {0}", bufferSize);
                throw new ArgumentOutOfRangeException(msg);
            }

            _buffer = new SensorData[bufferSize];
            _bufferSize = bufferSize;
            _bufferPtr = 0;
            _syncObject = new object();
        }

        private void ShiftPtr(ref int ptr)
        {
            ptr++;
            if (ptr >= _bufferSize)
            {
                ptr = 0;
            }

        }

    }

    public class IntegrationSensor : CoreSensor, IAccumulatorSensor
    {
        readonly object _syncObject = new object();
        //
        private CircularBuffer _sensorDataBuffer;
        public const int DEFAULT_SLOTS_COUNT = 50;
        //
        Sensor _baseSensor;
        readonly string _baseSensorId;
        //
        bool _firstRun = true;
        bool _suspendCalculations;
        long _previouseTime;
        long _avgTime;
        double _sum;
        //
        double _currentValue;
        readonly int _interval;
        readonly int _slotsCount;


        public IntegrationSensor(string baseSensorId)
            : this(baseSensorId, 0, DEFAULT_SLOTS_COUNT)
        {

        }

        public IntegrationSensor(string baseSensorId, int interval)
            : this(baseSensorId, interval, DEFAULT_SLOTS_COUNT)
        {
            
        }

        public IntegrationSensor(string baseSensorId, int interval, int slotsCount)
        {
            _baseSensorId = baseSensorId;
            _interval = interval;
            _slotsCount = slotsCount;

            if (interval > 0)
            {
                _sensorDataBuffer = _slotsCount >= DEFAULT_SLOTS_COUNT ? new CircularBuffer(slotsCount) : new CircularBuffer(DEFAULT_SLOTS_COUNT);
                
            }
        }

        public void Reset()
        {
            _currentValue = 0;
            _firstRun = true;
            if (_interval > 0)
            {
                _sensorDataBuffer = new CircularBuffer(_slotsCount);
            }
        }

        public void Suspend()
        {
            _firstRun = true;
        }

        public override double Value
        {
            get
            {
                lock (_syncObject)
                {
                    return this.value;
                }
                // There is no need to update value relatively current time, just return value
                // move this calculations into Limited and Unlimited calculations logic
                #region unused code
                /*
                if (_interval > 0)
                {
                    double avgSpeeds = 0;
                    long avgTimeIntervals = 0;
                    double valueLocal;
                    long previouseTimeLocal;

                    lock (_syncObject)
                    {
                        valueLocal = _value;
                        previouseTimeLocal = _previouseTime;
                    }
                    

                    var bufferedData = _sensorDataBuffer.Get();
                    var currentTime = DateTimeMs.Now;
                    var satisfiedTime = currentTime - _interval;
                    for (var i = 0 ; i < bufferedData.Count() -1 ; i++)
                    {
                        var sensorData0 = bufferedData[i];
                        var sensorData1 = bufferedData[i + 1];

                        if (sensorData1.TimeStamp <= satisfiedTime)
                            continue;
                        var t0 = sensorData0.TimeStamp >= satisfiedTime
                                      ? sensorData0.TimeStamp
                                      : satisfiedTime;
                        var t1 = sensorData1.TimeStamp;

                        avgSpeeds += sensorData0.Value*(t1 - t0);
                        avgTimeIntervals += (t1 - t0);
                    }

                    #if DEBUG_LIMITED

                    var deltaTime = currentTime - previouseTimeLocal;
                    double tmpValue = valueLocal * deltaTime;
                    avgSpeeds += tmpValue;
                    long tmpTime = avgTimeIntervals + deltaTime;
                    double v = avgSpeeds / tmpTime;
                    return v;

                    #else

                    return (avgSpeeds + (valueLocal * (currentTime - previouseTimeLocal))) / (avgTimeIntervals + (currentTime - previouseTimeLocal));    

                    #endif
                }
                else
                {
                    lock (_syncObject)
                    {
                        var currentTime = DateTimeMs.Now;
                        return (_sum + (_value * (currentTime - _previouseTime))) / (_avgTime + (currentTime - _previouseTime));
                    }
                }
                 */
                #endregion
            }
        }

        public long AvgTime
        {
            get
            {
                // There is no need to update value regarding current time just return value
                lock (_syncObject)
                {
                    return _avgTime;
                }
            }
        }

        protected bool FirstRun
        {
            get
            {
                return _firstRun || (this.TimeStamp - _previouseTime) < 0 ||
                       (this.TimeStamp - _previouseTime) > _interval;
            }
            set { _firstRun = value; }

        }

        protected override void Activate()
        {
            Reset();
            try
            {
                _baseSensor = registry.Sensor(_baseSensorId, this);
            }
            catch (Exception)
            {
                throw new NullReferenceException("Null sensor");
            }
            registry.AddListener(_baseSensor, OnBasedSensorChange);
        }

        protected override void Deactivate()
        {
            Reset();
            registry.RemoveListener(OnBasedSensorChange);
        }

        void OnBasedSensorChange(Sensor s)
        {
            if (_interval > 0)
            {
                TimeLimitedCalculation(s);
            }
            else
            {
                TimeUnLimitedCalculation(s);
            }
            registry.TriggerListeners(this);
        }

        private void TimeLimitedCalculation(Sensor s)
        {
            if (FirstRun)
            {
                _sensorDataBuffer.Add(new SensorData { Value = s.Value, TimeStamp = s.TimeStamp });
                _firstRun = false;
                _suspendCalculations = false;
                return;
            }
            if (!_suspendCalculations)
            {
                lock (_syncObject)
                {
                    _sensorDataBuffer.Add(new SensorData { Value = s.Value, TimeStamp = s.TimeStamp });
                }
                //
                double avgValues = 0;
                long avgTimeIntervals = 0;
                //
                var bufferedData = _sensorDataBuffer.Get();
                //
                var currentTime = s.TimeStamp;
                var satisfiedTime = currentTime - _interval;
                //
                var count = bufferedData.Count();
                for (var i = 0; i < count - 1; i++)
                {
                    var sensorData = bufferedData[i];
                    // next sensor data used for time intervals calculation.
                    // timeInterval = (TimStamp1 - TimeStamp0) 
                    var sensorDataNext = bufferedData[i + 1];

                    if (sensorDataNext.TimeStamp <= satisfiedTime)
                        continue;
                    var t0 = sensorData.TimeStamp >= satisfiedTime
                                  ? sensorData.TimeStamp
                                  : satisfiedTime;
                    var t1 = sensorDataNext.TimeStamp;

                    avgValues += sensorData.Value * (t1 - t0);
                    avgTimeIntervals += (t1 - t0);
                }
                lock (_syncObject)
                {
                    this.value = avgValues;
                    _avgTime = avgTimeIntervals;
                }
            }
        }

        private void TimeUnLimitedCalculation(Sensor s)
        {
            if (FirstRun)
            {
                _currentValue = s.Value;
                _previouseTime = s.TimeStamp;
                _firstRun = false;
                _suspendCalculations = false;
                this.TimeStamp = s.TimeStamp;
                return;
            }
            //
            if (!_suspendCalculations)
            {
                lock (_syncObject)
                {
                    // Calculate sum for all sensor values excluding new value
                    // time interval for new value is unknown, we just got it
                    _sum += _currentValue * (s.TimeStamp - _previouseTime);
                    _avgTime += s.TimeStamp - _previouseTime;
                    _previouseTime = s.TimeStamp;
                    _currentValue = s.Value;
                    this.value = _sum;
                    this.TimeStamp = s.TimeStamp;
                }
            }
        }
    }
}
