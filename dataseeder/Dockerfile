FROM python:3.12-slim

WORKDIR /app

# Install Python dependencies (separate layer for Docker cache efficiency)
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy all seeder source files
COPY . .

CMD ["python", "main.py"]
