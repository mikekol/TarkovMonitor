#!/usr/bin/env bash
cd "$(dirname "$0")"
if [ ! -d .venv ]; then
    echo "Creating virtual environment..."
    python3 -m venv .venv
    .venv/bin/pip install -r requirements.txt
fi
.venv/bin/python -m tarkovmonitor_tui.app "$@"
