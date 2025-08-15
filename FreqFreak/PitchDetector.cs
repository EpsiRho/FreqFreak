using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace FreqFreak
{
    public static class PitchDetector
    {
        private static double lastDetectedPitch = 0;
        private static double smoothing = 0.9f;
        private static int currentNoteIndex = -1;
        private static double noteConfidence = 0f;
        private static double lastStrongFrequency = 0f;
        private const double switchThreashold = 0.98; // How confident before switching notes
        private const double holdThreshold = 0.2;   // Minimum confidence to maintain current note
        private const double confidenceDelay = 0.99; // How fast confidence decays
        private const double confidenceBoost = 0.15; // How much confidence increases on match
        private static Queue<double> pitchHistory = new Queue<double>();
        private const int historySize = 30;


        public static (double frequency, string note) DetectPitch(double[] fftFrame, double[] binFrequencies)
        {
            if(fftFrame == null || fftFrame.Length == 0)
            {
                return (lastStrongFrequency, FrequencyToNoteName(lastStrongFrequency));
            }
            // Find peaks with adaptive threshold
            double maxMagnitude = fftFrame.Max();
            double avgMagnitude = fftFrame.Average();
            double threshold = maxMagnitude * 0.99f;

            var peaks = new List<(int bin, double magnitude)>();

            // Simple peak detection, it's above the threshold then it's a peak baby
            for (int i = 1; i < fftFrame.Length - 1; i++)
            {
                if (fftFrame[i] > threshold &&
                    fftFrame[i] > fftFrame[i - 1] &&
                    fftFrame[i] > fftFrame[i + 1])
                {
                    peaks.Add((i, fftFrame[i]));
                }
            }

            // There are no peaks L
            if (peaks.Count == 0)
            {
                // Decay confidence when no peaks are found
                noteConfidence *= confidenceDelay;

                // If the confidence is too low, fuck it reset the state
                if (noteConfidence < holdThreshold)
                {
                    currentNoteIndex = -1;
                    lastDetectedPitch = 0;
                    return (0f, "—");
                }

                // Otherwise, keep the current note but with decayed confidence
                return (lastStrongFrequency, FrequencyToNoteName(lastStrongFrequency));
            }

            // Sort by magnitude
            peaks = peaks.OrderByDescending(p => p.magnitude).ToList();

            // Detect frequency based on the center of the bin
            double detectedFreq = 0f;

            if (peaks.Count == 0)
            {
                // No peaks, add 0 to history
                pitchHistory.Enqueue(0);
                if (pitchHistory.Count > historySize)
                    pitchHistory.Dequeue();

                noteConfidence *= confidenceDelay;
                if (noteConfidence < holdThreshold)
                {
                    currentNoteIndex = -1;
                    lastDetectedPitch = 0;
                    return (0f, "—");
                }
                return (lastStrongFrequency, FrequencyToNoteName(lastStrongFrequency));
            }

            foreach (var peak in peaks.Take(5))
            {
                double freq = binFrequencies[peak.bin];

                if (freq < 120 || freq > 2800) // We don't care about low bass or high freq noise
                    continue;

                double harmonicFreq = freq * 2f;
                bool hasHarmonic = peaks.Any(p =>
                    Math.Abs(binFrequencies[p.bin] - harmonicFreq) < harmonicFreq * 0.1f);

                if (hasHarmonic || peaks.Count == 1)
                {
                    detectedFreq = freq;
                    break;
                }
            }

            // Fallback to strongest peak in musical range (No harmonics found)
            if (detectedFreq == 0f)
            {
                var musicalPeak = peaks.FirstOrDefault(p =>
                    binFrequencies[p.bin] >= 80 && binFrequencies[p.bin] <= 2000);
                if (musicalPeak.bin != 0)
                    detectedFreq = binFrequencies[musicalPeak.bin];
            }

            // If still no frequency, decay and potentially reset
            if (detectedFreq == 0f)
            {
                //noteConfidence *= confidenceDelay;
                //if (noteConfidence < holdThreshold)
                //{
                //    currentNoteIndex = -1;
                //    lastDetectedPitch = 0;
                //    return (0f, "—");
                //}
                return (lastStrongFrequency, FrequencyToNoteName(lastStrongFrequency));
            }

            // Add detected frequency to history
            pitchHistory.Enqueue(detectedFreq);
            if (pitchHistory.Count > historySize)
                pitchHistory.Dequeue();

            // Calculate averaged frequency from history
            var validPitches = pitchHistory.Where(p => p > 0).ToList();
            if (validPitches.Count > 0)
            {
                // Weighted average
                double weightedSum = 0;
                double weightTotal = 0;
                for (int i = 0; i < validPitches.Count; i++)
                {
                    double weight = (i + 1.0) / validPitches.Count;
                    weightedSum += validPitches[i] * weight;
                    weightTotal += weight;
                }
                detectedFreq = weightedSum / weightTotal;
            }

            // Get Note Index
            double midiNote = 12f * Math.Log(detectedFreq / 440f, 2f) + 69f;
            int noteNumber = (int)Math.Round(midiNote);
            int detectedNoteIndex = noteNumber % 12;
            double peakStrength = peaks[0].magnitude / avgMagnitude; // Relative strength

            // Checking past detections and determining confidence based on them. Mini AI, aw look how smol
            if (currentNoteIndex == -1)
            {
                // No current note, accept the new one
                currentNoteIndex = detectedNoteIndex;
                noteConfidence = peakStrength * 0.5; // Start with moderate confidence
                lastStrongFrequency = detectedFreq;
            }
            else if (detectedNoteIndex == currentNoteIndex)
            {
                // Same note detected, boost confidence
                noteConfidence = Math.Min(100.0, noteConfidence + confidenceBoost);
                lastStrongFrequency = detectedFreq; // Update frequency within same note
            }
            else
            {
                // Different note detected
                double switchConfidence = peakStrength * 0.6; // How confident are we in the new note

                if (switchConfidence > switchThreashold && switchConfidence > noteConfidence)
                {
                    // Switch to new note
                    currentNoteIndex = detectedNoteIndex;
                    noteConfidence = switchConfidence;
                    lastStrongFrequency = detectedFreq;
                }
                else
                {
                    // Not confident enough to switch, decay current confidence slightly
                    noteConfidence *= 0.95;
                    detectedFreq = lastStrongFrequency; // Keep current frequency
                }
            }

            // Temporal smoothing
            if (lastDetectedPitch > 0 && detectedFreq > 0)
            {
                detectedFreq = lastDetectedPitch * smoothing +
                              detectedFreq * (1 - smoothing);
            }
            lastDetectedPitch = detectedFreq;

            string noteName = FrequencyToNoteName(detectedFreq);
            return (detectedFreq, noteName);
        }

        public static double DetectBassAmplitude(double[] fftFrame, double[] binFrequencies, int max)
        {
            if (fftFrame == null || fftFrame.Length == 0)
            {
                return 0;
            }

            // Find the strongest peak in the bass range (20Hz to 200Hz)
            //var lowEnd = new List<double>();
            double totalMagnitude = 0;
            double sumMagnitude = 0;
            double minMagnitude = 0;
            for(int i = 0; i < fftFrame.Length; i++)
            {
                if (binFrequencies[i] < 50)
                {
                    continue;
                }
                else if(binFrequencies[i] > 200)
                {
                    break;
                }

                //lowEnd.Add(fftFrame[i]);
                sumMagnitude += fftFrame[i];
                totalMagnitude += max;
                minMagnitude += Visualizer.InstanceOptions._minHeight + 2; // The floats like to shift, don't let them (sometimes frames are like minHeight.83 or some shit
            }

            if(minMagnitude >= sumMagnitude)
            {
                return 0;
            }

            return sumMagnitude / totalMagnitude;
        }

        private static string FrequencyToNoteName(double frequency)
        {
            if (frequency < 80) return "—";

            // Note names
            string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

            // Calculate MIDI note number (A4 = 440Hz = MIDI 69)
            double midiNote = 12f * Math.Log(frequency / 440f, 2f) + 69f;
            int noteNumber = (int)Math.Round(midiNote);

            // Get note name and octave
            int noteIndex = noteNumber % 12;
            int octave = (noteNumber / 12) - 1;

            // Calculate cents offset
            double cents = (midiNote - noteNumber) * 100f;
            string centsStr = cents >= 0 ? $"+{cents:0}" : $"{cents:0}";

            return $"{Math.Round(frequency).ToString("00000")}hz - {noteNames[noteIndex]}{octave} {centsStr}¢";
        }
        
        public static Color GetPitchColor(double frequency, Color[] colors)
        {
            if (frequency < 80) return Color.FromArgb(255, 80, 80, 80);

            // Get MIDI note
            double midiNote = 12f * Math.Log(frequency / 440f, 2f) + 69f;
            double noteNumber = Math.Round(midiNote);
            double noteIndex = noteNumber % 12;

            return Visualizer.GetGradientColor(colors, noteIndex / (double)12);
        }

        public static double[] CalculateBinCenters(double[] edges)
        {
            // Centers array will have one less element than edges
            double[] centers = new double[edges.Length - 1];

            for (int i = 0; i < centers.Length; i++)
            {
                double lowerEdge = edges[i];
                double upperEdge = edges[i + 1];

                // Use geometric mean for frequency bins (most appropriate for log-scaled data)
                centers[i] = Math.Sqrt(lowerEdge * upperEdge);

                // Alternative: arithmetic mean (less ideal for frequency)
                // centers[i] = (float)((lowerEdge + upperEdge) / 2.0);

                // Alternative: logarithmic mean (also good for frequency)
                // centers[i] = (float)((upperEdge - lowerEdge) / Math.Log(upperEdge / lowerEdge));
            }

            return centers;
        }
    }
}
