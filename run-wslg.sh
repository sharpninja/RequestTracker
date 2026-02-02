#!/usr/bin/env bash
# Run RequestTracker under WSL with WSLg.
# Use from repo root: ./run-wslg.sh

set -e
cd "$(dirname "$0")/src/RequestTracker"

# WSLg: optional env vars that can help with display/focus (uncomment if needed)
# export GDK_BACKEND=x11
# export DISPLAY=${DISPLAY:-:0}

dotnet run -c Debug "$@"
