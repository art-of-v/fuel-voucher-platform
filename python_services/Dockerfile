FROM python:3.9-slim

WORKDIR /app

# Install system dependencies for OpenCV and EasyOCR
RUN apt-get update && apt-get install -y \
    libgl1 \
    libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

# Install Python dependencies
COPY requirements.txt .
# If requirements.txt doesn't exist, we install manually
RUN pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cpu
RUN pip install fastapi uvicorn python-multipart easyocr opencv-python-headless numpy

# Copy source code
COPY . .

# Create directory for EasyOCR models to persist
RUN mkdir -p .easyocr_models

# Expose port
EXPOSE 8000

# Start server
CMD ["uvicorn", "ocr_server:app", "--host", "0.0.0.0", "--port", "8000"]
