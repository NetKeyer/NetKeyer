"""
Morse Code Neural Network Training Script (PyTorch)

This script trains two models:
1. Dense Model: Simple feedforward network (baseline)
2. LSTM Model: Sequence-based recurrent network (recommended)

Both models are exported as ONNX for C# integration.
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

# Set random seeds for reproducibility
np.random.seed(42)
torch.manual_seed(42)

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
    Simple Dense (feedforward) neural network.
    Good baseline for single-pulse classification.
    """
    def __init__(self, input_dim=1, num_classes=4):
        super(DenseModel, self).__init__()
        self.network = nn.Sequential(
            nn.Linear(input_dim, 64),
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
    LSTM-based model for sequence classification.
    Better for context-aware decoding (recommended for production).
    """
    def __init__(self, input_dim=1, hidden_dim=64, num_classes=4):
        super(LSTMModel, self).__init__()
        self.lstm1 = nn.LSTM(input_dim, hidden_dim, batch_first=True)
        self.dropout1 = nn.Dropout(0.3)
        self.lstm2 = nn.LSTM(hidden_dim, 32, batch_first=True)
        self.dropout2 = nn.Dropout(0.2)
        self.fc1 = nn.Linear(32, 16)
        self.relu = nn.ReLU()
        self.fc2 = nn.Linear(16, num_classes)
    
    def forward(self, x):
        # x shape: (batch, seq_len, features)
        out, _ = self.lstm1(x)
        out = self.dropout1(out)
        out, _ = self.lstm2(out)
        out = self.dropout2(out)
        # Take the last output
        out = out[:, -1, :]
        out = self.fc1(out)
        out = self.relu(out)
        out = self.fc2(out)
        return out

def load_and_prepare_data(csv_path='morse_training_data.csv'):
    """Load training data and prepare for modeling."""
    print("Loading training data...")
    df = pd.read_csv(csv_path)
    
    # Features and labels
    X = df['duration_ms'].values.reshape(-1, 1)
    y = df['label'].values
    
    # Split data
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.2, random_state=42, stratify=y
    )
    
    # Normalize features
    scaler = StandardScaler()
    X_train_scaled = scaler.fit_transform(X_train)
    X_test_scaled = scaler.transform(X_test)
    
    print(f"Training samples: {len(X_train)}")
    print(f"Test samples: {len(X_test)}")
    print(f"Number of classes: {len(np.unique(y))}")
    
    return X_train_scaled, X_test_scaled, y_train, y_test, scaler

def prepare_sequence_data(X_train, X_test, y_train, y_test, sequence_length=10):
    """
    Convert individual samples into sequences for LSTM training.
    Each sample becomes a sliding window of previous timings.
    """
    def create_sequences(X, y, seq_len):
        X_seq, y_seq = [], []
        for i in range(len(X) - seq_len):
            X_seq.append(X[i:i+seq_len])
            y_seq.append(y[i+seq_len])
        return np.array(X_seq), np.array(y_seq)
    
    X_train_seq, y_train_seq = create_sequences(X_train, y_train, sequence_length)
    X_test_seq, y_test_seq = create_sequences(X_test, y_test, sequence_length)
    
    return X_train_seq, X_test_seq, y_train_seq, y_test_seq

def train_model(model, train_loader, val_loader, epochs=100, patience=10, device='cpu'):
    """Train a PyTorch model with early stopping."""
    criterion = nn.CrossEntropyLoss()
    optimizer = optim.Adam(model.parameters())
    
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
        
        # Print progress every 10 epochs
        if (epoch + 1) % 10 == 0:
            print(f"Epoch {epoch+1}/{epochs} - "
                  f"Loss: {train_loss:.4f}, Acc: {train_acc:.4f}, "
                  f"Val Loss: {val_loss:.4f}, Val Acc: {val_acc:.4f}")
        
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
    
    labels = {0: 'Dit', 1: 'Dah', 2: 'ElementSpace', 3: 'WordSpace'}
    print(f"\nPer-Class Accuracy:")
    for class_id in range(4):
        mask = all_labels == class_id
        if np.sum(mask) > 0:
            class_acc = np.mean(all_preds[mask] == class_id)
            print(f"  {labels[class_id]}: {class_acc*100:.2f}%")
    
    # Confusion insights
    print(f"\nConfusion Analysis:")
    for true_class in range(4):
        mask = all_labels == true_class
        if np.sum(mask) > 0:
            confused_with = all_preds[mask]
            for pred_class in range(4):
                if pred_class != true_class:
                    confusion_rate = np.mean(confused_with == pred_class)
                    if confusion_rate > 0.05:  # Show if > 5%
                        print(f"  {labels[true_class]} confused as {labels[pred_class]}: {confusion_rate*100:.1f}%")

def plot_training_history(history, model_name):
    """Plot training and validation metrics."""
    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(12, 4))
    
    epochs = range(1, len(history['train_acc']) + 1)
    
    # Accuracy
    ax1.plot(epochs, history['train_acc'], label='Train Accuracy')
    ax1.plot(epochs, history['val_acc'], label='Val Accuracy')
    ax1.set_title(f'{model_name} - Accuracy')
    ax1.set_xlabel('Epoch')
    ax1.set_ylabel('Accuracy')
    ax1.legend()
    ax1.grid(True)
    
    # Loss
    ax2.plot(epochs, history['train_loss'], label='Train Loss')
    ax2.plot(epochs, history['val_loss'], label='Val Loss')
    ax2.set_title(f'{model_name} - Loss')
    ax2.set_xlabel('Epoch')
    ax2.set_ylabel('Loss')
    ax2.legend()
    ax2.grid(True)
    
    plt.tight_layout()
    plt.savefig(f'{model_name.lower().replace(" ", "_")}_training.png')
    print(f"Training plot saved: {model_name.lower().replace(' ', '_')}_training.png")

def export_to_onnx(model, model_name, input_shape, device='cpu'):
    """Export PyTorch model to ONNX format for C# integration."""
    onnx_path = f'{model_name.lower().replace(" ", "_")}.onnx'
    
    model.eval()
    dummy_input = torch.randn(input_shape).to(device)
    
    try:
        # Try newer export API
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
            dynamo=False  # Disable dynamo for compatibility
        )
        print(f"[OK] ONNX model exported: {onnx_path}")
    except Exception as e:
        print(f"[WARNING] ONNX export failed: {e}")
        # Save PyTorch model as backup
        pt_path = f'{model_name.lower().replace(" ", "_")}.pt'
        torch.save(model.state_dict(), pt_path)
        print(f"[OK] PyTorch model saved instead: {pt_path}")
        print("  You can convert to ONNX manually later using a stable PyTorch version")
        return pt_path
    
    return onnx_path

def main():
    print("="*60)
    print("Morse Code Neural Network Training (PyTorch)")
    print("="*60)
    
    device = 'cuda' if torch.cuda.is_available() else 'cpu'
    print(f"Using device: {device}")
    
    # Load and prepare data
    X_train, X_test, y_train, y_test, scaler = load_and_prepare_data()
    
    # Save scaler parameters for C# integration
    print(f"\nScaler Parameters (for C# integration):")
    print(f"  Mean: {scaler.mean_[0]:.4f}")
    print(f"  Std: {scaler.scale_[0]:.4f}")
    
    # ============================================================
    # Train Dense Model (Baseline)
    # ============================================================
    print("\n" + "="*60)
    print("Training Dense Model (Baseline)")
    print("="*60)
    
    # Prepare data loaders
    train_val_split = int(0.8 * len(X_train))
    X_train_split, X_val_split = X_train[:train_val_split], X_train[train_val_split:]
    y_train_split, y_val_split = y_train[:train_val_split], y_train[train_val_split:]
    
    train_dataset = MorseDataset(X_train_split, y_train_split)
    val_dataset = MorseDataset(X_val_split, y_val_split)
    test_dataset = MorseDataset(X_test, y_test)
    
    train_loader = DataLoader(train_dataset, batch_size=32, shuffle=True)
    val_loader = DataLoader(val_dataset, batch_size=32)
    test_loader = DataLoader(test_dataset, batch_size=32)
    
    # Create and train model
    dense_model = DenseModel().to(device)
    print(dense_model)
    
    dense_history = train_model(dense_model, train_loader, val_loader, 
                                epochs=100, patience=10, device=device)
    
    # Evaluate
    evaluate_model(dense_model, test_loader, "Dense Model", device)
    
    # Plot and export
    plot_training_history(dense_history, "Dense Model")
    export_to_onnx(dense_model, "morse_dense_model", (1, 1), device)
    
    # ============================================================
    # Train LSTM Model (Sequence-based, Recommended)
    # ============================================================
    print("\n" + "="*60)
    print("Training LSTM Model (Sequence-based)")
    print("="*60)
    
    sequence_length = 10
    X_train_seq, X_test_seq, y_train_seq, y_test_seq = prepare_sequence_data(
        X_train, X_test, y_train, y_test, sequence_length
    )
    
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
    
    train_seq_loader = DataLoader(train_seq_dataset, batch_size=32, shuffle=True)
    val_seq_loader = DataLoader(val_seq_dataset, batch_size=32)
    test_seq_loader = DataLoader(test_seq_dataset, batch_size=32)
    
    # Create and train model
    lstm_model = LSTMModel().to(device)
    print(lstm_model)
    
    lstm_history = train_model(lstm_model, train_seq_loader, val_seq_loader,
                              epochs=100, patience=10, device=device)
    
    # Evaluate
    evaluate_model(lstm_model, test_seq_loader, "LSTM Model", device)
    
    # Plot and export
    plot_training_history(lstm_history, "LSTM Model")
    export_to_onnx(lstm_model, "morse_lstm_model", (1, sequence_length, 1), device)
    
    # ============================================================
    # Final Summary
    # ============================================================
    print("\n" + "="*60)
    print("Training Complete!")
    print("="*60)
    print("\nGenerated Files:")
    print("  1. morse_dense_model.onnx - Simple feedforward model")
    print("  2. morse_lstm_model.onnx - Context-aware LSTM model (recommended)")
    print("  3. dense_model_training.png - Dense model training curves")
    print("  4. lstm_model_training.png - LSTM model training curves")
    print("\nNext Steps:")
    print("  1. Add ONNX Runtime to C# project: Microsoft.ML.OnnxRuntime")
    print("  2. Load the .onnx model in CWMonitor.cs")
    print("  3. Replace TimingAnalyzer logic with model inference")
    print(f"  4. Use scaler params: Mean={scaler.mean_[0]:.4f}, Std={scaler.scale_[0]:.4f}")

if __name__ == "__main__":
    main()
