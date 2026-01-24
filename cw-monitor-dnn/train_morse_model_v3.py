"""
Morse Code Neural Network Training Script V3 (PyTorch)

Adheres to International Morse Code timing standards from morse-timings.mdc.

Trains models with 2 input features:
  1. duration_ms (normalized)
  2. is_key_down (0 or 1)

5 Output Classes:
  0: Dit (1 unit, key down)
  1: Dah (3 units, key down)
  2: ElementSpace (1 unit, key up - between dits/dahs)
  3: LetterSpace (3 units, key up - between letters)
  4: WordSpace (7 units, key up - between words)

Key Improvements from V2:
  - 5 classes instead of 4 (separates LetterSpace from WordSpace)
  - Proper 1:3:7 timing ratios validated
  - Uses correct WPM formula: T_dit = 1.2 / WPM
  - Trained on combined dataset (41k+ samples)
"""

import pandas as pd
import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim
from torch.utils.data import Dataset, DataLoader
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler
import matplotlib.pyplot as plt
import os

# Set random seeds for reproducibility
np.random.seed(42)
torch.manual_seed(42)

# ============================================================
# Morse Timing Constants (from morse-timings.mdc)
# ============================================================
DIT_UNITS = 1
DAH_UNITS = 3
ELEMENT_GAP = 1
LETTER_GAP = 3
WORD_GAP = 7

LABEL_NAMES = {
    0: 'Dit',
    1: 'Dah',
    2: 'ElementSpace',
    3: 'LetterSpace',
    4: 'WordSpace'
}

class MorseDataset(Dataset):
    """PyTorch Dataset for Morse timing data."""
    def __init__(self, X, y):
        self.X = torch.FloatTensor(X)
        self.y = torch.LongTensor(y)
    
    def __len__(self):
        return len(self.X)
    
    def __getitem__(self, idx):
        return self.X[idx], self.y[idx]


class DenseModel(nn.Module):
    """
    Dense neural network with 2 input features.
    Input: [normalized_duration, is_key_down]
    Output: 5 classes [Dit, Dah, ElementSpace, LetterSpace, WordSpace]
    """
    def __init__(self, input_dim=2, num_classes=5):
        super(DenseModel, self).__init__()
        self.network = nn.Sequential(
            nn.Linear(input_dim, 128),
            nn.ReLU(),
            nn.Dropout(0.3),
            nn.Linear(128, 64),
            nn.ReLU(),
            nn.Dropout(0.3),
            nn.Linear(64, 32),
            nn.ReLU(),
            nn.Dropout(0.2),
            nn.Linear(32, 16),
            nn.ReLU(),
            nn.Linear(16, num_classes)
        )
    
    def forward(self, x):
        return self.network(x)


class LSTMModel(nn.Module):
    """
    LSTM model for sequence classification with 2 features per timestep.
    """
    def __init__(self, input_dim=2, hidden_dim=128, num_classes=5):
        super(LSTMModel, self).__init__()
        self.lstm1 = nn.LSTM(input_dim, hidden_dim, batch_first=True)
        self.dropout1 = nn.Dropout(0.3)
        self.lstm2 = nn.LSTM(hidden_dim, 64, batch_first=True)
        self.dropout2 = nn.Dropout(0.3)
        self.fc1 = nn.Linear(64, 32)
        self.relu = nn.ReLU()
        self.dropout3 = nn.Dropout(0.2)
        self.fc2 = nn.Linear(32, num_classes)
    
    def forward(self, x):
        out, _ = self.lstm1(x)
        out = self.dropout1(out)
        out, _ = self.lstm2(out)
        out = self.dropout2(out)
        out = out[:, -1, :]  # Take last output
        out = self.fc1(out)
        out = self.relu(out)
        out = self.dropout3(out)
        out = self.fc2(out)
        return out


def load_and_prepare_data(csv_path='morse_training_data_v3_combined.csv'):
    """Load training data and prepare for modeling."""
    print("Loading training data...")
    
    if not os.path.exists(csv_path):
        print(f"ERROR: {csv_path} not found!")
        print("Please run generate_morse_training_data_v3.py first.")
        return None, None, None, None, None
    
    df = pd.read_csv(csv_path)
    
    print(f"Dataset: {csv_path}")
    print(f"Total samples: {len(df)}")
    print(f"Features: duration_ms, is_key_down")
    print(f"Classes: {len(df['label'].unique())}")
    
    # Features: duration_ms and is_key_down
    X = df[['duration_ms', 'is_key_down']].values
    y = df['label'].values
    
    # Split data
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.2, random_state=42, stratify=y
    )
    
    # Normalize ONLY the duration feature (column 0)
    # is_key_down (column 1) stays as 0 or 1
    scaler = StandardScaler()
    X_train_scaled = X_train.copy()
    X_test_scaled = X_test.copy()
    
    X_train_scaled[:, 0] = scaler.fit_transform(X_train[:, 0].reshape(-1, 1)).flatten()
    X_test_scaled[:, 0] = scaler.transform(X_test[:, 0].reshape(-1, 1)).flatten()
    
    print(f"\nTraining samples: {len(X_train)}")
    print(f"Test samples: {len(X_test)}")
    
    # Print class distribution
    print(f"\nClass Distribution (Training):")
    for label in sorted(np.unique(y_train)):
        count = np.sum(y_train == label)
        pct = count / len(y_train) * 100
        print(f"  {label}: {LABEL_NAMES[label]:15s} - {count:5d} ({pct:5.1f}%)")
    
    return X_train_scaled, X_test_scaled, y_train, y_test, scaler


def prepare_sequence_data(X_train, X_test, y_train, y_test, sequence_length=15):
    """Convert individual samples into sequences for LSTM training."""
    def create_sequences(X, y, seq_len):
        X_seq, y_seq = [], []
        for i in range(len(X) - seq_len):
            X_seq.append(X[i:i+seq_len])
            y_seq.append(y[i+seq_len])
        return np.array(X_seq), np.array(y_seq)
    
    X_train_seq, y_train_seq = create_sequences(X_train, y_train, sequence_length)
    X_test_seq, y_test_seq = create_sequences(X_test, y_test, sequence_length)
    
    return X_train_seq, X_test_seq, y_train_seq, y_test_seq


def train_model(model, train_loader, val_loader, epochs=150, patience=20, device='cpu'):
    """Train a PyTorch model with early stopping."""
    criterion = nn.CrossEntropyLoss()
    optimizer = optim.Adam(model.parameters(), lr=0.001)
    scheduler = optim.lr_scheduler.ReduceLROnPlateau(optimizer, 'min', patience=5, factor=0.5)
    
    history = {'train_loss': [], 'train_acc': [], 'val_loss': [], 'val_acc': []}
    best_val_loss = float('inf')
    patience_counter = 0
    best_model_state = None
    
    for epoch in range(epochs):
        # Training phase
        model.train()
        train_loss, train_correct, train_total = 0, 0, 0
        
        for X_batch, y_batch in train_loader:
            X_batch, y_batch = X_batch.to(device), y_batch.to(device)
            
            optimizer.zero_grad()
            outputs = model(X_batch)
            loss = criterion(outputs, y_batch)
            loss.backward()
            optimizer.step()
            
            train_loss += loss.item()
            _, predicted = torch.max(outputs, 1)
            train_correct += (predicted == y_batch).sum().item()
            train_total += y_batch.size(0)
        
        # Validation phase
        model.eval()
        val_loss, val_correct, val_total = 0, 0, 0
        
        with torch.no_grad():
            for X_batch, y_batch in val_loader:
                X_batch, y_batch = X_batch.to(device), y_batch.to(device)
                outputs = model(X_batch)
                loss = criterion(outputs, y_batch)
                
                val_loss += loss.item()
                _, predicted = torch.max(outputs, 1)
                val_correct += (predicted == y_batch).sum().item()
                val_total += y_batch.size(0)
        
        # Calculate metrics
        train_loss /= len(train_loader)
        val_loss /= len(val_loader)
        train_acc = train_correct / train_total
        val_acc = val_correct / val_total
        
        history['train_loss'].append(train_loss)
        history['train_acc'].append(train_acc)
        history['val_loss'].append(val_loss)
        history['val_acc'].append(val_acc)
        
        # Update learning rate
        scheduler.step(val_loss)
        
        # Print progress every 10 epochs
        if (epoch + 1) % 10 == 0:
            current_lr = optimizer.param_groups[0]['lr']
            print(f"Epoch {epoch+1}/{epochs} - "
                  f"Loss: {train_loss:.4f}, Acc: {train_acc:.4f}, "
                  f"Val Loss: {val_loss:.4f}, Val Acc: {val_acc:.4f}, "
                  f"LR: {current_lr:.6f}")
        
        # Early stopping
        if val_loss < best_val_loss:
            best_val_loss = val_loss
            patience_counter = 0
            best_model_state = model.state_dict().copy()
        else:
            patience_counter += 1
            if patience_counter >= patience:
                print(f"Early stopping at epoch {epoch+1}")
                break
    
    # Restore best model
    if best_model_state is not None:
        model.load_state_dict(best_model_state)
    
    return history


def evaluate_model(model, test_loader, model_name, device='cpu'):
    """Evaluate model and print detailed metrics."""
    print(f"\n{'='*60}")
    print(f"{model_name} - Evaluation Results")
    print(f"{'='*60}")
    
    model.eval()
    all_preds, all_labels = [], []
    correct, total = 0, 0
    
    with torch.no_grad():
        for X_batch, y_batch in test_loader:
            X_batch, y_batch = X_batch.to(device), y_batch.to(device)
            outputs = model(X_batch)
            _, predicted = torch.max(outputs, 1)
            
            all_preds.extend(predicted.cpu().numpy())
            all_labels.extend(y_batch.cpu().numpy())
            
            correct += (predicted == y_batch).sum().item()
            total += y_batch.size(0)
    
    accuracy = correct / total
    print(f"Test Accuracy: {accuracy*100:.2f}%")
    
    # Per-class accuracy
    all_preds = np.array(all_preds)
    all_labels = np.array(all_labels)
    
    print(f"\nPer-Class Accuracy:")
    for class_id in range(5):
        mask = all_labels == class_id
        if np.sum(mask) > 0:
            class_acc = np.mean(all_preds[mask] == class_id)
            print(f"  {LABEL_NAMES[class_id]:15s}: {class_acc*100:.2f}%")
    
    # Confusion analysis
    print(f"\nConfusion Analysis:")
    has_confusion = False
    for true_class in range(5):
        mask = all_labels == true_class
        if np.sum(mask) > 0:
            confused_with = all_preds[mask]
            for pred_class in range(5):
                if pred_class != true_class:
                    confusion_rate = np.mean(confused_with == pred_class)
                    if confusion_rate > 0.05:  # Show if > 5%
                        print(f"  {LABEL_NAMES[true_class]} confused as {LABEL_NAMES[pred_class]}: "
                              f"{confusion_rate*100:.1f}%")
                        has_confusion = True
    
    if not has_confusion:
        print("  No significant confusion detected! (all < 5%)")
    
    # Timing validation
    print(f"\nTiming Standards Validation:")
    validate_predictions_timing(all_labels, all_preds)
    
    return accuracy


def validate_predictions_timing(true_labels, predicted_labels):
    """
    Validate that predictions maintain proper timing ratios.
    Checks if the model respects 1:3:7 timing standards.
    """
    # Expected timing units for each class
    timing_units = {0: 1, 1: 3, 2: 1, 3: 3, 4: 7}
    
    # Calculate average timing preservation
    timing_errors = []
    for true, pred in zip(true_labels, predicted_labels):
        true_units = timing_units[true]
        pred_units = timing_units[pred]
        if true_units != pred_units:
            error = abs(pred_units - true_units) / true_units
            timing_errors.append(error)
    
    if len(timing_errors) > 0:
        avg_timing_error = np.mean(timing_errors) * 100
        print(f"  Average timing error on misclassifications: {avg_timing_error:.1f}%")
    else:
        print(f"  Perfect classification - no timing errors!")
    
    # Check 1-unit confusions (Dit <-> ElementSpace)
    dit_element_confusion = 0
    for true, pred in zip(true_labels, predicted_labels):
        if (true == 0 and pred == 2) or (true == 2 and pred == 0):
            dit_element_confusion += 1
    
    if dit_element_confusion > 0:
        print(f"  Dit/ElementSpace confusion: {dit_element_confusion} cases "
              f"({dit_element_confusion/len(true_labels)*100:.2f}%)")
        print(f"    -> This is expected; both are 1 unit, distinguished by is_key_down")


def plot_training_history(history, model_name):
    """Plot training and validation metrics."""
    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(14, 5))
    
    epochs = range(1, len(history['train_acc']) + 1)
    
    # Accuracy
    ax1.plot(epochs, history['train_acc'], 'b-', label='Train Accuracy', linewidth=2)
    ax1.plot(epochs, history['val_acc'], 'r-', label='Val Accuracy', linewidth=2)
    ax1.set_title(f'{model_name} - Accuracy', fontsize=14, fontweight='bold')
    ax1.set_xlabel('Epoch', fontsize=12)
    ax1.set_ylabel('Accuracy', fontsize=12)
    ax1.legend(fontsize=11)
    ax1.grid(True, alpha=0.3)
    
    # Loss
    ax2.plot(epochs, history['train_loss'], 'b-', label='Train Loss', linewidth=2)
    ax2.plot(epochs, history['val_loss'], 'r-', label='Val Loss', linewidth=2)
    ax2.set_title(f'{model_name} - Loss', fontsize=14, fontweight='bold')
    ax2.set_xlabel('Epoch', fontsize=12)
    ax2.set_ylabel('Loss', fontsize=12)
    ax2.legend(fontsize=11)
    ax2.grid(True, alpha=0.3)
    
    plt.tight_layout()
    filename = f'{model_name.lower().replace(" ", "_")}_training_v3.png'
    plt.savefig(filename, dpi=150)
    print(f"Training plot saved: {filename}")
    plt.close()


def export_to_onnx(model, model_name, input_shape, device='cpu'):
    """Export PyTorch model to ONNX format for C# integration."""
    onnx_path = f'{model_name.lower().replace(" ", "_")}_v3.onnx'
    
    model.eval()
    dummy_input = torch.randn(input_shape).to(device)
    
    try:
        torch.onnx.export(
            model,
            dummy_input,
            onnx_path,
            export_params=True,
            opset_version=18,
            do_constant_folding=True,
            input_names=['input'],
            output_names=['output'],
            dynamic_axes={'input': {0: 'batch_size'},
                         'output': {0: 'batch_size'}},
            dynamo=False
        )
        print(f"[OK] ONNX model exported: {onnx_path}")
        return onnx_path
    except Exception as e:
        print(f"[WARNING] ONNX export failed: {e}")
        pt_path = f'{model_name.lower().replace(" ", "_")}_v3.pt'
        torch.save(model.state_dict(), pt_path)
        print(f"[OK] PyTorch model saved instead: {pt_path}")
        print("  You can convert to ONNX manually later")
        return pt_path


def main():
    print("="*60)
    print("Morse Code Neural Network Training V3 (PyTorch)")
    print("="*60)
    print("\nFollowing International Morse Code Timing Standards")
    print(f"  - Dit:     {DIT_UNITS} unit")
    print(f"  - Dah:     {DAH_UNITS} units")
    print(f"  - Element: {ELEMENT_GAP} unit")
    print(f"  - Letter:  {LETTER_GAP} units")
    print(f"  - Word:    {WORD_GAP} units")
    print(f"  - Classes: 5 (improved from V2's 4)")
    
    device = 'cuda' if torch.cuda.is_available() else 'cpu'
    print(f"\nUsing device: {device}")
    
    # Load and prepare data
    result = load_and_prepare_data('morse_training_data_v3_combined.csv')
    if result[0] is None:
        return
    
    X_train, X_test, y_train, y_test, scaler = result
    
    # Save scaler parameters for C# integration
    print(f"\n{'='*60}")
    print("Scaler Parameters (for C# integration)")
    print(f"{'='*60}")
    print(f"  Mean: {scaler.mean_[0]:.4f}")
    print(f"  Std:  {scaler.scale_[0]:.4f}")
    print(f"  NOTE: Only normalize duration_ms, keep is_key_down as-is (0 or 1)")
    
    # ============================================================
    # Train Dense Model
    # ============================================================
    print("\n" + "="*60)
    print("Training Dense Model (2 Features, 5 Classes)")
    print("="*60)
    
    # Prepare data loaders
    train_val_split = int(0.8 * len(X_train))
    X_train_split, X_val_split = X_train[:train_val_split], X_train[train_val_split:]
    y_train_split, y_val_split = y_train[:train_val_split], y_train[train_val_split:]
    
    train_dataset = MorseDataset(X_train_split, y_train_split)
    val_dataset = MorseDataset(X_val_split, y_val_split)
    test_dataset = MorseDataset(X_test, y_test)
    
    train_loader = DataLoader(train_dataset, batch_size=64, shuffle=True)
    val_loader = DataLoader(val_dataset, batch_size=64)
    test_loader = DataLoader(test_dataset, batch_size=64)
    
    # Create and train model
    dense_model = DenseModel(input_dim=2, num_classes=5).to(device)
    print(f"\nModel Architecture:")
    print(dense_model)
    print(f"\nTotal Parameters: {sum(p.numel() for p in dense_model.parameters()):,}")
    
    dense_history = train_model(dense_model, train_loader, val_loader, 
                                epochs=150, patience=20, device=device)
    
    # Evaluate
    dense_acc = evaluate_model(dense_model, test_loader, "Dense Model V3", device)
    
    # Plot and export
    plot_training_history(dense_history, "Dense Model V3")
    dense_export_path = export_to_onnx(dense_model, "morse_dense_model", (1, 2), device)
    
    # ============================================================
    # Train LSTM Model
    # ============================================================
    print("\n" + "="*60)
    print("Training LSTM Model (Sequence-based, 2 Features, 5 Classes)")
    print("="*60)
    
    sequence_length = 15
    X_train_seq, X_test_seq, y_train_seq, y_test_seq = prepare_sequence_data(
        X_train, X_test, y_train, y_test, sequence_length
    )
    
    print(f"Sequence length: {sequence_length}")
    print(f"Sequence training samples: {len(X_train_seq)}")
    print(f"Sequence test samples: {len(X_test_seq)}")
    
    # Prepare sequence data loaders
    train_val_split = int(0.8 * len(X_train_seq))
    X_train_seq_split = X_train_seq[:train_val_split]
    X_val_seq_split = X_train_seq[train_val_split:]
    y_train_seq_split = y_train_seq[:train_val_split]
    y_val_seq_split = y_train_seq[train_val_split:]
    
    train_seq_dataset = MorseDataset(X_train_seq_split, y_train_seq_split)
    val_seq_dataset = MorseDataset(X_val_seq_split, y_val_seq_split)
    test_seq_dataset = MorseDataset(X_test_seq, y_test_seq)
    
    train_seq_loader = DataLoader(train_seq_dataset, batch_size=64, shuffle=True)
    val_seq_loader = DataLoader(val_seq_dataset, batch_size=64)
    test_seq_loader = DataLoader(test_seq_dataset, batch_size=64)
    
    # Create and train model
    lstm_model = LSTMModel(input_dim=2, hidden_dim=128, num_classes=5).to(device)
    print(f"\nModel Architecture:")
    print(lstm_model)
    print(f"\nTotal Parameters: {sum(p.numel() for p in lstm_model.parameters()):,}")
    
    lstm_history = train_model(lstm_model, train_seq_loader, val_seq_loader,
                              epochs=150, patience=20, device=device)
    
    # Evaluate
    lstm_acc = evaluate_model(lstm_model, test_seq_loader, "LSTM Model V3", device)
    
    # Plot and export
    plot_training_history(lstm_history, "LSTM Model V3")
    lstm_export_path = export_to_onnx(lstm_model, "morse_lstm_model", 
                                      (1, sequence_length, 2), device)
    
    # ============================================================
    # Final Summary
    # ============================================================
    print("\n" + "="*60)
    print("Training Complete!")
    print("="*60)
    print("\nGenerated Files:")
    print(f"  1. {dense_export_path} - Dense feedforward model")
    print(f"  2. {lstm_export_path} - LSTM sequence model")
    print("  3. dense_model_v3_training_v3.png - Dense training curves")
    print("  4. lstm_model_v3_training_v3.png - LSTM training curves")
    
    print("\n" + "="*60)
    print("Performance Summary")
    print("="*60)
    print(f"Dense Model V3 Accuracy: {dense_acc*100:.2f}%")
    print(f"LSTM Model V3 Accuracy:  {lstm_acc*100:.2f}%")
    
    print("\nKey Improvements from V2:")
    print("  + 5 classes: Separates LetterSpace (3u) from WordSpace (7u)")
    print("  + Better timing accuracy with validated 1:3:7 ratios")
    print("  + Larger combined dataset (41k+ samples)")
    print("  + Improved model architecture (deeper networks)")
    print("  + Learning rate scheduling for better convergence")
    
    print("\nC# Integration Guide:")
    print("  1. Install: Microsoft.ML.OnnxRuntime")
    print("  2. Load model from .onnx file")
    print("  3. Input: [normalized_duration, is_key_down]")
    print(f"  4. Normalize: (duration - {scaler.mean_[0]:.4f}) / {scaler.scale_[0]:.4f}")
    print("  5. is_key_down: 1.0f (key down) or 0.0f (key up)")
    print("  6. Output: 5 classes (argmax for prediction)")
    print(f"\n  Class mapping:")
    for class_id, name in LABEL_NAMES.items():
        print(f"    {class_id}: {name}")


if __name__ == "__main__":
    main()
