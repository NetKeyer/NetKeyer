import pandas as pd
import numpy as np
import random

def generate_morse_dataset(num_samples=5000, base_dit_ms=100):
    """
    Generates synthetic Morse timing data with human-like jitter.
    Labels: 0=Dit, 1=Dah, 2=ElementSpace, 3=WordSpace
    """
    data = []
    
    # Simulate a range of speeds (WPM)
    # 1.2 * (1200 / base_dit_ms) = WPM
    
    for _ in range(num_samples):
        # Occasionally shift the base speed to simulate different senders
        current_dit = base_dit_ms * random.uniform(0.8, 1.2)
        current_dah = current_dit * 3
        
        # Add 'Fist Jitter' (Random variation in individual pulses)
        jitter = lambda x: x * np.random.normal(1.0, 0.08) 
        
        # Randomly choose an element to generate
        choice = random.random()
        
        if choice < 0.3: # Dit
            data.append([jitter(current_dit), 0])
        elif choice < 0.6: # Dah
            data.append([jitter(current_dah), 1])
        elif choice < 0.9: # Element Space
            data.append([jitter(current_dit), 2])
        else: # Word Space
            data.append([jitter(current_dit * 7), 3])

    df = pd.DataFrame(data, columns=['duration_ms', 'label'])
    df.to_csv('morse_training_data.csv', index=False)
    print(f"Generated {num_samples} samples in morse_training_data.csv")
    
    # Print statistics
    print("\nDataset Statistics:")
    print(df.groupby('label').agg({
        'duration_ms': ['count', 'mean', 'std', 'min', 'max']
    }).round(2))
    
    print("\nLabel Distribution:")
    print(df['label'].value_counts().sort_index())

if __name__ == "__main__":
    generate_morse_dataset()
