"""
Morse Code Training Data Generator V3

Strictly adheres to International Morse Code timing standards as defined
in morse-timings.mdc for DNN training with proper timing constants.

Key Improvements from V2:
- Uses correct WPM to dit duration formula: T_dit = 1.2 / WPM
- Implements proper 1:3:7 timing ratios (dit:letter_gap:word_gap)
- Adds Gaussian jitter with σ=0.05-0.1 for realistic human variation
- Validates timing standards during generation
- Uses "PARIS" standard (50 units) for WPM calibration
"""

import pandas as pd
import numpy as np
import random

# ============================================================
# Morse Timing Constants (per morse-timings.mdc)
# ============================================================
DIT_UNITS = 1
DAH_UNITS = 3
ELEMENT_GAP = 1   # Gap between dits and dahs within a letter
LETTER_GAP = 3    # Gap between letters
WORD_GAP = 7      # Gap between words

# Human jitter standard deviation (in timing units)
JITTER_SIGMA = 0.08  # 8% variation (within 0.05 to 0.1 range)

# WPM range for training data
WPM_MIN = 10
WPM_MAX = 40


def wpm_to_dit_duration(wpm):
    """
    Convert WPM to dit duration in seconds.
    Formula: T_dit (seconds) = 1.2 / WPM
    
    Args:
        wpm: Words per minute (using "PARIS" standard = 50 units)
    
    Returns:
        Dit duration in seconds
    """
    return 1.2 / wpm


def dit_to_milliseconds(dit_units, wpm):
    """
    Convert timing units to milliseconds.
    
    Args:
        dit_units: Number of dit timing units
        wpm: Words per minute
    
    Returns:
        Duration in milliseconds
    """
    dit_duration_sec = wpm_to_dit_duration(wpm)
    return dit_units * dit_duration_sec * 1000  # Convert to ms


def apply_human_jitter(duration_ms, sigma=JITTER_SIGMA):
    """
    Apply Gaussian noise to simulate human timing variation.
    
    Args:
        duration_ms: Base duration in milliseconds
        sigma: Standard deviation as fraction of mean (default 0.08 = 8%)
    
    Returns:
        Duration with jitter applied
    """
    jittered = duration_ms * np.random.normal(1.0, sigma)
    return max(jittered, 1.0)  # Ensure minimum 1ms


def generate_morse_dataset_v3(num_samples=20000, wpm_range=(WPM_MIN, WPM_MAX)):
    """
    Generates synthetic Morse timing data with strict adherence to
    International Morse Code timing standards.
    
    Features:
        - duration_ms: Pulse/gap duration in milliseconds
        - is_key_down: 1 if key down (signal), 0 if key up (silence)
    
    Labels:
        - 0: Dit (1 unit, key down)
        - 1: Dah (3 units, key down)
        - 2: ElementSpace (1 unit, key up - between dits/dahs)
        - 3: LetterSpace (3 units, key up - between letters)
        - 4: WordSpace (7 units, key up - between words)
    
    Args:
        num_samples: Number of samples to generate
        wpm_range: Tuple of (min_wpm, max_wpm) for speed variation
    
    Returns:
        DataFrame with training data
    """
    data = []
    
    print(f"Generating {num_samples} samples with WPM range: {wpm_range[0]}-{wpm_range[1]}")
    print(f"Human jitter: sigma={JITTER_SIGMA:.2f} ({JITTER_SIGMA*100:.0f}%)")
    
    # Generate samples
    for i in range(num_samples):
        # Vary WPM to simulate different operators
        # Use slower changes to simulate operator consistency
        if i % 100 == 0:
            current_wpm = random.uniform(wpm_range[0], wpm_range[1])
        
        # Randomly choose an element type with realistic distribution
        choice = random.random()
        
        if choice < 0.20:  # Dit (20%)
            timing_units = DIT_UNITS
            is_key_down = 1
            label = 0  # Dit
            
        elif choice < 0.35:  # Dah (15%)
            timing_units = DAH_UNITS
            is_key_down = 1
            label = 1  # Dah
            
        elif choice < 0.55:  # ElementSpace (20%)
            timing_units = ELEMENT_GAP
            is_key_down = 0
            label = 2  # ElementSpace
            
        elif choice < 0.80:  # LetterSpace (25%)
            timing_units = LETTER_GAP
            is_key_down = 0
            label = 3  # LetterSpace
            
        else:  # WordSpace (20%)
            timing_units = WORD_GAP
            is_key_down = 0
            label = 4  # WordSpace
        
        # Convert to milliseconds using proper formula
        duration_ms = dit_to_milliseconds(timing_units, current_wpm)
        
        # Apply human jitter
        duration_ms = apply_human_jitter(duration_ms, JITTER_SIGMA)
        
        data.append([duration_ms, is_key_down, label])
    
    # Create DataFrame
    df = pd.DataFrame(data, columns=['duration_ms', 'is_key_down', 'label'])
    
    return df


def generate_sequence_based_data_v3(num_characters=3000, wpm_range=(WPM_MIN, WPM_MAX)):
    """
    Generates realistic Morse character sequences with proper timing.
    Each character follows the exact timing standards for DNN training.
    
    Args:
        num_characters: Number of characters to encode
        wpm_range: Tuple of (min_wpm, max_wpm)
    
    Returns:
        DataFrame with sequence-based training data
    """
    morse_code = {
        'A': '.-',    'B': '-...',  'C': '-.-.',  'D': '-..',   'E': '.',
        'F': '..-.',  'G': '--.',   'H': '....',  'I': '..',    'J': '.---',
        'K': '-.-',   'L': '.-..',  'M': '--',    'N': '-.',    'O': '---',
        'P': '.--.',  'Q': '--.-',  'R': '.-.',   'S': '...',   'T': '-',
        'U': '..-',   'V': '...-',  'W': '.--',   'X': '-..-',  'Y': '-.--',
        'Z': '--..',  '0': '-----', '1': '.----', '2': '..---', '3': '...--',
        '4': '....-', '5': '.....', '6': '-....', '7': '--...', '8': '---..',
        '9': '----.'
    }
    
    data = []
    
    print(f"\nGenerating sequence data for {num_characters} characters...")
    
    for i in range(num_characters):
        # Change WPM occasionally to simulate different operators
        if i % 50 == 0:
            current_wpm = random.uniform(wpm_range[0], wpm_range[1])
        
        # Pick random character
        char = random.choice(list(morse_code.keys()))
        pattern = morse_code[char]
        
        # Generate sequence for this character
        for j, symbol in enumerate(pattern):
            # Key down pulse (Dit or Dah)
            if symbol == '.':
                timing_units = DIT_UNITS
                label = 0  # Dit
            else:  # '-'
                timing_units = DAH_UNITS
                label = 1  # Dah
            
            duration_ms = dit_to_milliseconds(timing_units, current_wpm)
            duration_ms = apply_human_jitter(duration_ms, JITTER_SIGMA)
            data.append([duration_ms, 1, label])
            
            # Key up gap after each element (except last in character)
            if j < len(pattern) - 1:
                # ELEMENT_GAP between dits/dahs within character
                duration_ms = dit_to_milliseconds(ELEMENT_GAP, current_wpm)
                duration_ms = apply_human_jitter(duration_ms, JITTER_SIGMA)
                data.append([duration_ms, 0, 2])  # ElementSpace
        
        # Add letter or word space after character
        if random.random() < 0.25:  # 25% chance of word space
            timing_units = WORD_GAP
            label = 4  # WordSpace
        else:
            timing_units = LETTER_GAP
            label = 3  # LetterSpace
        
        duration_ms = dit_to_milliseconds(timing_units, current_wpm)
        duration_ms = apply_human_jitter(duration_ms, JITTER_SIGMA)
        data.append([duration_ms, 0, label])
    
    df = pd.DataFrame(data, columns=['duration_ms', 'is_key_down', 'label'])
    
    return df


def validate_timing_standards(df):
    """
    Validates that generated data respects 1:3:7 timing ratios.
    
    Args:
        df: DataFrame with morse timing data
    
    Returns:
        Dictionary with validation results
    """
    print("\n" + "="*60)
    print("Timing Standards Validation")
    print("="*60)
    
    label_names = {
        0: 'Dit (1u)',
        1: 'Dah (3u)',
        2: 'ElementSpace (1u)',
        3: 'LetterSpace (3u)',
        4: 'WordSpace (7u)'
    }
    
    results = {}
    
    # Calculate mean durations for each label
    for label in range(5):
        mask = df['label'] == label
        if mask.sum() > 0:
            mean_duration = df[mask]['duration_ms'].mean()
            std_duration = df[mask]['duration_ms'].std()
            results[label] = {
                'mean': mean_duration,
                'std': std_duration,
                'count': mask.sum()
            }
    
    # Check ratios (use Dit as baseline)
    dit_mean = results[0]['mean']
    
    print(f"\nMean Durations (across all WPM):")
    for label, name in label_names.items():
        if label in results:
            r = results[label]
            ratio = r['mean'] / dit_mean
            expected = [1, 3, 1, 3, 7][label]
            print(f"  {name:20s}: {r['mean']:6.1f} ms ± {r['std']:5.1f} "
                  f"(ratio={ratio:.2f}, expected={expected})")
    
    print(f"\nTiming Ratio Validation:")
    print(f"  Dit:Dah ratio:     {results[1]['mean']/dit_mean:.2f} (expected 3.00)")
    print(f"  Dit:Letter ratio:  {results[3]['mean']/dit_mean:.2f} (expected 3.00)")
    print(f"  Dit:Word ratio:    {results[4]['mean']/dit_mean:.2f} (expected 7.00)")
    
    # Check if ratios are within acceptable range (±20% tolerance)
    dah_ratio = results[1]['mean'] / dit_mean
    letter_ratio = results[3]['mean'] / dit_mean
    word_ratio = results[4]['mean'] / dit_mean
    
    tolerance = 0.20
    dah_valid = abs(dah_ratio - 3.0) / 3.0 < tolerance
    letter_valid = abs(letter_ratio - 3.0) / 3.0 < tolerance
    word_valid = abs(word_ratio - 7.0) / 7.0 < tolerance
    
    print(f"\nValidation Results:")
    print(f"  Dah ratio (3:1):    {'PASS' if dah_valid else 'FAIL'}")
    print(f"  Letter ratio (3:1): {'PASS' if letter_valid else 'FAIL'}")
    print(f"  Word ratio (7:1):   {'PASS' if word_valid else 'FAIL'}")
    
    return results


def print_dataset_statistics(df, dataset_name="Dataset"):
    """Print comprehensive statistics about the dataset."""
    print("\n" + "="*60)
    print(f"{dataset_name} Statistics")
    print("="*60)
    
    label_names = {
        0: 'Dit',
        1: 'Dah',
        2: 'ElementSpace',
        3: 'LetterSpace',
        4: 'WordSpace'
    }
    
    print(f"\nTotal Samples: {len(df)}")
    
    print(f"\nLabel Distribution:")
    for label in sorted(df['label'].unique()):
        count = (df['label'] == label).sum()
        pct = count / len(df) * 100
        print(f"  {label}: {label_names[label]:15s} - {count:5d} ({pct:5.1f}%)")
    
    print(f"\nKey State Distribution:")
    key_down = df['is_key_down'].sum()
    key_up = len(df) - key_down
    print(f"  Key Down (1): {key_down:5d} ({key_down/len(df)*100:5.1f}%)")
    print(f"  Key Up (0):   {key_up:5d} ({key_up/len(df)*100:5.1f}%)")
    
    print(f"\nDuration Statistics (ms):")
    stats = df.groupby('label')['duration_ms'].describe()
    stats.index = [label_names[i] for i in stats.index]
    print(stats.round(2))
    
    print(f"\nSample Rows:")
    print(df.head(15).to_string(index=False))


def save_dataset(df, filename, description=""):
    """Save dataset to CSV with metadata."""
    df.to_csv(filename, index=False)
    print(f"\n[OK] Saved: {filename}")
    print(f"  Samples: {len(df)}")
    if description:
        print(f"  Description: {description}")


def main():
    """Generate V3 training datasets with strict timing standards."""
    print("="*60)
    print("Morse Code Training Data Generator V3")
    print("="*60)
    print("\nFollowing International Morse Code timing standards:")
    print(f"  - Dit duration:     {DIT_UNITS} unit")
    print(f"  - Dah duration:     {DAH_UNITS} units")
    print(f"  - Element gap:      {ELEMENT_GAP} unit")
    print(f"  - Letter gap:       {LETTER_GAP} units")
    print(f"  - Word gap:         {WORD_GAP} units")
    print(f"  - WPM formula:      T_dit = 1.2 / WPM seconds")
    print(f"  - Reference word:   PARIS (50 units)")
    print(f"  - Human jitter:     sigma = {JITTER_SIGMA:.2f}")
    
    # ============================================================
    # Generate Random Sample Dataset
    # ============================================================
    print("\n" + "="*60)
    print("Generating Random Sample Dataset")
    print("="*60)
    
    df_random = generate_morse_dataset_v3(num_samples=20000, wpm_range=(WPM_MIN, WPM_MAX))
    print_dataset_statistics(df_random, "Random Sample Dataset")
    validate_timing_standards(df_random)
    save_dataset(df_random, 'morse_training_data_v3.csv', 
                 "Random samples with realistic timing distribution")
    
    # ============================================================
    # Generate Sequence-Based Dataset
    # ============================================================
    print("\n" + "="*60)
    print("Generating Sequence-Based Dataset")
    print("="*60)
    
    df_sequence = generate_sequence_based_data_v3(num_characters=3000, 
                                                  wpm_range=(WPM_MIN, WPM_MAX))
    print_dataset_statistics(df_sequence, "Sequence-Based Dataset")
    validate_timing_standards(df_sequence)
    save_dataset(df_sequence, 'morse_sequence_data_v3.csv',
                 "Realistic character sequences with proper timing")
    
    # ============================================================
    # Generate Combined Dataset
    # ============================================================
    print("\n" + "="*60)
    print("Generating Combined Dataset")
    print("="*60)
    
    df_combined = pd.concat([df_random, df_sequence], ignore_index=True)
    
    # Shuffle the combined dataset
    df_combined = df_combined.sample(frac=1, random_state=42).reset_index(drop=True)
    
    print_dataset_statistics(df_combined, "Combined Dataset")
    validate_timing_standards(df_combined)
    save_dataset(df_combined, 'morse_training_data_v3_combined.csv',
                 "Combined random + sequence data for robust training")
    
    # ============================================================
    # Summary
    # ============================================================
    print("\n" + "="*60)
    print("Generation Complete!")
    print("="*60)
    print("\nGenerated Files:")
    print("  1. morse_training_data_v3.csv           - Random samples (20k)")
    print("  2. morse_sequence_data_v3.csv           - Sequence-based (~10k)")
    print("  3. morse_training_data_v3_combined.csv  - Combined dataset (~30k)")
    
    print("\nKey Improvements from V2:")
    print("  + Correct WPM formula: T_dit = 1.2 / WPM")
    print("  + Strict 1:3:7 timing ratios (validated)")
    print("  + 5 classes: Dit, Dah, ElementSpace, LetterSpace, WordSpace")
    print("  + Gaussian jitter: sigma=0.08 (8% human variation)")
    print("  + PARIS standard for WPM calibration")
    print("  + Realistic operator speed variation (10-40 WPM)")
    
    print("\nNext Steps:")
    print("  1. Train models using: python train_morse_model_v3.py")
    print("  2. Compare V3 vs V2 performance")
    print("  3. Validate timing accuracy in real-world scenarios")
    print("  4. Export best model to ONNX for C# integration")


if __name__ == "__main__":
    # Set random seed for reproducibility
    random.seed(42)
    np.random.seed(42)
    
    main()
