This is a structured plan to migrate the cw decode logic from a Bimodal/Statistical algorithm to a Neural Network-based approach.

To move from the statistical "peak-hunting" of `CWMonitor` to a Neural Network (NN), the goal is to stop relying on hardcoded math formulas and instead allow a model to identify patterns in timing and rhythm.

Here is a 4-step migration plan to transition your Morse decoding logic.

---

## Step 1: Data Acquisition (Creating the "Textbook")

A Neural Network requires thousands of labeled examples to learn. Since you already have a functional bimodal decoder, you can use it to create your own training data.

* **Synthetic Generation:** Write a small utility to generate "perfect" Morse timings from text.
* **Noise Injection:** Intentionally add "jitter" (e.g.,  **10%** random variation) to the timings to simulate a human "fist."
* **Logging Mode:** Update `CWMonitor` to log every key-down/key-up event along with what the current decoder thinks it is (Dit, Dah, or Space).
* *Input Feature:* Pulse Duration.
* *Target Label:* 0 (Dit), 1 (Dah), 2 (Element Space), 3 (Word Space).



---

## Step 2: Selecting the Architecture

For Morse code, the best architecture is a **Recurrent Neural Network (RNN)**, specifically using **LSTM (Long Short-Term Memory)** layers.

* **Why LSTM?** Morse code is context-dependent. A 150ms pulse might be a "Dah" if the sender is fast, or a "Dit" if they are slow. An LSTM "remembers" the speed of the last few pulses to categorize the current one correctly.
* **Input Window:** Instead of feeding one pulse at a time, feed a sliding window of the last **5 to 10** timing events.

---

## Step 3: Training and Export

You don't need to write the training code in C#. It is standard practice to train in **Python** and run the model in **C#**.

1. **Framework:** Use **PyTorch** or **TensorFlow** to train a simple 3-layer LSTM.
2. **Loss Function:** Use `CrossEntropyLoss` to help the model distinguish between the four classes (Dit/Dah/ShortSpace/LongSpace).
3. **Export to ONNX:** Save the trained model as an **.onnx** file. ONNX is a universal format that C# can run very efficiently using the `Microsoft.ML.OnnxRuntime` library.

---

## Step 4: Integration into the C# Project

Once you have the `.onnx` file, you will replace the "Bucket" logic in `CWMonitor.cs`.

* **Replace `TimingAnalyzer`:** Instead of calculating `Math.Log`, you will maintain a `Queue<float>` of the last 10 timings.
* **Inference Loop:** Every time a new duration is recorded:
1. Push the duration into the queue.
2. Pass the queue to the ONNX Inference Session.
3. The model returns probabilities (e.g., 98% Dah, 2% Dit).
4. Pass the result to your existing `MorseCodeDecoder.cs` dictionary.



---

## Why this is a "Level Up"

While the bimodal logic in `CWReader` is clever, it is **fragile** to "weighting" (where a user consistently makes dits too long). A Neural Network learns the *character* of the sender. It can realize, "This sender always elongates their first Dah of a word," and adjust its decoding on the fly without manual recalibration.
