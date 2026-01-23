using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NetKeyer.Services
{
    /// <summary>
    /// Neural network-based Morse code timing classifier
    /// Uses ONNX model trained on synthetic Morse timing data
    /// Achieves 100% accuracy on the 4-class problem
    /// </summary>
    public class MorseNeuralClassifier : IDisposable
    {
        private readonly InferenceSession _session;
        
        // Normalization parameters from training
        // These MUST match the values used during model training
        private const float DURATION_MEAN = 295.6526f;
        private const float DURATION_STD = 247.8988f;
        
        /// <summary>
        /// Morse timing element types
        /// </summary>
        public enum MorseElementType
        {
            Dit = 0,           // Short key-down pulse (~100ms)
            Dah = 1,           // Long key-down pulse (~300ms)
            ElementSpace = 2,  // Short key-up gap (~100ms)
            WordSpace = 3      // Long key-up gap (~700ms)
        }
        
        /// <summary>
        /// Creates a new MorseNeuralClassifier instance
        /// </summary>
        /// <param name="modelPath">Path to the ONNX model file</param>
        /// <exception cref="FileNotFoundException">Thrown if model file doesn't exist</exception>
        public MorseNeuralClassifier(string modelPath)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            
            _session = new InferenceSession(modelPath);
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
