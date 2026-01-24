using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NetKeyer.Services
{
    /// <summary>
    /// Neural network-based Morse code timing classifier V3
    /// Uses ONNX model trained on International Morse Code timing standards
    /// Follows 1:3:7 timing ratios (Dit:Letter:Word) from morse-timings.mdc
    /// Achieves 87.35% accuracy on the 5-class problem with validated timing
    /// </summary>
    public class MorseNeuralClassifier : IDisposable
    {
        private readonly InferenceSession _session;
        
        // V3 Normalization parameters from training (41k samples, 10-40 WPM)
        // These MUST match the values used during model training
        private const float DURATION_MEAN = 133.4177f;
        private const float DURATION_STD = 126.2867f;
        
        /// <summary>
        /// Morse timing element types following International standards
        /// </summary>
        public enum MorseElementType
        {
            Dit = 0,           // 1 unit - Short key-down pulse
            Dah = 1,           // 3 units - Long key-down pulse
            ElementSpace = 2,  // 1 unit - Short key-up gap (between dits/dahs within letter)
            LetterSpace = 3,   // 3 units - Medium key-up gap (between letters)
            WordSpace = 4      // 7 units - Long key-up gap (between words)
        }
        
        /// <summary>
        /// Creates a new MorseNeuralClassifier V3 instance
        /// </summary>
        /// <param name="modelPath">Path to the ONNX model file (morse_dense_model_v3.onnx)</param>
        /// <exception cref="FileNotFoundException">Thrown if model file doesn't exist</exception>
        public MorseNeuralClassifier(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            
            _session = new InferenceSession(modelPath);
        }
        
        /// <summary>
        /// Gets the timing units for a given element type (1, 3, or 7)
        /// Based on International Morse Code timing standards
        /// </summary>
        public int GetTimingUnits(MorseElementType elementType)
        {
            return elementType switch
            {
                MorseElementType.Dit => 1,
                MorseElementType.Dah => 3,
                MorseElementType.ElementSpace => 1,
                MorseElementType.LetterSpace => 3,
                MorseElementType.WordSpace => 7,
                _ => 0
            };
        }
        
        /// <summary>
        /// Classifies a Morse timing element
        /// </summary>
        /// <param name="durationMs">Duration in milliseconds</param>
        /// <param name="isKeyDown">True if key-down (signal), False if key-up (silence)</param>
        /// <returns>Predicted element type with confidence scores</returns>
        public (MorseElementType prediction, float[] probabilities) Classify(
            float durationMs, 
            bool isKeyDown)
        {
            // Normalize duration (feature 0)
            float normalizedDuration = (durationMs - DURATION_MEAN) / DURATION_STD;
            
            // is_key_down as 0 or 1 (feature 1)
            float keyState = isKeyDown ? 1.0f : 0.0f;
            
            // Create input tensor [batch_size=1, features=2]
            var inputTensor = new DenseTensor<float>(new[] { 1, 2 });
            inputTensor[0, 0] = normalizedDuration;
            inputTensor[0, 1] = keyState;
            
            // Run inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor)
            };
            
            using var results = _session.Run(inputs);
            var output = results.First().AsEnumerable<float>().ToArray();
            
            // Apply softmax to get probabilities
            var probabilities = Softmax(output);
            
            // Get predicted class
            int predictedClass = Array.IndexOf(probabilities, probabilities.Max());
            
            return ((MorseElementType)predictedClass, probabilities);
        }
        
        /// <summary>
        /// Simplified classification returning only the prediction
        /// </summary>
        /// <param name="durationMs">Duration in milliseconds</param>
        /// <param name="isKeyDown">True if key-down (signal), False if key-up (silence)</param>
        /// <returns>Predicted element type</returns>
        public MorseElementType ClassifySimple(float durationMs, bool isKeyDown)
        {
            return Classify(durationMs, isKeyDown).prediction;
        }
        
        /// <summary>
        /// Applies softmax to convert logits to probabilities
        /// </summary>
        private float[] Softmax(float[] logits)
        {
            var max = logits.Max();
            var exp = logits.Select(x => Math.Exp(x - max)).ToArray();
            var sum = exp.Sum();
            return exp.Select(x => (float)(x / sum)).ToArray();
        }
        
        /// <summary>
        /// Releases resources used by the ONNX inference session
        /// </summary>
        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
