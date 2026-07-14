"""Regenerate Python gRPC stubs from the shared proto file."""

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).parent
PROTO_DIR = ROOT.parent / "TarkovMonitor.Service" / "Protos"

subprocess.run(
    [
        sys.executable, "-m", "grpc_tools.protoc",
        f"-I{PROTO_DIR}",
        f"--python_out={ROOT / 'tarkovmonitor_tui'}",
        f"--grpc_python_out={ROOT / 'tarkovmonitor_tui'}",
        str(PROTO_DIR / "game_events.proto"),
    ],
    check=True,
)
print("Proto stubs regenerated.")
