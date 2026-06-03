#!/bin/bash

# Target URL for the health check
URL="https://fuel-flow-admin-panel-bac.onrender.com/api/health"

echo "[$(date)] Pinging $URL to keep it awake..."
response=$(curl -s -o /dev/null -w "%{http_code}" "$URL")

if [ "$response" -eq 200 ]; then
  echo "[$(date)] Success: Received 200 OK"
else
  echo "[$(date)] Error: Received status code $response"
fi
