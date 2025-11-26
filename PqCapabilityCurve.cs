using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json; // Make sure to use this namespace

namespace BessHilSimulator
{
    // 1. Create a class to represent the JSON structure
    public class CurveConfig
    {
        public double[] Voltages { get; set; }
        public double[] CosTheta { get; set; }
        public double[][] QMaxData { get; set; }
    }

    public class PqCapabilityCurve
    {
        private Dictionary<double, (double[] cosTheta, double[] qMax, double[] qMin)> _curves;
        private List<double> _voltageLevels;
        
        // 2. Pass the file path to the constructor
        public PqCapabilityCurve(string configFilePath = "pq-curves.json")
        {
            _curves = new Dictionary<double, (double[], double[], double[])>();
            _voltageLevels = new List<double>();
            LoadCurves(configFilePath);
        }
        
        private void LoadCurves(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Curve configuration not found at {path}");

            // 3. Deserialize the JSON
            string jsonString = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<CurveConfig>(jsonString);

            // 4. Use the loaded data (Logic remains mostly the same)
            double[] voltages = config.Voltages;
            double[] cosTheta = config.CosTheta;
            double[][] qMaxData = config.QMaxData;

            // Generate Qmin (assuming symmetry based on your original code)
            double[][] qMinData = qMaxData.Select(row => row.Select(x => -x).ToArray()).ToArray();

            for (int i = 0; i < voltages.Length; i++)
            {
                _curves[voltages[i]] = (cosTheta, qMaxData[i], qMinData[i]);
            }
            
            _voltageLevels = voltages.ToList();
        }
        
        public (double maxQ, double minQ) GetReactiveLimits(double voltage, double activePower, double apparentPowerLimit = 1.0)
        {
            // Find the closest voltage level
            double closestVoltage = _voltageLevels.OrderBy(v => Math.Abs(v - voltage)).First();
            var curve = _curves[closestVoltage];
            
            // Calculate P/Smax ratio (normalized active power)
            double pOverSmax = activePower / apparentPowerLimit;
            pOverSmax = Math.Max(-1.0, Math.Min(1.0, pOverSmax));
            
            // Find the closest P/Smax value in the capability curve
            int closestIndex = 0;
            double minDiff = double.MaxValue;
            
            for (int i = 0; i < curve.cosTheta.Length; i++)
            {
                double diff = Math.Abs(curve.cosTheta[i] - pOverSmax);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestIndex = i;
                }
            }
            
            // Get Q/Smax from the curve and scale by apparent power limit
            double qOverSmax_max = curve.qMax[closestIndex];
            double maxQ = qOverSmax_max * apparentPowerLimit;
            
            double qOverSmax_min = curve.qMin[closestIndex]; 
            double minQ = qOverSmax_min * apparentPowerLimit;
            
            return (maxQ, minQ);
        }

        // Helper method to get interpolated values between voltage levels
        public (double maxQ, double minQ) GetInterpolatedReactiveLimits(double voltage, double activePower, double apparentPowerLimit = 1.0)
        {
            if (voltage <= _voltageLevels.First())
                return GetReactiveLimits(_voltageLevels.First(), activePower, apparentPowerLimit);
            
            if (voltage >= _voltageLevels.Last())
                return GetReactiveLimits(_voltageLevels.Last(), activePower, apparentPowerLimit);
            
            // Find the two closest voltage levels
            var lowerVoltage = _voltageLevels.Where(v => v <= voltage).Last();
            var upperVoltage = _voltageLevels.Where(v => v >= voltage).First();
            
            if (lowerVoltage == upperVoltage)
                return GetReactiveLimits(lowerVoltage, activePower, apparentPowerLimit);
            
            // Get limits at both voltage levels
            var (maxQ_lower, minQ_lower) = GetReactiveLimits(lowerVoltage, activePower, apparentPowerLimit);
            var (maxQ_upper, minQ_upper) = GetReactiveLimits(upperVoltage, activePower, apparentPowerLimit);
            
            // Linear interpolation
            double ratio = (voltage - lowerVoltage) / (upperVoltage - lowerVoltage);
            double maxQ = maxQ_lower + (maxQ_upper - maxQ_lower) * ratio;
            double minQ = minQ_lower + (minQ_upper - minQ_lower) * ratio;
            
            return (maxQ, minQ);
        }
    }

}
