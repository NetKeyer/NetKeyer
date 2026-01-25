"""
Morse Code Training Data Generator V2
Adds is_key_down feature to distinguish Dit from ElementSpace
"""

import pandas as pd
import numpy as np
import random

def generate_morse_dataset_v2(num_samples=10000, base_dit_ms=100):
    """
    Generates synthetic Morse timing data with key state as a feature.
    
    Features:
        - duration_ms: Pulse/gap duration in milliseconds
        - is_key_down: 1 if key down (signal), 0 if key up (silence)
    
    Labels:
        - 0: Dit (short key-down pulse)
        - 1: Dah (long key-down pulse)
        - 2: ElementSpace (short key-up gap, between dits/dahs)
        - 3: WordSpace (long key-up gap, between words)
    """
    data = []
    
    # Simulate a range of speeds (WPM)
    # Different operators have different speeds
    
    for _ in range(num_samples):
        # Occasionally shift the base speed to simulate different senders
        current_dit = base_dit_ms * random.uniform(0.8, 1.2)
        current_dah = current_dit * 3
        
        # Add 'Fist Jitter' (Random variation in individual pulses)
        jitter = lambda x: x * np.random.normal(1.0, 0.08)
        
        # Randomly choose an element to generate
        choice = random.random()
        
        if choice < 0.25:  # Dit (key down)
            duration = jitter(current_dit)
            is_key_down = 1
            label = 0
        elif choice < 0.50:  # Dah (key down)
            duration = jitter(current_dah)
            is_key_down = 1
            label = 1
        elif choice < 0.75:  # ElementSpace (key up)
            duration = jitter(current_dit)
            is_key_down = 0
            label = 2
        else:  # WordSpace (key up)
            duration = jitter(current_dit * 7)
            is_key_down = 0
            label = 3
        
        data.append([duration, is_key_down, label])
    
    df = pd.DataFrame(data, columns=['duration_ms', 'is_key_down', 'label'])
    df.to_csv('morse_training_data_v2.csv', index=False)
    print(f"Generated {num_samples} samples in morse_training_data_v2.csv")
    
    # Print statistics
    print("\nDataset Statistics:")
    stats = df.groupby('label').agg({
        'duration_ms': ['count', 'mean', 'std', 'min', 'max'],
        'is_key_down': 'mean'
    }).round(2)
    stats.columns = ['Count', 'Mean Duration', 'Std Duration', 'Min Duration', 'Max Duration', 'Key Down %']
    print(stats)
    
    print("\nLabel Distribution:")
    label_names = {0: 'Dit', 1: 'Dah', 2: 'ElementSpace', 3: 'WordSpace'}
    dist = df['label'].value_counts().sort_index()
    for label, count in dist.items():
        print(f"  {label_names[label]}: {count} ({count/len(df)*100:.1f}%)")
    
    print("\nKey State Distribution:")
    print(f"  Key Down (1): {df['is_key_down'].sum():.0f} ({df['is_key_down'].mean()*100:.1f}%)")
    print(f"  Key Up (0): {(~df['is_key_down'].astype(bool)).sum()} ({(1-df['is_key_down'].mean())*100:.1f}%)")
    
    # Show sample data
    print("\nSample rows:")
    print(df.head(20).to_string())
    
    return df

def generate_sequence_based_data(num_characters=2000, base_dit_ms=100):
    """
    Bonus: Generates realistic Morse character sequences (for future LSTM improvement)
    
    Each character generates a proper alternating sequence:
    e.g., "A" (.-) = Dit, ElementSpace, Dah, CharacterSpace
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
    
    for _ in range(num_characters):
        # Pick random character
        char = random.choice(list(morse_code.keys()))
        pattern = morse_code[char]
        
        # Random speed for this character
        current_dit = base_dit_ms * random.uniform(0.8, 1.2)
        current_dah = current_dit * 3
        jitter = lambda x: x * np.random.normal(1.0, 0.08)
        
        # Generate sequence for this character
        for i, symbol in enumerate(pattern):
            # Key down pulse
            if symbol == '.':
                data.append([jitter(current_dit), 1, 0])  # Dit
            else:  # '-'
                data.append([jitter(current_dah), 1, 1])  # Dah
            
            # Key up gap (after each symbol except last)
            if i < len(pattern) - 1:
                data.append([jitter(current_dit), 0, 2])  # ElementSpace
        
        # Add character space or word space randomly
        if random.random() < 0.3:  # 30% chance of word space
            data.append([jitter(current_dit * 7), 0, 3])  # WordSpace
        else:
            data.append([jitter(current_dit * 3), 0, 2])  # CharacterSpace (treat as ElementSpace)
    
    df = pd.DataFrame(data, columns=['duration_ms', 'is_key_down', 'label'])
    df.to_csv('morse_sequence_data.csv', index=False)
    print(f"\nGenerated {len(df)} sequence samples in morse_sequence_data.csv")
    print(f"  ({num_characters} characters encoded)")
    
    return df

if __name__ == "__main__":
    print("="*60)
    print("Morse Training Data Generator V2")
    print("="*60)
    
    # Generate improved training data with is_key_down feature
    df = generate_morse_dataset_v2(num_samples=10000)
    
    print("\n" + "="*60)
    print("Bonus: Generating Sequence-Based Data")
    print("="*60)
    
    # Generate sequence-based data for future LSTM improvements
    df_seq = generate_sequence_based_data(num_characters=2000)
